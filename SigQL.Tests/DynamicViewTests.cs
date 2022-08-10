﻿using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.Tests
{
    [TestClass]
    public class DynamicViewTests
    {
        [TestMethod]
        public void As_CastsInterfaceToDynamicView()
        {
            var sql = "select * from worklog";
            var returnValue = new DynamicView(sql).As<IEnumerable<WorkLog.IWorkLogId>>();

            var dynamicView = (IDynamicView) returnValue;
            Assert.AreEqual(sql, dynamicView.Sql);
        }

        [TestMethod]
        public void As_CastsClassToDynamicView()
        {
            var sql = "select * from worklog";
            var returnValue = new DynamicView(sql).As<List<WorkLog>>();

            var dynamicView = (IDynamicView) returnValue;
            Assert.AreEqual(sql, dynamicView.Sql);
        }
    }
    
    public class DynamicViewRepo
    {
        public IEnumerable<WorkLog.IWorkLogId> GetWorkLogsDynamicView(IEnumerable<int> id)
        {
            var view = new DynamicView("select * from worklog").As<IEnumerable<WorkLog.IWorkLogId>>();
            return view;
        }
    }
}
