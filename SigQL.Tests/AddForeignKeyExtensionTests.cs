using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Schema;

namespace SigQL.Tests
{
    [TestClass]
    public class AddForeignKeyExtensionTests
    {
        private TableDefinition employeeTable;
        private TableDefinition workLogTable;
        private TableDefinition addressTable;

        [TestInitialize]
        public void Setup()
        {
            var dbo = new SchemaDefinition("dbo");
            employeeTable = new TableDefinition(dbo, "Employee", new[] { "Id", "Name" });
            employeeTable.PrimaryKey = new TableKeyDefinition(employeeTable.Columns.FindByName("Id"));

            workLogTable = new TableDefinition(dbo, "WorkLog", new[] { "Id", "EmployeeId", "LocationId" });
            workLogTable.PrimaryKey = new TableKeyDefinition(workLogTable.Columns.FindByName("Id"));

            addressTable = new TableDefinition(dbo, "Address", new[] { "Id", "StreetAddress", "City", "State" });
            addressTable.PrimaryKey = new TableKeyDefinition(addressTable.Columns.FindByName("Id"));
        }

        [TestMethod]
        public void AddForeignKey_SingleColumn_AddsForeignKeyToCollection()
        {
            workLogTable.AddForeignKey(
                t => t.Columns.FindByName("EmployeeId"),
                employeeTable.Columns.FindByName("Id"));

            var fk = workLogTable.ForeignKeyCollection.Single();
            Assert.AreEqual("Employee", fk.PrimaryKeyTable.Name);
            Assert.AreEqual("EmployeeId", fk.KeyPairs.Single().ForeignTableColumn.Name);
            Assert.AreEqual("Id", fk.KeyPairs.Single().PrimaryTableColumn.Name);
        }

        [TestMethod]
        public void AddForeignKey_MultiColumn_AddsCompositeForeignKeyToCollection()
        {
            var streetAddressCoordinateTable = new TableDefinition(
                new SchemaDefinition("dbo"),
                "StreetAddressCoordinate",
                new[] { "Id", "StreetAddress", "City", "State" });

            streetAddressCoordinateTable.AddForeignKey(
                t => new[]
                {
                    t.Columns.FindByName("StreetAddress"),
                    t.Columns.FindByName("City"),
                    t.Columns.FindByName("State")
                },
                new[]
                {
                    addressTable.Columns.FindByName("StreetAddress"),
                    addressTable.Columns.FindByName("City"),
                    addressTable.Columns.FindByName("State")
                });

            var fk = streetAddressCoordinateTable.ForeignKeyCollection.Single();
            Assert.AreEqual("Address", fk.PrimaryKeyTable.Name);
            Assert.AreEqual(3, fk.KeyPairs.Count());
        }

        [TestMethod]
        public void AddForeignKey_ReturnsTableForMethodChaining()
        {
            var result = workLogTable.AddForeignKey(
                t => t.Columns.FindByName("EmployeeId"),
                employeeTable.Columns.FindByName("Id"));

            Assert.AreSame(workLogTable, result);
        }

        [TestMethod]
        public void AddForeignKey_MultipleCalls_AddsMultipleForeignKeys()
        {
            workLogTable
                .AddForeignKey(
                    t => t.Columns.FindByName("EmployeeId"),
                    employeeTable.Columns.FindByName("Id"))
                .AddForeignKey(
                    t => t.Columns.FindByName("LocationId"),
                    addressTable.Columns.FindByName("Id"));

            Assert.AreEqual(2, workLogTable.ForeignKeyCollection.Count());
        }

        [TestMethod]
        public void AddForeignKey_ForeignKeyDefinition_HasCorrectKeyPairs()
        {
            workLogTable.AddForeignKey(
                t => t.Columns.FindByName("EmployeeId"),
                employeeTable.Columns.FindByName("Id"));

            var fk = workLogTable.ForeignKeyCollection.Single();
            var keyPair = fk.KeyPairs.Single();
            Assert.AreSame(workLogTable, keyPair.ForeignTableColumn.Table);
            Assert.AreSame(employeeTable, keyPair.PrimaryTableColumn.Table);
        }
    }
}
