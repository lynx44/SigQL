using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Exceptions;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Tests.Infrastructure;

namespace SigQL.Tests
{
    /// <summary>
    /// Covers how a [Select(TableName, ColumnName)] scalar method turns rows into return values.
    /// The rows are stubbed, so these exercise the materializer rather than the database.
    /// </summary>
    [TestClass]
    public class ScalarSelectMaterializerTests
    {
        private StubQueryExecutor queryExecutor;
        private IMonolithicRepository monolithicRepository;

        [TestInitialize]
        public void Setup()
        {
            var configurator = new MockWorkLogDatabaseConfigurator();
            queryExecutor = new StubQueryExecutor();
            monolithicRepository =
                new RepositoryBuilder(queryExecutor, configurator.WorkLogDatabaseConfiguration).Build<IMonolithicRepository>();
        }

        [TestMethod]
        public void SingleValue_ReturnsColumnValue()
        {
            queryExecutor.Returns(EmployeeRows((1, "Bob")));

            Assert.AreEqual("Bob", monolithicRepository.GetEmployeeNameScalar(1));
        }

        [TestMethod]
        public async Task SingleValueAsync_ReturnsColumnValue()
        {
            queryExecutor.Returns(EmployeeRows((1, "Bob")));

            Assert.AreEqual("Bob", await monolithicRepository.GetEmployeeNameScalarAsync(1));
        }

        [TestMethod]
        public void Collection_ReturnsOneValuePerRow()
        {
            queryExecutor.Returns(EmployeeRows((1, "Bob"), (2, "Alice"), (3, "Carol")));

            CollectionAssert.AreEqual(new[] { "Bob", "Alice", "Carol" },
                monolithicRepository.GetAllEmployeeNamesScalar().ToList());
        }

        /// <summary>
        /// Duplicate values must survive. The materializer keys rows on the primary key, which is
        /// why the primary key columns are selected alongside the projected column.
        /// </summary>
        [TestMethod]
        public void Collection_WithDuplicateValues_ReturnsEveryRow()
        {
            queryExecutor.Returns(EmployeeRows((1, "Bob"), (2, "Bob"), (3, "Bob")));

            CollectionAssert.AreEqual(new[] { "Bob", "Bob", "Bob" },
                monolithicRepository.GetAllEmployeeNamesScalar().ToList());
        }

        [TestMethod]
        public void Collection_WithNoRows_ReturnsEmptyCollection()
        {
            queryExecutor.Returns(EmployeeRows());

            Assert.IsFalse(monolithicRepository.GetAllEmployeeNamesScalar().Any());
        }

        [TestMethod]
        public void ValueTypeCollection_ReturnsConvertedValues()
        {
            queryExecutor.Returns(EmployeeRows((1, "Bob"), (2, "Alice")));

            CollectionAssert.AreEqual(new[] { 1, 2 }, monolithicRepository.GetAllEmployeeIdsScalar().ToList());
        }

        [TestMethod]
        public void EnumColumn_ReturnsEnumValues()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Classification", typeof(int));
            table.Rows.Add(1, (int) AddressClassification.Work);
            table.Rows.Add(2, (int) AddressClassification.Home);
            queryExecutor.Returns(table);

            CollectionAssert.AreEqual(new[] { AddressClassification.Work, AddressClassification.Home },
                monolithicRepository.GetAddressClassificationsScalar().ToList());
        }

        [TestMethod]
        public void NullableColumn_WithNullValue_ReturnsNull()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("StartDate", typeof(DateTime));
            table.Rows.Add(1, DBNull.Value);
            queryExecutor.Returns(table);

            CollectionAssert.AreEqual(new DateTime?[] { null },
                monolithicRepository.GetWorkLogStartDatesScalar().ToList());
        }

        [TestMethod]
        public void ReferenceType_WithNoRows_ReturnsNull()
        {
            queryExecutor.Returns(EmployeeRows());

            Assert.IsNull(monolithicRepository.GetEmployeeNameScalar(1));
        }

        [TestMethod]
        public void ReferenceType_WithNullValue_ReturnsNull()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Rows.Add(1, DBNull.Value);
            queryExecutor.Returns(table);

            Assert.IsNull(monolithicRepository.GetEmployeeNameScalar(1));
        }

        [TestMethod]
        public void NonNullableValueType_WithNoRows_Throws()
        {
            queryExecutor.Returns(EmployeeRows());

            var ex = Assert.ThrowsException<NullScalarValueException>(() =>
                monolithicRepository.GetEmployeeIdScalar("Bob"));

            Assert.AreEqual(
                "No rows were returned for column Id, which cannot be represented by the return type Int32. Declare the return type as Int32? to allow no result.",
                ex.Message);
        }

        [TestMethod]
        public void NonNullableValueType_WithNullValue_Throws()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Rows.Add(DBNull.Value);
            queryExecutor.Returns(table);

            var ex = Assert.ThrowsException<NullScalarValueException>(() =>
                monolithicRepository.GetEmployeeIdScalar("Bob"));

            Assert.AreEqual(
                "Column Id returned null, which cannot be represented by the return type Int32. Declare the return type as Int32? to allow null values.",
                ex.Message);
        }

        [TestMethod]
        public void NullableValueType_WithNoRows_ReturnsNull()
        {
            queryExecutor.Returns(EmployeeRows());

            Assert.IsNull(monolithicRepository.GetNullableEmployeeIdScalar("Bob"));
        }

        private static DataTable EmployeeRows(params (int Id, string Name)[] rows)
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            foreach (var row in rows)
            {
                table.Rows.Add(row.Id, row.Name);
            }

            return table;
        }

        private class StubQueryExecutor : IQueryExecutor
        {
            private DataTable results = new DataTable();

            public void Returns(DataTable table)
            {
                results = table;
            }

            public IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters,
                int? commandTimeout = null)
            {
                return results.CreateDataReader();
            }

            public Task<IDataReader> ExecuteReaderAsync(string commandText, IDictionary<string, object> parameters,
                int? commandTimeout = null)
            {
                return Task.FromResult(ExecuteReader(commandText, parameters, commandTimeout));
            }

            public int ExecuteNonQuery(string commandText, IDictionary<string, object> parameters,
                int? commandTimeout = null) => 0;

            public Task<int> ExecuteNonQueryAsync(string commandText, IDictionary<string, object> parameters,
                int? commandTimeout = null) => Task.FromResult(0);
        }
    }
}
