using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

        [TestMethod]
        public void ColumnNameResolver_QuotedQualified_WhenDepthOfOne_GeneratesExpectedQualifiedName()
        {
            var nameFor = sqlGenerator.GetColumnNameResolver<WorkLog.IWorkLogWithEmployee>();
            Assert.AreEqual("\"Employee\"", nameFor(p => p.Employee));
        }

        [TestMethod]
        public void ColumnNameResolver_QuotedQualified_WhenDepthOfTwo_GeneratesExpectedQualifiedName()
        {
            var nameFor = sqlGenerator.GetColumnNameResolver<WorkLog.IWorkLogWithEmployee>();
            Assert.AreEqual("\"Employee.Name\"", nameFor(p => p.Employee.Name));
        }

        [TestMethod]
        public void ColumnNameResolver_QuotedQualified_WithCollectionUsingFirst_GeneratesExpectedQualifiedName()
        {
            var nameFor = sqlGenerator.GetColumnNameResolver<WorkLog.IWorkLogWithEmployeeWithAddress>();
            Assert.AreEqual("\"Employee.Addresses.StreetAddress\"", nameFor(p => p.Employee.Addresses.First().StreetAddress));
        }

        [TestMethod]
        public void ColumnNameResolver_NonQuotedQualified_WithCollectionUsingFirst_GeneratesExpectedQualifiedName()
        {
            var nameFor = sqlGenerator.GetColumnNameResolver<WorkLog.IWorkLogWithEmployeeWithAddress>(false);
            Assert.AreEqual("Employee.Addresses.StreetAddress", nameFor(p => p.Employee.Addresses.First().StreetAddress));
        }

        // NOT IMPLEMENTED
        // this would allow the ability to call .Select
        //[TestMethod]
        //public void ColumnNameResolver_WithCollectionUsingSelect_GeneratesExpectedQualifiedName()
        //{
        //    var nameFor = sqlGenerator.GetColumnNameResolver<WorkLog.IWorkLogWithEmployeeWithAddress>();
        //    Assert.AreEqual("Employee.Addresses.StreetAddress", nameFor(p => p.Employee.Addresses.Select(a => a.StreetAddress)));
        //}
    }
}
