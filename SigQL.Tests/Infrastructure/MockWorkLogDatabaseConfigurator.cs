using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SigQL.Diagnostics;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.Tests.Infrastructure
{
    internal class MockWorkLogDatabaseConfigurator
    {
        public MockWorkLogDatabaseConfigurator()
        {
            var builder = new SqlStatementBuilder();
            WorkLogDatabaseConfiguration = BuildWorkLogDatabase();
            MethodParser = new MethodParser(builder, WorkLogDatabaseConfiguration, DefaultPluralizationHelper.Instance);
            var preparedStatementCollectorFactory = new PreparedStatementCollectorFactory(WorkLogDatabaseConfiguration);
            PreparedSqlStatements = new List<PreparedSqlStatement>();
        }

        public List<PreparedSqlStatement> PreparedSqlStatements { get; set; }

        public MethodParser MethodParser { get; set; }

        public DatabaseConfiguration WorkLogDatabaseConfiguration { get; set; }

        private DatabaseConfiguration BuildWorkLogDatabase()
        {
            var dbo = new SchemaDefinition("dbo");
            var workLogTable = new TableDefinition(dbo, nameof(WorkLog), SetupColumns(typeof(WorkLog).GetProperties()));
            var employeeTable = new TableDefinition(dbo, nameof(Employee), SetupColumns(typeof(Employee).GetProperties()));
            var locationTable = new TableDefinition(dbo, nameof(Location), SetupColumns(typeof(Location).GetProperties()));
            var addressTable = new TableDefinition(dbo, nameof(Address), SetupColumns(typeof(Address).GetProperties()));
            var streetAddressCoordinateTable = new TableDefinition(dbo, nameof(StreetAddressCoordinate), SetupColumns(typeof(StreetAddressCoordinate).GetProperties()));
            var diagnosticLogTable = new TableDefinition(dbo, nameof(DiagnosticLog), SetupColumns(typeof(DiagnosticLog).GetProperties()));
            var itvfGetWorkLogsByEmployeeIdFunction = new TableDefinition(dbo, nameof(itvf_GetWorkLogsByEmployeeId), SetupColumns(typeof(itvf_GetWorkLogsByEmployeeId).GetProperties()));
            var workLogEmployeeView = new TableDefinition(dbo, nameof(WorkLogEmployeeView), SetupColumns(typeof(WorkLogEmployeeView).GetProperties()));
            var employeeStatusesTable = new TableDefinition(dbo, nameof(EmployeeStatuses), SetupColumns(typeof(EmployeeStatuses).GetProperties()));
            itvfGetWorkLogsByEmployeeIdFunction.ObjectType = DatabaseObjectType.Function;
            workLogTable.PrimaryKey = new TableKeyDefinition(workLogTable.Columns.FindByName(nameof(WorkLog.Id)));
            employeeTable.PrimaryKey = new TableKeyDefinition(employeeTable.Columns.FindByName(nameof(Employee.Id)));
            ((ColumnDefinition)employeeTable.PrimaryKey.Columns.First()).IsIdentity = true;
            locationTable.PrimaryKey = new TableKeyDefinition(locationTable.Columns.FindByName(nameof(Location.Id)));
            addressTable.PrimaryKey = new TableKeyDefinition(addressTable.Columns.FindByName(nameof(Address.Id)));
            diagnosticLogTable.PrimaryKey = new TableKeyDefinition();
            itvfGetWorkLogsByEmployeeIdFunction.PrimaryKey = new TableKeyDefinition();
            workLogEmployeeView.PrimaryKey = new TableKeyDefinition();
            streetAddressCoordinateTable.PrimaryKey = new TableKeyDefinition(streetAddressCoordinateTable.Columns.FindByName(nameof(StreetAddressCoordinate.Id)));
            employeeStatusesTable.PrimaryKey = new TableKeyDefinition(employeeStatusesTable.Columns.FindByName(nameof(EmployeeStatuses.Id)));
            var employeeAddressTable = new TableDefinition(dbo, nameof(EmployeeAddress), typeof(EmployeeAddress).GetProperties().Select(p => p.Name));
            workLogTable.ForeignKeyCollection = new ForeignKeyDefinitionCollection().AddForeignKeys(
                new ForeignKeyDefinition(employeeTable, new ForeignKeyPair(workLogTable.Columns.FindByName(nameof(WorkLog.EmployeeId)), employeeTable.Columns.FindByName(nameof(Employee.Id)))),
                new ForeignKeyDefinition(locationTable, new ForeignKeyPair(workLogTable.Columns.FindByName(nameof(WorkLog.LocationId)), locationTable.Columns.FindByName(nameof(Location.Id))))
                );
            locationTable.ForeignKeyCollection = new ForeignKeyDefinitionCollection().AddForeignKeys(
                new ForeignKeyDefinition(addressTable, new ForeignKeyPair(locationTable.Columns.FindByName(nameof(Location.AddressId)), addressTable.Columns.FindByName(nameof(Address.Id))))
                );
            employeeAddressTable.ForeignKeyCollection = new ForeignKeyDefinitionCollection().AddForeignKeys(
                new ForeignKeyDefinition(addressTable, new ForeignKeyPair(employeeAddressTable.Columns.FindByName(nameof(EmployeeAddress.AddressId)), addressTable.Columns.FindByName(nameof(Address.Id)))),
                new ForeignKeyDefinition(employeeTable, new ForeignKeyPair(employeeAddressTable.Columns.FindByName(nameof(EmployeeAddress.EmployeeId)), employeeTable.Columns.FindByName(nameof(Employee.Id))))
            );
            streetAddressCoordinateTable.ForeignKeyCollection = new ForeignKeyDefinitionCollection().AddForeignKeys(
                new ForeignKeyDefinition(addressTable,
                    new ForeignKeyPair(streetAddressCoordinateTable.Columns.FindByName(nameof(StreetAddressCoordinate.StreetAddress)), addressTable.Columns.FindByName(nameof(Address.StreetAddress))),
                    new ForeignKeyPair(streetAddressCoordinateTable.Columns.FindByName(nameof(StreetAddressCoordinate.City)), addressTable.Columns.FindByName(nameof(Address.City))),
                    new ForeignKeyPair(streetAddressCoordinateTable.Columns.FindByName(nameof(StreetAddressCoordinate.State)), addressTable.Columns.FindByName(nameof(Address.State))))
            );

            var databaseConfiguration = new DatabaseConfiguration(new TableDefinitionCollection(new[]
            {
                workLogTable,
                employeeTable,
                locationTable,
                addressTable,
                employeeAddressTable,
                streetAddressCoordinateTable,
                diagnosticLogTable,
                itvfGetWorkLogsByEmployeeIdFunction,
                workLogEmployeeView,
                employeeStatusesTable
            }));

            return databaseConfiguration;
        }

        private IEnumerable<ColumnDefinitionField> SetupColumns(IEnumerable<PropertyInfo> properties)
        {
            return properties.Select(SetupColumn).ToList();
        }

        private ColumnDefinitionField SetupColumn(PropertyInfo property)
        {
            return new ColumnDefinitionField()
            {
                Name = property.Name,
                DataTypeDeclaration =
                    property.PropertyType == typeof(int) ? "int" :
                    property.PropertyType == typeof(DateTime) ? "datetime" :
                    "nvarchar(max)"
            };
        }
    }
}
