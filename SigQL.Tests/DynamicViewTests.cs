using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.Tests
{
    [TestClass]
    public class DynamicViewTests
    {
        private IDynamicViewFactory dynamicViewFactory;

        [TestInitialize]
        public void Setup()
        {
            dynamicViewFactory = new DynamicViewFactory();
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
    }
    
    public class DynamicViewRepo
    {
        private IDynamicViewFactory dynamicViewFactory;
        public IEnumerable<WorkLog.IWorkLogId> GetWorkLogsDynamicView(IEnumerable<int> id)
        {
            var view = dynamicViewFactory.Create<IEnumerable<WorkLog.IWorkLogId>>("select * from worklog");
            return view;
        }
    }
}
