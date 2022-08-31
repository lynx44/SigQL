

# SigQL

SigQL is a .NET ORM that uses code signatures as the query language. Defining only a method signature, the SQL query and object materialization is handled automatically without writing an implementation.

    public interface IRepository 
    {
	    // retrieve a record from the WorkLog table without writing the implementation
	    public WorkLog Get(int id);
    }

The goal of SigQL is to enable developers quick and concise access to data by merging SQL directives into method signatures. It is capable of covering the most common use cases, and is not a full featured ORM.

# Contents

**Basic Usage**

 - [SELECT Queries](#select-queries) 
 - [Projections](#projections) 
   - [POCOs](#pocos)
   - [Ignoring Properties](#ignoring-properties)
 - [WHERE Clause](#where-clause) 
   - [Parameter Alias](#parameter-alias)
   - [Logical Operators](#logical-operators)
   - [In Clause](#in-clause)
   - [Ignoring null or empty parameters](#ignoring-null-or-empty-parameters)
 - [Offset and Fetch](#offset-and-fetch)
 - [Order By](#order-by)
 - [Single Result](#single-result)
 - [Collection Results](#collection-results)
 - [Filtering by related tables](#filtering-by-related-tables)
 - [Returning Relations](#returning-relations)
   - [Circular References](#circular-references)
 - [Perspective](#perspective)
 - [Count](#count)
 - [Views](#views)
 - [Inline Table-valued Functions](#inline-table-valued-functions)
 - [Feature Matrix](#feature-matrix)
   - [Input Matrix](#input-parameter-matrix)
   - [Output Matrix](#output-matrix)

**Insert, Update, and Delete**

 - [Insert](#insert)
 - [Update](#update)
 - [Delete](#delete)
 
**Custom SQL**

 - [Custom SQL](#custom-sql)
   - [Generating SELECT statements](#generating-select-statements)
   - [Mixing custom SQL with SigQL method signatures](#mixing-custom-sql-with-sigql-method-signatures)
  
  **Configuration**
  
 - [Installation](#installation)
 - [Logging](#logging)

**More Information**
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

#### Projections

While full table output is technically supported, it is highly discouraged. One of the primary goals of SigQL is to retrieve only the data that is needed, which is more performant and reduces ambiguity during development. Projections are defined as inner types under a class with the same name as the target table:

    // targeting table Employee
    public class Employee {
	    // ... Optional: Employee columns can be defined here
	    
	    // this projection selects only the Name column
	    public interface IName 
	    {
		    string Name { get; }
	    }
    }
    
	public interface IEmployeeRepository {
 	    IEnumerable<Employee.IName> GetAllNames();
	}

*Note that projections do NOT need to be defined within a canonical data class. Projections inside any class with a matching a table name will instruct SigQL to use the aforementioned table.*

There are two major benefits to this approach:

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

##### POCOs

Plain Old CLR Objects (POCOs) are supported as projections:

    public class Employee 
    {
    
	    public class NameProjection 
	    {
		    public string Name { get; set; }
	    }
	    
    }

*Note that get and set properties need to be public*

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

*Note that the name of the method is irrelevant. SigQL uses the parameters and return type to generate the query.*

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

    IEnumerable<Employee.IName> GetWithNames(IEnumerable<string> name);

*Note that the parameter is still named name rather than names. Pluralization is not currently supported*

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

#### Offset and Fetch

Offset and Fetch are also supported, which is often used for paging:

    IEnumerable<Employee.IName> Search(
	    [IgnoreIfNullOrEmpty] IEnumerable<int> id, 
	    [IgnoreIfNull, StartsWith] string name, 
	    [Offset] int offset, 
	    [Fetch] int);

#### Order By

Sort direction can also be specified via parameters:

    IEnumerable<Employee.IName> Search(
	    [IgnoreIfNullOrEmpty] IEnumerable<int> id, 
	    [IgnoreIfNull, StartsWith, Column("Name")] string nameFilter, 
	    [Offset] int offset, 
	    [Fetch] int, 
	    OrderByDirection name = OrderByDirection.Ascending);

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
	    [ViaRelation("Employee->WorkLog.StartDate")] startDate);
    ...
    // at call site
    var employeesWorkingToday = 
	    employeeRepository.GetEmployeesWithStartDate(DateTime.Today);

*Note that the first table in the relational path (Employee->) must be the same as the target table specified in the return type. Also note that the columns that join the tables together are not specified, they are automatically resolved.*

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
    public IEnumerable<WorkLog.IWithStartDateAndEmployees> Get([ViaRelation("WorkLog->Employee.Name")] employeeName);

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
| [Parameter] | Yes| No (planned) | No |
| [Set] | Yes| Yes (cannot mix with filter properties) | No |
| [ViaRelation] | Yes| No (planned) | No |
| OrderByDirection | Yes | No (planned) | No |
| IEnumerable<> (IN clause) | Yes | Yes | Yes |

#### Output Matrix

The below list documents all features applicable to return types. All other features are unsupported.

| Feature | Class (L1) | Class Property (L2) |
|--|--|--|--|
| [ClrOnly] | Yes | Yes |
| [Column] | No (planned) | No (planned) |
| [ViaRelation] | No (planned) | No (planned) |

### Insert, Update, and Delete

Modifying data in SigQL currently supports basic functionality.

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

*Note that inserting relations is currently unsupported.*

#### Update

Update statements use the Set attribute to assign values:

    [Update(TableName = nameof(Employee))]
    void UpdateAll([Set] string name);

Filters work the same as queries:

	[Update(TableName = nameof(Employee))]
	void Update([Set] string name, int id);

	[Update(TableName = nameof(Employee))]
	void Update([Set] string name, 
		[ViaRelation("Employee->WorkLog.StartDate"), GreaterThanOrEqual] DateTime startDate);

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
    
However, Set and and query filters currently cannot be combined in the same class (although this feature is planned):

    public class Employee
    {
			public class INVALID_UpdateName 
			{
				[Set] public string Name { get; set; }
				public int Id { get; set; }
			}
		}

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
    void Delete([ViaRelation("Employee->WorkLog.StartDate"), GreaterThanOrEqual] DateTime startDate);

Currently, delete statements cannot return a return value.

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

Column selection ergonomics are a core principle of SigQL. When writing a custom query, it is possible to select only the columns specified by the expected output type:

    // see installation section for details. this should be a singleton
    var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(...); 
    var selectClauseBuilder = new SelectClauseBuilder(sqlDatabaseConfiguration);
    var result = materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
	    $"{selectClauseBuilder.Build<IEnumerable<WorkLog.IWorkLogId>>().AsText} from WorkLog where Id in (@id1, @id2)",
	    new
	    {
	        id1 = 1,
	        id2 = 4
	    });

*Note that the SelectClauseBuilder assumes all tables will use their database object names; using table aliases will generally cause a syntax error.*

#### Mixing custom SQL with SigQL method signatures

When mixing custom SQL with SigQL method signatures, the simplest implementation is to place all code in an abstract class:

    public abstract class WorkLogRepository 
    {
	    private SelectClauseBuider selectClauseBuilder;
	    private IQueryMaterializer queryMaterializer;
	    
	    // Dependency Injection is supported
	    public WorkLogRepository(SelectClauseBuilder selectClauseBuilder, IQueryMaterializer queryMaterializer) 
	    {
		    this.selectClauseBuilder = selectClauseBuilder;
		    this.queryMaterializer = queryMaterializer;
	    }
	    
	    // this will be constructed by SigQL 
	    public abstract IEnumerable<WorkLog.IIdAndStartDate> GetAllWithStartDate();
 	    
 	    public IEnumerable<WorkLog.IWorkLogId> GetWorkLogsWithIds(int id1, int id2) 
 	    {
	 	    return this.queryMaterializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
		    $"{this.selectClauseBuilder.Build<IEnumerable<WorkLog.IWorkLogId>>().AsText} from WorkLog where Id in (@id1, @id2)",
		    new
		    {
		        id1,
		        id2
	        });
 	    }
    }

### Installation

SigQL can be found on NuGet:

    install-package SigQL

Install the corresponding database package (currently only SQL server is supported):

    install-package SigQL.SqlServer

***IMPORTANT:** Note that the SQL User you define in your connection string must have LOGIN privileges on master. This is currently limitation based on the choice to use SQL Management Objects to retrieve schema information, and likely will be replaced at some point in the future.*

Create an interface or abstract class to define a SigQL data access method:

    public interface IEmployeeRepository {
	    IEnumerable<Employee.IName> GetAll();
    }

Create an instance of RepositoryBuilder:

    var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(connectionString);
    var repositoryBuilder = new RepositoryBuilder(new SqlQueryExecutor(() => new SqlConnection(connectionString)), sqlDatabaseConfiguration);
    var employeeRepository = repositoryBuilder.Build<IEmployeeRepository>();

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
        return repositoryBuilder.Build(typeof(IEmployeeRepository), s.GetService);
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

**Can SigQL make schema changes or migrate my database?**
No. However, it can be run in tandem with your preferred migration technology. Specifically, EF migrations have been tested to work alongside SigQL.

**Are enums supported?**
Yes, they are supported in return types, parameters and filter classes.

**What versions of .NET are targeted?**
.NET Standard 2 (Core) and .NET Framework 4.6.2/4.7.2
