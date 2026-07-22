



# SigQL

SigQL is a .NET ORM that uses code signatures as the query language. SigQL analyzes method signatures to derive a SQL query, then returns a strongly typed result without the need for developers to write an implementation.

    public interface IRepository 
    {
	    // retrieve a record from the WorkLog table
	    public WorkLog Get(int id);
	    
	    
	    // save all fields and dependencies
	    [Sync]
        Employee SyncEmployee(Employee.Sync employee);
        
        
        // filter and page employees
        IEnumerable<Employee> Search(
          Employee.Filters filter, 
          [Offset] int offset, 
          [Fetch] int fetch);
    }

The goal of SigQL is to enable developers precise and concise access to data by merging SQL directives into method signatures. It is capable of covering the most common use cases. It also includes facilities to make complex queries simpler to write and maintain.

# Contents

**Basic Usage**

 - [SELECT Queries](#select-queries) 
 - [Projections](#projections) 
   - [POCOs](#pocos)
   - [SqlIdentifier Attribute](#sqlidentifier-attribute)
   - [Ignoring Properties](#ignoring-properties)
 - [WHERE Clause](#where-clause) 
   - [Parameter Alias](#parameter-alias)
   - [Logical Operators](#logical-operators)
   - [In Clause](#in-clause)
   - [Ignoring null or empty parameters](#ignoring-null-or-empty-parameters)
 - [Offset and Fetch](#offset-and-fetch)
 - [Order By](#order-by)
   - [Static Order By](#static-order-by)
   - [Order By with ViaRelation](#order-by-with-viarelation)
   - [Dynamic Order By](#dynamic-order-by)
 - [Single Result](#single-result)
 - [Collection Results](#collection-results)
 - [Filtering by related tables](#filtering-by-related-tables)
 - [Returning Relations](#returning-relations)
   - [Flattening Relations with ViaRelation](#flattening-relations-with-viarelation)
   - [JoinRelation Attribute](#joinrelation-attribute)
   - [Circular References](#circular-references)
 - [Perspective](#perspective)
 - [Count](#count)
 - [Views](#views)
 - [Inline Table-valued Functions](#inline-table-valued-functions)
 - [Feature Matrix](#feature-matrix)
   - [Input Matrix](#input-parameter-matrix)
   - [Output Matrix](#output-matrix)

**Insert, Update, Upsert, Sync, and Delete**

 - [Insert](#insert)
 - [Update](#update)
   - [Mixed Set and Filter Properties](#mixed-set-and-filter-properties)
   - [IgnoreIfNull on Update](#ignoreifnull-on-update)
 - [UpdateByKey](#updatebykey)
   - [Alternate Keys on UpdateByKey](#alternate-keys-on-updatebykey)
   - [IgnoreIfNull on UpdateByKey](#ignoreifnull-on-updatebykey)
 - [Upsert](#upsert)
   - [Alternate Keys on Upsert](#alternate-keys-on-upsert)
   - [IgnoreIfNull on Upsert](#ignoreifnull-on-upsert)
 - [Sync](#sync)
   - [Alternate Keys on Sync](#alternate-keys-on-sync)
   - [IgnoreIfNull on Sync](#ignoreifnull-on-sync)
 - [Delete](#delete)
 
**Custom SQL**

 - [Custom SQL](#custom-sql)
   - [Generating SELECT statements](#generating-select-statements)
  
  **Configuration**
  
 - [Installation](#installation)
 - [Adding Foreign Keys](#adding-foreign-keys)
 - [Logging](#logging)

**More Information**
 - [Design Principles](#design-principles)
 - [Examples](#examples)
 - [FAQs](#faqs)

## Basic Usage

### SELECT Queries

Assume table Employee exists in the database. This is modeled by a data class:

     public class Employee
     {
	     public int Id { get; set; }
	     public string Name { get; set; }
     }

To retrieve all Employees, interface IEmployeeRepository is defined with the following method:

    public interface IEmployeeRepository 
    {
	    IEnumerable<Employee> GetAll();
    }

When SigQL builds an instance of IEmployeeRepository, it will dynamically create the implementation at runtime, which will generate the necessary SQL and populate the return value:

    var repositoryBuilder = new RepositoryBuilder(...);
    var employeeRepository = repositoryBuilder.Build<IEmployeeRepository>();
     
    var allEmployees = employeeRepository.GetAll();

This generates SQL similar to:

    select Id, Name from Employee
    
#### Projections

While full table output is technically supported, it is not the intended or optimal way to use SigQL. One of the primary goals of SigQL is to retrieve only the data that is needed, which is more performant and reduces ambiguity during development.

Projections are defined as inner types under a class with the same name as the target table:

    // targeting table Employee
    public class Employee {
	    // ... Optional: Employee columns can be defined here
	    
	    // this projection selects only the Name column
	    public class EmployeeName 
	    {
		    public string EmployeeName { get; set; }
	    }
    }
    
	public interface IEmployeeRepository {
 	    IEnumerable<Employee.EmployeeName> GetAllNames();
	}

*Note that projections do NOT need to be defined within a specific or originating data model class. Projections inside any class with a matching a table name will instruct SigQL to use the aforementioned table.*

See [Design Principles](#design-principles) for more information.

##### POCOs

Plain Old CLR Objects (POCOs) are supported as projections. POCOs should be preferred over interfaces for performance reasons (especially in large result sets), unless there is a specific need for the type system to utilize an interface:

    public class Employee 
    {
    
	    public class NameProjection 
	    {
		    public string Name { get; set; }
	    }
	    
    }

*Note that get and set properties need to be public*

##### SqlIdentifier Attribute

Instead of using an inner class, the [SqlIdentifier] attribute can be used to specify the projected table:

    [SqlIdentifier("Employee")]
    public class EmployeeName 
    {
	    public string Name { get; set; }
    }

##### Ignoring Properties

If a projection property should be ignored by SigQL, use the ClrOnly attribute:

    public class Employee 
    {
    
	    public class NameProjection 
	    {
		    public int Id { get; set; }
		    public string Name { get; set; }
		    [ClrOnly]
		    public string SerializedId => $"{Id}|{Name}";
	    }
	    
    }

#### WHERE Clause

SigQL will generate a WHERE clause condition when a parameter matches a column name. To retrieve Employees by Name:

    IEnumerable<Employee.IName> Get(string name);

*Note that the name of the method is irrelevant. SigQL uses only the parameters and return type to generate the query.*

Specifying multiple parameters concatenates AND operators between each condition:

    IEnumerable<Employee.IName> GetByIdAndName(int id, string name);

Similarly, a class can be passed as filter parameters. This is functionally equivalent to the above example:

    public class Employee 
    {
	    // ... Optional: Employee columns can be defined here
	    // ... Optional: Employee projections can be defined here
	    
	    public class IdAndNameFilter 
	    {
		    public int Id { get; set; }
		    public int Name { get; set; }
	    }
    }
    
    ...
    
    IEnumerable<Employee.IName> GetByIdAndName(Employee.IdAndNameFilter filter);

##### Parameter Alias

If a parameter name differs from the desired column name, the Column attribute can be used:

    IEnumerable<Employee.IName> Get([Column("Name")] string employeeName);

##### Logical Operators

Other logical operators are supported as Attributes:

	IEnumerable<Employee.IName> Get([GreaterThan] int id);

 - GreaterThan
 - GreaterThanOrEqual
 - LessThan
 - LessThanOrEqual
 - StartsWith
 - EndsWith
 - Contains
 - Not

##### In Clause

IN clauses can be specified by passing a collection:

    IEnumerable<Employee.IName> GetWithNames(IEnumerable<string> names);

*Note that the parameter is called names, even though the column identifier in the database is name (non-plural). Basic pluralization is supported, but the most reliable and unambiguous naming scheme would use the exact column identifier*

###### Large collections

SQL Server caps a single command at 2100 parameters. When a SigQL query — or a bulk `Insert`/`Upsert`/`Sync`/`UpdateByKey` — would exceed that limit, SigQL automatically serializes the offending collection to a single JSON parameter and reads it back on the server using `OPENJSON`. The behavior is transparent: the method signature and caller code are unchanged, and small inputs continue to use ordinary parameters. This means you can pass thousands of IDs to an `IN` query, or upsert thousands of rows in one call, without manually chunking the input.

##### StartsWith / Contains / EndsWith on collections

The `[StartsWith]`, `[Contains]`, and `[EndsWith]` attributes can also be applied to a collection parameter. This finds all rows that match *any* of the supplied values, generating a set of `LIKE` predicates combined with `OR`:

    IEnumerable<Employee.IEmployeeId> GetEmployeesByNames([Column("Name"), Contains] List<string> names);

    // where (Name like '%smith%' or Name like '%jones%')
    repository.GetEmployeesByNames(new List<string>() { "smith", "jones" });

This also works when the collection is a property of a filter class:

    public class EmployeeFilter
    {
        [Column("Name"), Contains] public List<string> EmployeeNameContains { get; set; }
    }

    IEnumerable<Employee> Get(EmployeeFilter filter);

When combined with `[Not]`, the predicates are combined with `AND` instead (`Name not like '%smith%' and Name not like '%jones%'`). A `null` element additionally checks the column for `null`. An empty collection returns no results unless `[IgnoreIfNullOrEmpty]` (or `[IgnoreIfNull]` for a null collection) is also specified, in which case the filter is omitted.

##### Ignoring null or empty parameters

When building search interfaces, it can be useful to omit a parameter from the WHERE clause if it is null or empty:

    IEnumerable<Employee.IName> Search(
	    [IgnoreIfNullOrEmpty] IEnumerable<int> id, 
	    [IgnoreIfNull, StartsWith] name);
    
    ...
    
    // ASP.NET MVC example call site
    public IActionResult Index(EmployeeModel model) 
    {
		// if the user does not select any IDs or specify a name filter in the UI,
		// these parameters will be ignored
		var searchResult = employeeRepository.Search(model.IdFilter, model.NameFilter);
		...
    }

*Note that both IgnoreIfNull and IgnoreIfNullOrEmpty attributes are implemented.*

##### OrGroup

*Before considering this feature, please verify that IgnoreIfNullOrEmpty, a collection parameter (for an IN clause), or another feature does not already serve the specific purpose you're looking to acheive. The need for the OrGroup attribute is surprisingly rare.*

Parameters can use the OR operator by decorating the included columns with the [OrGroup] attribute:

    IEnumerable<WorkLog.IWorkLogId> GetByStartOrEndDate([OrGroup] DateTime startDate, [OrGroup] DateTime endDate);

This generates a WHERE clause similar to:
   
    where (startDate = @startDate or endDate = @endDate) 

Note that both *id* and *name* are decorated with the OrGroup, which logically groups them together with an OR operator between them. Specifying just parameter will have no effect.

Parameters outside of an OrGroup will use an AND operator:
    
    IEnumerable<WorkLog.IWorkLogId> Get([OrGroup] DateTime startDate, [OrGroup] DateTime endDate, int employeeId);

This will generate a WHERE clause similar to:

    where (startDate = @startDate or endDate = @endDate) and employeeId = @employeeId

To OR two groups or more, a name can be specified to differentiate the groups:

    IEnumerable<WorkLog.IWorkLogId> Get([OrGroup("dates")] DateTime startDate, [OrGroup("dates")] DateTime endDate, [OrGroup("ids")] int id, [OrGroup("ids")] int employeeId);

This generates a where clause similar to:

    where (startDate = @startDate or endDate = @endDate) 
      and (id = @id or employeeId = @employeeId)

The OrGroup attribute can be used at any level. It can be used inside a class filter:

	public class WorkLog 
	{
	    public class OrColumns
        {
            [OrGroup]
            public DateTime StartDate { get; set; }
            [OrGroup]
            public DateTime EndDate { get; set; }
        }
	}
	...
	// this generates the same basic SQL as the first example
	IEnumerable<WorkLog.IWorkLogId> OrGroupInClassFilter(
            WorkLog.OrColumns filter);

It can also be used between class filters:

    public class WorkLog
    {
        public class BetweenDates
        {
            [GreaterThanOrEqual]
            public DateTime StartDate { get; set; }
            [LessThanOrEqual]
            public DateTime EndDate { get; set; }
        }
        public class IdAndEmployeeId
        {
            public int Id { get; set; }
            public int EmployeeId { get; set; }
        }
    }
    IEnumerable<WorkLog.IWorkLogId> OrGroupWithTwoClassFilters(
            [OrGroup] WorkLog.BetweenDates dates,
            [OrGroup] WorkLog.IdAndEmployeeId filter2);

This generates a WHERE clause similar to:

    where (startDate <= @startDate and endDate >= @endDate) 
      or (id = @id and employeeId = @employeeId)

This logic can be mixed and matched anywhere within the parameter tree of a method.

An OrGroup can span multiple relations (including via `[ViaRelation]`), and those relations may also carry plain, ungrouped filters at the same time. The grouped predicates are OR'd together across the relations, while the ungrouped predicates continue to AND, even when a single relation is targeted by both a grouped and an ungrouped filter:

    public class JobSearch
    {
        // plain filters - AND'd
        [IgnoreIfNullOrEmpty, ViaRelation("Jobs->Positions", "Name")]
        public IEnumerable<string> Positions { get; set; }
        [IgnoreIfNullOrEmpty, ViaRelation("Jobs->Categories", "Name")]
        public IEnumerable<string> Categories { get; set; }
        // keyword search - OR'd across the same relations
        [IgnoreIfNullOrEmpty, Contains, ViaRelation("Jobs->Positions", "Name"), OrGroup("query")]
        public IEnumerable<string> PositionContains { get; set; }
        [IgnoreIfNullOrEmpty, Contains, ViaRelation("Jobs->Categories", "Name"), OrGroup("query")]
        public IEnumerable<string> CategoryContains { get; set; }
    }

This generates a WHERE clause similar to:

    where (exists (Positions where ...) and exists (Categories where ...))       -- plain filters, AND'd
      and (exists (Positions where Name like @query) or exists (Categories where Name like @query))  -- OrGroup, OR'd

#### Offset and Fetch

Offset and Fetch are also supported, which is often used for paging:

    IEnumerable<Employee.IName> Search(
	    [IgnoreIfNullOrEmpty] IEnumerable<int> id, 
	    [IgnoreIfNull, StartsWith] string name, 
	    [Offset] int offset, 
	    [Fetch] int);

#### Order By

##### Static Order By

Sort direction can be specified via parameters using the `OrderByDirection` enum:

    IEnumerable<Employee.IName> Search(
	    [IgnoreIfNullOrEmpty] IEnumerable<int> id,
	    [IgnoreIfNull, StartsWith, Column("Name")] string nameFilter,
	    [Offset] int offset,
	    [Fetch] int,
	    OrderByDirection name = OrderByDirection.Ascending);

Multiple sort directions can be specified:

    IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogs(
        OrderByDirection startDate = OrderByDirection.Ascending,
        OrderByDirection endDate = OrderByDirection.Ascending,
        OrderByDirection employeeId = OrderByDirection.Ascending);

Order By can also be specified via class filter properties:

    public class WorkLog
    {
        public class OrderByDirectionStartDate
        {
            public OrderByDirection StartDate { get; set; }
            public OrderByDirection EndDate { get; set; }
        }
    }
    ...
    IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogs(WorkLog.OrderByDirectionStartDate filter);

##### Order By with ViaRelation

Ordering by columns in related tables is supported using the `[ViaRelation]` attribute on an `OrderByDirection` parameter:

    IEnumerable<WorkLog.IWorkLogWithEmployee> GetOrderedWorkLogs(
        [ViaRelation("WorkLog->Employee", "Name")] OrderByDirection employeeName);

This can be combined with other Order By parameters:

    IEnumerable<WorkLog.IWorkLogWithEmployee> GetOrderedWorkLogs(
        [ViaRelation("WorkLog->Employee", "Name")] OrderByDirection employeeName,
        [Column("StartDate")] OrderByDirection direction,
        IOrderBy dynamicOrderBy);

Multi-hop relation paths are also supported:

    IEnumerable<WorkLog.IWorkLogWithEmployeeWithAddress> GetWorkLogsOrderedByAddressId(
        [ViaRelation("WorkLog->Employee->EFAddressEFEmployee->Address", "Id")] OrderByDirection addressIdSortOrder = OrderByDirection.Ascending);

Order By with ViaRelation can also be specified via a class filter with a nested navigation property:

    public class Employee
    {
        public class EmployeeNameOrder
        {
            public OrderByDirection Name { get; set; }
        }
    }
    public class WorkLog
    {
        public class OrderByDirectionEmployeeName
        {
            public Employee.EmployeeNameOrder Employee { get; set; }
        }
    }
    ...
    IEnumerable<WorkLog.IWorkLogWithEmployee> GetOrderedWorkLogs(WorkLog.OrderByDirectionEmployeeName filter);

##### Dynamic Order By

For cases where the sort column needs to be determined at runtime, SigQL provides the `IOrderBy` interface with two implementations: `OrderBy` and `OrderByRelation`.

**Single dynamic order:**

    IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogs(IOrderBy order);
    ...
    // at call site
    var workLogs = repository.GetOrderedWorkLogs(
        new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Descending));

**Multiple dynamic orders:**

    IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogs(IEnumerable<IOrderBy> orders);
    ...
    var workLogs = repository.GetOrderedWorkLogs(new List<IOrderBy>()
    {
        new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Ascending),
        new OrderBy(nameof(WorkLog), nameof(WorkLog.Id), OrderByDirection.Descending)
    });

**Dynamic order with ViaRelation (OrderByRelation):**

To dynamically order by a column in a related table, use the `OrderByRelation` class:

    IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetOrderedWorkLogs(IOrderBy order);
    ...
    var workLogs = repository.GetOrderedWorkLogs(
        new OrderByRelation("WorkLog->Employee", "Name", OrderByDirection.Ascending));

**Dynamic order in class filters:**

    public class WorkLog
    {
        public class DynamicOrderByEnumerable
        {
            public IEnumerable<IOrderBy> OrderBys { get; set; }
        }
    }
    ...
    IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogs(WorkLog.DynamicOrderByEnumerable filter);

**Combining static and dynamic orders:**

Static and dynamic order parameters can be mixed in the same method signature:

    IEnumerable<WorkLog.IWorkLogWithEmployee> GetOrderedWorkLogs(
        [ViaRelation("WorkLog->Employee", "Name")] OrderByDirection employeeName,
        [Column("StartDate")] OrderByDirection direction,
        IOrderBy dynamicOrderBy);

Dynamic order also works with Offset and Fetch:

    IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogs(
        [Offset] int skip,
        IEnumerable<IOrderBy> order);

#### Single Result

Offset does not need to be specified to use Fetch. The following example is the recommended approach to retrieve a single item:

    Employee.IName Get(string name, [Fetch] int fetch = 1);
    ...
    var employee = employeeRepository.Get("John"); // no need to specify default fetch

*Note that passing a value greater than the non-default value for fetch (1) will throw an exception, since the return type is not a collection*

#### Collection Results

The following collection return types are supported:

 - IEnumerable\<T\>
 - IList\<T\>
 - List\<T\>
 - T[]
 - IReadOnlyCollection\<T\>
 - ReadOnlyCollection\<T\>

#### Filtering by related tables

It's often useful to retrieve items based on a condition in a related table.

Assume we have a related table, WorkLog, which stores shift information about Employees:

    public class WorkLog 
    {
	    public int Id { get; set; }
	    public DateTime StartDate { get; set; }
	    public IEnumerable<Employee> Employees { get; set; }
    }

To retrieve all employees that started their shift on a specific day, the ViaRelation attribute can be used:

    public IEnumerable<Employee.IName> GetEmployeesWithStartDate(
	    [ViaRelation("Employee->WorkLog", "StartDate")] startDate);
    ...
    // at call site
    var employeesWorkingToday = 
	    employeeRepository.GetEmployeesWithStartDate(DateTime.Today);

*Note that the first table in the relational path (Employee->) must be the same as the target table specified in the return type. Also note that the columns that join the tables together are not neccessary when a default foreign key is detected.*

Relational filters can also be defined by inner classes:

    public class Employee 
    {
	    public class EmployeeStartDateFilter 
	    {
		    public WorkLog.StartDateFilter StartDateFilter { get; set; }
	    }
    }
    public class WorkLog 
    {
       ...
       public class StartDateFilter {
	       public DateTime StartDate { get; set; }
       }
    }
    ...
    public IEnumerable<Employee.IName> GetEmployeesWithStartDate(Employee.EmployeeStartDateFilter filter);
    ...
    // at call site
    var employeesWorkingToday = employeeRepository.GetEmployeesWithStartDate(
	    new Employee.EmployeeStartDateFilter() { 
		    StartDateFilter = new WorkLog.StartDateFilter() { 
			    StartDate = DateTime.Today 
		    }
	    });

This is functionally equivalent to the less verbose signature above, but has some advantages if many relational parameters or an object instance is preferred.

*Note that the nesting of properties is important, and must match the relationship of the tables in the database.*

#### Returning Relations

Returning related tables is supported:

    public class Employee 
    {
	    public interface IWithWorkLogs 
	    {
		    int Id { get; }
		    IEnumerable<WorkLog.IWorkLogWithStartDate> WorkLogs { get; }
	    }
    }
    public class WorkLog 
    {
	    public interface IWorkLogWithStartDate 
	    {
		    DateTime StartDate { get; set; }
	    }
    }
    ...
    public IEnumerable<Employee.IWithWorkLogs> GetWithWorkLogs();
    
*Note that the name of the property WorkLogs is not important. SigQL understands to use the WorkLog table because IWorkLogWithStartDate is an inner class of WorkLog.*

*Note also that joined rows are de-duplicated into a single instance based on their primary key.*

##### Flattening Relations with ViaRelation

The `[ViaRelation]` attribute can be used on output projection properties to flatten related table columns into a single result. Instead of returning nested objects, the related columns appear directly on the output type:

    public class Employee
    {
        public class EmployeeWithWorkLogFlattened
        {
            public int Id { get; set; }
            public string Name { get; set; }
            [ViaRelation("Employee->WorkLog", "StartDate")]
            public DateTime? StartDate { get; set; }
            [ViaRelation("Employee->WorkLog", "EndDate")]
            public DateTime? EndDate { get; set; }
        }
    }
    ...
    IEnumerable<Employee.EmployeeWithWorkLogFlattened> GetEmployeesWithWorkLogFlattened();

This generates a LEFT OUTER JOIN and returns one row per related record. For example, an Employee with two WorkLogs produces two result rows, each with the same Employee fields but different StartDate/EndDate values.

Both classes and interfaces are supported:

    public class Employee
    {
        public interface IEmployeeWithWorkLogFlattened
        {
            int Id { get; }
            string Name { get; }
            [ViaRelation("Employee->WorkLog", "StartDate")]
            DateTime? StartDate { get; }
            [ViaRelation("Employee->WorkLog", "EndDate")]
            DateTime? EndDate { get; }
        }
    }

Multi-hop relation paths are also supported:

    public class Employee
    {
        public class EmployeeWithAddressCityFlattened
        {
            public int Id { get; set; }
            public string Name { get; set; }
            [ViaRelation("Employee->EmployeeAddress->Address", "City")]
            public string City { get; set; }
        }
    }

*Note that ViaRelation properties should be nullable types when the relationship is optional, since a LEFT OUTER JOIN is used.*

##### JoinRelation Attribute

In cases where a manual join is desired, the [JoinRelation] attribute can be specified:

     public interface IWorkLogToView
        {
            int Id { get; }
            [JoinRelation("WorkLog(EmployeeId)->(EmployeeId)WorkLogEmployeeView")]
            WorkLogEmployeeView.IFields View { get; }
        }

The _path_ parameter specifies the relational path from one table to the next. Columns are enclosed in parenthesis, and adjoining arrows relate the specified columns together.

If a multi-tabled relational path is desired, columns can be specified on both sides of the adjoining table:

    [JoinRelation("WorkLog(EmployeeId)->(Id)Employee(Id)->(EmployeesId)AddressEmployee(AddressesId)->(Id)Address")]

##### Circular References

Data classes are often written with circular references. In a traditional ORM, the preceding example data classes are commonly defined as:

    public class Employee 
    {
	    public int Id { get; set;}
	    public string Name { get; set;}
	    public List<WorkLog> WorkLogs { get; set; }
    }
    public class WorkLog 
    {
	    public int Id { get; set; }
	    public DateTime StartDate { get; set; }
	    public Employee Employee { get; set; }
    }

In these cases, SigQL will only query and materialize properties that have not previously found in it's direct line of ancestors.

In the example above, if WorkLogs are queried:

    public IEnumerable<WorkLog> GetWorkLogs();

WorkLog.Employees will be queried and materialized, however, WorkLog.Employees.WorkLogs will return null, since the WorkLog property of Employee introduces a cycle in this branch.

Circular data is rarely desired as a model trait, and SigQL encourages projections to retrieve only necessary data for a particular use case. When projections are defined, it is extremely rare to find a use case where a circular reference is desired as opposed a specifically crafted shape.

#### Perspective

It is important to note the matter of perspective when writing a SigQL method. In a SELECT query, the perspective is defined by the return type.

For example, the same results can be retrieved from two different perspectives when a relation is involved:

    public IEnumerable<Employee.IWithWorkLogs> Get(string name);
 
If a WorkLog centered approach is desired:

    public class WorkLog 
    {
	    public interface IWithStartDateAndEmployees 
	    {
		    public DateTime StartDate { get; }
		    public IEnumerable<Employee.IName> Employees { get; }
	    }
    }
    ...
    public IEnumerable<WorkLog.IWithStartDateAndEmployees> Get([ViaRelation("WorkLog->Employee", "Name")] employeeName);

Both of these result in similar data being retrieved, but the orientation of the data is switched.

#### Count

Counting the total number of rows in a result set can be achieved by using the ICountResult\<T\> interface:

    ICountResult<WorkLog.IWorkLogId> CountWorkLogs(int employeeId);
	...
	var countResult = repository.CountWorkLogs(1);
	var numberOfItems = countResult.Count;

#### Views

Views are supported:

    // assuming the view in the database is named "vw_EmployeesWithWorkLogCount"
    public class vw_EmployeesWithWorkLogCount 
    {
	    public int EmployeeId { get; set; }
	    public int WorkLogCount { get; set; }
    }

And can be filtered, just like tables:

    // retrieve employees with more than specified number of WorkLogs
    IEnumerable<vw_EmployeesWithWorkLogCount> GetEmployeesWithWorkLogCount(
	    [GreaterThan] int workLogCount);

However, because the database does not have knowledge about the relationship between views and other objects, it currently cannot be joined to another result set (table, view, function, etc).

#### Inline Table-valued Functions

Inline Table-valued Functions are supported:

    public class itvf_EmployeesThatWorkedOnDate 
    {
	    public int EmployeeId { get; set; }
    }
    ...
    // this function defines @StartDate as a SQL input parameter
    IEnumerable<itvf_EmployeesThatWorkedOnDate> GetEmployees([Parameter] DateTime startDate);

And can be filtered:

    IEnumerable<itvf_EmployeesThatWorkedOnDate> GetEmployees([Parameter] DateTime startDate, IEnumerable<int> id);

### Feature Matrix

Since SigQL is still in active development, supported feature use can cause confusion. The below matrix defines three levels. The sections below details these levels.

**Parameters - (Level 0)**

The first place filter parameters can be defined are in arguments to a method. 

	IEnumerable<Employee.IId> GetPage(IEnumerable<int> id, [Offset] int offset, [Fetch] int fetch);

**Primary Class Properties - (Level 1)**

The next level is specifying a class filter. The below class represents the same parameters, but with parameter values organized into class properties instead of individual method arguments.

	public class Employee 
	{
		public class PageFilter 
		{
			public IEnumerable<int> Id { get; set; }
			[Offset]
			public int Offset { get; set; }
			[Fetch]
			public int Fetch { get; set; }
		}
	}
	...
	public interface IEmployeeRepository 
	{
		IEnumerable<Employee.IFields> GetPage(PageFilter filter);
	}

**Level 2 - Nested Class Properties**

The final level is specifying class filters for tables related to the primary table.

Similar to a primary class filter (Level 1), the StartDate filter property is valid.

However, while it may seem as if Offset/Fetch could limit the number of WorkLog records retrieved on an Employee, use of this feature at this level is invalid:

	public class Employee 
	{
		public class PageFilter 
		{
			public IEnumerable<int> Id { get; set; }
			public WorkLog.PageFilter WorkLogPageFilter { get; set; }
		}
	}
	public class WorkLog 
	{
		public class PageFilter 
		{
			[GreaterThan]
			public DateTime StartDate { get; set; }
			[Fetch]
			public int INVALID_Fetch { get; set; }
		}
	}
	...
	IEnumerable<Employee.IEmployeeWithWorkLogs> GetPage(Employee.PageFilter filter);
	...
	var invalid_employeesWith10WorkLogs = 
		employeeRepository.GetPage(
			new Employee.PageFilter() { 
				WorkLogPageFilter = new WorkLog.PageFilter() 
				{ 
					INVALID_Fetch = 10 
				} 
			};

#### Input Parameter Matrix

The below list documents all features applicable to input parameters (WHERE clause filters).

| Feature | Parameter (L0) | Class (L1) | Class Property (L2) |
|--|--|--|--|
| [ClrOnly]  | No | Yes | Yes |
| [Column] | Yes | Yes | Yes |
| [Fetch] | Yes | Yes | No |
| [GreaterThan] | Yes | Yes | Yes |
| [GreaterThanOrEqual] | Yes | Yes | Yes |
| [LessThan] | Yes | Yes | Yes |
| [LessThanOrEqual] | Yes | Yes | Yes |
| [IgnoreIfNull] | Yes | Yes | Yes |
| [IgnoreIfNullOrEmpty] | Yes | Yes | Yes |
| [StartsWith] | Yes | Yes | Yes |
| [Contains] | Yes | Yes | Yes |
| [EndsWith] | Yes | Yes | Yes |
| [Not] | Yes | Yes | Yes |
| [Offset] | Yes | Yes | No |
| [Parameter] | Yes | No (planned) | No |
| [Set] | Yes | Yes (can mix with filter properties) | No |
| [ViaRelation] | Yes| Yes | No |
| OrderByDirection | Yes | Yes | No |
| IOrderBy (Dynamic Order By) | Yes | Yes | No |
| IEnumerable<> (IN clause) | Yes | Yes | Yes |

#### Output Matrix

The below list documents all features applicable to return types. All other features are unsupported.

| Feature | Class (L1) | Class Property (L2) |
|--|--|--|--|
| [ClrOnly] | Yes | Yes |
| [Column] | Yes | Yes |
| [JoinRelation] | Yes | Yes |
| [ViaRelation] | Yes | Yes |

### Insert, Update, Upsert, Sync, and Delete

#### Insert

To insert an Employee by parameters only:

    [Insert(TableName = "Employee")]
    void Insert(string name);

By class:

    public class Employee 
    {
	    ...
	    public class Insert 
	    {
		    public string Name { get; set; }
	    }
    }
    ...
    [Insert]
    void Insert(Employee.Insert fields);

To return the inserted value:

    public class Employee 
    {
	    public interface IId 
	    {
		    int Id { get; }
	    }
    }
    ...
    [Insert]
    Employee.IId Insert(Employee.Insert employee);

Insert multiple and return values with any corresponding relations:

    [Insert]
    IEnumerable<Employee.IWithWorkLogs> Insert(IEnumerable<Employee.Insert> employees);

Insert with relations:

    public class InsertEmployeeWithWorkLogs
    {
            public string Name { get; set; }
            public IEnumerable<WorkLog> WorkLogs { get; set; }
    }

*Note that all relations will be inserted. If updating existing relations is desired, use the [Upsert] attribute*

#### UpdateByKey

UpdateByKey will update one or multiple rows, including relations, based on a provided primary key.

Update by parameters only:

    [UpdateByKey(TableName = nameof(Employee))]
    void UpdateEmployee(int id, string name);

Update by one or multiple classes and relations:

    [UpdateByKey]
    void UpdateEmployees(IEnumerable<Employee.UpdateWithWorkLogs> employeesWithWorkLogs);

*Note that all relations will be updated. If inserting new relations is desired, use the [Upsert] attribute*

##### Alternate Keys on UpdateByKey

By default, UpdateByKey matches rows using the table's primary key. The `KeyColumns` property allows matching by an alternate column instead:

    [UpdateByKey(TableName = nameof(WorkLog), KeyColumns = "StartDate")]
    void UpdateByKeyWorkLogByStartDate(IEnumerable<WorkLog.UpdateByKeyFieldsByStartDate> workLogs);

In this example, rows are matched by `StartDate` instead of `Id`.

##### IgnoreIfNull on UpdateByKey

`[IgnoreIfNull]` and `[IgnoreIfNullOrEmpty]` can be used on UpdateByKey fields to skip updating a column when the value is null (or empty). The existing database value is preserved:

    public class Employee
    {
        public class UpdateByKeyFieldsIgnoreIfNull
        {
            public int Id { get; set; }
            [IgnoreIfNull] public string Name { get; set; }
        }
    }
    ...
    [UpdateByKey(TableName = nameof(Employee))]
    void UpdateByKeyEmployees(IEnumerable<Employee.UpdateByKeyFieldsIgnoreIfNull> employees);

#### Upsert

Upsert will insert or update one or multiple rows, including relations, based on the existence of a primary key.

Upsert by parameters only:

    [Upsert(TableName = nameof(Employee))]
    Employee UpsertEmployee(int? id, string name);

Upsert by one or multiple classes and relations:

    [Upsert]
    IEnumerable<Employee> UpsertEmployees(IEnumerable<Employee.UpsertWithWorkLogs> employeesWithWorkLogs);

*Note that all relations will be inserted or updated. Removing items from a collection will not delete the row. To delete relations, use [Sync]*

##### Alternate Keys on Upsert

By default, Upsert determines whether to insert or update based on the table's primary key. The `KeyColumns` property allows matching by an alternate column instead:

    [Upsert(TableName = nameof(Employee), KeyColumns = "Name")]
    void UpsertEmployeeByName(IEnumerable<Employee.UpsertFieldsByName> employees);

In this example, if an Employee with the specified Name already exists, it will be updated. Otherwise, a new Employee will be inserted.

##### IgnoreIfNull on Upsert

`[IgnoreIfNull]` and `[IgnoreIfNullOrEmpty]` can be used on Upsert fields to skip updating a column when the value is null (or empty). When a row is being updated (not inserted), the existing database value is preserved:

    public class Employee
    {
        public class UpsertFieldsIgnoreIfNull
        {
            public int? Id { get; set; }
            [IgnoreIfNull] public string Name { get; set; }
        }
    }
    ...
    [Upsert(TableName = nameof(Employee))]
    void UpsertEmployees(IEnumerable<Employee.UpsertFieldsIgnoreIfNull> employees);

#### Sync

Sync is the same as Upsert, but it will dissassociate or delete orphaned relations.


    [Sync]
    IEnumerable<Employee> SyncEmployees(IEnumerable<Employee.UpsertWithWorkLogs> employeesWithWorkLogs);

In the above example:

 1. All fields of both Employee and WorkLog will be updated
 2. Any additional WorkLogs that are added will be inserted
 3. Any WorkLogs that no longer exist in the WorkLog collection will be deleted

Depending on the type of relationship to an adjacent table, the relationship will either be disassociated or deleted:

 - For *One-To-Many* relationships (such as WorkLogs, in the above example), the *Many* side of the relationship will be deleted.
 - For *Many-To-Many* relationships, the row in the Many-To-Many table will be deleted, but all other data will remain in-tact.
 - For *Many-To-One* relationships, the *One* side of the relationship will remain, but it will be disassociated from the *Many* side.
 - In all instances, no rows of the root table (Employee, in the above example) will be deleted.

##### Alternate Keys on Sync

By default, Sync determines whether to insert or update based on the table's primary key. The `KeyColumns` property allows matching by an alternate column:

    [Sync(KeyColumns = "Name")]
    void SyncEmployeeByNameWithWorkLogs(Employee.SyncFieldsByNameWithWorkLogs employees);

##### IgnoreIfNull on Sync

`[IgnoreIfNull]` and `[IgnoreIfNullOrEmpty]` can be used on Sync fields to skip updating a column when the value is null (or empty):

    public class Employee
    {
        public class SyncFieldsIgnoreIfNull
        {
            public int? Id { get; set; }
            [IgnoreIfNull] public string Name { get; set; }
        }
    }
    ...
    [Sync(TableName = nameof(Employee))]
    void SyncEmployees(Employee.SyncFieldsIgnoreIfNull employees);

#### Update

Update statements use the Set attribute to assign values:

    [Update(TableName = nameof(Employee))]
    void UpdateAll([Set] string name);

Filters work the same as queries:

	[Update(TableName = nameof(Employee))]
	void Update([Set] string name, int id);

	[Update(TableName = nameof(Employee))]
	void Update([Set] string name, 
		[ViaRelation("Employee->WorkLog", "StartDate"), GreaterThanOrEqual] DateTime startDate);

Complex objects can be passed:

		public class Employee
		{
			public class UpdateName 
			{
				public string Name { get; set; }
			}
		}
		...
		[Update(TableName = nameof(Employee))]
		void UpdateAll([Set] Employee.UpdateName values);
		
		[Update(TableName = nameof(Employee))]
		void Update([Set] Employee.UpdateName values, int id);
    
##### Mixed Set and Filter Properties

Set and filter properties can be combined in the same class. Properties decorated with `[Set]` will be used in the SET clause, while undecorated properties will be used as WHERE clause filters:

    public class WorkLog
    {
        public class SetDatesWithIdFilter
        {
            [Set]
            public DateTime StartDate { get; set; }
            [Set]
            public DateTime EndDate { get; set; }
            public int Id { get; set; }
        }
    }
    ...
    [Update(TableName = nameof(WorkLog))]
    void UpdateWorkLogDates(WorkLog.SetDatesWithIdFilter workLog);

Filter properties in a mixed class support the same attributes as standard query filters, such as `[IgnoreIfNull]`, `[OrGroup]`, and `[GreaterThan]`:

    public class WorkLog
    {
        public class SetDatesWithIgnoreIfNullFilter
        {
            [Set]
            public DateTime StartDate { get; set; }
            [Set]
            public DateTime EndDate { get; set; }
            [IgnoreIfNull]
            public int? EmployeeId { get; set; }
        }

        public class SetDatesWithOrFilter
        {
            [Set]
            public DateTime StartDate { get; set; }
            [Set]
            public DateTime EndDate { get; set; }
            [OrGroup]
            public int? EmployeeId { get; set; }
            [OrGroup]
            public int? LocationId { get; set; }
        }

        public class SetDatesWithGreaterThanFilter
        {
            [Set]
            public DateTime StartDate { get; set; }
            [Set]
            public DateTime EndDate { get; set; }
            [GreaterThan]
            public int Id { get; set; }
        }
    }

A mixed class can also be combined with additional scalar filter parameters:

    [Update(TableName = nameof(WorkLog))]
    void UpdateWorkLogDates(WorkLog.SetDatesWithIdFilter workLog, int employeeId);

##### IgnoreIfNull on Update

The `[IgnoreIfNull]` and `[IgnoreIfNullOrEmpty]` attributes can be used on `[Set]` properties to retain the original database value when the provided value is null (or empty). This is useful for partial updates where only some fields should be modified:

    [Update(TableName = nameof(Employee))]
    void UpdateEmployee([Set][IgnoreIfNull] string name, int id);

    [Update(TableName = nameof(Employee))]
    void UpdateEmployee([Set][IgnoreIfNullOrEmpty] string name, int id);

When used via a class:

    public class WorkLog
    {
        public class SetDatesIgnoreIfNullWithIdFilter
        {
            [Set, IgnoreIfNull]
            public DateTime? StartDate { get; set; }
            [Set, IgnoreIfNull]
            public DateTime? EndDate { get; set; }
            public int Id { get; set; }
        }
    }

When `name` is null (or empty for `IgnoreIfNullOrEmpty`), the existing database value is preserved rather than being overwritten.

Currently, update statements cannot return a return value:

    // this is not supported
    [Update(TableName = nameof(Employee))]
    IEnumerable<Employee.IId> UNSUPPORTED_UpdateAll([Set] Employee.UpdateName values);

*Note that updating relations is currently unsupported.*

#### Delete

Delete statements are executed via filter parameters:

    [Delete(TableName = nameof(Employee))]
    void Delete(int id);
    
    [Delete(TableName = nameof(Employee))]
    void Delete([ViaRelation("Employee->WorkLog", "StartDate"), GreaterThanOrEqual] DateTime startDate);

Currently, delete statements cannot return a return value. Nested relations also cannot be deleted via a [Delete] method.

### Custom SQL

Custom SQL is supported via the AdoMaterializer class (with an interface of IQueryMaterializer), which converts a result set to populated objects:

    var sqlExecutor = new SqlQueryExecutor(() => new SqlConnection(...));
    materializer = new AdoMaterializer(sqlExecutor);
    var result = materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
        "select Id from WorkLog where Id in (@id1, @id2)",
        new
        {
            id1 = 1,
            id2 = 4
        });

Note that the materializer expects each column to follow the naming convention of the class. For example, to select a WorkLog with an Employee property, adjacent columns need to be qualified with the property name via an alias:

    select Id, StartDate, Employee.Id "Employee.Id", Employee.Name "Employee.Name" 
    from WorkLog
    inner join WorkLog on WorkLog.EmployeeId = Employee.Id

Note that the WorkLog column names (Id, StartDate) are not qualified, because they are the primary table (determined by the output type IEnumerable<**WorkLog**.IWorkLogId>). 

However, Employee columns (Id, Name) are qualified because they tell the materializer that the joined table should be assigned to the property Employee.

This is also true of collection properties. Assume we have the following projection:

    public class Employee 
    {
	    public interface IEmployeeWithWorkLogs 
	    {
		    int Id { get; }
		    int Name { get; }
		    IEnumerable<WorkLog.IIdAndStartDate> AllWorkLogs { get; }
	    }
    }
    public class WorkLog 
    {
	    public interface IIdAndStartDate 
	    {
		    int Id { get; }
		    DateTime StartDate { get; }
	    }
    }

To materialize this projection, use the following SQL statement:

    select Id, Name, WorkLog.Id "AllWorkLogs.Id", WorkLog.StartDate "AllWorkLogs.StartDate"
    from Employee
    inner join WorkLog on WorkLog.EmployeeId = Employee.Id

Note that the WorkLog table is qualified with AllWorkLogs, because that is the property name that is defined in the IEmployeeWithWorkLogs projection.

#### Generating SELECT statements

When writing a custom query, it is possible to utilize the type system to generate a SELECT statement.

The SqlGenerator class provides two primary methods to customize queries beyond the *SELECT ... FROM ... JOIN* statements:

 1. **CreateSelectQuery:** This method takes a type as a parameter and returns the corresponding SQL query.
 2. **GetColumnNameResolver:** This method returns a function that transforms a specified class property property into a qualified column name.

As a basic example, suppose there is a need to add custom conditions to a query based on interface *WorkLog.IWorkLogId*:

    public IEnumerable<WorkLog.IWorkLogId> GetWorkLogs();

To generate the same SQL statement that SigQL would create for the above method, ***SqlGenerator.CreateSelectQuery*** can be utilized:

    materializer = new AdoMaterializer(...)
    var sqlGenerator = new SqlGenerator(sqlDatabaseConfiguration, DefaultPluralizationHelper.Instance);
    PreparedSqlStatement query = sqlGenerator.CreateSelectQuery(typeof(IEnumerable<WorkLog.IWorkLogId>>));
    var result = materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(query.CommandText);

In the above example, the value of query.CommandText would create a basic sql statement simlar to:

    select Id from WorkLog

If the desire was to further customize this query with additional conditions, the query can be extended by using ***SqlGenerator.GetColumnNameResolver***:

    var nameFor = sqlGenerator.GetColumnNameResolver<WorkLog.IWorkLogId>();   
    materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>($"{query.CommandText} where {nameFor(p => p.Id)} % 2 = 0");


In the above example, the *nameFor* method takes a lambda expression to resolve the fully qualified name of the column *Id* that is generated by SigQL. This produces SQL similar to:

    select Id from WorkLog where Id % 2 = 0

Note that when using *SqlGenerator.CreateSelectQuery*, only columns specified in the output type are able to be referenced in the query. For example, referencing a missing column/property in the query would throw a SQL syntax error at runtime:

    // SQL ERROR: Invalid column name 'name'.
    materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>($"{query.CommandText} where name = 'john'");

This is because SigQL generates necessary alias names so the materializer can bind the columns back to the class/interface at runtime, and the result is wrapped in a subquery.

### Installation

SigQL can be found on NuGet:

    install-package SigQL

Install the corresponding database package (currently only SQL server is supported):

    install-package SigQL.SqlServer

Create an interface or abstract class to define a SigQL data access method:

    public abstract class EmployeeRepository {
	    public abstract IEnumerable<Employee.IName> GetAll();
    }

While interfaces are commonly used for repositories, an abstract class may be more ergonomic, since they are more easily extended with method bodies when custom SQL calls are required.

Create an instance of RepositoryBuilder:

    var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(connectionString);
    var repositoryBuilder = new RepositoryBuilder(new SqlQueryExecutor(() => new SqlConnection(connectionString)), sqlDatabaseConfiguration);
    var employeeRepository = repositoryBuilder.Build<EmployeeRepository>();

For Dependency Injection in ASP.NET MVC Core:

    services.AddSingleton(s =>
    {
        var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(connectionString);
        return sqlDatabaseConfiguration;
    });
    services.AddSingleton(s =>
    {
        var sqlDatabaseConfiguration = s.GetService(typeof(SqlDatabaseConfiguration)) as SqlDatabaseConfiguration;
        var repositoryBuilder = new RepositoryBuilder(new SqlQueryExecutor(() => new SqlConnection(connectionString)), sqlDatabaseConfiguration);
        return repositoryBuilder;
    });
    services.AddSingleton(type, s =>
    {
        var repositoryBuilder = s.GetService(typeof(RepositoryBuilder)) as RepositoryBuilder;
        return repositoryBuilder.Build(typeof(EmployeeRepository), s.GetService);
    });

For a more generic approach, this code can be adapted to scan and register all interfaces and abstract classes in the same namespace as a specified repository class (in this case, *LocationRepository*):

        builder.Services.AddSingleton(s =>
    {
        var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(connectionString);
        return sqlDatabaseConfiguration;
    });
    builder.Services.AddSingleton(s =>
    {
        var sqlDatabaseConfiguration = s.GetService(typeof(SqlDatabaseConfiguration)) as SqlDatabaseConfiguration;
        var repositoryBuilder = new RepositoryBuilder(new SqlQueryExecutor(() => new SqlConnection(connectionString)), sqlDatabaseConfiguration);
        return repositoryBuilder;
    });
    var registeredInterfaces = new List<Type>();
    // register abstract repositories
    foreach (var type in Assembly.GetAssembly(typeof(LocationRepository)).GetTypes().Where(t => (t.Namespace == typeof(LocationRepository).Namespace && t.Name.EndsWith("Repository") && t.Name != "Repository")))
    {
        if ((type.IsAbstract && type.IsClass))
        {
            var interfaceType = type.GetInterfaces().SingleOrDefault(i => i.Name.EndsWith("Repository"));
            if (interfaceType != null)
            {
                builder.Services.AddScoped(interfaceType, s =>
                {
                    var repositoryBuilder = s.GetService(typeof(RepositoryBuilder)) as RepositoryBuilder;
                    return repositoryBuilder.Build(type, t => s.GetService(t));
                });
                registeredInterfaces.Add(interfaceType);            
            }
        }
    }
    
    // register interfaces without an implementation
    foreach (var type in Assembly.GetAssembly(typeof(LocationRepository)).GetTypes().Where(t => (t.Namespace == typeof(LocationRepository).Namespace && t.Name.EndsWith("Repository") && t.Name != "Repository")))
    {
        if (!registeredInterfaces.Contains(type))
        {
            builder.Services.AddScoped(type, s =>
            {
                var repositoryBuilder = s.GetService(typeof(RepositoryBuilder)) as RepositoryBuilder;
                return repositoryBuilder.Build(type, t => s.GetService(t));
            });
            registeredInterfaces.Add(type);
        }
    }

### Adding Foreign Keys

SigQL reads foreign key relationships from the database schema by default. However, in some cases you may need to add foreign key relationships programmatically (e.g. when the database does not define them, or when working with views or custom relationships).

Foreign keys can be added to a table using the `AddForeignKey` extension method on `ITableDefinition`:

    var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(connectionString);

    // single column foreign key
    var workLogTable = sqlDatabaseConfiguration.Tables.FindByName("WorkLog");
    var employeeTable = sqlDatabaseConfiguration.Tables.FindByName("Employee");

    workLogTable.AddForeignKey(
        t => t.Columns.FindByName("EmployeeId"),
        employeeTable.Columns.FindByName("Id"));

Multiple foreign keys can be chained:

    workLogTable
        .AddForeignKey(
            t => t.Columns.FindByName("EmployeeId"),
            employeeTable.Columns.FindByName("Id"))
        .AddForeignKey(
            t => t.Columns.FindByName("LocationId"),
            addressTable.Columns.FindByName("Id"));

Composite (multi-column) foreign keys are also supported:

    streetAddressCoordinateTable.AddForeignKey(
        t => new[]
        {
            t.Columns.FindByName("StreetAddress"),
            t.Columns.FindByName("City"),
            t.Columns.FindByName("State")
        },
        new[]
        {
            addressTable.Columns.FindByName("StreetAddress"),
            addressTable.Columns.FindByName("City"),
            addressTable.Columns.FindByName("State")
        });

### Logging

The RepositoryBuilder constructor contains a sqlLogger arg for logging SQL statements:

    public RepositoryBuilder(
            IQueryExecutor queryExecutor, 
            IDatabaseConfiguration databaseConfiguration, 
            Action<PreparedSqlStatement> sqlLogger = null)

This Action passes an instance of PreparedSqlStatement, which is defined with two properties:

    public class PreparedSqlStatement
    {
        public string CommandText { get; set; }
        public IDictionary<string, object> Parameters { get; set; }
    }

A logging method can log the command text, parameters, or both:

    // your code; the call site:
    var repositoryBuilder = new RepositoryBuilder(
    sqlQueryExecutor, 
    sqlDatabaseConfiguration, 
    statement => Log(statement) /* Log() is a method for you to define */);

### Design Principles

There are two major benefits to using projections over full data classes in queries:

 - Better performance by reducing the number of columns/joins
 - Reduced ambiguity at the call site. If the property is not defined in the signature, it is not selected. If the property is defined, all values accurate describe the database state (for example, developers can be sure a null property is NULL in the database, not simply an un-selected column).

For example, this code would not compile since Employee.IName does not have an Id property specified:

    var allEmployees = employeeRepository.GetAllNames();
    var firstEmployee = allEmployees.First();
    // the following line would fail, no Id property exists in Employee.IName
    Console.WriteLine("First Employee ID: " + firstEmployee.Id);

This forces the developer to either update the Employee.IName projection to include the Id column, or create a new projection.

Contrast this with other frameworks, such as Entity Framework:

    public class EmployeeRepository {
	    ...
	    public IEnumerable<Employee> GetAllNames() 
	    {
		    return dbContext.Employees.Select(e => new Employee() { Name = e.Name }).ToList();
	    }
	    ...
    }

While this achieves the goal of only selecting the Name column, it is not clear to the caller that the Id column was omitted, since the property still exists on the data class Employee. If a caller attempted to use the Id column returned, they would receive invalid data.

This can be solved in EF with a similar strategy of returning an interface, however, that can lead to further inconsistencies. For example, assuming Employee implements Employee.IName, there is nothing stopping the implementation from selecting all columns:

    public class EmployeeRepository {
	    
	    ...
	    
	    public IEnumerable<Employee.IName> GetAllNames() 
	    {
		    return dbContext.Employees.ToList();
	    }
	    
    }

Or worse, all relations:

    public class EmployeeRepository {
	    
	    ...
	    
	    public IEnumerable<Employee.IName> GetAllNames() 
	    {
		    return GetAll().ToList();
	    }
	    
	    ...
	    
	    // after a long development process, it's not uncommon
	    // to centralize retrieval
	    private IQueryable<Employee> GetAll() 
	    {
		    return dbContext.Employees.Include(e => e.WorkLogs);
	    }
	    
    }

One major problem with this code is that the method signature is lying to the caller. Imagine being tasked with a performance issue and examining the above signature while reviewing the code. Depending on how many Employees are in the database, it may not be obvious that there could be a performance issue with this particular method. 

While a projection-based approach can be achieved in other ORMs, the effort of doing it the right way is time consuming and cumbersome. SigQL aims to make doing the right thing the easy.

### Examples

See the [SigQLExamples](https://github.com/lynx44/SigQLExamples) repository for example usage.

### FAQS

**Is SigQL complete?**
No, in fact, it is experimental software. There are numerous features, optimizations, and bug fixes, both known and unknown.

**Which SQL databases is SigQL compatible with?**
Currently, only SQL Server is supported. It is designed to be extensible, however.

**My application throws a ConnectionFailureException when configuring SigQL.**

Your SQL user must have LOGIN privileges on master.

    -- on azure, do not use this first line. select the database from the drop down in SSMS instead
    USE master 
    CREATE USER [youruser]
    	FOR LOGIN [youruser]
    	WITH DEFAULT_SCHEMA = dbo
    GO

**In a one to many relationship, will the collection return null if no values are found?**
No, if no matching rows are found, the collection returned will be empty.

**Is SigQL performant?**
SigQL is designed to increase developer productivity by making it simple to select the exact set of data needed for the functionality being developed, which results in smaller and more precise result sets. However, because it is still in an experimental phase, no benchmarks have currently been run, and no specific performance goals are being targeted.

**Are schemas supported?**
Currently SigQL does not explicitly support schemas. It does not qualify objects with schema names. 

**Does SigQL support Stored Procedures/Grouping/Functions/Having/etc?**
Not currently. Consider using an Inline Table-valued Function, View, raw SQL, or a different ORM for these methods.

**I have a question about usage, where can I ask it?**
Please search this repository for issues, and open a ticket if your question is not addressed.

**I have an issue or think I found a bug, what should I do?**
First, check the documentation and the available test cases to validate that your use case is supported. If this does not resolve the problem, you may open a new issue. You may also debug the code by downloading the sources. If you believe you are able to fix the problem, please submit a pull request (and please include tests).

**How do I debug the library?**

In Visual Studio:

  1. Tools->Options->Debugging->General
  - Uncheck _Enable Just My Code_  
  - Check _Enable Source Server Support_
  - Check _Enable Source Link support_
  - Check _Suppress JIT optimization on module load (Managed only)_
  2. Tools->Options->Debugging->Symbols
  - Check _NuGet.org Symbol Server_
  - Under _Load only specific modules_, click _Specify included modules_
    - Add _SigQL*_
  3. To set a breakpoint:
   - Open Assembly Explorer
   - Expand SigQL
   - Find a suitable entry point and set the breakpoint
     - If you're unsure where to start, open _MethodParser_ and set a breakpoint at the beginning of the _SqlFor_ method

**Can SigQL make schema changes or migrate my database?**
No. However, it can be run in tandem with your preferred migration technology. Specifically, EF migrations have been tested to work alongside SigQL.

**Are enums supported?**
Yes, they are supported in return types, parameters and filter classes. Nullable enum properties (e.g. `MyEnum? Status`) on POCO and interface projections are also supported.

**What versions of .NET are targeted?**
.NET Standard 2 (Core) and .NET Framework 4.6.2/4.7.2

**I receive exception: System.ComponentModel.Win32Exception: The certificate chain was issued by an authority that is not trusted**
SigQL uses Microsoft.Data.SqlClient rather than System.Data.SqlClient, which contains a breaking change for connection strings. The simplest fix is to update your connection string to include _TrustServerCertificate=True;_.

