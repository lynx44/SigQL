using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Exceptions;
using SigQL.SqlServer.Tests.Data;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Types.Attributes;

namespace SigQL.SqlServer.Tests
{
    /// <summary>
    /// Repository members that supply their own implementation, run against a real database.
    /// </summary>
    [TestClass]
    public class CustomImplementationTests
    {
        private IDbConnection laborDbConnection;
        private LaborDbContext laborDbContext;
        private RepositoryBuilder repositoryBuilder;
        private IQueryMaterializer materializer;

        [TestInitialize]
        public void Setup()
        {
            laborDbConnection = TestSettings.LaborDbConnection;
            DatabaseHelpers.DropAllObjects(laborDbConnection as SqlConnection);

            laborDbContext = new LaborDbContext();
            laborDbContext.Database.Migrate();

            var sqlConnection = laborDbConnection as SqlConnection;
            var queryExecutor = new SqlQueryExecutor(() => laborDbConnection);
            materializer = new AdoMaterializer(queryExecutor);

            var options = new RepositoryBuilderOptions()
            {
                ServiceResolver = serviceType =>
                {
                    if (serviceType == typeof(IQueryMaterializer)) return materializer;
                    if (serviceType == typeof(IWorkLogSummarizer)) return new WorkLogSummarizer();
                    return null;
                }
            };

            repositoryBuilder = new RepositoryBuilder(queryExecutor,
                new SqlDatabaseConfiguration(sqlConnection.ConnectionString), materializer, options);
        }

        private IList<EFWorkLog> InsertWorkLogs(int count)
        {
            var workLogs = Enumerable.Range(1, count).Select(i => new EFWorkLog() { }).ToList();
            laborDbContext.WorkLog.AddRange(workLogs);
            laborDbContext.SaveChanges();
            return workLogs;
        }

        [TestMethod]
        public void CustomMethod_ComposesGeneratedQuery()
        {
            InsertWorkLogs(5);

            var repository = repositoryBuilder.Build<ICustomImplementationRepository>();

            Assert.AreEqual(5, repository.CountAllIds());
        }

        [TestMethod]
        public void GeneratedMethod_StillWorksAlongsideCustomOnes()
        {
            var expected = InsertWorkLogs(5);

            var repository = repositoryBuilder.Build<ICustomImplementationRepository>();

            CollectionAssert.AreEquivalent(
                expected.Select(w => w.Id).ToList(),
                repository.GetAllIds().Select(w => w.Id).ToList());
        }

        [TestMethod]
        public void InjectedParameter_IsSuppliedWithoutTheCallerPassingIt()
        {
            InsertWorkLogs(3);

            var repository = repositoryBuilder.Build<ICustomImplementationRepository>();

            Assert.AreEqual("summarized 3", repository.SummarizeAll());
        }

        [TestMethod]
        public void InjectedProperty_IsSuppliedToCustomBody()
        {
            InsertWorkLogs(2);

            var repository = repositoryBuilder.Build<ICustomImplementationRepository>();

            Assert.AreEqual("summarized 2", repository.SummarizeViaProperty());
        }

        [TestMethod]
        public void CustomMethod_RunsAOneOffRawQuery()
        {
            var workLogs = InsertWorkLogs(5);
            var thirdId = workLogs.Select(w => w.Id).OrderBy(id => id).ElementAt(2);

            var repository = repositoryBuilder.Build<IRawQueryRepository>();

            var expected = workLogs.Select(w => w.Id).Where(id => id > thirdId).ToList();
            var actual = repository.GetIdsAbove(thirdId).Select(w => w.Id).ToList();

            CollectionAssert.AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void CustomMethod_AndGeneratedMethod_ShareTheSameRepository()
        {
            InsertWorkLogs(5);

            var repository = repositoryBuilder.Build<IRawQueryRepository>();

            Assert.AreEqual(5, repository.GetAllIds().Count());
            Assert.AreEqual(5, repository.GetIdsAbove(0).Count());
        }

        [TestMethod]
        public void AbstractClass_VirtualMethodWithBody_IsNotParsedAsSql()
        {
            InsertWorkLogs(4);

            var repository = repositoryBuilder.Build<CustomImplementationAbstractRepository>();

            Assert.AreEqual(4, repository.CountAllIds());
            Assert.AreEqual("summarized 4", repository.SummarizeAll());
        }

        [TestMethod]
        public void InjectedParameter_WithoutDefaultValue_ThrowsDescriptiveException()
        {
            var repository = repositoryBuilder.Build<IInvalidInjectRepository>();

            var exception = Assert.ThrowsException<InvalidAttributeException>(
                () => repository.SummarizeAll(new WorkLogSummarizer()));

            Assert.AreEqual(typeof(InjectAttribute), exception.AttributeType);
        }
    }
}
