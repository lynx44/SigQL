using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Diagnostics;
using SigQL.Exceptions;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Types;

namespace SigQL.Tests
{
    [TestClass]
    public class MethodParserTests
    {
        private MethodParser methodParser;
        private DatabaseConfiguration workLogDatabaseConfiguration;
        private List<PreparedSqlStatement> preparedSqlStatements;
        private IMonolithicRepository monolithicRepository;

        [TestInitialize]
        public void Setup()
        {
            var builder = new SqlStatementBuilder();
            workLogDatabaseConfiguration = BuildWorkLogDatabase();
            methodParser = new MethodParser(builder, workLogDatabaseConfiguration);
            var preparedStatementCollectorFactory = new PreparedStatementCollectorFactory(workLogDatabaseConfiguration);
            preparedSqlStatements = new List<PreparedSqlStatement>();
            monolithicRepository = preparedStatementCollectorFactory.Build<IMonolithicRepository>(preparedSqlStatements);
        }

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
            itvfGetWorkLogsByEmployeeIdFunction.ObjectType = DatabaseObjectType.Function;
            workLogTable.PrimaryKey = new TableKeyDefinition(workLogTable.Columns.FindByName(nameof(WorkLog.Id)));
            employeeTable.PrimaryKey = new TableKeyDefinition(employeeTable.Columns.FindByName(nameof(Employee.Id)));
            locationTable.PrimaryKey = new TableKeyDefinition(locationTable.Columns.FindByName(nameof(Location.Id)));
            addressTable.PrimaryKey = new TableKeyDefinition(addressTable.Columns.FindByName(nameof(Address.Id)));
            diagnosticLogTable.PrimaryKey = new TableKeyDefinition();
            itvfGetWorkLogsByEmployeeIdFunction.PrimaryKey = new TableKeyDefinition();
            streetAddressCoordinateTable.PrimaryKey = new TableKeyDefinition(streetAddressCoordinateTable.Columns.FindByName(nameof(StreetAddressCoordinate.Id)));
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
                itvfGetWorkLogsByEmployeeIdFunction
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

        #region Queries

