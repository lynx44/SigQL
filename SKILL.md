# SigQL Implementation Skill

SigQL is a .NET ORM where method signatures are the query language. Developers declare interfaces (or abstract classes) with method signatures; SigQL generates SQL and materializes results at runtime — no implementation code required.

---

## Table of Contents

1. [Installation](#installation)
2. [Setup and DI Registration](#setup-and-di-registration)
3. [Naming Conventions](#naming-conventions)
4. [SELECT Queries](#select-queries)
5. [Projections](#projections)
6. [WHERE Clause](#where-clause)
7. [Logical Operators](#logical-operators)
8. [IN Clause](#in-clause)
9. [OR Groups](#or-groups)
10. [Null Handling](#null-handling)
11. [Pagination (Offset / Fetch)](#pagination)
12. [ORDER BY](#order-by)
13. [Joins and Related Data](#joins-and-related-data)
14. [ViaRelation — Filter and Flatten via Related Tables](#viarelation)
15. [COUNT and Total Count](#count-and-total-count)
16. [Views and Inline Table-Valued Functions](#views-and-itvfs)
17. [INSERT](#insert)
18. [UPDATE](#update)
19. [UPSERT](#upsert)
20. [SYNC](#sync)
21. [DELETE](#delete)
22. [Return Types Reference](#return-types-reference)
23. [Attributes Reference](#attributes-reference)
24. [Async Support](#async-support)
25. [SQL Logging](#sql-logging)
26. [Advanced — SqlGenerator and Custom SQL](#advanced)
27. [Best Practices](#best-practices)

---

## Installation

```
install-package SigQL           # core + DI builder
install-package SigQL.SqlServer # SQL Server executor and schema reader
install-package SigQL.Types     # attributes, return-type interfaces, filter helpers
```

Namespaces you will use:
```csharp
using SigQL;
using SigQL.SqlServer;
using SigQL.Types;
using SigQL.Types.Attributes;
```

---

## Setup and DI Registration

### Minimal (no DI)

```csharp
var connectionString = "Server=.;Database=MyDb;Integrated Security=true;";

var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(connectionString);
var repositoryBuilder = new RepositoryBuilder(
    new SqlQueryExecutor(() => new SqlConnection(connectionString)),
    sqlDatabaseConfiguration);

var repo = repositoryBuilder.Build<IMyRepository>();
```

### ASP.NET Core

```csharp
// Program.cs / Startup.cs
services.AddSingleton(sp =>
    new SqlDatabaseConfiguration(connectionString));

services.AddSingleton(sp =>
{
    var dbConfig = sp.GetRequiredService<SqlDatabaseConfiguration>();
    return new RepositoryBuilder(
        new SqlQueryExecutor(() => new SqlConnection(connectionString)),
        dbConfig);
});

// Register each repository interface
services.AddSingleton<IMyRepository>(sp =>
    sp.GetRequiredService<RepositoryBuilder>().Build<IMyRepository>());
```

### RepositoryBuilder overloads

```csharp
// Standard
new RepositoryBuilder(IQueryExecutor, IDatabaseConfiguration)

// With SQL logger
new RepositoryBuilder(IQueryExecutor, IDatabaseConfiguration,
    statement => Console.WriteLine(statement.CommandText))

// With custom materializer
new RepositoryBuilder(IQueryExecutor, IDatabaseConfiguration, IQueryMaterializer)

// Full control
new RepositoryBuilder(IQueryExecutor, IDatabaseConfiguration, IQueryMaterializer,
    RepositoryBuilderOptions, Action<PreparedSqlStatement> sqlLogger)
```

---

## Naming Conventions

| C# | SQL |
|---|---|
| Class name | Table name (case-insensitive) |
| Property name | Column name (case-insensitive) |
| Parameter name | Column name (case-insensitive), with basic de-pluralization (`ids` → `id`) |
| `[SqlIdentifier("X")]` on class | Maps class to table named `X` |
| `[Column("X")]` on param/property | Maps to column named `X` |

SigQL reads the full schema (tables, views, functions, PKs, FKs, identity columns) from the live database at startup via `SqlDatabaseConfiguration`.

---

## SELECT Queries

Method name is arbitrary. Return type and parameter names drive the SQL.

```csharp
public interface IWorkLogRepository
{
    // SELECT * FROM WorkLog
    IEnumerable<WorkLog> GetAll();

    // SELECT * FROM WorkLog WHERE Id = @id
    WorkLog Get(int id);

    // SELECT Id, StartDate FROM WorkLog  (projection)
    IEnumerable<WorkLog.IIds> GetIds();
}
```

A method with **no parameters** produces no WHERE clause.
A method with parameters produces `WHERE col = @param` for each parameter (AND by default).

---

## Projections

Return only the columns you need. Projections can be interfaces, inner classes, or POCO classes.

### Interface projection (inner)

```csharp
public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; }

    // Projection: only Id
    public interface IId { int Id { get; } }

    // Projection: Id + Name
    public interface INameAndId { int Id { get; } string Name { get; } }
}

IEnumerable<Employee.IId> GetAllIds();
IEnumerable<Employee.INameAndId> GetAllNames();
```

### POCO inner class projection

```csharp
public class Employee
{
    public class NameOnly
    {
        public string Name { get; set; }
    }
}

IEnumerable<Employee.NameOnly> GetNames();
```

### External class with `[SqlIdentifier]`

```csharp
[SqlIdentifier("Employee")]
public class EmployeeViewModel
{
    public int Id { get; set; }
    public string Name { get; set; }
}

IEnumerable<EmployeeViewModel> GetAll();
```

### Exclude properties with `[ClrOnly]`

```csharp
public class Employee
{
    public class Projected
    {
        public int Id { get; set; }
        public string Name { get; set; }

        [ClrOnly]          // computed in C#, never in SQL
        public string DisplayName => $"{Id}: {Name}";
    }
}
```

---

## WHERE Clause

### By parameter name (matches column name)

```csharp
IEnumerable<Employee> GetByName(string name);
IEnumerable<Employee> GetByIdAndName(int id, string name);   // AND
```

### Alias with `[Column]`

```csharp
IEnumerable<Employee> Get([Column("Name")] string employeeName);
IEnumerable<Employee> Get([Column(nameof(Employee.Name))] string empName);
```

### Filter class (group related conditions)

```csharp
public class Employee
{
    public class Filter
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}

IEnumerable<Employee> Search(Employee.Filter filter);
```

Filter class properties support ALL the same attributes as method parameters.

### Nested filter classes (traverse relations)

```csharp
public class Employee
{
    public class FilterWithWorkLog
    {
        public string Name { get; set; }
        public WorkLog.DateFilter WorkLog { get; set; }  // navigates to WorkLog table
    }
}

public class WorkLog
{
    public class DateFilter
    {
        public DateTime StartDate { get; set; }
    }
}

// Generates: SELECT * FROM Employee INNER JOIN WorkLog ... WHERE Employee.Name = @Name AND WorkLog.StartDate = @StartDate
IEnumerable<Employee> Search(Employee.FilterWithWorkLog filter);
```

---

## Logical Operators

Apply these attributes to parameters **or** filter class properties:

| Attribute | SQL |
|---|---|
| `[GreaterThan]` | `> @value` |
| `[GreaterThanOrEqual]` | `>= @value` |
| `[LessThan]` | `< @value` |
| `[LessThanOrEqual]` | `<= @value` |
| `[StartsWith]` | `LIKE 'value%'` |
| `[Contains]` | `LIKE '%value%'` |
| `[EndsWith]` | `LIKE '%value'` |
| `[Not]` | `NOT (col = @value)` |

```csharp
IEnumerable<WorkLog> GetByDateRange(
    [GreaterThanOrEqual] DateTime startDate,
    [LessThanOrEqual]   DateTime endDate);

IEnumerable<Employee> SearchByName([StartsWith] string name);
IEnumerable<Employee> SearchByNamePart([Contains] string name);
```

Combine with `[Not]`:
```csharp
IEnumerable<Employee> ExcludeId([Not] int id);
IEnumerable<Employee> ExcludeStartsWith([Not][StartsWith] string name);
```

---

## IN Clause

Pass `IEnumerable<T>` — SigQL generates `IN (...)`.
For large lists (>2100 params) SigQL automatically switches to `OPENJSON`.
The same auto-switch also covers bulk inserts/upserts/sync/updateByKey —
row data is serialized to a single JSON parameter and unpacked server-side
via `INSERT @Lookup SELECT ... FROM openjson(@rows) WITH (...)`, so large
bulk writes never hit SQL Server's 2100-parameter limit.

```csharp
IEnumerable<Employee> GetByIds(IEnumerable<int> ids);
IEnumerable<Employee> GetByNames(IEnumerable<string> names);
```

---

## OR Groups

By default parameters are AND-ed. Use `[OrGroup]` to OR them.

### Default group (unnamed)

```csharp
IEnumerable<WorkLog> GetByStartOrEnd(
    [OrGroup] DateTime startDate,
    [OrGroup] DateTime endDate);
// WHERE (startDate = @startDate OR endDate = @endDate)
```

### Named groups (multiple independent OR clusters)

```csharp
IEnumerable<WorkLog> Get(
    [OrGroup("dates")] DateTime startDate,
    [OrGroup("dates")] DateTime endDate,
    [OrGroup("ids")]   int id,
    [OrGroup("ids")]   int employeeId);
// WHERE (startDate = @s OR endDate = @e) AND (id = @id OR employeeId = @eid)
```

### In filter classes

```csharp
public class WorkLog
{
    public class OrFilter
    {
        [OrGroup] public DateTime StartDate { get; set; }
        [OrGroup] public DateTime EndDate   { get; set; }
    }
}

IEnumerable<WorkLog> Search(WorkLog.OrFilter filter);
```

---

## Null Handling

Skip filter conditions when the value is null (or null/empty for collections):

```csharp
IEnumerable<Employee> Search(
    [IgnoreIfNull]           string name,
    [IgnoreIfNullOrEmpty]    IEnumerable<int> ids);
```

These work on filter class properties too:

```csharp
public class Employee
{
    public class OptionalFilter
    {
        [IgnoreIfNull]        public string Name { get; set; }
        [IgnoreIfNullOrEmpty] public IEnumerable<int> Ids { get; set; }
    }
}
```

---

## Pagination

```csharp
IEnumerable<Employee> Search(
    string name,
    [Offset] int offset,
    [Fetch]  int fetch);

// Single result convenience
Employee GetFirst(string name, [Fetch] int fetch = 1);
```

In a filter class:

```csharp
public class Employee
{
    public class PagedFilter
    {
        [IgnoreIfNull] public string Name { get; set; }
        [Offset]       public int Offset  { get; set; }
        [Fetch]        public int Fetch   { get; set; }
    }
}

IEnumerable<Employee> Search(Employee.PagedFilter filter);
```

Requires an ORDER BY when using OFFSET/FETCH in SQL Server — add an `OrderByDirection` parameter alongside.

---

## ORDER BY

### Static (parameter-based)

Parameter type is `OrderByDirection` (enum: `Ascending`, `Descending`).
Parameter name (or `[Column]` alias) matches the column to sort.

```csharp
IEnumerable<WorkLog> GetOrdered(
    OrderByDirection startDate = OrderByDirection.Ascending,
    OrderByDirection endDate   = OrderByDirection.Descending);
```

With alias:
```csharp
IEnumerable<WorkLog> GetOrdered(
    [Column(nameof(WorkLog.StartDate))] OrderByDirection direction = OrderByDirection.Ascending);
```

### In filter/order class

```csharp
public class WorkLog
{
    public class OrderByFilter
    {
        public OrderByDirection StartDate { get; set; }
        public OrderByDirection EndDate   { get; set; }
    }
}

IEnumerable<WorkLog> GetOrdered(WorkLog.OrderByFilter order);
```

### Via related table (ViaRelation on OrderByDirection)

```csharp
IEnumerable<WorkLog> GetOrderedByEmployeeName(
    [ViaRelation("WorkLog->Employee", "Name")] OrderByDirection empName
        = OrderByDirection.Ascending);
```

Multi-hop:
```csharp
IEnumerable<WorkLog> GetOrderedByCity(
    [ViaRelation("WorkLog->Employee->EmployeeAddress->Address", "City")]
    OrderByDirection citySort = OrderByDirection.Ascending);
```

### Dynamic ORDER BY (runtime)

```csharp
IEnumerable<WorkLog> GetOrdered(IOrderBy order);
IEnumerable<WorkLog> GetOrdered(IEnumerable<IOrderBy> orders);

// Call site — same table
var logs = repo.GetOrdered(
    new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Descending));

// Call site — related table
var logs = repo.GetOrdered(
    new OrderByRelation("WorkLog->Employee", "Name", OrderByDirection.Ascending));
```

---

## Joins and Related Data

Return a navigation property typed as another entity (or its projection) to trigger a JOIN.

```csharp
public class Employee
{
    public int Id   { get; set; }
    public string Name { get; set; }
    public IEnumerable<WorkLog> WorkLogs { get; set; }   // collection nav property

    public interface IWithWorkLogs
    {
        int Id { get; }
        IEnumerable<WorkLog.IId> WorkLogs { get; }
    }
}

IEnumerable<Employee.IWithWorkLogs> GetAllWithWorkLogs();
// SELECT Employee.Id, WorkLog.Id FROM Employee LEFT JOIN WorkLog ...
```

- Collection nav props → LEFT JOIN with de-duplication (one Employee instance per PK)
- Scalar nav props → INNER JOIN
- Circular references are handled automatically (only first level materialized)

Supported collection return wrappers: `IEnumerable<T>`, `IList<T>`, `List<T>`, `T[]`, `IReadOnlyCollection<T>`, `ReadOnlyCollection<T>`

### Manual join with `[JoinRelation]`

When FK column names are non-standard:
```csharp
public interface IWorkLogWithView
{
    int Id { get; }
    [JoinRelation("WorkLog(EmployeeId)->(EmployeeId)WorkLogEmployeeView")]
    WorkLogEmployeeView.IFields View { get; }
}
```

---

## ViaRelation

Use `[ViaRelation("TableA->TableB->...", "ColumnName")]` to traverse foreign key relationships.

### On filter parameters / filter class properties (WHERE via JOIN)

```csharp
// Filter Employees by a WorkLog column
IEnumerable<Employee> GetByStartDate(
    [ViaRelation("Employee->WorkLog", "StartDate")] DateTime startDate);

// Multi-hop
IEnumerable<Employee> GetByCity(
    [ViaRelation("Employee->EmployeeAddress->Address", "City")] string city);
```

### On output properties (flatten related column into result)

```csharp
public class Employee
{
    public class Flattened
    {
        public int Id   { get; set; }
        public string Name { get; set; }

        [ViaRelation("Employee->WorkLog", "StartDate")]
        public DateTime? StartDate { get; set; }   // nullable — LEFT OUTER JOIN

        [ViaRelation("Employee->EmployeeAddress->Address", "City")]
        public string City { get; set; }
    }
}

IEnumerable<Employee.Flattened> GetFlattened();
```

---

## COUNT and Total Count

```csharp
// Count only
ICountResult<WorkLog> CountAll();
var n = repo.CountAll().Count;

// Total count (ignores OFFSET/FETCH — useful for pagination UI)
ITotalCount<WorkLog.IId> TotalCountWorkLogs();
var total = repo.TotalCountWorkLogs().TotalCount;

// Combined: total count + paged result in one call
ITotalCountResult<IEnumerable<WorkLog.IId>> GetPaged(
    [Offset] int offset, [Fetch] int fetch);

var result = repo.GetPaged(0, 10);
var items  = result.Result;
var total  = result.TotalCount;
```

---

## Views and ITVFs

### Views

Name the class to match the view name (or use `[SqlIdentifier]`):

```csharp
public class vw_EmployeesWithWorkLogCount
{
    public int EmployeeId    { get; set; }
    public int WorkLogCount  { get; set; }
}

IEnumerable<vw_EmployeesWithWorkLogCount> GetHeavyUsers(
    [GreaterThan] int workLogCount);
```

### Inline Table-Valued Functions

Method name must match the function name. Use `[Parameter]` for function arguments.

```csharp
public class itvf_GetWorkLogsByEmployeeId
{
    public int Id         { get; set; }
    public DateTime StartDate { get; set; }
}

IEnumerable<itvf_GetWorkLogsByEmployeeId> itvf_GetWorkLogsByEmployeeId(
    [Parameter] int empId);

// Additional WHERE filters on ITVF results
IEnumerable<itvf_GetWorkLogsByEmployeeId> itvf_GetWorkLogsByEmployeeId(
    [Parameter] int empId,
    [GreaterThan] DateTime startDate);
```

---

## INSERT

### By parameters

```csharp
[Insert(TableName = nameof(Employee))]
void InsertEmployee(string name);

// Return inserted identity
[Insert(TableName = nameof(Employee))]
Employee.IId InsertEmployeeReturningId(string name);
```

### By class

```csharp
public class Employee
{
    public class InsertFields
    {
        public string Name { get; set; }
    }
}

[Insert]   // TableName inferred from parameter class
void InsertEmployee(Employee.InsertFields employee);

// Bulk insert
[Insert]
IEnumerable<Employee.IId> InsertMany(IEnumerable<Employee.InsertFields> employees);
```

### With related rows

```csharp
public class Employee
{
    public class InsertWithWorkLogs
    {
        public string Name { get; set; }
        public IEnumerable<WorkLog.InsertFields> WorkLogs { get; set; }
    }
}

[Insert]
void InsertWithWorkLogs(Employee.InsertWithWorkLogs employee);
```

---

## UPDATE

### By parameters — mark SET columns with `[Set]`

```csharp
// Update all rows
[Update(TableName = nameof(Employee))]
void UpdateAll([Set] string name);

// Update by id (WHERE generated from non-[Set] params)
[Update(TableName = nameof(Employee))]
void UpdateById([Set] string name, int id);

// Partial update — skip if null
[Update(TableName = nameof(Employee))]
void UpdateMaybe([Set][IgnoreIfNull] string name, int id);
```

### By class

```csharp
public class WorkLog
{
    public class UpdateDates
    {
        [Set] public DateTime StartDate { get; set; }
        [Set] public DateTime EndDate   { get; set; }
        public int Id { get; set; }    // WHERE clause (no [Set])
    }
}

[Update(TableName = nameof(WorkLog))]
void UpdateDates(WorkLog.UpdateDates workLog);
```

Filter class properties also accept `[GreaterThan]`, `[OrGroup]`, etc. for the WHERE clause.

### UpdateByKey — update by primary (or alternate) key

```csharp
[UpdateByKey(TableName = nameof(Employee))]
void UpdateEmployee(int id, string name);   // id is the PK

// Alternate key
[UpdateByKey(TableName = nameof(WorkLog), KeyColumns = "StartDate")]
void UpdateByStartDate(IEnumerable<WorkLog.UpdateByStartDate> rows);
```

---

## UPSERT

INSERT when key is not found, UPDATE when found.

```csharp
[Upsert(TableName = nameof(Employee))]
void UpsertEmployee(int? id, string name);   // null id → INSERT

// Return the row
[Upsert(TableName = nameof(Employee))]
Employee UpsertEmployeeReturning(int? id, string name);

// Bulk via class
[Upsert]
IEnumerable<Employee> UpsertMany(IEnumerable<Employee.UpsertFields> employees);
```

Alternate key:
```csharp
[Upsert(TableName = nameof(Employee), KeyColumns = "Name")]
void UpsertByName(Employee.UpsertByName employee);
```

With `[IgnoreIfNull]` — skips SET on UPDATE when property is null (INSERT still sets the value):
```csharp
public class Employee
{
    public class UpsertOptional
    {
        public int? Id { get; set; }
        [IgnoreIfNull] public string Name { get; set; }
    }
}

[Upsert]
void UpsertOptional(Employee.UpsertOptional employee);
```

With relations:
```csharp
[Upsert]
void UpsertWithWorkLogs(Employee.UpsertWithWorkLogs employee);
```

---

## SYNC

Full synchronization: inserts new rows, updates existing rows, **deletes orphaned rows** in related collections.

```csharp
[Sync]
void SyncEmployee(Employee.SyncFields employee);

[Sync]
Task SyncEmployeeAsync(Employee.SyncFields employee);   // async variant
```

Relation behavior:

| Relation type | Orphan behavior |
|---|---|
| One-to-Many (collection) | Orphaned rows **deleted** |
| Many-to-Many (junction) | Junction row **deleted**, related data preserved |
| Many-to-One (scalar nav) | FK on child **set to null** (disassociated) |
| Root table | **Never deleted** |

Alternate key:
```csharp
[Sync(KeyColumns = "Name")]
void SyncByName(Employee.SyncByName employee);
```

---

## DELETE

```csharp
[Delete(TableName = nameof(Employee))]
void DeleteById(int id);

[Delete(TableName = nameof(Employee))]
void DeleteByName(string name);
```

---

## Return Types Reference

| Type | Description |
|---|---|
| `T` | Single row (null if not found) |
| `IEnumerable<T>` | Zero or more rows |
| `IList<T>`, `List<T>`, `T[]` | Concrete collection |
| `IReadOnlyCollection<T>`, `ReadOnlyCollection<T>` | Read-only collection |
| `ICountResult<T>` | `.Count` — row count |
| `ITotalCount<T>` | `.TotalCount` — count ignoring paging |
| `ITotalCountResult<IEnumerable<T>>` | `.TotalCount` + `.Result` combined |
| `Task<T>` / `Task<IEnumerable<T>>` | Async variants of any above |

---

## Attributes Reference

### Query / WHERE / filter

| Attribute | Applies to | SQL effect |
|---|---|---|
| `[Column("name")]` | param, property | Aliases to named column |
| `[GreaterThan]` | param, property | `> @val` |
| `[GreaterThanOrEqual]` | param, property | `>= @val` |
| `[LessThan]` | param, property | `< @val` |
| `[LessThanOrEqual]` | param, property | `<= @val` |
| `[StartsWith]` | param, property | `LIKE 'val%'` |
| `[Contains]` | param, property | `LIKE '%val%'` |
| `[EndsWith]` | param, property | `LIKE '%val'` |
| `[Not]` | param, property | Negates the condition |
| `[IgnoreIfNull]` | param, property | Omit condition if null |
| `[IgnoreIfNullOrEmpty]` | param, property | Omit condition if null/empty |
| `[Offset]` | param, property | `OFFSET n ROWS` |
| `[Fetch]` | param, property | `FETCH NEXT n ROWS ONLY` |
| `[OrGroup]` / `[OrGroup("name")]` | param, property | Group conditions with OR |
| `[ViaRelation("A->B", "Col")]` | param, property | WHERE via related table JOIN |
| `[Parameter]` | param | ITVF function argument |
| `[ClrOnly]` | property | Exclude from SQL entirely |

### Output / projection

| Attribute | Applies to | Effect |
|---|---|---|
| `[ViaRelation("A->B", "Col")]` | output property | Flatten related column |
| `[JoinRelation("A(fk)->(pk)B")]` | output property | Manual join path |
| `[Column("name")]` | property | Map property to column |
| `[ClrOnly]` | property | C#-only, excluded from SQL |
| `[SqlIdentifier("TableName")]` | class | Map class to table name |

### DML operations (method-level)

| Attribute | Properties | Effect |
|---|---|---|
| `[Insert]` | `TableName` (optional) | INSERT |
| `[Update]` | `TableName` | UPDATE with WHERE |
| `[UpdateByKey]` | `TableName`, `KeyColumns` | UPDATE WHERE PK/AK |
| `[Upsert]` | `TableName`, `KeyColumns` | INSERT or UPDATE |
| `[Sync]` | `TableName`, `KeyColumns` | Upsert + delete orphans |
| `[Delete]` | `TableName` | DELETE |

### DML property-level

| Attribute | Effect |
|---|---|
| `[Set]` | Marks property for SET clause in UPDATE/UPSERT/SYNC |
| `[IgnoreIfNull]` | Skip SET if value is null (preserves existing DB value on UPDATE) |
| `[IgnoreIfNullOrEmpty]` | Skip SET if null/empty |

---

## Async Support

Any method return type can be wrapped in `Task<>`:

```csharp
Task<IEnumerable<Employee>> GetAllAsync();
Task<Employee> GetAsync(int id);
Task InsertAsync(Employee.InsertFields employee);
Task SyncAsync(Employee.SyncFields employee);
```

---

## SQL Logging

```csharp
var repositoryBuilder = new RepositoryBuilder(
    sqlQueryExecutor,
    sqlDatabaseConfiguration,
    statement =>
    {
        Console.WriteLine(statement.CommandText);
        foreach (var p in statement.Parameters)
            Console.WriteLine($"  @{p.Key} = {p.Value}");
    });
```

`PreparedSqlStatement` properties:
- `CommandText` — the SQL string
- `Parameters` — `IDictionary<string, object>` of bound parameters

---

## Advanced

### SqlGenerator — custom SQL with SigQL SELECT

```csharp
var sqlGenerator = new SqlGenerator(sqlDatabaseConfiguration, DefaultPluralizationHelper.Instance);

// Generate SELECT clause only; append custom WHERE
var query = sqlGenerator.CreateSelectQuery(typeof(IEnumerable<WorkLog.IId>));
// query.CommandText => "select WorkLog.Id from WorkLog"

var materializer = new AdoMaterializer(...);
var result = materializer.Materialize<IEnumerable<WorkLog.IId>>(
    query.CommandText + " WHERE Id % 2 = 0");
```

### Adding foreign keys programmatically

Useful when FK constraints don't exist in the database schema:

```csharp
var workLogTable   = sqlDatabaseConfiguration.Tables.FindByName("WorkLog");
var employeeTable  = sqlDatabaseConfiguration.Tables.FindByName("Employee");

// Single FK column
workLogTable.AddForeignKey(
    t => t.Columns.FindByName("EmployeeId"),
    employeeTable.Columns.FindByName("Id"));

// Composite FK
workLogTable.AddForeignKey(
    t => new[] { t.Columns.FindByName("ColA"), t.Columns.FindByName("ColB") },
    new[] { otherTable.Columns.FindByName("ColA"), otherTable.Columns.FindByName("ColB") });
```

---

## Best Practices

1. **Use projections, not full entity classes** — select only the columns you need. If a property exists on the projection, it is SELECTed; there is no lazy-loading.

2. **Prefer filter classes over many parameters** — easier to extend and re-use across methods.

3. **Use `[SqlIdentifier]` when class name ≠ table name** — avoids ambiguity.

4. **Combine `[IgnoreIfNull]` with `[IgnoreIfNullOrEmpty]` for optional search APIs** — skip conditions that aren't provided by the caller.

5. **Use `[ViaRelation]` instead of manual joins** — SigQL handles the join path automatically.

6. **Use `ITotalCountResult<IEnumerable<T>>` for paginated APIs** — returns count and data in one call.

7. **Use `[Sync]` for child collection ownership** — SigQL removes orphaned rows automatically.

8. **Use `[Upsert]` with nullable PK** — `null` pk → INSERT, non-null pk → UPDATE.

9. **Always measure with logging during development** — enable the SQL logger to verify generated queries match intent.

10. **Schemas are not yet supported** — all tables, views, and functions must be in the default schema.
