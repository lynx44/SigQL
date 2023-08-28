using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Tests.Infrastructure;

namespace SigQL.Tests
{
    [TestClass]
    public class SqlGeneratorTests
    {
        private SqlGenerator sqlGenerator;

        [TestInitialize]
        public void Setup()
        {
            var workLogDatabaseConfigurator = new MockWorkLogDatabaseConfigurator();
            sqlGenerator = new SqlGenerator(workLogDatabaseConfigurator.WorkLogDatabaseConfiguration, DefaultPluralizationHelper.Instance);
        }

        [TestMethod]
        public void CreateSelectQuery_GeneratesExpectedSql()
        {
            var query = sqlGenerator.CreateSelectQuery(typeof(WorkLog.IWorkLogWithEmployee));
            Assert.AreEqual("select * from (select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))) __generatedouterquery ",
                query.CommandText);
        }

        [TestMethod]
        public void CreateSelectQuery_GeneratesExpectedPrimaryKeyCollection()
        {
            var query = sqlGenerator.CreateSelectQuery(typeof(WorkLog.IWorkLogWithEmployee));
            Assert.AreEqual(2,
                query.PrimaryKeyColumns.ToGroup().Count);
            Assert.AreEqual("Id",
                query.PrimaryKeyColumns.ToGroup()[""].Single());
            Assert.AreEqual("Id",
                query.PrimaryKeyColumns.ToGroup()["Employee"].Single());
        }
    }
}