        [TestMethod]
        public void GetProjection_WithRepositoryInterface_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IWorkLogRepository_IRepository).GetMethod(nameof(IWorkLogRepository_IRepository.GetAllIds));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void GetProjection_WithRepositoryInterfaceAndAliasedName_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWithAliasedName(1));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"WorkLog\".\"Id\" \"WorkLogId\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" = @id))", sql);
        }

        [TestMethod]
        public void GetProjection_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IWorkLogRepository).GetMethod(nameof(IWorkLogRepository.GetAllIds));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void Get_CanonicalTypeWithRecursion_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogs());

            Assert.AreEqual("select \"WorkLog<WorkLog>\".\"Id\" \"Id\", \"WorkLog<WorkLog>\".\"StartDate\" \"StartDate\", \"WorkLog<WorkLog>\".\"EndDate\" \"EndDate\", \"WorkLog<WorkLog>\".\"EmployeeId\" \"EmployeeId\", \"WorkLog<WorkLog>\".\"LocationId\" \"LocationId\", \"Employee<WorkLog.Employee>\".\"Id\" \"Employee.Id\", \"Employee<WorkLog.Employee>\".\"Name\" \"Employee.Name\", \"Address<WorkLog.Employee.Addresses>\".\"Id\" \"Employee.Addresses.Id\", \"Address<WorkLog.Employee.Addresses>\".\"StreetAddress\" \"Employee.Addresses.StreetAddress\", \"Address<WorkLog.Employee.Addresses>\".\"City\" \"Employee.Addresses.City\", \"Address<WorkLog.Employee.Addresses>\".\"State\" \"Employee.Addresses.State\", \"Address<WorkLog.Employee.Addresses>\".\"Classification\" \"Employee.Addresses.Classification\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Id\" \"Employee.Addresses.Locations.Id\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Name\" \"Employee.Addresses.Locations.Name\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" \"Employee.Addresses.Locations.AddressId\", \"Location<WorkLog.Location>\".\"Id\" \"Location.Id\", \"Location<WorkLog.Location>\".\"Name\" \"Location.Name\", \"Location<WorkLog.Location>\".\"AddressId\" \"Location.AddressId\", \"Address<WorkLog.Location.Address>\".\"Id\" \"Location.Address.Id\", \"Address<WorkLog.Location.Address>\".\"StreetAddress\" \"Location.Address.StreetAddress\", \"Address<WorkLog.Location.Address>\".\"City\" \"Location.Address.City\", \"Address<WorkLog.Location.Address>\".\"State\" \"Location.Address.State\", \"Address<WorkLog.Location.Address>\".\"Classification\" \"Location.Address.Classification\", \"Employee<WorkLog.Location.Address.Employees>\".\"Id\" \"Location.Address.Employees.Id\", \"Employee<WorkLog.Location.Address.Employees>\".\"Name\" \"Location.Address.Employees.Name\" from \"WorkLog\" \"WorkLog<WorkLog>\" left outer join \"Employee\" \"Employee<WorkLog.Employee>\" on ((\"WorkLog<WorkLog>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\")) left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Employee>\" on ((\"EmployeeAddress<WorkLog.Employee>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\")) left outer join \"Address\" \"Address<WorkLog.Employee.Addresses>\" on ((\"EmployeeAddress<WorkLog.Employee>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\")) left outer join \"Location\" \"Location<WorkLog.Employee.Addresses.Locations>\" on ((\"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\")) left outer join \"Location\" \"Location<WorkLog.Location>\" on ((\"WorkLog<WorkLog>\".\"LocationId\" = \"Location<WorkLog.Location>\".\"Id\")) left outer join \"Address\" \"Address<WorkLog.Location.Address>\" on ((\"Location<WorkLog.Location>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\")) left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Location.Address>\" on ((\"EmployeeAddress<WorkLog.Location.Address>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\")) left outer join \"Employee\" \"Employee<WorkLog.Location.Address.Employees>\" on ((\"EmployeeAddress<WorkLog.Location.Address>\".\"EmployeeId\" = \"Employee<WorkLog.Location.Address.Employees>\".\"Id\"))", sql);
        }

        [TestMethod]
        public void GetTableCollection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetAllEmployeeFields));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\"", sql);
        }

        [TestMethod]
        public void GetSingleWithWhere_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.Get));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Id\" = @id))", sql);
        }

        [TestMethod]
        public void GetSingleWithNot_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetNot(1));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Id\" != @id))", sql);
        }

        [TestMethod]
        public void GetSingleWithNull_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByName(null));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Name\" is null))", sql);
        }

        [TestMethod]
        public void GetSingleWithNotNull_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByNameNot(null));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Name\" is not null))", sql);
        }

        [TestMethod]
        public void GetSingleWithFilterIsNullProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByNameFilter(new Employee.EmployeeNameFilter() { Name = null }));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Name\" is null))", sql);
        }

        [TestMethod]
        public void GetSingleWithFilterIsNotNullProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByNameFilterNot(new Employee.EmployeeNameNotFilter() { Name = null }));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Name\" is not null))", sql);
        }

        [TestMethod]
        public void GetSingleWithFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetByFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Id\" = @Id))", sql);
        }

        [TestMethod]
        public void GetSingleWithParameterSpecifiedColumnName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWithSpecifiedColumnName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Id\" = @employeeId))", sql);
        }

        [TestMethod]
        public void GetSingleWithPropertySpecifiedColumnName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWithFilterSpecifiedColumnName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where ((\"Employee\".\"Id\" = @EmployeeId))", sql);
        }

        [TestMethod]
        public void GetSingleWithNestedPropertySpecifiedColumnName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWithFilterNestedSpecifiedColumnName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where ((exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress))))))))", sql);
        }

        [TestMethod]
        public void GetWithSingleNavigationPropertyEntity_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithEmployee));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void GetWithSingleNavigationPropertyInnerProjectionInterface_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithEmployeeNames));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void GetWithSingleNavigationPropertyEntity_CompositeKey_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetAddressWithStreetAddress));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Address\".\"Id\" \"Id\", \"StreetAddressCoordinate\".\"Id\" \"Coordinates.Id\", \"StreetAddressCoordinate\".\"Latitude\" \"Coordinates.Latitude\", \"StreetAddressCoordinate\".\"Longitude\" \"Coordinates.Longitude\" from \"Address\" left outer join \"StreetAddressCoordinate\" on ((\"StreetAddressCoordinate\".\"StreetAddress\" = \"Address\".\"StreetAddress\") and (\"StreetAddressCoordinate\".\"City\" = \"Address\".\"City\") and (\"StreetAddressCoordinate\".\"State\" = \"Address\".\"State\"))", sql);
        }

        [TestMethod]
        public void GetWithMultipleNavigationPropertyInnerProjectionInterfaces_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithEmployeeAndLocation));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\", \"Location\".\"Id\" \"Location.Id\", \"Location\".\"Name\" \"Location.Name\", \"Location\".\"AddressId\" \"Location.AddressId\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")) left outer join \"Location\" on ((\"WorkLog\".\"LocationId\" = \"Location\".\"Id\"))", sql);
        }

        [TestMethod]
        public void GetWithNestedNavigationPropertyInnerProjectionInterfaces_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithLocationAndLocationAddress));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Location\".\"Id\" \"Location.Id\", \"Location\".\"Name\" \"Location.Name\", \"Address\".\"Id\" \"Location.Address.Id\", \"Address\".\"StreetAddress\" \"Location.Address.StreetAddress\" from \"WorkLog\" left outer join \"Location\" on ((\"WorkLog\".\"LocationId\" = \"Location\".\"Id\")) left outer join \"Address\" on ((\"Location\".\"AddressId\" = \"Address\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Get_OneToMany_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetAddressWithLocations));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Address\".\"Id\" \"Id\", \"Location\".\"Id\" \"Locations.Id\", \"Location\".\"Name\" \"Locations.Name\", \"Location\".\"AddressId\" \"Locations.AddressId\" from \"Address\" left outer join \"Location\" on ((\"Location\".\"AddressId\" = \"Address\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Get_ManyToMany_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeWithAddresses));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Address\".\"Id\" \"Addresses.Id\", \"Address\".\"StreetAddress\" \"Addresses.StreetAddress\" from \"Employee\" left outer join \"EmployeeAddress\" on ((\"EmployeeAddress\".\"EmployeeId\" = \"Employee\".\"Id\")) left outer join \"Address\" on ((\"EmployeeAddress\".\"AddressId\" = \"Address\".\"Id\"))", sql);
        }

        [TestMethod]
        public void GetClrOnlyParameter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetClrOnly(1));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\"", sql);
        }

        [TestMethod]
        public void GetClrOnlyMixedWithColumnParams_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetClrOnlyMixedWithColumnParams(1, 2));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"EmployeeId\" = @employeeId))", sql);
        }

        [TestMethod]
        public void GetClrOnlyParameterClass_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetClrOnlyParameterClass(new Employee.IdFilter()));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\"", sql);
        }

        [TestMethod]
        public void GetClrOnlyFilterClassProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetClrOnlyFilterClassProperty(new WorkLog.ClrOnlyFilter()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"EmployeeId\" = @EmployeeId))", sql);
        }

        [TestMethod]
        public void GetPocoWithClrOnlyProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithClrOnlyProperty());

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void GetPocoWithNestedClrOnlyProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithNestedClrOnlyProperty());

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Where_MultipleParameters_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogByLocationIdAndEmployeeId));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"LocationId\" = @locationId) and (\"WorkLog\".\"EmployeeId\" = @employeeId))", sql);
        }

        [TestMethod]
        public void Where_ByNavigationProperty_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogByEmployeeName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))))", sql);
        }

        // consider if this behavior is desired
        // [TestMethod]
        // public void Where_ByDirectNavigationProperty_ReturnsExpectedSql()
        // {
        //     var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogByEmployeeNameDirect));
        //     var sql = GetSqlFor(methodInfo);
        //
        //     Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))))", sql);
        // }

        [TestMethod]
        public void Where_ByManyToManyNavigationProperty_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeByStreetAddress));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where ((exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress))))))))", sql);
        }

        [TestMethod]
        public void Where_Like_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeesByNameWithLike));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where ((\"Employee\".\"Name\" like @name))", sql);
        }

        [TestMethod]
        public void Where_LikeInNavigationProperty_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsByEmployeeNameWithLike));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" like @Employee0Name)))))", sql);
        }

        [TestMethod]
        public void Where_In_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyId(new List<int?>() { 1 }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" in (@id0)))", sql);
        }

        [TestMethod]
        public void Where_In_IgnoreIfNullOrEmpty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(new List<int>()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((1 = 1))", sql);
        }

        [TestMethod]
        public void Where_In_IgnoreIfNull_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(null));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((1 = 1))", sql);
        }

        [TestMethod]
        public void Where_In_WithNullItem_SpecifiesInClauseAndChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyId(new List<int?>() { 1, null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (((\"WorkLog\".\"Id\" in (@id0)) or (\"WorkLog\".\"Id\" is null)))", sql);
        }

        [TestMethod]
        public void Where_In_WithOnlyNullItem_OnlyChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyId(new List<int?>() { null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" is null))", sql);
        }

        [TestMethod]
        public void Where_NotIn_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdNotIn(new List<int?>() { 1 }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" not in (@id0)))", sql);
        }

        [TestMethod]
        public void Where_NotIn_WithNullItem_SpecifiesInClauseAndChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdNotIn(new List<int?>() { 1, null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (((\"WorkLog\".\"Id\" not in (@id0)) and (\"WorkLog\".\"Id\" is not null)))", sql);
        }

        [TestMethod]
        public void Where_NotIn_WithOnlyNullItem_OnlyChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdNotIn(new List<int?>() { null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" is not null))", sql);
        }

        [TestMethod]
        public void Where_GreaterThan_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanStartDate));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" > @startDate))", sql);
        }

        [TestMethod]
        public void Where_GreaterThanEqualTo_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanOrEqualToStartDate));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" >= @startDate))", sql);
        }

        [TestMethod]
        public void Where_GreaterThanWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" > @StartDate))", sql);
        }

        [TestMethod]
        public void Where_GreaterThanEqualToWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanOrEqualToStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" >= @StartDate))", sql);
        }

        [TestMethod]
        public void Where_BetweenDatesViaAlias_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsBetweenDatesViaAlias));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" >= @startDate) and (\"WorkLog\".\"StartDate\" <= @endDate))", sql);
        }

        [TestMethod]
        public void Where_LessThan_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsLessThanStartDate));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" < @startDate))", sql);
        }

        [TestMethod]
        public void Where_LessThanEqualTo_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsLessThanOrEqualToStartDate));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" <= @startDate))", sql);
        }

        [TestMethod]
        public void Where_LessThanWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsLessThanStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" < @StartDate))", sql);
        }

        [TestMethod]
        public void Where_LessThanEqualToWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsLessThanOrEqualToStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" <= @StartDate))", sql);
        }

        [TestMethod]
        public void Where_In_NavigationProperty_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsByEmployeeNamesWithIn));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" in ())))))", sql);
        }

        [TestMethod]
        public void OrderByAttribute_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsAttribute(OrderByDirection.Ascending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc", sql);
        }

        [TestMethod]
        public void OrderByAttributeDesc_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsAttribute(OrderByDirection.Descending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" desc", sql);
        }

        [TestMethod]
        public void OrderByDirection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogs(OrderByDirection.Ascending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc", sql);
        }

        [TestMethod]
        public void OrderByDirectionDescending_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogs(OrderByDirection.Descending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" desc", sql);
        }

        [TestMethod]
        public void OrderByDirectionMultipleParameters_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogs(OrderByDirection.Ascending, OrderByDirection.Ascending, OrderByDirection.Descending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc, \"WorkLog\".\"EndDate\" asc, \"WorkLog\".\"EmployeeId\" desc", sql);
        }

        [TestMethod]
        public void OrderByDirectionMixedOrderByTypes_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogs(OrderByDirection.Ascending, OrderByDirection.Descending, new OrderBy(nameof(WorkLog), nameof(WorkLog.EndDate))));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")) order by \"Employee\".\"Name\" asc, \"WorkLog\".\"StartDate\" desc, \"WorkLog\".\"EndDate\" asc", sql);
        }

        [TestMethod]
        public void OrderByDirectionViaClassFilterNavigationProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsByClassFilterNavigationProperty(new WorkLog.OrderByDirectionEmployeeName() { Employee = new Employee.EmployeeNameOrder() {  Name = OrderByDirection.Ascending }}));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")) order by \"Employee\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void OrderByDirectionViaClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsViaClassFilter(new WorkLog.OrderByDirectionStartDate() { StartDate = OrderByDirection.Ascending }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc", sql);
        }

        [TestMethod]
        public void OrderByDirectionMultiple_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsMultiple(OrderByDirection.Descending, OrderByDirection.Ascending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" desc, \"WorkLog\".\"EndDate\" asc", sql);
        }

        [TestMethod]
        public void OrderByDirectionMultipleViaClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsViaClassFilterMultiple(new WorkLog.OrderByDirectionStartDateEndDate() { StartDate = OrderByDirection.Ascending, EndDate = OrderByDirection.Descending }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc, \"WorkLog\".\"EndDate\" desc", sql);
        }

        [TestMethod]
        public void OrderByDirectionByNonProjectedNavigationTable_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsByNonProjectedNavigationTable(OrderByDirection.Ascending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")) order by \"Employee\".\"Name\" asc", sql);
        }

        //[TestMethod]
        //public void OrderByMethodWithDynamicGenericTypeConstraint_ReturnsExpectedSql()
        //{
        //    var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsGenericType<WorkLog.IStartDate>(direction: OrderByDirection.Ascending));

        //    Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc", sql);
        //}

        [TestMethod]
        public void OrderByDynamic_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderBy(new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Ascending)));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc", sql);
        }

        [TestMethod]
        public void OrderByDynamicEnumerable_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
               new List<OrderBy>()
               {
                   new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Ascending),
                   new OrderBy(nameof(WorkLog), nameof(WorkLog.Id), OrderByDirection.Descending)
               }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc, \"WorkLog\".\"Id\" desc", sql);
        }

        [TestMethod]
        public void OrderByDynamicEnumerable_EmptyCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
               new List<OrderBy>()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void OrderByDynamicEnumerable_NullCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
               null));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void OrderByRelationDynamic_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderByRelation(new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee) + "." + nameof(Employee.Name), OrderByDirection.Ascending)));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")) order by \"Employee\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void OrderByRelationDynamic_WhenTableDoesNotExistOnOutputType_ThrowsException()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderByRelation(new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee) + "." + nameof(Employee.Name), OrderByDirection.Ascending)));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from \"WorkLog\" left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")) order by \"Employee\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void OrderByRelationDynamic_ReturnsExpectedSqlWithAliasPath()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderByRelationCanonicalDataType(new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee) + "." + nameof(Employee.Name), OrderByDirection.Ascending)));

            Assert.AreEqual("select \"WorkLog<WorkLog>\".\"Id\" \"Id\", \"WorkLog<WorkLog>\".\"StartDate\" \"StartDate\", \"WorkLog<WorkLog>\".\"EndDate\" \"EndDate\", \"WorkLog<WorkLog>\".\"EmployeeId\" \"EmployeeId\", \"WorkLog<WorkLog>\".\"LocationId\" \"LocationId\", \"Employee<WorkLog.Employee>\".\"Id\" \"Employee.Id\", \"Employee<WorkLog.Employee>\".\"Name\" \"Employee.Name\", \"Address<WorkLog.Employee.Addresses>\".\"Id\" \"Employee.Addresses.Id\", \"Address<WorkLog.Employee.Addresses>\".\"StreetAddress\" \"Employee.Addresses.StreetAddress\", \"Address<WorkLog.Employee.Addresses>\".\"City\" \"Employee.Addresses.City\", \"Address<WorkLog.Employee.Addresses>\".\"State\" \"Employee.Addresses.State\", \"Address<WorkLog.Employee.Addresses>\".\"Classification\" \"Employee.Addresses.Classification\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Id\" \"Employee.Addresses.Locations.Id\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Name\" \"Employee.Addresses.Locations.Name\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" \"Employee.Addresses.Locations.AddressId\", \"Location<WorkLog.Location>\".\"Id\" \"Location.Id\", \"Location<WorkLog.Location>\".\"Name\" \"Location.Name\", \"Location<WorkLog.Location>\".\"AddressId\" \"Location.AddressId\", \"Address<WorkLog.Location.Address>\".\"Id\" \"Location.Address.Id\", \"Address<WorkLog.Location.Address>\".\"StreetAddress\" \"Location.Address.StreetAddress\", \"Address<WorkLog.Location.Address>\".\"City\" \"Location.Address.City\", \"Address<WorkLog.Location.Address>\".\"State\" \"Location.Address.State\", \"Address<WorkLog.Location.Address>\".\"Classification\" \"Location.Address.Classification\", \"Employee<WorkLog.Location.Address.Employees>\".\"Id\" \"Location.Address.Employees.Id\", \"Employee<WorkLog.Location.Address.Employees>\".\"Name\" \"Location.Address.Employees.Name\" from \"WorkLog\" \"WorkLog<WorkLog>\" left outer join \"Employee\" \"Employee<WorkLog.Employee>\" on ((\"WorkLog<WorkLog>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\")) left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Employee>\" on ((\"EmployeeAddress<WorkLog.Employee>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\")) left outer join \"Address\" \"Address<WorkLog.Employee.Addresses>\" on ((\"EmployeeAddress<WorkLog.Employee>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\")) left outer join \"Location\" \"Location<WorkLog.Employee.Addresses.Locations>\" on ((\"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\")) left outer join \"Location\" \"Location<WorkLog.Location>\" on ((\"WorkLog<WorkLog>\".\"LocationId\" = \"Location<WorkLog.Location>\".\"Id\")) left outer join \"Address\" \"Address<WorkLog.Location.Address>\" on ((\"Location<WorkLog.Location>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\")) left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Location.Address>\" on ((\"EmployeeAddress<WorkLog.Location.Address>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\")) left outer join \"Employee\" \"Employee<WorkLog.Location.Address.Employees>\" on ((\"EmployeeAddress<WorkLog.Location.Address>\".\"EmployeeId\" = \"Employee<WorkLog.Location.Address.Employees>\".\"Id\")) order by \"Employee<WorkLog.Employee>\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void OrderByDynamicEnumerableViaClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderByViaClassFilter(
               new WorkLog.DynamicOrderByEnumerable()
               {
                   OrderBys = new List<OrderBy>()
                   {
                       new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Ascending),
                       new OrderBy(nameof(WorkLog), nameof(WorkLog.Id), OrderByDirection.Descending)
                   }
               }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc, \"WorkLog\".\"Id\" desc", sql);
        }

        [TestMethod]
        public void OrderByDynamicEnumerableViaNavigationClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderByViaNavigationClassFilter(
                new WorkLog.NavigationDynamicOrderByEnumerable()
                {
                    Employee = new Employee.DynamicOrderBy()
                    {
                        Order = new[] { new OrderBy(nameof(WorkLog), nameof(WorkLog.Id)) }
                    }
                }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by \"WorkLog\".\"Id\" asc", sql);
        }

        [TestMethod]
        public void OrderByDynamicEnumerableViaClassFilter_EmptyCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderByViaClassFilter(
                new WorkLog.DynamicOrderByEnumerable()
                {
                    OrderBys = new List<OrderBy>()
                }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void OrderByDynamicEnumerableViaClassFilter_NullCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderByViaClassFilter(
                new WorkLog.DynamicOrderByEnumerable()
                {
                    OrderBys = null
                }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        //[TestMethod]
        //public void OrderBy_ThrowsExceptionForTypeWithMultipleProperties()
        //{
        //    var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.ILLEGAL_GetOrderedWorkLogs));
        //    try
        //    {
        //        methodParser.SqlFor(methodInfo);
        //        Assert.Fail("Expected exception to be thrown");
        //    }
        //    catch (InvalidOrderByException ex)
        //    {
        //        Assert.IsTrue(ex.Message.Contains("contains more than one property"), "Expected message to note that the class specified has more than one property.");
        //    }
        //    catch (Exception ex)
        //    {
        //        Assert.Fail($"Expected InvalidOrderByException to be thrown. Got {ex.GetType()}");
        //    }
        //}

        [TestMethod]
        public void Offset_NoOrderBy_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetNextWorkLogs));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" order by (select 1) offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Offset_RetainsOrderBy_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetNextWorkLogsWithOrder(1, OrderByDirection.Ascending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" order by \"WorkLog0\".\"StartDate\" asc offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Offset_RetainsPrimaryWhereClause_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetNextWorkLogsWithPrimaryTableFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" where ((\"WorkLog0\".\"StartDate\" = @startDate)) order by (select 1) offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Offset_RetainsNavigationWhereClause_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetNextWorkLogsWithNavigationTableFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog0\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name))))) order by (select 1) offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }
        
        [TestMethod]
        public void OffsetViaClassFilter_NoOrderBy_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.GetNextWorkLogsViaClassFilter(new WorkLog.FilterWithOffset() { Offset = 50 }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" order by (select 1) offset @filterOffset rows) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void OffsetViaClassFilter_RetainsPrimaryWhereClause_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.GetNextWorkLogsViaClassFilterAndParameter(new WorkLog.FilterWithOffsetAndParameter() { Offset = 50, StartDate = DateTime.Today }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" where ((\"WorkLog0\".\"StartDate\" = @StartDate)) order by (select 1) offset @filterOffset rows) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void OffsetViaClassFilter_RetainsNavigationWhereClause_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.GetNextWorkLogsWithNavigationTableFilterWithOffset(new WorkLog.GetByEmployeeNameFilterWithOffset() { Offset = 50, Employee = new Employee.EmployeeNameFilter() { Name = "something" }}));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog0\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name))))) order by (select 1) offset @filterOffset rows) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Fetch_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.TakeWorkLogs));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" order by (select 1) offset 0 rows fetch next @take rows only) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void OffsetFetch_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.SkipTakeWorkLogs));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" order by (select 1) offset @skip rows fetch next @take rows only) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void Fetch_OneTableOnly_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.TakeWorkLogsOnly));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by (select 1) offset 0 rows fetch next @take rows only", sql);
        }

        [TestMethod]
        public void Fetch_OneTableOnly_RetainsWhereClause()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.TakeWorkLogsOnlyWithFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" = @id)) order by (select 1) offset 0 rows fetch next @take rows only", sql);
        }

        [TestMethod]
        public void FetchViaClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.TakeWorkLogsViaClassFilter(new WorkLog.FilterWithFetch()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" order by (select 1) offset 0 rows fetch next @filterFetch rows only) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void OffsetFetchViaClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.SkipTakeWorkLogsViaClassFilter(new WorkLog.FilterWithOffsetFetch()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog0\".\"Id\" from \"WorkLog\" \"WorkLog0\" order by (select 1) offset @filterOffset rows fetch next @filterFetch rows only) \"offset_WorkLog\" inner join \"WorkLog\" on ((\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\")) left outer join \"Employee\" on ((\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\"))", sql);
        }

        [TestMethod]
        public void FetchViaClassFilter_OneTableOnly_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.TakeWorkLogsOnlyViaClassFilter(new WorkLog.FilterWithFetch()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" order by (select 1) offset 0 rows fetch next @filterFetch rows only", sql);
        }

        [TestMethod]
        public void FetchViaClassFilter_OneTableOnly_RetainsWhereClause()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.TakeWorkLogsOnlyWithFilterViaClassFilter(new WorkLog.FilterWithFetchAndParameter()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" = @Id)) order by (select 1) offset 0 rows fetch next @filterFetch rows only", sql);
        }

        [TestMethod]
        public void EnumReturnProperty_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetAddressesWithEnumClassification));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Address\".\"Id\" \"Id\", \"Address\".\"Classification\" \"Classification\" from \"Address\"", sql);
        }

        [TestMethod]
        public void GetViaRelationManyToOne_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeIdsForWorkLogLocationId));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where ((exists (select 1 from \"WorkLog\" \"WorkLog0\" where ((\"WorkLog0\".\"EmployeeId\" = \"Employee\".\"Id\") and (\"WorkLog0\".\"LocationId\" = @WorkLog0LocationId)))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToMany_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToMany_WithDifferingParameterName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeNameWithDifferingParameterName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))))", sql);
        }

        [TestMethod]
        public void GetViaRelationManyToMany_WithIntermediateTableSpecified_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeIdsForStreetAddress));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where ((exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress))))))))", sql);
        }

        [TestMethod]
        public void GetViaRelationManyToOneViaClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeIdsForWorkLogLocationIdClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where ((exists (select 1 from \"WorkLog\" \"WorkLog0\" where ((\"WorkLog0\".\"EmployeeId\" = \"Employee\".\"Id\") and (\"WorkLog0\".\"LocationId\" = @WorkLog0LocationId)))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToManyViaClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeNameViaClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToManyViaClassFilter_WithDifferingParameterName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeNameWithDifferingParameterNameViaClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))))", sql);
        }

        [TestMethod]
        public void GetViaRelationManyToManyViaClassFilter_WithIntermediateTableSpecified_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeIdsForStreetAddressViaClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where ((exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress))))))))", sql);
        }

        [TestMethod]
        public void TableValuedFunction_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.itvf_GetWorkLogsByEmployeeId(2));

            Assert.AreEqual("select \"itvf_GetWorkLogsByEmployeeId\".\"Id\" \"Id\" from itvf_GetWorkLogsByEmployeeId(@empId)", sql);
        }

        //[TestMethod]
        //public void TableValuedFunctionWithClassParameters_ReturnsExpectedSql()
        //{
        //    var sql = GetSqlForCall(() => this.monolithicRepository.itvf_GetWorkLogsByEmployeeIdWithClassParameters(new itvf_GetWorkLogsByEmployeeId.Parameters()));

        //    Assert.AreEqual("select \"itvf_GetWorkLogsByEmployeeId\".\"Id\" \"Id\" from itvf_GetWorkLogsByEmployeeId(@EmpId)", sql);
        //}

        [TestMethod]
        public void TableValuedFunction_WithFilterParams_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.itvf_GetWorkLogsByEmployeeId(2, DateTime.Today));

            Assert.AreEqual("select \"itvf_GetWorkLogsByEmployeeId\".\"Id\" \"Id\" from itvf_GetWorkLogsByEmployeeId(@empId) where ((\"itvf_GetWorkLogsByEmployeeId\".\"StartDate\" > @startDate))", sql);
        }

        [TestMethod]
        public void Count_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.CountWorkLogs());

            Assert.AreEqual("select count(1) \"Count\" from (select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\") Subquery", sql);
        }

        #endregion Queries

        #region Insert 

        [TestMethod]
        public void InsertSingle_Void_ValuesByParam_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.InsertEmployeeWithAttributeTableNameWithValuesByParams));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("insert \"Employee\"(\"Name\") values(@name)", sql);
        }

        //[TestMethod]
        //public void InsertSingle_OutputId_ValuesByParam_ReturnsExpectedSql()
        //{
        //    var sql = GetSqlForCall(() => this.monolithicRepository.InsertEmployeeWithAttributeTableNameWithValuesByParamsOutputId("bah"));

        //    AssertSqlEqual("declare @insertedEmployee table(\"Id\" int, \"_index\" int)\n" +
        //                   "declare @EmployeeLookup table(\"Name\" nvarchar(max), \"_index\" int)\n" +
        //                   "insert @EmployeeLookup(\"Name\", \"_index\") values(@Name0, 0)\n" +
        //                   "merge \"Employee\" using (select \"Name\", \"_index\" from @EmployeeLookup) as i (\"Name\",\"_index\") on (1 = 0)\n" +
        //                   " when not matched then\n" +
        //                   " insert (\"Name\") values(\"i\".\"Name\") output \"inserted\".\"Id\", \"i\".\"_index\" into @insertedEmployee(\"Id\", \"_index\");\n" +
        //                   "select \"Employee\".\"Id\" \"Id\" from \"Employee\" inner join @insertedEmployee \"i\" on ((\"Employee\".\"Id\" = \"i\".\"Id\")) order by \"i\".\"_index\"", sql);
        //}

        [TestMethod]
        public void InsertSingle_Void_ValuesByDetectedClass_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.InsertEmployeeWithAttributeWithValuesByDetectedClass));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("insert \"Employee\"(\"Name\") values(@Name)", sql);
        }

        [TestMethod]
        public void InsertSingle_OutputId_ValuesByDetectedClass_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertEmployeeWithAttributeWithValuesByDetectedClassReturnId(
               new Employee.InsertFields() { Name = "bah" }));

            AssertSqlEqual("declare @insertedEmployee table(\"Id\" int, \"_index\" int)\n" +
                           "declare @EmployeeLookup table(\"Name\" nvarchar(max), \"_index\" int)\n" +
                           "insert @EmployeeLookup(\"Name\", \"_index\") values(@Name0, 0)\n" +
                           "merge \"Employee\" using (select \"Name\", \"_index\" from @EmployeeLookup) as i (\"Name\",\"_index\") on (1 = 0)\n" +
                           " when not matched then\n" +
                           " insert (\"Name\") values(\"i\".\"Name\") output \"inserted\".\"Id\", \"i\".\"_index\" into @insertedEmployee(\"Id\", \"_index\");\n" +
                           "select \"Employee\".\"Id\" \"Id\" from \"Employee\" inner join @insertedEmployee \"i\" on ((\"Employee\".\"Id\" = \"i\".\"Id\")) order by \"i\".\"_index\"", sql);
        }

        [TestMethod]
        public void InsertMultiple_Void_ValuesByDetectedClass_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleEmployeesWithAttributeWithValuesByDetectedClass(
                new Employee.InsertFields[] { new Employee.InsertFields() { Name = "bah" }, new Employee.InsertFields() { Name = "baah" } }));

            AssertSqlEqual("declare @EmployeeLookup table(\"Name\" nvarchar(max), \"_index\" int)\n" +
                           "insert @EmployeeLookup(\"Name\", \"_index\") values(@Name0, 0), (@Name1, 1)\n" +
                           "merge \"Employee\" using (select \"Name\", \"_index\" from @EmployeeLookup) as i (\"Name\",\"_index\") on (1 = 0)\n" +
                           " when not matched then\n" +
                           " insert (\"Name\") values(\"i\".\"Name\");", sql);
        }

        [TestMethod]
        public void InsertMultiple_Void_ValuesByDetectedClass_MultipleProperties_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleAddressesWithAttributeWithValuesByDetectedClass(
                new Address.InsertFields[]
                {
                    new Address.InsertFields() { StreetAddress = "123 fake", City = "Seattle", State = "WA" },
                    new Address.InsertFields() { StreetAddress = "456 fake", City = "Portland", State = "OR" }
                }));

            AssertSqlEqual("declare @AddressLookup table(\"StreetAddress\" nvarchar(max), \"City\" nvarchar(max), \"State\" nvarchar(max), \"_index\" int)\n" +
                           "insert @AddressLookup(\"StreetAddress\", \"City\", \"State\", \"_index\") values(@StreetAddress0, @City0, @State0, 0), (@StreetAddress1, @City1, @State1, 1)\n" +
                           "merge \"Address\" using (select \"StreetAddress\", \"City\", \"State\", \"_index\" from @AddressLookup) as i (\"StreetAddress\",\"City\",\"State\",\"_index\") on (1 = 0)\n" +
                           " when not matched then\n" +
                           " insert (\"StreetAddress\", \"City\", \"State\") values(\"i\".\"StreetAddress\", \"i\".\"City\", \"i\".\"State\");", sql);
        }

        [TestMethod]
        public void InsertMultiple_OutputIds_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleEmployeesAndReturnIds(new Employee.InsertFields[] { new Employee.InsertFields() { Name = "bah" }, new Employee.InsertFields() { Name = "bah" }, new Employee.InsertFields() { Name = "bah" } }));

            AssertSqlEqual("declare @insertedEmployee table(\"Id\" int, \"_index\" int)\n" +
                           "declare @EmployeeLookup table(\"Name\" nvarchar(max), \"_index\" int)\n" +
                           "insert @EmployeeLookup(\"Name\", \"_index\") values(@Name0, 0), (@Name1, 1), (@Name2, 2)\n" +
                            "merge \"Employee\" using (select \"Name\", \"_index\" from @EmployeeLookup) as i (\"Name\",\"_index\") on (1 = 0)\n" +
                            " when not matched then\n" +
                            " insert (\"Name\") values(\"i\".\"Name\") output \"inserted\".\"Id\", \"i\".\"_index\" into @insertedEmployee(\"Id\", \"_index\");\n" +
                            "select \"Employee\".\"Id\" \"Id\" from \"Employee\" inner join @insertedEmployee \"i\" on ((\"Employee\".\"Id\" = \"i\".\"Id\")) order by \"i\".\"_index\"", sql);
        }

        // WIP
        // [TestMethod]
        // public void InsertMultipleWithNavigationProperty_Void_ReturnsExpectedSql()
        // {
        //     var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(
        //             new Employee.InsertFieldsWithWorkLogs[]
        //             {
        //                 new Employee.InsertFieldsWithWorkLogs() 
        //                 { 
        //                     Name = "bah", 
        //                     WorkLogs = new []
        //                     {
        //                         new WorkLog.DataFields() { StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2) },
        //                         new WorkLog.DataFields() { StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2) }
        //                     }
        //                 }
        //             }));
        //
        //     AssertSqlEqual("declare @insertedEmployee table(Id int, _index int)\n" +
        //                    "declare @EmployeeLookup table(Name nvarchar(max), _index int)\n" +
        //                    "insert @EmployeeLookup(Name, _index) values(@Name0, 0), (@Name1, 1), (@Name2, 2)\n" +
        //                    "merge Employee using (select Name, _index from @EmployeeLookup) as i (Name,_index) on (1 = 0)\n" +
        //                    " when not matched then\n" +
        //                    " insert (Name) values(i.Name) output inserted.Id, i._index into @insertedEmployee(Id, _index);\n" +
        //                    "declare @insertedEmployeeWorkLog table(Id int, _index int)\n" +
        //                    "declare @EmployeeWorkLogLookup table(Id int, StartDate datetime, EndDate datetime, _index int, EmployeeIndex int, EmployeeId int)\n" +
        //                    "insert @EmployeeWorkLogLookup(StartDate, EndDate, _index, EmployeeIndex) values (@WorkLogStartDate0, @WorkLogEndDate0, 0, 2)\n" +
        //                    "update @EmployeeWorkLogLookup set EmployeeId=i.Id from @EmployeeWorkLogLookup inner join @insertedEmployee i on i._index = EmployeeIndex\n" +
        //                    "merge WorkLog using(select StartDate, EndDate, _index, EmployeeId  from @EmployeeWorkLogLookup) as i(StartDate, EndDate, _index, EmployeeId) on (1 = 0)\n" +
        //                    " when not matched then\n" +
        //                    " insert (StartDate, EndDate, EmployeeId) values(i.StartDate, i.EndDate, i.EmployeeId) output inserted.Id, i._index into @insertedEmployeeWorkLog(Id, _index);", sql);
        // }

        // [TestMethod]
        // public void InsertMultiple_WithNavigationEntities_ReturnsExpectedSql()
        // {
        //     var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleEmployeesAndReturnIds(new Employee.InsertFields[] { new Employee.InsertFields() { Name = "bah" }}));
        //
        //     AssertSqlEqual("declare @insertedEmployee table(Id int)\n" +
        //                    "insert into Employee(Name) output inserted.Id into @insertedEmployee(Id) values(@Name0)\n" +
        //                    "select Employee.Id \"Id\" from \"Employee\" where exists (select 1 from @insertedEmployee where ((Employee.Id = Id)))", sql);
        // }


        // [TestMethod]
        // public void InsertSingle_Void_ValuesByUnknownClass_ViaAttribute_ReturnsExpectedSql()
        // {
        //     var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.InsertEmployeeWithAttributeWithValuesByUnknownClass));
        //     var sql = GetSqlFor(methodInfo);
        //
        //     Assert.AreEqual("insert into Employee(Name) values(@Name)", sql);
        // }

        #endregion Insert

        #region Delete

        [TestMethod]
        public void Delete_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.DeleteEmployeeWithAttributeTableNameWithValuesByParams("bob"));

            AssertSqlEqual("delete from \"Employee\" where ((\"Employee\".\"Name\" = @name))", sql);
        }

        #endregion Delete

        #region Update 

        [TestMethod]
        public void Update_Void_WithNoFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateAllEmployees("bob"));

            AssertSqlEqual("update \"Employee\" set \"Name\" = @name from \"Employee\"", sql);
        }

        [TestMethod]
        public void Update_Void_WithParamFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateEmployeeById("bob", 1));

            AssertSqlEqual("update \"Employee\" set \"Name\" = @name from \"Employee\" where ((\"Employee\".\"Id\" = @id))", sql);
        }

        [TestMethod]
        public void Update_Void_WithMultipleSetFields_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateAllWorkLogsStartDateAndEndDate(DateTime.Today, DateTime.Today));

            AssertSqlEqual("update \"WorkLog\" set \"StartDate\" = @startDate, \"EndDate\" = @endDate from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void Update_Void_WithSetFieldsClass_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateAllWorkLogsStartDateAndEndDateSetClass(new WorkLog.SetDateFields()
            {
                StartDate = DateTime.Today,
                EndDate = DateTime.Today
            }));

            AssertSqlEqual("update \"WorkLog\" set \"StartDate\" = @workLogDatesStartDate, \"EndDate\" = @workLogDatesEndDate from \"WorkLog\"", sql);
        }

        // todo
        //[TestMethod]
        //public void Update_Void_WithSetAndFilterFieldsClass_ReturnsExpectedSql()
        //{
        //    var sql = GetSqlForCall(() => this.monolithicRepository.UpdateAllWorkLogsStartDateAndEndDateSetAndFilterClass(new WorkLog.SetDatesWithIdFilter()
        //    {
        //        StartDate = DateTime.Today,
        //        EndDate = DateTime.Today,
        //        Id = 1
        //    }));

        //    AssertSqlEqual("update \"WorkLog\" set \"StartDate\" = @workLogStartDate, \"EndDate\" = @workLogEndDate from \"WorkLog\" where ((\"WorkLog\".\"Id\" = @workLogId))", sql);
        //}



        #endregion Update

        #region Exceptions

        [TestMethod]
        public void WhenOutputColumnNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingColumnName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database column for property IInvalidColumn.WorkLogId. Column WorkLogId does not exist in table WorkLog.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenOutputTableNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingTableName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database table for type NonExistingTable.IId. Table NonExistingTable does not exist.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenNestedOutputTableNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingNestedTableName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database table for property IInvalidNestedColumn.NonExistingTable of type System.Collections.Generic.IEnumerable`1[SigQL.Tests.Common.Databases.Labor.NonExistingTable+IId]. Table NonExistingTable does not exist.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenParameterColumnNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingParameterColumnName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database column for parameter nonExistent. Column nonExistent does not exist in table WorkLog.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenParameterColumnNotExistsWithAlias_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingParameterColumnNameWithAlias));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database column for parameter id. Column nonExistent does not exist in table WorkLog.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenPropertyColumnNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingPropertyColumnName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database column for property IInvalidColumn.WorkLogId. Column WorkLogId does not exist in table WorkLog.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenPropertyColumnNotExistsWithAlias_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingPropertyColumnNameWithAlias));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database column for property IInvalidColumnWithAlias.Id. Column WorkLogId does not exist in table WorkLog.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenPropertyTableNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingPropertyTableName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database table for property IInvalidNestedColumn.NonExistingTable of type System.Collections.Generic.IEnumerable`1[SigQL.Tests.Common.Databases.Labor.NonExistingTable+IId]. Table NonExistingTable does not exist.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenPropertyTableIsNotRelatedToParent_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingPropertyForeignKey));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database foreign key for property IInvalidAddressRelation.Address. No foreign key between WorkLog and Address could be found.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenViaRelationSpecifiesUnknownColumn_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingViaRelationColumnName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database column for parameter locationId with ViaRelation[\"Employee->WorkLog.NonExistent\"]. Column NonExistent does not exist in table WorkLog.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenViaRelationSpecifiesUnknownTable_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingViaRelationTableName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database table for parameter locationId with ViaRelation[\"Employee->NonExistent.Id\"]. Table NonExistent does not exist.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenViaRelationSpecifiesUnknownForeignKey_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingViaRelationForeignKey));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database foreign key for parameter locationId with ViaRelation[\"Employee->Location.Id\"]. No foreign key between Employee and Location could be found.",
                        ex.Message);
                    throw;
                }
            });
        }

        // prevent sql injection via dynamic order by parameters
        [TestMethod]
        public void OrderByDynamicEnumerable_UnknownTable_ThrowsException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                GetSqlForCall(() =>
                {
                    try
                    {
                        this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
                            new List<OrderBy>()
                            {
                                new OrderBy(nameof(NonExistingTable), nameof(WorkLog.StartDate),
                                    OrderByDirection.Ascending)
                            });
                    }
                    catch (InvalidIdentifierException ex)
                    {
                        Assert.AreEqual(
                            "Unable to identify matching database table for order by parameter orders with specified table name \"NonExistingTable\". Table NonExistingTable could not be found.",
                            ex.Message);
                        throw;
                    }
                });
            });
        }

        // prevent sql injection via dynamic order by parameters
        [TestMethod]
        public void OrderByDynamicEnumerable_UnknownColumn_ThrowsException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                GetSqlForCall(() =>
                {
                    try
                    {
                        this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
                            new List<OrderBy>()
                            {
                                new OrderBy(nameof(WorkLog), "NonExistent",
                                    OrderByDirection.Ascending)
                            });
                    }
                    catch (InvalidIdentifierException ex)
                    {
                        Assert.AreEqual(
                            "Unable to identify matching database column for order by parameter orders with specified column name \"NonExistent\". Column NonExistent does not exist in table WorkLog.",
                            ex.Message);
                        throw;
                    }
                });
            });
        }

        [TestMethod]
        public void OrderByEnumerable_UnknownColumn_ThrowsException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                GetSqlForCall(() =>
                {
                    try
                    {
                        this.monolithicRepository.INVALID_GetOrderedWorkLogs();
                    }
                    catch (InvalidIdentifierException ex)
                    {
                        Assert.AreEqual(
                            "Unable to identify matching database column for parameter theStartDate. Column theStartDate does not exist in table WorkLog.",
                            ex.Message);
                        throw;
                    }
                });
            });
        }

        #endregion Exceptions

        private void AssertSqlEqual(string expected, string actual)
        {
            var expectedCleaned = RemoveExtraWhitespace(expected);
            var actualCleaned = RemoveExtraWhitespace(actual);
            Assert.AreEqual(expectedCleaned, actualCleaned);
        }

        private string RemoveExtraWhitespace(string value)
        {
            value = value.Replace("\r", string.Empty);
            return Regex.Replace(value, "\\s[+]", " ");
        }

        private string GetSqlFor(MethodInfo methodInfo)
        {
            return this.methodParser.SqlFor(methodInfo).GetPreparedStatement(new ParameterArg[0]).CommandText;
        }

        private string GetSqlForCall(Action call)
        {
            call();
            return this.preparedSqlStatements.First().CommandText;
        }
    }
}
