using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.Tests
{
    [TestClass]
    public class DynamicViewTests
    {
        private IDynamicViewFactory dynamicViewFactory;
        private List<PreparedSqlStatement> sqlStatements;
        private RepositoryBuilder repositoryBuilder;
        private DynamicViewRepository repository;

        [TestInitialize]
        public void Setup()
        {
            dynamicViewFactory = new DynamicViewFactory();
            var queryExecutorMock = new Mock<IQueryExecutor>();
            var dataReaderMock = new Mock<IDataReader>();
            queryExecutorMock.Setup(s => s.ExecuteReader(It.IsAny<string>(), It.IsAny<IDictionary<string, object>>())).Returns(dataReaderMock.Object);
            var databaseConfigurationMock = new Mock<IDatabaseConfiguration>();
            databaseConfigurationMock.Setup(c => c.Tables)
                .Returns(new TableDefinitionCollection(new List<ITableDefinition>()));
            sqlStatements = new List<PreparedSqlStatement>();
            repositoryBuilder = new RepositoryBuilder(queryExecutorMock.Object, databaseConfigurationMock.Object,
                p => sqlStatements.Add(p));
            repository = repositoryBuilder.Build<DynamicViewRepository>();
        }

        [TestMethod]
        public void As_CastsInterfaceToDynamicView()
        {
            var sql = "select * from worklog";
            var returnValue = dynamicViewFactory.Create<IEnumerable<WorkLog.IWorkLogId>>(sql);

            var dynamicView = (IDynamicView) returnValue;
            Assert.AreEqual(sql, dynamicView.Sql);
        }

        [TestMethod]
        public void As_CastsClassToDynamicView()
        {
            var sql = "select * from worklog";
            var returnValue = dynamicViewFactory.Create<List<WorkLog>>(sql);

            var dynamicView = (IDynamicView) returnValue;
            Assert.AreEqual(sql, dynamicView.Sql);
        }

        [TestMethod]
        public void GeneratesExpectedBaseSql()
        {
            var result = repository.GetWorkLogs();

            var sqlStatement = AssertSingleSqlStatement();
            Assert.AreEqual("select * from (select * from worklog) \"DynamicView\"", sqlStatement.CommandText);
        }

        [TestMethod]
        public void GeneratesExpectedParameterSql()
        {
            var result = repository.GetWorkLogsWithParameters(new [] { 1, 2 });

            var sqlStatement = AssertSingleSqlStatement();
            Assert.AreEqual("select * from (select * from worklog) \"DynamicView\" where id in (@id1, @id2)", sqlStatement.CommandText);
        }

        private PreparedSqlStatement AssertSingleSqlStatement()
        {
            Assert.AreEqual(1, this.sqlStatements.Count);
            return sqlStatements.Single();
        }
    }
    
    public class DynamicViewRepository
    {
        private IDynamicViewFactory dynamicViewFactory;

        public DynamicViewRepository()
        {
            this.dynamicViewFactory = new DynamicViewFactory();
        }

        public virtual IEnumerable<WorkLog.IWorkLogId> GetWorkLogs()
        {
            var view = dynamicViewFactory.Create<IEnumerable<WorkLog.IWorkLogId>>("select * from worklog");
            return view;
        }

        public virtual IEnumerable<WorkLog.IWorkLogId> GetWorkLogsWithParameters(IEnumerable<int> id)
        {
            var view = dynamicViewFactory.Create<IEnumerable<WorkLog.IWorkLogId>>("select * from worklog");
            return view;
        }
    }
}
