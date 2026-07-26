using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Exceptions;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.Tests
{
    [TestClass]
    public class OutputFactoryTests
    {
        private List<object> rows;

        [TestInitialize]
        public void Setup()
        {
            rows = new List<object>() { new Employee.EmployeeIdImpl() { Id = 1 }, new Employee.EmployeeIdImpl() { Id = 2 } };
        }

        [TestMethod]
        public void Cast_ToIEnumerable()
        {
            var actual = OutputFactory.Cast(rows, typeof(IEnumerable<Employee.IEmployeeId>));

            Assert.IsInstanceOfType(actual, typeof(IEnumerable<Employee.IEmployeeId>));
            Assert.AreEqual(2, ((IEnumerable<Employee.IEmployeeId>) actual).Count());
        }

        [TestMethod]
        public void Cast_ToList()
        {
            var actual = OutputFactory.Cast(rows, typeof(List<Employee.IEmployeeId>));

            Assert.IsInstanceOfType(actual, typeof(List<Employee.IEmployeeId>));
        }

        [TestMethod]
        public void Cast_ToIList()
        {
            var actual = OutputFactory.Cast(rows, typeof(IList<Employee.IEmployeeId>));

            Assert.IsInstanceOfType(actual, typeof(IList<Employee.IEmployeeId>));
        }

        [TestMethod]
        public void Cast_ToICollection()
        {
            var actual = OutputFactory.Cast(rows, typeof(ICollection<Employee.IEmployeeId>));

            Assert.IsInstanceOfType(actual, typeof(ICollection<Employee.IEmployeeId>));
            Assert.AreEqual(2, ((ICollection<Employee.IEmployeeId>) actual).Count);
        }

        [TestMethod]
        public void Cast_ToIReadOnlyList()
        {
            var actual = OutputFactory.Cast(rows, typeof(IReadOnlyList<Employee.IEmployeeId>));

            Assert.IsInstanceOfType(actual, typeof(IReadOnlyList<Employee.IEmployeeId>));
            Assert.AreEqual(2, ((IReadOnlyList<Employee.IEmployeeId>) actual).Count);
        }

        [TestMethod]
        public void Cast_ToIReadOnlyCollection()
        {
            var actual = OutputFactory.Cast(rows, typeof(IReadOnlyCollection<Employee.IEmployeeId>));

            Assert.IsInstanceOfType(actual, typeof(IReadOnlyCollection<Employee.IEmployeeId>));
        }

        [TestMethod]
        public void Cast_ToReadOnlyCollection()
        {
            var actual = OutputFactory.Cast(rows, typeof(ReadOnlyCollection<Employee.IEmployeeId>));

            Assert.IsInstanceOfType(actual, typeof(ReadOnlyCollection<Employee.IEmployeeId>));
        }

        [TestMethod]
        public void Cast_ToArray()
        {
            var actual = OutputFactory.Cast(rows, typeof(Employee.IEmployeeId[]));

            Assert.IsInstanceOfType(actual, typeof(Employee.IEmployeeId[]));
        }

        [TestMethod]
        public void Cast_ToSingleResult_WithOneRow_ReturnsRow()
        {
            var actual = OutputFactory.Cast(rows.Take(1).ToList(), typeof(Employee.IEmployeeId));

            Assert.AreEqual(1, ((Employee.IEmployeeId) actual).Id);
        }

        [TestMethod]
        public void Cast_ToSingleResult_WithNoRows_ReturnsNull()
        {
            Assert.IsNull(OutputFactory.Cast(new List<object>(), typeof(Employee.IEmployeeId)));
        }

        [TestMethod]
        public void Cast_ToSingleResult_WithMultipleRows_ThrowsDescriptiveException()
        {
            var exception = Assert.ThrowsException<MultipleResultsException>(() =>
                OutputFactory.Cast(rows, typeof(Employee.IEmployeeId)));

            Assert.AreEqual(
                "Expected at most one IEmployeeId, but the query returned 2 rows. Return a collection of IEmployeeId, or narrow the query with a filter or [Fetch] parameter.",
                exception.Message);
        }

        [TestMethod]
        public void Cast_ToUnsupportedCollectionType_ThrowsDescriptiveException()
        {
            var exception = Assert.ThrowsException<InvalidTypeException>(() =>
                OutputFactory.Cast(rows, typeof(HashSet<Employee.IEmployeeId>)));

            Assert.IsTrue(exception.Message.Contains("HashSet"), exception.Message);
        }
    }
}
