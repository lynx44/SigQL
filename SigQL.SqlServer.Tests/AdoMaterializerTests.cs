using System.Collections.Generic;
using System.Data;
using System.Linq;
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

            var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(sqlConnection.DataSource, sqlConnection.Database);
            var sqlExecutor = new SqlQueryExecutor(() => laborDbConnection);
            materializer = new AdoMaterializer(sqlExecutor, (s) => sqlStatements.Add(s));
            selectClauseBuilder = new SelectClauseBuilder(sqlDatabaseConfiguration);
        }

        [TestMethod]
        public void Materialize_RawSql()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = materializer.Materialize(typeof(IEnumerable<WorkLog.IWorkLogId>), new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });

            Assert.IsTrue(result is IEnumerable<WorkLog.IWorkLogId>);
            var typedResult = result as IEnumerable<WorkLog.IWorkLogId>;
            Assert.AreEqual(5, typedResult.Count());
            CollectionAssert.AreEqual(new [] {1, 2, 3, 4, 5}, typedResult.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public void Materialize_RawSql_WithParameters()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = materializer.Materialize(typeof(IEnumerable<WorkLog.IWorkLogId>), new PreparedSqlStatement(
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
        public void Materialize_RawSql_GenericTypeArg()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(new PreparedSqlStatement()
            {
                CommandText = "select Id from WorkLog"
            });
            
            Assert.AreEqual(5, result.Count());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, result.Select(wl => wl.Id).ToArray());
        }

        [TestMethod]
        public void Materialize_RawSql_CommandTextAndArgsOverload()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
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
        public void Materialize_RawSql_UsingSelectListBuilder()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var result = materializer.Materialize<IEnumerable<WorkLog.IWorkLogId>>(
                $"{selectClauseBuilder.Build<IEnumerable<WorkLog.IWorkLogId>>().AsText} from WorkLog where Id in (@id1, @id2)",
                new
                {
                    id1 = 1,
                    id2 = 4
                });
            
            Assert.AreEqual(2, result.Count());
            CollectionAssert.AreEqual(new[] { 1, 4 }, result.Select(wl => wl.Id).ToArray());
        }
    }
}
