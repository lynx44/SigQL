using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Schema;

namespace SigQL.Tests
{
    [TestClass]
    public class ConventionForeignKeyResolverTests
    {
        private SchemaDefinition dbo;
        private TableDefinition employeeTable;
        private TableDefinition addressTable;

        [TestInitialize]
        public void Setup()
        {
            dbo = new SchemaDefinition("dbo");

            employeeTable = new TableDefinition(dbo, "Employee", new[] { "Id", "Name" });
            employeeTable.PrimaryKey = new TableKeyDefinition(employeeTable.Columns.FindByName("Id"));

            addressTable = new TableDefinition(dbo, "Address", new[] { "Id", "StreetAddress" });
            addressTable.PrimaryKey = new TableKeyDefinition(addressTable.Columns.FindByName("Id"));
        }

        private DatabaseConfiguration BuildDatabase(params ITableDefinition[] tables)
        {
            return new DatabaseConfiguration(new TableDefinitionCollection(tables));
        }

        [TestMethod]
        public void GetForeignKeys_ColumnNameMatchesTable_ResolvesForeignKeyToPrimaryKey()
        {
            var workLogTable = new TableDefinition(dbo, "WorkLog", new[] { "Id", "EmployeeId" });
            var database = BuildDatabase(workLogTable, employeeTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var fk = resolver.GetForeignKeys(workLogTable).Single();

            Assert.AreEqual("Employee", fk.PrimaryKeyTable.Name);
            Assert.AreEqual("EmployeeId", fk.KeyPairs.Single().ForeignTableColumn.Name);
            Assert.AreEqual("Id", fk.KeyPairs.Single().PrimaryTableColumn.Name);
        }

        [TestMethod]
        public void GetForeignKeys_MultipleConventionalColumns_ResolvesEachForeignKey()
        {
            var workLogTable = new TableDefinition(dbo, "WorkLog", new[] { "Id", "EmployeeId", "AddressId" });
            var database = BuildDatabase(workLogTable, employeeTable, addressTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var foreignKeys = resolver.GetForeignKeys(workLogTable).ToList();

            Assert.AreEqual(2, foreignKeys.Count);
            Assert.IsTrue(foreignKeys.Any(fk => fk.PrimaryKeyTable.Name == "Employee"));
            Assert.IsTrue(foreignKeys.Any(fk => fk.PrimaryKeyTable.Name == "Address"));
        }

        [TestMethod]
        public void GetForeignKeys_ColumnNameCasingDiffersFromIdSuffix_StillResolves()
        {
            var workLogTable = new TableDefinition(dbo, "WorkLog", new[] { "Id", "EmployeeID" });
            var database = BuildDatabase(workLogTable, employeeTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var fk = resolver.GetForeignKeys(workLogTable).Single();

            Assert.AreEqual("Employee", fk.PrimaryKeyTable.Name);
        }

        [TestMethod]
        public void GetForeignKeys_ColumnNameHasNoMatchingTable_IsIgnored()
        {
            var workLogTable = new TableDefinition(dbo, "WorkLog", new[] { "Id", "UnknownVendorId" });
            var database = BuildDatabase(workLogTable, employeeTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var foreignKeys = resolver.GetForeignKeys(workLogTable);

            Assert.IsFalse(foreignKeys.Any());
        }

        [TestMethod]
        public void GetForeignKeys_ReferencedTableHasCompositePrimaryKey_IsIgnored()
        {
            var streetAddressCoordinateTable = new TableDefinition(dbo, "StreetAddressCoordinate", new[] { "StreetAddress", "City" });
            streetAddressCoordinateTable.PrimaryKey = new TableKeyDefinition(
                streetAddressCoordinateTable.Columns.FindByName("StreetAddress"),
                streetAddressCoordinateTable.Columns.FindByName("City"));
            var workLogTable = new TableDefinition(dbo, "WorkLog", new[] { "Id", "StreetAddressCoordinateId" });
            var database = BuildDatabase(workLogTable, streetAddressCoordinateTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var foreignKeys = resolver.GetForeignKeys(workLogTable);

            Assert.IsFalse(foreignKeys.Any());
        }

        [TestMethod]
        public void GetForeignKeys_ReferencedTableHasNoPrimaryKey_IsIgnored()
        {
            var viewTable = new TableDefinition(dbo, "EmployeeView", new[] { "EmployeeId", "Name" });
            viewTable.PrimaryKey = new TableKeyDefinition();
            var workLogTable = new TableDefinition(dbo, "WorkLog", new[] { "Id", "EmployeeViewId" });
            var database = BuildDatabase(workLogTable, viewTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var foreignKeys = resolver.GetForeignKeys(workLogTable);

            Assert.IsFalse(foreignKeys.Any());
        }

        [TestMethod]
        public void GetForeignKeys_PrimaryKeyColumnItself_IsNotTreatedAsForeignKey()
        {
            var database = BuildDatabase(employeeTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var foreignKeys = resolver.GetForeignKeys(employeeTable);

            Assert.IsFalse(foreignKeys.Any());
        }

        [TestMethod]
        public void GetForeignKeys_SelfReferencingColumn_ResolvesForeignKeyToSameTable()
        {
            var employeeTableWithSelfReference = new TableDefinition(dbo, "Employee", new[] { "Id", "Name", "EmployeeId" });
            employeeTableWithSelfReference.PrimaryKey = new TableKeyDefinition(employeeTableWithSelfReference.Columns.FindByName("Id"));
            var database = BuildDatabase(employeeTableWithSelfReference);
            var resolver = new ConventionForeignKeyResolver(database);

            var fk = resolver.GetForeignKeys(employeeTableWithSelfReference).Single();

            Assert.AreSame(employeeTableWithSelfReference, fk.PrimaryKeyTable);
            Assert.AreEqual("EmployeeId", fk.KeyPairs.Single().ForeignTableColumn.Name);
        }

        [TestMethod]
        public void GetForeignKeys_NoConventionalColumns_ReturnsEmptyCollection()
        {
            var database = BuildDatabase(addressTable);
            var resolver = new ConventionForeignKeyResolver(database);

            var foreignKeys = resolver.GetForeignKeys(addressTable);

            Assert.IsFalse(foreignKeys.Any());
        }
    }
}
