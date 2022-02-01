using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.Tests
{
    [TestClass]
    public class RowProjectionBuilderTests
    {
        private RowProjectionBuilder builder;

        [TestInitialize]
        public void Setup()
        {
            builder = new RowProjectionBuilder();
        }

        [TestMethod]
        public void Build_ReturnsExpectedBaseProperties_TypeInt()
        {
            var actual = builder.Build(typeof(WorkLog.IWorkLogId), new RowValues() { Values = new Dictionary<string, object>() {{nameof(WorkLog.IWorkLogId.Id), 1}}}) as WorkLog.IWorkLogId;

            Assert.AreEqual(1, actual.Id);
        }

        [TestMethod]
        public void Build_Poco()
        {
            var actual = builder.Build(typeof(WorkLog.WorkLogIdPoco), new RowValues() { Values = new Dictionary<string, object>() {{nameof(WorkLog.IWorkLogId.Id), 1}}}) as WorkLog.WorkLogIdPoco;

            Assert.AreEqual(1, actual.Id);
        }

        [TestMethod]
        public void Build_Poco_IgnoresUnknownProperty()
        {
            var actual = builder.Build(typeof(WorkLog.WorkLogIdPocoWithExtraProperty), new RowValues() { Values = new Dictionary<string, object>() {{nameof(WorkLog.IWorkLogId.Id), 1}}}) as WorkLog.WorkLogIdPocoWithExtraProperty;

            Assert.AreEqual(1, actual.Id);
            Assert.AreEqual(null, actual.PayRateExtra);
        }

        [TestMethod]
        public void Build_Poco_IgnoresUnknownGetOnlyProperty()
        {
            var actual = builder.Build(typeof(WorkLog.WorkLogIdPocoWithClrOnlyProperty), new RowValues() { Values = new Dictionary<string, object>() {{nameof(WorkLog.IWorkLogId.Id), 1}}}) as WorkLog.WorkLogIdPocoWithClrOnlyProperty;

            Assert.AreEqual(1, actual.Id);
            Assert.AreEqual("example1", actual.ClrOnlyProperty);
        }
    }
}
