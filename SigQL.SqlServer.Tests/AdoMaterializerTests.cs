using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.SqlServer.Tests.Data;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.SqlServer.Tests
{
    [TestClass]
    public class AdoMaterializerTests
    {
        private IDbConnection laborDbConnection;
        private LaborDbContext laborDbContext;
        private List<PreparedSqlStatement> sqlStatements;
        private IQueryMaterializer materializer;
        private SelectClauseBuilder selectClauseBuilder;

        [TestInitialize]
        public void Setup()
        {
            laborDbConnection = TestSettings.LaborDbConnection;
            var sqlConnection = (laborDbConnection as SqlConnection);
            DatabaseHelpers.DropAllObjects(sqlConnection);

            this.laborDbContext = new LaborDbContext();
            laborDbContext.Database.Migrate();
            sqlStatements = new List<PreparedSqlStatement>();

            var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(sqlConnection.ConnectionString);
            var sqlExecutor = new SqlQueryExecutor(() => laborDbConnection);
            materializer = new AdoMaterializer(sqlExecutor, (s) => sqlStatements.Add(s));
            selectClauseBuilder = new SelectClauseBuilder(sqlDatabaseConfiguration, DefaultPluralizationHelper.Instance);
        }

        [TestMethod]
        public async Task Materialize_RawSql()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(IEnumerable<WorkLog.IWorkLogId>), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result as IEnumerable<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new [] {1, 2, 3, 4, 5}, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_RawSql_WithParameters()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(IEnumerable<WorkLog.IWorkLogId>), new PreparedSqlStatement(
                "select Id from WorkLog where Id in (@id1, @id2)", 
                new
                {
                    id1 = 1,
                    id2 = 4
                }));

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result as IEnumerable<WorkLog.IWorkLogId>;
            Assert.AreEqual(2, typedResult.Count());
            CollectionAssert.AreEqual(new [] {1, 4}, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_RawSql_GenericTypeArg()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });
            
            Assert.AreEqual(5, result.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_RawSql_CommandTextAndArgsOverload()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
                "select Id from WorkLog where Id in (@id1, @id2)",
                new
                {
                    id1 = 1,
                    id2 = 4
                });
            
            Assert.AreEqual(2, result.Count());
            CollectionAssert.AreEqual(new[] { 1, 4 }, result.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_RawSql_UsingSelectListBuilder()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
                $"{selectClauseBuilder.Build<IEnumerable<WorkLog.IWorkLogId>>().AsText} from WorkLog where Id in (@id1, @id2)",
                new
                {
                    id1 = 1,
                    id2 = 4
                });
            
            Assert.AreEqual(2, result.Count());
            CollectionAssert.AreEqual(new[] { 1, 4 }, result.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_IList()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(IList<WorkLog.IWorkLogId>), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });
            
            Assert.IsTrue(result is IList<WorkLog.IWorkLogId>);
            var typedResult = result as IList<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_List()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(List<WorkLog.IWorkLogId>), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });
            
            Assert.IsTrue(result is List<WorkLog.IWorkLogId>);
            var typedResult = result as List<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_Array()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(WorkLog.IWorkLogId[]), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });
            
            Assert.IsTrue(result is WorkLog.IWorkLogId[]);
            var typedResult = result as WorkLog.IWorkLogId[];
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_ReadOnlyCollection()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(ReadOnlyCollection<WorkLog.IWorkLogId>), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });
            
            Assert.IsTrue(result is ReadOnlyCollection<WorkLog.IWorkLogId>);
            var typedResult = result as ReadOnlyCollection<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_IReadOnlyCollection()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(IReadOnlyCollection<WorkLog.IWorkLogId>), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });
            
            Assert.IsTrue(result is IReadOnlyCollection<WorkLog.IWorkLogId>);
            var typedResult = result as IReadOnlyCollection<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_DictionaryArgument_ConvertNullToDbNull()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(IEnumerable<WorkLog.IWorkLogId>), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog where StartDate is null or StartDate=@startDate",
                Parameters = new Dictionary<string, object>()
                {
                    {"startDate", null}
                }
            });

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result as IEnumerable<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_DynamicArgument_ConvertNullToDbNull()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
                "select Id from WorkLog where StartDate is null or StartDate=@startDate",
                new
                {
                    startDate = (DateTime?) null
                });

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_NoParameters()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize(typeof(IEnumerable<WorkLog.IWorkLogId>),
                "select Id from WorkLog");

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result as IEnumerable<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_GenericType_NoParameters()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
                "select Id from WorkLog");

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_GenericType_DictionaryArgument()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
                "select Id from WorkLog where Id = @id", new Dictionary<string, object>()
                {
                    {"id", 2}
                });

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result;
            Assert.AreEqual(1, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 2 }, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public async Task Materialize_PreparedSqlStatement_SpecifyPrimaryKeys()
        {

            var addresses = Enumerable.Range(1, 3).Select(i => new EFAddress() { StreetAddress = "123 fake st" }).ToList();
            var employees = Enumerable.Range(1, 3).Select(i => new EFEmployee() { Name = "empname", Addresses = addresses }).ToList();

            this.laborDbContext.Employee.AddRange(employees);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<Employee.IEmployeeWithAddresses>>(new PreparedSqlStatement()
            {
                CommandText = @"select Employee.Id, Address.Id ""Addresses.Id"", Address.StreetAddress ""Addresses.StreetAddress"" from Employee 
                              inner join EFAddressEFEmployee on EFAddressEFEmployee.EmployeesId=Employee.Id
                              inner join Address on EFAddressEFEmployee.AddressesId=Address.Id",
                PrimaryKeyColumns = new [] { "Id", "Addresses.Id" }
            });
            
            var typedResult = result;
            Assert.AreEqual(3, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, typedResult.Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, typedResult.SelectMany(a => a.Addresses.Select(a => a.Id)).ToArray());
        }

        [TestMethod]
        public async Task Materialize_QueryWithEmptyDynamicParameters_SpecifyPrimaryKeys()
        {

            var addresses = Enumerable.Range(1, 3).Select(i => new EFAddress() { StreetAddress = "123 fake st" }).ToList();
            var employees = Enumerable.Range(1, 3).Select(i => new EFEmployee() { Name = "empname", Addresses = addresses }).ToList();

            this.laborDbContext.Employee.AddRange(employees);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<Employee.IEmployeeWithAddresses>>(
                @"select Employee.Id, Address.Id ""Addresses.Id"", Address.StreetAddress ""Addresses.StreetAddress"" from Employee 
                              inner join EFAddressEFEmployee on EFAddressEFEmployee.EmployeesId=Employee.Id
                              inner join Address on EFAddressEFEmployee.AddressesId=Address.Id",
                new {},
                new [] { "Id", "Addresses.Id" }
            );
            
            var typedResult = result;
            Assert.AreEqual(3, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, typedResult.Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, typedResult.SelectMany(a => a.Addresses.Select(a => a.Id)).ToArray());
        }

        [TestMethod]
        public async Task Materialize_QueryWithEmptyDictionaryParameters_SpecifyPrimaryKeys()
        {

            var addresses = Enumerable.Range(1, 3).Select(i => new EFAddress() { StreetAddress = "123 fake st" }).ToList();
            var employees = Enumerable.Range(1, 3).Select(i => new EFEmployee() { Name = "empname", Addresses = addresses }).ToList();

            this.laborDbContext.Employee.AddRange(employees);
            this.laborDbContext.SaveChanges();

            var result = await materializer.Materialize<IEnumerable<Employee.IEmployeeWithAddresses>>(
                @"select Employee.Id, Address.Id ""Addresses.Id"", Address.StreetAddress ""Addresses.StreetAddress"" from Employee 
                              inner join EFAddressEFEmployee on EFAddressEFEmployee.EmployeesId=Employee.Id
                              inner join Address on EFAddressEFEmployee.AddressesId=Address.Id",
                new Dictionary<string, object>(),
                new [] { "Id", "Addresses.Id" }
            );
            
            var typedResult = result;
            Assert.AreEqual(3, typedResult.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, typedResult.Select(e => e.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, typedResult.SelectMany(a => a.Addresses.Select(a => a.Id)).ToArray());
        }
    }
}
