﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Diagnostics;
using SigQL.Exceptions;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Tests.Infrastructure;
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
            var workLogDatabaseConfigurator = new MockWorkLogDatabaseConfigurator();
            workLogDatabaseConfiguration = workLogDatabaseConfigurator.WorkLogDatabaseConfiguration;
            methodParser = workLogDatabaseConfigurator.MethodParser;
            var preparedStatementCollectorFactory = new PreparedStatementCollectorFactory(workLogDatabaseConfiguration);
            preparedSqlStatements = new List<PreparedSqlStatement>();
            monolithicRepository = preparedStatementCollectorFactory.Build<IMonolithicRepository>(preparedSqlStatements);
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
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWithAliasedColumnName(1));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"WorkLogId\" from \"WorkLog\" where (\"WorkLog\".\"Id\" = @id)", sql);
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

            Assert.AreEqual("select \"WorkLog<WorkLog>\".\"Id\" \"Id\", \"WorkLog<WorkLog>\".\"StartDate\" \"StartDate\", \"WorkLog<WorkLog>\".\"EndDate\" \"EndDate\", \"WorkLog<WorkLog>\".\"EmployeeId\" \"EmployeeId\", \"WorkLog<WorkLog>\".\"LocationId\" \"LocationId\", \"Employee<WorkLog.Employee>\".\"Id\" \"Employee.Id\", \"Employee<WorkLog.Employee>\".\"Name\" \"Employee.Name\", \"Address<WorkLog.Employee.Addresses>\".\"Id\" \"Employee.Addresses.Id\", \"Address<WorkLog.Employee.Addresses>\".\"StreetAddress\" \"Employee.Addresses.StreetAddress\", \"Address<WorkLog.Employee.Addresses>\".\"City\" \"Employee.Addresses.City\", \"Address<WorkLog.Employee.Addresses>\".\"State\" \"Employee.Addresses.State\", \"Address<WorkLog.Employee.Addresses>\".\"Classification\" \"Employee.Addresses.Classification\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Id\" \"Employee.Addresses.Locations.Id\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Name\" \"Employee.Addresses.Locations.Name\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" \"Employee.Addresses.Locations.AddressId\", \"Location<WorkLog.Location>\".\"Id\" \"Location.Id\", \"Location<WorkLog.Location>\".\"Name\" \"Location.Name\", \"Location<WorkLog.Location>\".\"AddressId\" \"Location.AddressId\", \"Address<WorkLog.Location.Address>\".\"Id\" \"Location.Address.Id\", \"Address<WorkLog.Location.Address>\".\"StreetAddress\" \"Location.Address.StreetAddress\", \"Address<WorkLog.Location.Address>\".\"City\" \"Location.Address.City\", \"Address<WorkLog.Location.Address>\".\"State\" \"Location.Address.State\", \"Address<WorkLog.Location.Address>\".\"Classification\" \"Location.Address.Classification\", \"Employee<WorkLog.Location.Address.Employees>\".\"Id\" \"Location.Address.Employees.Id\", \"Employee<WorkLog.Location.Address.Employees>\".\"Name\" \"Location.Address.Employees.Name\" from \"WorkLog\" \"WorkLog<WorkLog>\" left outer join \"Employee\" \"Employee<WorkLog.Employee>\" on (\"WorkLog<WorkLog>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Employee>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Employee.Addresses>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Employee.Addresses.Locations>\" on (\"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Location>\" on (\"WorkLog<WorkLog>\".\"LocationId\" = \"Location<WorkLog.Location>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Location.Address>\" on (\"Location<WorkLog.Location>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Location.Address>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"Employee\" \"Employee<WorkLog.Location.Address.Employees>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"EmployeeId\" = \"Employee<WorkLog.Location.Address.Employees>\".\"Id\")", sql);
        }

        [TestMethod]
        public void GetTableCollection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetAllEmployeeFields));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\"", sql);
        }

        [TestMethod]
        public void Get_WithTableSqlIdentifierAttribute_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetWithSqlIdentifierAttribute());

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"WorkLog\".\"StartDate\" \"StartDate\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void Get_WithNavigationTableWithSqlIdentifierAttribute_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetNavigationPropertyWithSqlIdentifierAttribute());

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }
        
        [TestMethod]
        public void Get_WithParentSqlIdentifierAttribute()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetWithParentSqlIdentifierAttribute());
            AssertSqlEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\"", sql);
        }

        [TestMethod]
        public void GetSingleWithWhere_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.Get));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Id\" = @id)", sql);
        }

        [TestMethod]
        public void GetSingleWithNot_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetNot(1));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Id\" != @id)", sql);
        }

        [TestMethod]
        public void GetSingleWithNull_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByName(null));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Name\" is null)", sql);
        }

        [TestMethod]
        public void GetSingleWithNotNull_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByNameNot(null));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Name\" is not null)", sql);
        }

        [TestMethod]
        public void GetSingle_WithPluralTableName_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetEmployeesPlural(1));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Id\" = @id)", sql);
        }

        [TestMethod]
        public void GetSingle_WithSingularTableName_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetEmployeeStatusSingular(1));

            Assert.AreEqual("select \"EmployeeStatuses\".\"Id\" \"Id\" from \"EmployeeStatuses\" where (\"EmployeeStatuses\".\"Id\" = @id)", sql);
        }

        [TestMethod]
        public void GetSingleWithFilterIsNullProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByNameFilter(new Employee.EmployeeNameFilter() { Name = null }));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Name\" is null)", sql);
        }

        [TestMethod]
        public void GetSingleWithFilterIsNotNullProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetByNameFilterNot(new Employee.EmployeeNameNotFilter() { Name = null }));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Name\" is not null)", sql);
        }

        [TestMethod]
        public void GetSingleWithFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetByFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Id\" = @Id)", sql);
        }

        [TestMethod]
        public void GetSingleWithFilterUsingSqlIdentifierAttribute_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetByFilterWithSqlIdentifierAttribute(new MyEmployeeIdFilter() { Id = 1 }));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Id\" = @Id)", sql);
        }

        [TestMethod]
        public void GetSingleWithParameterSpecifiedColumnName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWithSpecifiedColumnName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Id\" = @employeeId)", sql);
        }

        [TestMethod]
        public void GetSingleWithPropertySpecifiedColumnName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWithFilterSpecifiedColumnName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Employee\".\"Name\" \"Name\" from \"Employee\" where (\"Employee\".\"Id\" = @EmployeeId)", sql);
        }

        [TestMethod]
        public void GetSingleWithNestedPropertySpecifiedColumnName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWithFilterNestedSpecifiedColumnName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress)))))))", sql);
        }

        [TestMethod]
        public void GetWithSingleNavigationPropertyEntity_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithEmployee));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void GetJoinRelationAttribute_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetWithJoinRelationAttribute());

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"WorkLogEmployeeView\".\"RowNumber\" \"View.RowNumber\", \"WorkLogEmployeeView\".\"WorkLogId\" \"View.WorkLogId\", \"WorkLogEmployeeView\".\"StartDate\" \"View.StartDate\", \"WorkLogEmployeeView\".\"EndDate\" \"View.EndDate\", \"WorkLogEmployeeView\".\"EmployeeId\" \"View.EmployeeId\", \"WorkLogEmployeeView\".\"EmployeeName\" \"View.EmployeeName\" from \"WorkLog\" left outer join (select ROW_NUMBER() over(order by \"WorkLogId\") \"RowNumber\", \"WorkLogId\", \"StartDate\", \"EndDate\", \"EmployeeId\", \"EmployeeName\" from \"WorkLogEmployeeView\") \"WorkLogEmployeeView\" on (\"WorkLog\".\"EmployeeId\" = \"WorkLogEmployeeView\".\"EmployeeId\")", sql);
        }

        [TestMethod]
        public void GetWithMultipleJoinRelationAttributes_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetWithMultipleJoinRelationAttributes());

            Assert.AreEqual("select \"WorkLog<IWorkLogWithMultipleJoinRelationAttributes>\".\"Id\" \"Id\", \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"RowNumber\" \"View.RowNumber\", \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"WorkLogId\" \"View.WorkLogId\", \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"StartDate\" \"View.StartDate\", \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"EndDate\" \"View.EndDate\", \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"EmployeeId\" \"View.EmployeeId\", \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"EmployeeName\" \"View.EmployeeName\", \"WorkLog<IWorkLogWithMultipleJoinRelationAttributes.WorkLogs>\".\"Id\" \"WorkLogs.Id\" from \"WorkLog\" \"WorkLog<IWorkLogWithMultipleJoinRelationAttributes>\" left outer join (select ROW_NUMBER() over(order by \"WorkLogId\") \"RowNumber\", \"WorkLogId\", \"StartDate\", \"EndDate\", \"EmployeeId\", \"EmployeeName\" from \"WorkLogEmployeeView\") \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\" on (\"WorkLog<IWorkLogWithMultipleJoinRelationAttributes>\".\"EmployeeId\" = \"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"EmployeeId\") left outer join \"WorkLog\" \"WorkLog<IWorkLogWithMultipleJoinRelationAttributes.WorkLogs>\" on (\"WorkLogEmployeeView<IWorkLogWithMultipleJoinRelationAttributes.View>\".\"WorkLogId\" = \"WorkLog<IWorkLogWithMultipleJoinRelationAttributes.WorkLogs>\".\"Id\")", sql);
        }

        [TestMethod]
        public void GetJoinRelationAttribute_WithMultiTableRelationalPath_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetJoinAttributeWithMultiTableRelationalPath());

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Address\".\"Id\" \"Addresses.Id\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join \"EmployeeAddress\" on (\"Employee\".\"Id\" = \"EmployeeAddress\".\"EmployeeId\") left outer join \"Address\" on (\"EmployeeAddress\".\"AddressId\" = \"Address\".\"Id\")", sql);
        }

        [TestMethod]
        public void GetJoinRelationAttributeOnViewWithTableNavigationCollection_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetWithJoinRelationAttributeOnViewWithTableNavigationCollection());

            Assert.AreEqual("select \"WorkLogEmployeeView\".\"RowNumber\" \"RowNumber\", \"WorkLogEmployeeView\".\"StartDate\" \"StartDate\", \"WorkLogEmployeeView\".\"EndDate\" \"EndDate\", \"WorkLogEmployeeView\".\"EmployeeName\" \"EmployeeName\", \"Employee\".\"Id\" \"Employees.Id\", \"Address\".\"Id\" \"Employees.Addresses.Id\", \"Address\".\"StreetAddress\" \"Employees.Addresses.StreetAddress\" from (select ROW_NUMBER() over(order by \"StartDate\") \"RowNumber\", \"WorkLogId\", \"StartDate\", \"EndDate\", \"EmployeeId\", \"EmployeeName\" from \"WorkLogEmployeeView\") \"WorkLogEmployeeView\" left outer join \"Employee\" on (\"WorkLogEmployeeView\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join \"EmployeeAddress\" on (\"EmployeeAddress\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join \"Address\" on (\"EmployeeAddress\".\"AddressId\" = \"Address\".\"Id\")", sql);
        }

        [TestMethod]
        public void GetJoinRelationAttributeOnTableWithViewNavigationCollection_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetWithJoinRelationAttributeOnTableWithViewNavigationCollection());

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"WorkLogEmployeeView\".\"RowNumber\" \"View.RowNumber\", \"WorkLogEmployeeView\".\"StartDate\" \"View.StartDate\", \"WorkLogEmployeeView\".\"EndDate\" \"View.EndDate\", \"WorkLogEmployeeView\".\"EmployeeName\" \"View.EmployeeName\" from \"Employee\" left outer join (select ROW_NUMBER() over(order by \"StartDate\") \"RowNumber\", \"WorkLogId\", \"StartDate\", \"EndDate\", \"EmployeeId\", \"EmployeeName\" from \"WorkLogEmployeeView\") \"WorkLogEmployeeView\" on (\"Employee\".\"Id\" = \"WorkLogEmployeeView\".\"EmployeeId\")", sql);
        }


        [TestMethod]
        public void GetJoinRelationAttributeNestedNavigationCollection_ReturnsExpectedSql()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetWithNestedJoinRelationAttribute());

            Assert.AreEqual("select \"Address\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"WorkLogEmployeeView\".\"RowNumber\" \"Employee.View.RowNumber\", \"WorkLogEmployeeView\".\"StartDate\" \"Employee.View.StartDate\", \"WorkLogEmployeeView\".\"EndDate\" \"Employee.View.EndDate\", \"WorkLogEmployeeView\".\"EmployeeName\" \"Employee.View.EmployeeName\" from \"Address\" left outer join \"EmployeeAddress\" on (\"EmployeeAddress\".\"AddressId\" = \"Address\".\"Id\") left outer join \"Employee\" on (\"EmployeeAddress\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join (select ROW_NUMBER() over(order by \"StartDate\") \"RowNumber\", \"WorkLogId\", \"StartDate\", \"EndDate\", \"EmployeeId\", \"EmployeeName\" from \"WorkLogEmployeeView\") \"WorkLogEmployeeView\" on (\"Employee\".\"Id\" = \"WorkLogEmployeeView\".\"EmployeeId\")", sql);
        }

        [TestMethod]
        public void GetWithSingleNavigationPropertyInnerProjectionInterface_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithEmployeeNames));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\", \"Location\".\"Id\" \"Location.Id\", \"Location\".\"Name\" \"Location.Name\", \"Location\".\"AddressId\" \"Location.AddressId\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join \"Location\" on (\"WorkLog\".\"LocationId\" = \"Location\".\"Id\")", sql);
        }

        [TestMethod]
        public void GetWithNestedNavigationPropertyInnerProjectionInterfaces_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogWithLocationAndLocationAddress));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Location\".\"Id\" \"Location.Id\", \"Location\".\"Name\" \"Location.Name\", \"Address\".\"Id\" \"Location.Address.Id\", \"Address\".\"StreetAddress\" \"Location.Address.StreetAddress\" from \"WorkLog\" left outer join \"Location\" on (\"WorkLog\".\"LocationId\" = \"Location\".\"Id\") left outer join \"Address\" on (\"Location\".\"AddressId\" = \"Address\".\"Id\")", sql);
        }

        [TestMethod]
        public void Get_OneToMany_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetAddressWithLocations));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Address\".\"Id\" \"Id\", \"Location\".\"Id\" \"Locations.Id\", \"Location\".\"Name\" \"Locations.Name\", \"Location\".\"AddressId\" \"Locations.AddressId\" from \"Address\" left outer join \"Location\" on (\"Location\".\"AddressId\" = \"Address\".\"Id\")", sql);
        }

        [TestMethod]
        public void Get_ManyToMany_ViaInnerProjection_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeWithAddresses));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\", \"Address\".\"Id\" \"Addresses.Id\", \"Address\".\"StreetAddress\" \"Addresses.StreetAddress\" from \"Employee\" left outer join \"EmployeeAddress\" on (\"EmployeeAddress\".\"EmployeeId\" = \"Employee\".\"Id\") left outer join \"Address\" on (\"EmployeeAddress\".\"AddressId\" = \"Address\".\"Id\")", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"EmployeeId\" = @employeeId)", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"EmployeeId\" = @EmployeeId)", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name))))", sql);
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

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress)))))))", sql);
        }

        [TestMethod]
        public void Where_Like_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeesByNameWithLike));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (\"Employee\".\"Name\" like @name)", sql);
        }

        [TestMethod]
        public void Where_NotLike_ReturnsExpectedSql()
        {
            
            var sql = GetSqlForCall(() => monolithicRepository.GetEmployeesByNameWithNotLike(Like.FromUnsafeRawValue("name")));

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (\"Employee\".\"Name\" not like @name)", sql);
        }

        [TestMethod]
        public void Where_LikeInNavigationProperty_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsByEmployeeNameWithLike));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" like @Employee0Name))))", sql);
        }

        [TestMethod]
        public void Where_In_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyId(new List<int?>() { 1 }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" in (@id0))", sql);
        }

        [TestMethod]
        public void Where_In_IgnoreIfNullOrEmpty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(new List<int>()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (1 = 1)", sql);
        }

        [TestMethod]
        public void Where_In_IgnoreIfNull_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(null));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (1 = 1)", sql);
        }

        [TestMethod]
        public void Where_In_WithNullItem_SpecifiesInClauseAndChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyId(new List<int?>() { 1, null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" in (@id0)) or (\"WorkLog\".\"Id\" is null))", sql);
        }

        [TestMethod]
        public void Where_In_WithOnlyNullItem_OnlyChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyId(new List<int?>() { null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" is null)", sql);
        }

        [TestMethod]
        public void InParameter_CompositeKey()
        {
            var sql = this.GetSqlForCall(() => this.monolithicRepository.GetInWithCompositeKeys(new List<Address.CityAndState>()
            {
                new Address.CityAndState()
                {
                    City = "Seattle",
                    State = "WA"
                },
                new Address.CityAndState()
                {
                    City = "Concord",
                    State = "MA"
                },
                new Address.CityAndState()
                {
                    City = "San Diego",
                    State = "CA"
                }
            }));
            
            AssertSqlEqual("select \"Address\".\"Id\" \"Id\", \"Address\".\"StreetAddress\" \"StreetAddress\" from \"Address\" where (((\"Address\".\"City\" = @City0) and (\"Address\".\"State\" = @State0)) or ((\"Address\".\"City\" = @City1) and (\"Address\".\"State\" = @State1)) or ((\"Address\".\"City\" = @City2) and (\"Address\".\"State\" = @State2)))", sql);
        }

        [TestMethod]
        public void Where_NotIn_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdNotIn(new List<int?>() { 1 }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" not in (@id0))", sql);
        }

        [TestMethod]
        public void Where_NotIn_WithNullItem_SpecifiesInClauseAndChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdNotIn(new List<int?>() { 1, null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"Id\" not in (@id0)) and (\"WorkLog\".\"Id\" is not null))", sql);
        }

        [TestMethod]
        public void Where_NotIn_WithOnlyNullItem_OnlyChecksColumnForNull()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdNotIn(new List<int?>() { null }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" is not null)", sql);
        }
        
        [TestMethod]
        public void Where_InPlural_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyIdPlural(new List<int>() { 1 }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" in (@ids0))", sql);
        }
        
        [TestMethod]
        public void Where_NullParameter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetWorkLogsWithAnyId(null));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" in (select null where (0 = 1)))", sql);
        }

        [TestMethod]
        public void Where_GreaterThan_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanStartDate));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" > @startDate)", sql);
        }

        [TestMethod]
        public void Where_GreaterThanEqualTo_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanOrEqualToStartDate));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" >= @startDate)", sql);
        }

        [TestMethod]
        public void Where_GreaterThanWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" > @StartDate)", sql);
        }

        [TestMethod]
        public void Where_GreaterThanEqualToWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsGreaterThanOrEqualToStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" >= @StartDate)", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" < @startDate)", sql);
        }

        [TestMethod]
        public void Where_LessThanEqualTo_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsLessThanOrEqualToStartDate));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" <= @startDate)", sql);
        }

        [TestMethod]
        public void Where_LessThanWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsLessThanStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" < @StartDate)", sql);
        }

        [TestMethod]
        public void Where_LessThanEqualToWithClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsLessThanOrEqualToStartDateClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" <= @StartDate)", sql);
        }

        [TestMethod]
        public void Where_In_NavigationProperty_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogsByEmployeeNamesWithIn));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" in ()))))", sql);
        }

        [TestMethod]
        public void Where_In_ViaRelation_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.GetWorkLogsByEmployeeNamesViaRelation(new WorkLog.GetEmployeeNamesViaRelation()
                    {Names = new[] {"bob", "joe"}}));
            
            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" in (@Employee0Name0, @Employee0Name1)))))", sql);
        }

        [TestMethod]
        public void Where_In_ViaRelation_EmptyCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.GetWorkLogsByEmployeeNamesViaRelation(new WorkLog.GetEmployeeNamesViaRelation()
                    {Names = Array.Empty<string>()}));
            
            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1))", sql);
        }

        [TestMethod]
        public void Where_In_ViaRelation_NullCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.GetWorkLogsByEmployeeNamesViaRelation(new WorkLog.GetEmployeeNamesViaRelation()
                    {Names = null}));
            
            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1))", sql);
        }

        [TestMethod]
        public void Where_In_ViaNestedRelation_NullCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.GetWorkLogsByEmployeeNamesAndAddressCitiesViaRelation(new WorkLog.GetEmployeeNamesAndAddressCitiesViaRelation()
                    {EmployeeNames = null, AddressCities = new List<string>() { "City1" } }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and ((1 = 1) and (exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress00\" where ((\"EmployeeAddress00\".\"EmployeeId\" = \"Employee0\".\"Id\") and (exists (select 1 from \"Address\" \"Address000\" where ((\"Address000\".\"Id\" = \"EmployeeAddress00\".\"AddressId\") and (\"Address000\".\"City\" in (@Address000City0))))))))))))", sql);
        }

        [TestMethod]
        public void Where_In_ViaNestedAdjacentRelations_NullCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.GetWorkLogsByEmployeeNamesAndEmployeeIdsViaRelation(new WorkLog.GetEmployeeNamesAndEmployeeIdsViaRelation()
                    {EmployeeNames = null, EmployeeIds = new List<int>() { 1 } }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and ((1 = 1) and (\"Employee0\".\"Id\" in (@Employee0Id0))))))", sql);
        }

        [TestMethod]
        public void Where_In_ViaNestedAdjacentRelations_EmptyCollection_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.GetWorkLogsByEmployeeNamesAndEmployeeIdsViaRelation(new WorkLog.GetEmployeeNamesAndEmployeeIdsViaRelation()
                    {EmployeeNames = new List<string>(), EmployeeIds = new List<int>() { 1 } }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and ((1 = 1) and (\"Employee0\".\"Id\" in (@Employee0Id0))))))", sql);
        }

        [TestMethod]
        public void Where_In_ViaNestedAdjacentRelations_PopulatedCollections_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.GetWorkLogsByEmployeeNamesAndEmployeeIdsViaRelation(new WorkLog.GetEmployeeNamesAndEmployeeIdsViaRelation()
                    {EmployeeNames = new List<string>() { "bob" }, EmployeeIds = new List<int>() { 1 } }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and ((\"Employee0\".\"Name\" in (@Employee0Name0)) and (\"Employee0\".\"Id\" in (@Employee0Id0))))))", sql);
        }

        [TestMethod]
        public void Where_OrGroupByTwoColumnsOfSameTable()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.OrGroupByTwoColumnsOfSameTable(DateTime.Today, DateTime.Today));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" = @startDate) or (\"WorkLog\".\"EndDate\" = @endDate))", sql);
        }

        [TestMethod]
        public void Where_OrGroupByTwoGroupsForColumnsOfSameTable()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.OrGroupByTwoGroupsForColumnsOfSameTable(DateTime.Today, DateTime.Today, 1, 4));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (((\"WorkLog\".\"StartDate\" = @startDate) or (\"WorkLog\".\"EndDate\" = @endDate)) and ((\"WorkLog\".\"Id\" = @id) or (\"WorkLog\".\"EmployeeId\" = @employeeId)))", sql);
        }

        [TestMethod]
        public void Where_OrGroupByTwoColumnsOfAdjacentTablesViaRelation()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.OrGroupByTwoColumnsOfAdjacentTablesViaRelation(DateTime.Today, "joe", "new york"));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where ((\"WorkLog\".\"StartDate\" = @startDate) or (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))) or (exists (select 1 from \"Location\" \"Location0\" where ((\"Location0\".\"Id\" = \"WorkLog\".\"LocationId\") and (\"Location0\".\"Name\" = @Location0Name)))))", sql);
        }

        [TestMethod]
        public void Where_OrGroupWithOffsetFetch_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                monolithicRepository.TwoOrGroupsWithViaRelationAndOffsetFetch(new WorkLog.TwoOrGroupsWithViaRelation(), 1, 1));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" where (((\"WorkLog\".\"Id\" = @Id) or (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Id\" = @Employee0Id))))) and ((\"WorkLog\".\"StartDate\" = @StartDate) or (\"WorkLog\".\"EndDate\" = @EndDate))) order by (select 1) offset @skip rows fetch next @take rows only) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc, \"WorkLog\".\"StartDate\" desc, \"WorkLog\".\"EndDate\" asc", sql);
        }

        [TestMethod]
        public void OrderByDirectionViaClassFilterNavigationProperty_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => monolithicRepository.GetOrderedWorkLogsByClassFilterNavigationProperty(new WorkLog.OrderByDirectionEmployeeName() { Employee = new Employee.EmployeeNameOrder() {  Name = OrderByDirection.Ascending }}));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"Employee.Id\", \"Employee\".\"Name\" \"Employee.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc", sql);
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
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderByRelation(new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name), OrderByDirection.Ascending)));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void OrderByRelationDynamic_WhenTableDoesNotExistOnOutputType_ThrowsException()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderByRelation(new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name), OrderByDirection.Ascending)));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void OrderByRelationDynamic_ReturnsExpectedSqlWithAliasPath()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderByRelationCanonicalDataType(new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name), OrderByDirection.Ascending)));

            Assert.AreEqual("select \"WorkLog<WorkLog>\".\"Id\" \"Id\", \"WorkLog<WorkLog>\".\"StartDate\" \"StartDate\", \"WorkLog<WorkLog>\".\"EndDate\" \"EndDate\", \"WorkLog<WorkLog>\".\"EmployeeId\" \"EmployeeId\", \"WorkLog<WorkLog>\".\"LocationId\" \"LocationId\", \"Employee<WorkLog.Employee>\".\"Id\" \"Employee.Id\", \"Employee<WorkLog.Employee>\".\"Name\" \"Employee.Name\", \"Address<WorkLog.Employee.Addresses>\".\"Id\" \"Employee.Addresses.Id\", \"Address<WorkLog.Employee.Addresses>\".\"StreetAddress\" \"Employee.Addresses.StreetAddress\", \"Address<WorkLog.Employee.Addresses>\".\"City\" \"Employee.Addresses.City\", \"Address<WorkLog.Employee.Addresses>\".\"State\" \"Employee.Addresses.State\", \"Address<WorkLog.Employee.Addresses>\".\"Classification\" \"Employee.Addresses.Classification\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Id\" \"Employee.Addresses.Locations.Id\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Name\" \"Employee.Addresses.Locations.Name\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" \"Employee.Addresses.Locations.AddressId\", \"Location<WorkLog.Location>\".\"Id\" \"Location.Id\", \"Location<WorkLog.Location>\".\"Name\" \"Location.Name\", \"Location<WorkLog.Location>\".\"AddressId\" \"Location.AddressId\", \"Address<WorkLog.Location.Address>\".\"Id\" \"Location.Address.Id\", \"Address<WorkLog.Location.Address>\".\"StreetAddress\" \"Location.Address.StreetAddress\", \"Address<WorkLog.Location.Address>\".\"City\" \"Location.Address.City\", \"Address<WorkLog.Location.Address>\".\"State\" \"Location.Address.State\", \"Address<WorkLog.Location.Address>\".\"Classification\" \"Location.Address.Classification\", \"Employee<WorkLog.Location.Address.Employees>\".\"Id\" \"Location.Address.Employees.Id\", \"Employee<WorkLog.Location.Address.Employees>\".\"Name\" \"Location.Address.Employees.Name\" from \"WorkLog\" \"WorkLog<WorkLog>\" left outer join \"Employee\" \"Employee<WorkLog.Employee>\" on (\"WorkLog<WorkLog>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Employee>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Employee.Addresses>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Employee.Addresses.Locations>\" on (\"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Location>\" on (\"WorkLog<WorkLog>\".\"LocationId\" = \"Location<WorkLog.Location>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Location.Address>\" on (\"Location<WorkLog.Location>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Location.Address>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"Employee\" \"Employee<WorkLog.Location.Address.Employees>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"EmployeeId\" = \"Employee<WorkLog.Location.Address.Employees>\".\"Id\") order by \"Employee<WorkLog.Employee>\".\"Name\" asc", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by (select 1) offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void Offset_RetainsOrderBy_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetNextWorkLogsWithOrder(1, OrderByDirection.Ascending));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"WorkLog\".\"StartDate\" asc", sql);
        }

        [TestMethod]
        public void Offset_OrderByManyToOneRelation_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetNextWorkLogsWithOrder(1, new List<IOrderBy>() { new OrderBy(nameof(Employee), nameof(Employee.Name))}));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void Offset_OrderByManyToOneRelation_OrderByRelation_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetNextWorkLogsWithOrder(1, new List<IOrderBy>() { new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))}));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\") order by \"Employee\".\"Name\" asc", sql);
        }

        [TestMethod]
        public void Offset_OrderByOneToManyRelation_ThrowsException()
        {
            Exception thrownException = null;
            try
            {
                GetSqlForCall(() => this.monolithicRepository.GetNextWorkLogsWithDynamicOrder(1,
                    new List<IOrderBy>() {new OrderBy(nameof(Address), nameof(Address.City))}));
            }
            catch (InvalidOrderByException ex)
            {
                thrownException = ex;
            }
            
            Assert.IsNotNull(thrownException);
            Assert.AreEqual("Unable to order by Address.City when using OFFSET/FETCH, since it is not a many-to-one or one-to-one relationship with WorkLog. Relationship to table EmployeeAddress causes one-to-many or many-to-many cardinality.", thrownException.Message);
        }

        [TestMethod]
        public void Offset_DynamicOrderBy_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetNextWorkLogsWithDynamicOrder(1, new List<IOrderBy>() { new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate)) }));

            Assert.AreEqual("select \"WorkLog<WorkLog>\".\"Id\" \"Id\", \"WorkLog<WorkLog>\".\"StartDate\" \"StartDate\", \"WorkLog<WorkLog>\".\"EndDate\" \"EndDate\", \"WorkLog<WorkLog>\".\"EmployeeId\" \"EmployeeId\", \"WorkLog<WorkLog>\".\"LocationId\" \"LocationId\", \"Employee<WorkLog.Employee>\".\"Id\" \"Employee.Id\", \"Employee<WorkLog.Employee>\".\"Name\" \"Employee.Name\", \"Address<WorkLog.Employee.Addresses>\".\"Id\" \"Employee.Addresses.Id\", \"Address<WorkLog.Employee.Addresses>\".\"StreetAddress\" \"Employee.Addresses.StreetAddress\", \"Address<WorkLog.Employee.Addresses>\".\"City\" \"Employee.Addresses.City\", \"Address<WorkLog.Employee.Addresses>\".\"State\" \"Employee.Addresses.State\", \"Address<WorkLog.Employee.Addresses>\".\"Classification\" \"Employee.Addresses.Classification\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Id\" \"Employee.Addresses.Locations.Id\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Name\" \"Employee.Addresses.Locations.Name\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" \"Employee.Addresses.Locations.AddressId\", \"Location<WorkLog.Location>\".\"Id\" \"Location.Id\", \"Location<WorkLog.Location>\".\"Name\" \"Location.Name\", \"Location<WorkLog.Location>\".\"AddressId\" \"Location.AddressId\", \"Address<WorkLog.Location.Address>\".\"Id\" \"Location.Address.Id\", \"Address<WorkLog.Location.Address>\".\"StreetAddress\" \"Location.Address.StreetAddress\", \"Address<WorkLog.Location.Address>\".\"City\" \"Location.Address.City\", \"Address<WorkLog.Location.Address>\".\"State\" \"Location.Address.State\", \"Address<WorkLog.Location.Address>\".\"Classification\" \"Location.Address.Classification\", \"Employee<WorkLog.Location.Address.Employees>\".\"Id\" \"Location.Address.Employees.Id\", \"Employee<WorkLog.Location.Address.Employees>\".\"Name\" \"Location.Address.Employees.Name\" from (select \"WorkLog<WorkLog>\".\"Id\" from \"WorkLog\" \"WorkLog<WorkLog>\" order by \"WorkLog<WorkLog>\".\"StartDate\" asc offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" \"WorkLog<WorkLog>\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog<WorkLog>\".\"Id\") left outer join \"Employee\" \"Employee<WorkLog.Employee>\" on (\"WorkLog<WorkLog>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Employee>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Employee.Addresses>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Employee.Addresses.Locations>\" on (\"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Location>\" on (\"WorkLog<WorkLog>\".\"LocationId\" = \"Location<WorkLog.Location>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Location.Address>\" on (\"Location<WorkLog.Location>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Location.Address>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"Employee\" \"Employee<WorkLog.Location.Address.Employees>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"EmployeeId\" = \"Employee<WorkLog.Location.Address.Employees>\".\"Id\") order by \"WorkLog<WorkLog>\".\"StartDate\" asc", sql);
        }

        [TestMethod]
        public void Offset_OrderByRelation_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetNextWorkLogsWithDynamicOrder(1, new List<IOrderBy>() { new OrderByRelation(nameof(WorkLog), nameof(WorkLog.StartDate)) }));

            Assert.AreEqual("select \"WorkLog<WorkLog>\".\"Id\" \"Id\", \"WorkLog<WorkLog>\".\"StartDate\" \"StartDate\", \"WorkLog<WorkLog>\".\"EndDate\" \"EndDate\", \"WorkLog<WorkLog>\".\"EmployeeId\" \"EmployeeId\", \"WorkLog<WorkLog>\".\"LocationId\" \"LocationId\", \"Employee<WorkLog.Employee>\".\"Id\" \"Employee.Id\", \"Employee<WorkLog.Employee>\".\"Name\" \"Employee.Name\", \"Address<WorkLog.Employee.Addresses>\".\"Id\" \"Employee.Addresses.Id\", \"Address<WorkLog.Employee.Addresses>\".\"StreetAddress\" \"Employee.Addresses.StreetAddress\", \"Address<WorkLog.Employee.Addresses>\".\"City\" \"Employee.Addresses.City\", \"Address<WorkLog.Employee.Addresses>\".\"State\" \"Employee.Addresses.State\", \"Address<WorkLog.Employee.Addresses>\".\"Classification\" \"Employee.Addresses.Classification\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Id\" \"Employee.Addresses.Locations.Id\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"Name\" \"Employee.Addresses.Locations.Name\", \"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" \"Employee.Addresses.Locations.AddressId\", \"Location<WorkLog.Location>\".\"Id\" \"Location.Id\", \"Location<WorkLog.Location>\".\"Name\" \"Location.Name\", \"Location<WorkLog.Location>\".\"AddressId\" \"Location.AddressId\", \"Address<WorkLog.Location.Address>\".\"Id\" \"Location.Address.Id\", \"Address<WorkLog.Location.Address>\".\"StreetAddress\" \"Location.Address.StreetAddress\", \"Address<WorkLog.Location.Address>\".\"City\" \"Location.Address.City\", \"Address<WorkLog.Location.Address>\".\"State\" \"Location.Address.State\", \"Address<WorkLog.Location.Address>\".\"Classification\" \"Location.Address.Classification\", \"Employee<WorkLog.Location.Address.Employees>\".\"Id\" \"Location.Address.Employees.Id\", \"Employee<WorkLog.Location.Address.Employees>\".\"Name\" \"Location.Address.Employees.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by \"WorkLog\".\"StartDate\" asc offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" \"WorkLog<WorkLog>\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog<WorkLog>\".\"Id\") left outer join \"Employee\" \"Employee<WorkLog.Employee>\" on (\"WorkLog<WorkLog>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Employee>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"EmployeeId\" = \"Employee<WorkLog.Employee>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Employee.Addresses>\" on (\"EmployeeAddress<WorkLog.Employee>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Employee.Addresses.Locations>\" on (\"Location<WorkLog.Employee.Addresses.Locations>\".\"AddressId\" = \"Address<WorkLog.Employee.Addresses>\".\"Id\") left outer join \"Location\" \"Location<WorkLog.Location>\" on (\"WorkLog<WorkLog>\".\"LocationId\" = \"Location<WorkLog.Location>\".\"Id\") left outer join \"Address\" \"Address<WorkLog.Location.Address>\" on (\"Location<WorkLog.Location>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"EmployeeAddress\" \"EmployeeAddress<WorkLog.Location.Address>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"AddressId\" = \"Address<WorkLog.Location.Address>\".\"Id\") left outer join \"Employee\" \"Employee<WorkLog.Location.Address.Employees>\" on (\"EmployeeAddress<WorkLog.Location.Address>\".\"EmployeeId\" = \"Employee<WorkLog.Location.Address.Employees>\".\"Id\") order by \"WorkLog<WorkLog>\".\"StartDate\" asc", sql);
        }

        [TestMethod]
        public void Offset_RetainsPrimaryWhereClause_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetNextWorkLogsWithPrimaryTableFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" = @startDate) order by (select 1) offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void Offset_RetainsNavigationWhereClause_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetNextWorkLogsWithNavigationTableFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))) order by (select 1) offset @skip rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }
        
        [TestMethod]
        public void OffsetViaClassFilter_NoOrderBy_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.GetNextWorkLogsViaClassFilter(new WorkLog.FilterWithOffset() { Offset = 50 }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by (select 1) offset @filterOffset rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void OffsetViaClassFilter_RetainsPrimaryWhereClause_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.GetNextWorkLogsViaClassFilterAndParameter(new WorkLog.FilterWithOffsetAndParameter() { Offset = 50, StartDate = DateTime.Today }));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" where (\"WorkLog\".\"StartDate\" = @StartDate) order by (select 1) offset @filterOffset rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void OffsetViaClassFilter_RetainsNavigationWhereClause_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.GetNextWorkLogsWithNavigationTableFilterWithOffset(new WorkLog.GetByEmployeeNameFilterWithOffset() { Offset = 50, Employee = new Employee.EmployeeNameFilter() { Name = "something" }}));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name)))) order by (select 1) offset @filterOffset rows) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void Fetch_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.TakeWorkLogs));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by (select 1) offset 0 rows fetch next @take rows only) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void OffsetFetch_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.SkipTakeWorkLogs));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by (select 1) offset @skip rows fetch next @take rows only) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" = @id) order by (select 1) offset 0 rows fetch next @take rows only", sql);
        }

        [TestMethod]
        public void FetchViaClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.TakeWorkLogsViaClassFilter(new WorkLog.FilterWithFetch()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by (select 1) offset 0 rows fetch next @filterFetch rows only) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
        }

        [TestMethod]
        public void OffsetFetchViaClassFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() =>
                this.monolithicRepository.SkipTakeWorkLogsViaClassFilter(new WorkLog.FilterWithOffsetFetch()));

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\", \"Employee\".\"Id\" \"EmployeeNames.Id\", \"Employee\".\"Name\" \"EmployeeNames.Name\" from (select \"WorkLog\".\"Id\" from \"WorkLog\" order by (select 1) offset @filterOffset rows fetch next @filterFetch rows only) \"offset_WorkLog\" inner join \"WorkLog\" on (\"offset_WorkLog\".\"Id\" = \"WorkLog\".\"Id\") left outer join \"Employee\" on (\"WorkLog\".\"EmployeeId\" = \"Employee\".\"Id\")", sql);
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

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (\"WorkLog\".\"Id\" = @Id) order by (select 1) offset 0 rows fetch next @filterFetch rows only", sql);
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

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (exists (select 1 from \"WorkLog\" \"WorkLog0\" where ((\"WorkLog0\".\"EmployeeId\" = \"Employee\".\"Id\") and (\"WorkLog0\".\"LocationId\" = @WorkLog0LocationId))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToMany_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToMany_WithDifferingParameterName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeNameWithDifferingParameterName));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name))))", sql);
        }

        [TestMethod]
        public void GetViaRelationManyToMany_WithIntermediateTableSpecified_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeIdsForStreetAddress));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress)))))))", sql);
        }

        [TestMethod]
        public void GetViaRelationManyToOneViaClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeIdsForWorkLogLocationIdClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (exists (select 1 from \"WorkLog\" \"WorkLog0\" where ((\"WorkLog0\".\"EmployeeId\" = \"Employee\".\"Id\") and (\"WorkLog0\".\"LocationId\" = @WorkLog0LocationId))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToManyViaClassFilter_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeNameViaClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name))))", sql);
        }

        [TestMethod]
        public void GetViaRelationOneToManyViaClassFilter_WithDifferingParameterName_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetWorkLogIdsForEmployeeNameWithDifferingParameterNameViaClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\" where (exists (select 1 from \"Employee\" \"Employee0\" where ((\"Employee0\".\"Id\" = \"WorkLog\".\"EmployeeId\") and (\"Employee0\".\"Name\" = @Employee0Name))))", sql);
        }

        [TestMethod]
        public void GetViaRelationManyToManyViaClassFilter_WithIntermediateTableSpecified_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.GetEmployeeIdsForStreetAddressViaClassFilter));
            var sql = GetSqlFor(methodInfo);

            Assert.AreEqual("select \"Employee\".\"Id\" \"Id\" from \"Employee\" where (exists (select 1 from \"EmployeeAddress\" \"EmployeeAddress0\" where ((\"EmployeeAddress0\".\"EmployeeId\" = \"Employee\".\"Id\") and (exists (select 1 from \"Address\" \"Address00\" where ((\"Address00\".\"Id\" = \"EmployeeAddress0\".\"AddressId\") and (\"Address00\".\"StreetAddress\" = @Address00StreetAddress)))))))", sql);
        }

        [TestMethod]
        public void TableValuedFunction_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.itvf_GetWorkLogsByEmployeeId(2));

            Assert.AreEqual("select \"itvf_GetWorkLogsByEmployeeId\".\"RowNumber\" \"RowNumber\", \"itvf_GetWorkLogsByEmployeeId\".\"Id\" \"Id\" from (select ROW_NUMBER() over(order by \"Id\") \"RowNumber\", \"Id\", \"StartDate\", \"EndDate\", \"EmployeeId\", \"LocationId\" from itvf_GetWorkLogsByEmployeeId(@empId)) \"itvf_GetWorkLogsByEmployeeId\"", sql);
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

            Assert.AreEqual("select \"itvf_GetWorkLogsByEmployeeId\".\"RowNumber\" \"RowNumber\", \"itvf_GetWorkLogsByEmployeeId\".\"Id\" \"Id\" from (select ROW_NUMBER() over(order by \"Id\") \"RowNumber\", \"Id\", \"StartDate\", \"EndDate\", \"EmployeeId\", \"LocationId\" from itvf_GetWorkLogsByEmployeeId(@empId)) \"itvf_GetWorkLogsByEmployeeId\" where (\"itvf_GetWorkLogsByEmployeeId\".\"StartDate\" > @startDate)", sql);
        }

        [TestMethod]
        public void Count_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.CountWorkLogs());

            Assert.AreEqual("select count(1) \"Count\" from (select \"WorkLog\".\"Id\" \"Id\" from \"WorkLog\") Subquery", sql);
        }

        [TestMethod]
        public void Get_MismatchingPKCase()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.GetEmployeeMismatchingPKCase(1));

            Assert.AreEqual("select \"Employee\".\"Id\" \"ID\" from \"Employee\" where (\"Employee\".\"Id\" = @id)", sql);
        }

        #endregion Queries

        #region Insert 

        [TestMethod]
        public void InsertSingle_Void_ValuesByParam_ReturnsExpectedSql()
        {
            var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.InsertEmployeeWithAttributeTableNameWithValuesByParams));
            var sql = GetSqlFor(methodInfo);

            AssertSqlEqual(@"declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@name, 0)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");", sql);
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
            var sql = GetSqlForCall(() =>
                monolithicRepository.InsertEmployeeWithAttributeWithValuesByDetectedClass(new Employee.InsertFields()
                    { Name = "John" }));

            AssertSqlEqual(@"declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@valuesName0, 0)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");", sql);
        }

        [TestMethod]
        public void InsertSingle_OutputId_ValuesByDetectedClass_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertEmployeeWithAttributeWithValuesByDetectedClassReturnId(
               new Employee.InsertFields() { Name = "bah" }));

            AssertSqlEqual(@"declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@valuesName0, 0)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
select ""Employee"".""Id"" ""Id"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""Employee"".""Id"" = ""EmployeeLookup"".""Id"") order by ""EmployeeLookup"".""_index""", sql);
        }

        [TestMethod]
        public void InsertMultiple_Void_ValuesByDetectedClass_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleEmployeesWithAttributeWithValuesByDetectedClass(
                new Employee.InsertFields[] { new Employee.InsertFields() { Name = "bah" }, new Employee.InsertFields() { Name = "baah" } }));

            AssertSqlEqual(@"declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@employeesName0, 0), (@employeesName1, 1)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");", sql);
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

            AssertSqlEqual(@"declare @insertedAddress table(""Id"" int, ""_index"" int)
declare @AddressLookup table(""Id"" int, ""StreetAddress"" nvarchar(max), ""City"" nvarchar(max), ""State"" nvarchar(max), ""_index"" int)
insert @AddressLookup(""StreetAddress"", ""City"", ""State"", ""_index"") values(@addressesStreetAddress0, @addressesCity0, @addressesState0, 0), (@addressesStreetAddress1, @addressesCity1, @addressesState1, 1)
merge ""Address"" using (select ""StreetAddress"", ""City"", ""State"", ""_index"" from @AddressLookup ""AddressLookup"") as i (""StreetAddress"",""City"",""State"",""_index"") on (1 = 0)
 when not matched then
 insert (""StreetAddress"", ""City"", ""State"") values(""i"".""StreetAddress"", ""i"".""City"", ""i"".""State"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedAddress(""Id"", ""_index"");
update ""AddressLookup"" set ""Id"" = ""insertedAddress"".""Id"" from @AddressLookup ""AddressLookup"" inner join @insertedAddress ""insertedAddress"" on (""AddressLookup"".""_index"" = ""insertedAddress"".""_index"");", sql);
        }

        [TestMethod]
        public void InsertMultiple_OutputIds_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleEmployeesAndReturnIds(new Employee.InsertFields[] { new Employee.InsertFields() { Name = "bah" }, new Employee.InsertFields() { Name = "bah" }, new Employee.InsertFields() { Name = "bah" } }));

            AssertSqlEqual(@"declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@employeesName0, 0), (@employeesName1, 1), (@employeesName2, 2)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
select ""Employee"".""Id"" ""Id"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""Employee"".""Id"" = ""EmployeeLookup"".""Id"") order by ""EmployeeLookup"".""_index""", sql);
        }
        
        [TestMethod]
        public void InsertMultipleWithOneToManyNavigationProperty_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(
                    new Employee.InsertFieldsWithWorkLogs[]
                    {
                         new Employee.InsertFieldsWithWorkLogs()
                         {
                             Name = "bah",
                             WorkLogs = new []
                             {
                                 new WorkLog.DataFields() { StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.DataFields() { StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         },
                         new Employee.InsertFieldsWithWorkLogs()
                         {
                             Name = "2bah",
                             WorkLogs = new []
                             {
                                 new WorkLog.DataFields() { StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.DataFields() { StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         }
                    }));

            AssertSqlEqual(@"declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@employeesName0, 0), (@employeesName1, 1)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int)
insert @WorkLogLookup(""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"") values(@employeesWorkLogs_StartDate0, @employeesWorkLogs_EndDate0, 0, 0), (@employeesWorkLogs_StartDate1, @employeesWorkLogs_EndDate1, 1, 0), (@employeesWorkLogs_StartDate2, @employeesWorkLogs_EndDate2, 2, 1), (@employeesWorkLogs_StartDate3, @employeesWorkLogs_EndDate3, 3, 1)
merge ""WorkLog"" using (select ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"" from @WorkLogLookup ""WorkLogLookup"") as i (""StartDate"",""EndDate"",""_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""StartDate"", ""EndDate"", ""EmployeeId"") values(""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");", sql);
        }

        [TestMethod]
        public void InsertMultipleWithManyToOneNavigationProperty_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleWorkLogsWithEmployees(
                new[]
                {
                    new WorkLog.InsertFieldsWithEmployee()
                    {
                        StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                        Employee =
                            new Employee.InsertFields()
                                { Name = "Mike" }
                    },
                    new WorkLog.InsertFieldsWithEmployee()
                    {
                        StartDate = new DateTime(2021, 3, 1),
                        EndDate = new DateTime(2021, 1, 2),
                        Employee =
                            new Employee.InsertFields()
                                { Name = "Lester" }
                    }
                }));

            AssertSqlEqual(@"declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@worklogsEmployee_Name0, 0), (@worklogsEmployee_Name1, 1)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int)
insert @WorkLogLookup(""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"") values(@worklogsStartDate0, @worklogsEndDate0, 0, 0), (@worklogsStartDate1, @worklogsEndDate1, 1, 1)
merge ""WorkLog"" using (select ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"" from @WorkLogLookup ""WorkLogLookup"") as i (""StartDate"",""EndDate"",""_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""StartDate"", ""EndDate"", ""EmployeeId"") values(""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");", sql);
        }

        [TestMethod]
        public void InsertMultipleWithAdjacentAndManyToManyNavigationProperties_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleWorkLogsWithAdjacentAndNestedRelations(
                new[]
            {
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                        {
                            Name = "Mike",
                            Addresses = new Address.InsertFields[]
                            {
                                new Address.InsertFields()
                                {
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                }
                            }

                        },
                    Location = new Location.Insert()
                    {
                        Name = "Ice Queen"
                    }
                },
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 3, 1),
                    EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                            {
                                Name = "Lester",
                                Addresses = new Address.InsertFields[]
                                {
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "234 fake st",
                                        City = "New York",
                                        State = "NY"
                                    }
                                }

                            },
                    Location = new Location.Insert()
                    {
                        Name = "Burger Hut"
                    }
                }
            }));

            AssertSqlEqual(@"declare @insertedEmployeeAddress table(""AddressId"", ""EmployeeId"", ""_index"" int)
declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedLocation table(""Id"" int, ""_index"" int)
declare @insertedAddress table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@employeesEmployee_Name0, 0), (@employeesEmployee_Name1, 1)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
declare @AddressLookup table(""Id"" int, ""StreetAddress"" nvarchar(max), ""City"" nvarchar(max), ""State"" nvarchar(max), ""_index"" int)
insert @AddressLookup(""StreetAddress"", ""City"", ""State"", ""_index"") values(@employeesEmployee_Addresses_StreetAddress0, @employeesEmployee_Addresses_City0, @employeesEmployee_Addresses_State0, 0), (@employeesEmployee_Addresses_StreetAddress1, @employeesEmployee_Addresses_City1, @employeesEmployee_Addresses_State1, 1)
merge ""Address"" using (select ""StreetAddress"", ""City"", ""State"", ""_index"" from @AddressLookup ""AddressLookup"") as i (""StreetAddress"",""City"",""State"",""_index"") on (1 = 0)
 when not matched then
 insert (""StreetAddress"", ""City"", ""State"") values(""i"".""StreetAddress"", ""i"".""City"", ""i"".""State"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedAddress(""Id"", ""_index"");
update ""AddressLookup"" set ""Id"" = ""insertedAddress"".""Id"" from @AddressLookup ""AddressLookup"" inner join @insertedAddress ""insertedAddress"" on (""AddressLookup"".""_index"" = ""insertedAddress"".""_index"");
declare @LocationLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @LocationLookup(""Name"", ""_index"") values(@employeesLocation_Name0, 0), (@employeesLocation_Name1, 1)
merge ""Location"" using (select ""Name"", ""_index"" from @LocationLookup ""LocationLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedLocation(""Id"", ""_index"");
update ""LocationLookup"" set ""Id"" = ""insertedLocation"".""Id"" from @LocationLookup ""LocationLookup"" inner join @insertedLocation ""insertedLocation"" on (""LocationLookup"".""_index"" = ""insertedLocation"".""_index"");
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int, ""LocationId_index"" int)
insert @WorkLogLookup(""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""LocationId_index"") values(@employeesStartDate0, @employeesEndDate0, 0, 0, 0), (@employeesStartDate1, @employeesEndDate1, 1, 1, 1)
merge ""WorkLog"" using (select ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""LocationId_index"" from @WorkLogLookup ""WorkLogLookup"") as i (""StartDate"",""EndDate"",""_index"",""EmployeeId_index"",""LocationId_index"") on (1 = 0)
 when not matched then
 insert (""StartDate"", ""EndDate"", ""EmployeeId"", ""LocationId"") values(""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index"")), (select ""Id"" from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""_index"" = ""i"".""LocationId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");
declare @EmployeeAddressLookup table(""Id"", ""EmployeeId"", ""AddressId"", ""_index"" int, ""AddressId_index"" int, ""EmployeeId_index"" int)
insert @EmployeeAddressLookup(""EmployeeId"", ""AddressId"", ""_index"", ""AddressId_index"", ""EmployeeId_index"") values(@EmployeeAddress_EmployeeId0, @EmployeeAddress_AddressId0, 0, 0, 0), (@EmployeeAddress_EmployeeId1, @EmployeeAddress_AddressId1, 1, 1, 1)
merge ""EmployeeAddress"" using (select ""_index"", ""AddressId_index"", ""EmployeeId_index"" from @EmployeeAddressLookup ""EmployeeAddressLookup"") as i (""_index"",""AddressId_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""AddressId"", ""EmployeeId"") values((select ""Id"" from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""_index"" = ""i"".""AddressId_index"")), (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""AddressId"", ""inserted"".""EmployeeId"", ""i"".""_index"" into @insertedEmployeeAddress(""AddressId"", ""EmployeeId"", ""_index"");
update ""EmployeeAddressLookup"" set ""AddressId"" = ""insertedEmployeeAddress"".""AddressId"", ""EmployeeId"" = ""insertedEmployeeAddress"".""EmployeeId"" from @EmployeeAddressLookup ""EmployeeAddressLookup"" inner join @insertedEmployeeAddress ""insertedEmployeeAddress"" on (""EmployeeAddressLookup"".""_index"" = ""insertedEmployeeAddress"".""_index"");", sql);
        }

        [TestMethod]
        public void InsertMultipleWithAdjacentAndManyToManyNavigationProperties_ReturnResult_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertMultipleWorkLogsWithAdjacentAndNestedRelationsAndReturnResult(
                new[]
            {
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                        {
                            Name = "Mike",
                            Addresses = new Address.InsertFields[]
                            {
                                new Address.InsertFields()
                                {
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "456 fake st",
                                    City = "Portland",
                                    State = "OR"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "567 fake st",
                                    City = "San Diego",
                                    State = "CA"
                                }
                            }

                        },
                    Location = new Location.Insert()
                    {
                        Name = "Ice Queen"
                    }
                },
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 3, 1),
                    EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                            {
                                Name = "Lester",
                                Addresses = new Address.InsertFields[]
                                {
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "234 fake st",
                                        City = "New York",
                                        State = "NY"
                                    },
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "345 fake st",
                                        City = "Manchester",
                                        State = "NH"
                                    }
                                }

                            },
                    Location = new Location.Insert()
                    {
                        Name = "Burger Hut"
                    }
                }
            }));

            AssertSqlEqual(@"declare @insertedEmployeeAddress table(""AddressId"", ""EmployeeId"", ""_index"" int)
declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedLocation table(""Id"" int, ""_index"" int)
declare @insertedAddress table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Name"", ""_index"") values(@employeesEmployee_Name0, 0), (@employeesEmployee_Name1, 1)
merge ""Employee"" using (select ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
declare @AddressLookup table(""Id"" int, ""StreetAddress"" nvarchar(max), ""City"" nvarchar(max), ""State"" nvarchar(max), ""_index"" int)
insert @AddressLookup(""StreetAddress"", ""City"", ""State"", ""_index"") values(@employeesEmployee_Addresses_StreetAddress0, @employeesEmployee_Addresses_City0, @employeesEmployee_Addresses_State0, 0), (@employeesEmployee_Addresses_StreetAddress1, @employeesEmployee_Addresses_City1, @employeesEmployee_Addresses_State1, 1), (@employeesEmployee_Addresses_StreetAddress2, @employeesEmployee_Addresses_City2, @employeesEmployee_Addresses_State2, 2), (@employeesEmployee_Addresses_StreetAddress3, @employeesEmployee_Addresses_City3, @employeesEmployee_Addresses_State3, 3), (@employeesEmployee_Addresses_StreetAddress4, @employeesEmployee_Addresses_City4, @employeesEmployee_Addresses_State4, 4)
merge ""Address"" using (select ""StreetAddress"", ""City"", ""State"", ""_index"" from @AddressLookup ""AddressLookup"") as i (""StreetAddress"",""City"",""State"",""_index"") on (1 = 0)
 when not matched then
 insert (""StreetAddress"", ""City"", ""State"") values(""i"".""StreetAddress"", ""i"".""City"", ""i"".""State"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedAddress(""Id"", ""_index"");
update ""AddressLookup"" set ""Id"" = ""insertedAddress"".""Id"" from @AddressLookup ""AddressLookup"" inner join @insertedAddress ""insertedAddress"" on (""AddressLookup"".""_index"" = ""insertedAddress"".""_index"");
declare @LocationLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @LocationLookup(""Name"", ""_index"") values(@employeesLocation_Name0, 0), (@employeesLocation_Name1, 1)
merge ""Location"" using (select ""Name"", ""_index"" from @LocationLookup ""LocationLookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedLocation(""Id"", ""_index"");
update ""LocationLookup"" set ""Id"" = ""insertedLocation"".""Id"" from @LocationLookup ""LocationLookup"" inner join @insertedLocation ""insertedLocation"" on (""LocationLookup"".""_index"" = ""insertedLocation"".""_index"");
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int, ""LocationId_index"" int)
insert @WorkLogLookup(""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""LocationId_index"") values(@employeesStartDate0, @employeesEndDate0, 0, 0, 0), (@employeesStartDate1, @employeesEndDate1, 1, 1, 1)
merge ""WorkLog"" using (select ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""LocationId_index"" from @WorkLogLookup ""WorkLogLookup"") as i (""StartDate"",""EndDate"",""_index"",""EmployeeId_index"",""LocationId_index"") on (1 = 0)
 when not matched then
 insert (""StartDate"", ""EndDate"", ""EmployeeId"", ""LocationId"") values(""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index"")), (select ""Id"" from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""_index"" = ""i"".""LocationId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");
declare @EmployeeAddressLookup table(""Id"", ""EmployeeId"", ""AddressId"", ""_index"" int, ""AddressId_index"" int, ""EmployeeId_index"" int)
insert @EmployeeAddressLookup(""EmployeeId"", ""AddressId"", ""_index"", ""AddressId_index"", ""EmployeeId_index"") values(@EmployeeAddress_EmployeeId0, @EmployeeAddress_AddressId0, 0, 0, 0), (@EmployeeAddress_EmployeeId1, @EmployeeAddress_AddressId1, 1, 3, 1), (@EmployeeAddress_EmployeeId2, @EmployeeAddress_AddressId2, 2, 1, 0), (@EmployeeAddress_EmployeeId3, @EmployeeAddress_AddressId3, 3, 2, 0), (@EmployeeAddress_EmployeeId4, @EmployeeAddress_AddressId4, 4, 4, 1)
merge ""EmployeeAddress"" using (select ""_index"", ""AddressId_index"", ""EmployeeId_index"" from @EmployeeAddressLookup ""EmployeeAddressLookup"") as i (""_index"",""AddressId_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""AddressId"", ""EmployeeId"") values((select ""Id"" from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""_index"" = ""i"".""AddressId_index"")), (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""AddressId"", ""inserted"".""EmployeeId"", ""i"".""_index"" into @insertedEmployeeAddress(""AddressId"", ""EmployeeId"", ""_index"");
update ""EmployeeAddressLookup"" set ""AddressId"" = ""insertedEmployeeAddress"".""AddressId"", ""EmployeeId"" = ""insertedEmployeeAddress"".""EmployeeId"" from @EmployeeAddressLookup ""EmployeeAddressLookup"" inner join @insertedEmployeeAddress ""insertedEmployeeAddress"" on (""EmployeeAddressLookup"".""_index"" = ""insertedEmployeeAddress"".""_index"");
select ""WorkLog<WorkLog>"".""Id"" ""Id"", ""WorkLog<WorkLog>"".""StartDate"" ""StartDate"", ""WorkLog<WorkLog>"".""EndDate"" ""EndDate"", ""WorkLog<WorkLog>"".""EmployeeId"" ""EmployeeId"", ""WorkLog<WorkLog>"".""LocationId"" ""LocationId"", ""Employee<WorkLog.Employee>"".""Id"" ""Employee.Id"", ""Employee<WorkLog.Employee>"".""Name"" ""Employee.Name"", ""Address<WorkLog.Employee.Addresses>"".""Id"" ""Employee.Addresses.Id"", ""Address<WorkLog.Employee.Addresses>"".""StreetAddress"" ""Employee.Addresses.StreetAddress"", ""Address<WorkLog.Employee.Addresses>"".""City"" ""Employee.Addresses.City"", ""Address<WorkLog.Employee.Addresses>"".""State"" ""Employee.Addresses.State"", ""Address<WorkLog.Employee.Addresses>"".""Classification"" ""Employee.Addresses.Classification"", ""Location<WorkLog.Employee.Addresses.Locations>"".""Id"" ""Employee.Addresses.Locations.Id"", ""Location<WorkLog.Employee.Addresses.Locations>"".""Name"" ""Employee.Addresses.Locations.Name"", ""Location<WorkLog.Employee.Addresses.Locations>"".""AddressId"" ""Employee.Addresses.Locations.AddressId"", ""Location<WorkLog.Location>"".""Id"" ""Location.Id"", ""Location<WorkLog.Location>"".""Name"" ""Location.Name"", ""Location<WorkLog.Location>"".""AddressId"" ""Location.AddressId"", ""Address<WorkLog.Location.Address>"".""Id"" ""Location.Address.Id"", ""Address<WorkLog.Location.Address>"".""StreetAddress"" ""Location.Address.StreetAddress"", ""Address<WorkLog.Location.Address>"".""City"" ""Location.Address.City"", ""Address<WorkLog.Location.Address>"".""State"" ""Location.Address.State"", ""Address<WorkLog.Location.Address>"".""Classification"" ""Location.Address.Classification"", ""Employee<WorkLog.Location.Address.Employees>"".""Id"" ""Location.Address.Employees.Id"", ""Employee<WorkLog.Location.Address.Employees>"".""Name"" ""Location.Address.Employees.Name"" from ""WorkLog"" ""WorkLog<WorkLog>"" left outer join ""Employee"" ""Employee<WorkLog.Employee>"" on (""WorkLog<WorkLog>"".""EmployeeId"" = ""Employee<WorkLog.Employee>"".""Id"") left outer join ""EmployeeAddress"" ""EmployeeAddress<WorkLog.Employee>"" on (""EmployeeAddress<WorkLog.Employee>"".""EmployeeId"" = ""Employee<WorkLog.Employee>"".""Id"") left outer join ""Address"" ""Address<WorkLog.Employee.Addresses>"" on (""EmployeeAddress<WorkLog.Employee>"".""AddressId"" = ""Address<WorkLog.Employee.Addresses>"".""Id"") left outer join ""Location"" ""Location<WorkLog.Employee.Addresses.Locations>"" on (""Location<WorkLog.Employee.Addresses.Locations>"".""AddressId"" = ""Address<WorkLog.Employee.Addresses>"".""Id"") left outer join ""Location"" ""Location<WorkLog.Location>"" on (""WorkLog<WorkLog>"".""LocationId"" = ""Location<WorkLog.Location>"".""Id"") left outer join ""Address"" ""Address<WorkLog.Location.Address>"" on (""Location<WorkLog.Location>"".""AddressId"" = ""Address<WorkLog.Location.Address>"".""Id"") left outer join ""EmployeeAddress"" ""EmployeeAddress<WorkLog.Location.Address>"" on (""EmployeeAddress<WorkLog.Location.Address>"".""AddressId"" = ""Address<WorkLog.Location.Address>"".""Id"") left outer join ""Employee"" ""Employee<WorkLog.Location.Address.Employees>"" on (""EmployeeAddress<WorkLog.Location.Address>"".""EmployeeId"" = ""Employee<WorkLog.Location.Address.Employees>"".""Id"") inner join @WorkLogLookup ""WorkLogLookup"" on (""WorkLog<WorkLog>"".""Id"" = ""WorkLogLookup"".""Id"") order by ""WorkLogLookup"".""_index""", sql);
        }

        [TestMethod]
        public void InsertRepeatedChildren_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.InsertSingleEmployeeWithAddresses(
                new Employee.InsertEmployeeTwice()
                {
                    Name = "John",
                    WorkLog = new WorkLog.InsertFieldsWithEmployee()
                    {
                        StartDate = DateTime.Today,
                        Employee = new Employee.InsertFields()
                        {
                            Name = "Joe"
                        }
                    }
                }

            ));

            AssertSqlEqual(@"declare @insertedWorkLog$employees$WorkLog$ table(""Id"" int, ""_index"" int)
declare @insertedEmployee$employees$WorkLog$Employee$ table(""Id"" int, ""_index"" int)
declare @insertedEmployee$Employee$ table(""Id"" int, ""_index"" int)
declare @Employee$Employee$Lookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @Employee$Employee$Lookup(""Name"", ""_index"") values(@employeesName0, 0)
merge ""Employee"" using (select ""Name"", ""_index"" from @Employee$Employee$Lookup ""Employee$Employee$Lookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee$Employee$(""Id"", ""_index"");
update ""Employee$Employee$Lookup"" set ""Id"" = ""insertedEmployee$Employee$"".""Id"" from @Employee$Employee$Lookup ""Employee$Employee$Lookup"" inner join @insertedEmployee$Employee$ ""insertedEmployee$Employee$"" on (""Employee$Employee$Lookup"".""_index"" = ""insertedEmployee$Employee$"".""_index"");
declare @Employee$employees$WorkLog$Employee$Lookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @Employee$employees$WorkLog$Employee$Lookup(""Name"", ""_index"") values(@employeesWorkLog_Employee_Name0, 0)
merge ""Employee"" using (select ""Name"", ""_index"" from @Employee$employees$WorkLog$Employee$Lookup ""Employee$employees$WorkLog$Employee$Lookup"") as i (""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee$employees$WorkLog$Employee$(""Id"", ""_index"");
update ""Employee$employees$WorkLog$Employee$Lookup"" set ""Id"" = ""insertedEmployee$employees$WorkLog$Employee$"".""Id"" from @Employee$employees$WorkLog$Employee$Lookup ""Employee$employees$WorkLog$Employee$Lookup"" inner join @insertedEmployee$employees$WorkLog$Employee$ ""insertedEmployee$employees$WorkLog$Employee$"" on (""Employee$employees$WorkLog$Employee$Lookup"".""_index"" = ""insertedEmployee$employees$WorkLog$Employee$"".""_index"");
declare @WorkLog$employees$WorkLog$Lookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int, ""EmployeeId_index"" int)
insert @WorkLog$employees$WorkLog$Lookup(""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""EmployeeId_index"") values(@employeesWorkLog_StartDate0, @employeesWorkLog_EndDate0, 0, 0, 0, 0, 0)
merge ""WorkLog"" using (select ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""EmployeeId_index"" from @WorkLog$employees$WorkLog$Lookup ""WorkLog$employees$WorkLog$Lookup"") as i (""StartDate"",""EndDate"",""_index"",""EmployeeId_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""StartDate"", ""EndDate"", ""EmployeeId"", ""EmployeeId"") values(""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @Employee$employees$WorkLog$Employee$Lookup ""Employee$employees$WorkLog$Employee$Lookup"" where (""Employee$employees$WorkLog$Employee$Lookup"".""_index"" = ""i"".""EmployeeId_index"")), (select ""Id"" from @Employee$Employee$Lookup ""Employee$Employee$Lookup"" where (""Employee$Employee$Lookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog$employees$WorkLog$(""Id"", ""_index"");
update ""WorkLog$employees$WorkLog$Lookup"" set ""Id"" = ""insertedWorkLog$employees$WorkLog$"".""Id"" from @WorkLog$employees$WorkLog$Lookup ""WorkLog$employees$WorkLog$Lookup"" inner join @insertedWorkLog$employees$WorkLog$ ""insertedWorkLog$employees$WorkLog$"" on (""WorkLog$employees$WorkLog$Lookup"".""_index"" = ""insertedWorkLog$employees$WorkLog$"".""_index"");", sql);
        }

        // [TestMethod]
        // public void InsertSingle_Void_ValuesByUnknownClass_ViaAttribute_ReturnsExpectedSql()
        // {
        //     var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.InsertEmployeeWithAttributeWithValuesByUnknownClass));
        //     var sql = GetSqlFor(methodInfo);
        //
        //     Assert.AreEqual("insert into Employee(Name) values(@Name)", sql);
        // }

        #endregion Insert

        #region Upsert



        #endregion Upsert

        #region Delete

        [TestMethod]
        public void Delete_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.DeleteEmployeeWithAttributeTableNameWithValuesByParams("bob"));

            AssertSqlEqual("delete from \"Employee\" where (\"Employee\".\"Name\" = @name)", sql);
        }

        #endregion Delete

        #region Upsert

        [TestMethod]
        public void UpsertViaMethodParams()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpsertEmployeeViaMethodParams(1, "bob"));

            AssertSqlEqual(@"declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@id, @name, 0)
merge ""Employee"" using (select ""Id"", ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"" where ((""Id"" is null) or not exists (select 1 from ""Employee"" where (""Employee"".""Id"" = ""EmployeeLookup"".""Id"")))) as i (""Id"",""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"") where not exists (select 1 from ""Employee"" inner join @insertedEmployee ""insertedEmployee"" on (""Employee"".""Id"" = ""insertedEmployee"".""Id"") where (""EmployeeLookup"".""Id"" = ""insertedEmployee"".""Id""));", sql);
        }

        [TestMethod]
        public void UpsertMultipleWithOneToManyNavigationProperty_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpsertMultipleEmployeesWithWorkLogs(
                    new Employee.UpsertFieldsWithWorkLogs[]
                    {
                         new Employee.UpsertFieldsWithWorkLogs()
                         {
                             Id = 1,
                             Name = "Kyle",
                             WorkLogs = new []
                             {
                                 new WorkLog.UpsertFields() { Id = 1, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.UpsertFields() { StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         },
                         new Employee.UpsertFieldsWithWorkLogs()
                         {
                             Name = "Geno",
                             WorkLogs = new []
                             {
                                 new WorkLog.UpsertFields() { Id = 2, StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.UpsertFields() { StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         }
                    }));

            AssertSqlEqual(@"declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@employeesId0, @employeesName0, 0), (@employeesId1, @employeesName1, 1)
merge ""Employee"" using (select ""Id"", ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"" where ((""Id"" is null) or not exists (select 1 from ""Employee"" where (""Employee"".""Id"" = ""EmployeeLookup"".""Id"")))) as i (""Id"",""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"") where not exists (select 1 from ""Employee"" inner join @insertedEmployee ""insertedEmployee"" on (""Employee"".""Id"" = ""insertedEmployee"".""Id"") where (""EmployeeLookup"".""Id"" = ""insertedEmployee"".""Id""));
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int)
insert @WorkLogLookup(""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"") values(@employeesWorkLogs_Id0, @employeesWorkLogs_StartDate0, @employeesWorkLogs_EndDate0, 0, 0), (@employeesWorkLogs_Id1, @employeesWorkLogs_StartDate1, @employeesWorkLogs_EndDate1, 1, 0), (@employeesWorkLogs_Id2, @employeesWorkLogs_StartDate2, @employeesWorkLogs_EndDate2, 2, 1), (@employeesWorkLogs_Id3, @employeesWorkLogs_StartDate3, @employeesWorkLogs_EndDate3, 3, 1)
merge ""WorkLog"" using (select ""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"" from @WorkLogLookup ""WorkLogLookup"" where ((""Id"" is null) or not exists (select 1 from ""WorkLog"" where (""WorkLog"".""Id"" = ""WorkLogLookup"".""Id"")))) as i (""Id"",""StartDate"",""EndDate"",""_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StartDate"", ""EndDate"", ""EmployeeId"") values(""i"".""Id"", ""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");
update ""WorkLog"" set ""StartDate"" = ""WorkLogLookup"".""StartDate"", ""EndDate"" = ""WorkLogLookup"".""EndDate"", ""EmployeeId"" = (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""WorkLogLookup"".""EmployeeId_index"")) from ""WorkLog"" inner join @WorkLogLookup ""WorkLogLookup"" on (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"") where not exists (select 1 from ""WorkLog"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLog"".""Id"" = ""insertedWorkLog"".""Id"") where (""WorkLogLookup"".""Id"" = ""insertedWorkLog"".""Id""));", sql);
        }

        [TestMethod]
        public void UpsertMultipleWithOneToManyNavigationProperty_OutputIds_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpsertMultipleEmployeesWithWorkLogs_OutputIds(
                    new Employee.UpsertFieldsWithWorkLogs[]
                    {
                         new Employee.UpsertFieldsWithWorkLogs()
                         {
                             Id = 1,
                             Name = "Kyle",
                             WorkLogs = new []
                             {
                                 new WorkLog.UpsertFields() { Id = 1, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.UpsertFields() { StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         },
                         new Employee.UpsertFieldsWithWorkLogs()
                         {
                             Name = "Geno",
                             WorkLogs = new []
                             {
                                 new WorkLog.UpsertFields() { Id = 2, StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.UpsertFields() { StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         }
                    }));

            AssertSqlEqual(@"declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@employeesId0, @employeesName0, 0), (@employeesId1, @employeesName1, 1)
merge ""Employee"" using (select ""Id"", ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"" where ((""Id"" is null) or not exists (select 1 from ""Employee"" where (""Employee"".""Id"" = ""EmployeeLookup"".""Id"")))) as i (""Id"",""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"") where not exists (select 1 from ""Employee"" inner join @insertedEmployee ""insertedEmployee"" on (""Employee"".""Id"" = ""insertedEmployee"".""Id"") where (""EmployeeLookup"".""Id"" = ""insertedEmployee"".""Id""));
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int)
insert @WorkLogLookup(""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"") values(@employeesWorkLogs_Id0, @employeesWorkLogs_StartDate0, @employeesWorkLogs_EndDate0, 0, 0), (@employeesWorkLogs_Id1, @employeesWorkLogs_StartDate1, @employeesWorkLogs_EndDate1, 1, 0), (@employeesWorkLogs_Id2, @employeesWorkLogs_StartDate2, @employeesWorkLogs_EndDate2, 2, 1), (@employeesWorkLogs_Id3, @employeesWorkLogs_StartDate3, @employeesWorkLogs_EndDate3, 3, 1)
merge ""WorkLog"" using (select ""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"" from @WorkLogLookup ""WorkLogLookup"" where ((""Id"" is null) or not exists (select 1 from ""WorkLog"" where (""WorkLog"".""Id"" = ""WorkLogLookup"".""Id"")))) as i (""Id"",""StartDate"",""EndDate"",""_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StartDate"", ""EndDate"", ""EmployeeId"") values(""i"".""Id"", ""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");
update ""WorkLog"" set ""StartDate"" = ""WorkLogLookup"".""StartDate"", ""EndDate"" = ""WorkLogLookup"".""EndDate"", ""EmployeeId"" = (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""WorkLogLookup"".""EmployeeId_index"")) from ""WorkLog"" inner join @WorkLogLookup ""WorkLogLookup"" on (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"") where not exists (select 1 from ""WorkLog"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLog"".""Id"" = ""insertedWorkLog"".""Id"") where (""WorkLogLookup"".""Id"" = ""insertedWorkLog"".""Id""));
select ""Employee"".""Id"" ""Id"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""Employee"".""Id"" = ""EmployeeLookup"".""Id"") order by ""EmployeeLookup"".""_index""", sql);
        }
        
        [TestMethod]
        public void UpsertMultipleWithAdjacentAndManyToManyNavigationProperties_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpsertMultipleWorkLogsWithAdjacentAndNestedRelations(
                new[]
            {
                new WorkLog.UpsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.UpsertFieldsWithAddress()
                        {
                            Name = "Mike",
                            Addresses = new []
                            {
                                new Address.UpsertFields()
                                {
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                }
                            }

                        },
                    Location = new Location.Upsert()
                    {
                        Name = "Ice Queen"
                    }
                },
                new WorkLog.UpsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 3, 1),
                    EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.UpsertFieldsWithAddress()
                            {
                                Name = "Lester",
                                Addresses = new []
                                {
                                    new Address.UpsertFields()
                                    {
                                        StreetAddress = "234 fake st",
                                        City = "New York",
                                        State = "NY"
                                    }
                                }

                            },
                    Location = new Location.Upsert()
                    {
                        Name = "Burger Hut"
                    }
                }
            }));

            AssertSqlEqual(@"declare @insertedEmployeeAddress table(""AddressId"", ""EmployeeId"", ""_index"" int)
declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedLocation table(""Id"" int, ""_index"" int)
declare @insertedAddress table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@employeesEmployee_Id0, @employeesEmployee_Name0, 0), (@employeesEmployee_Id1, @employeesEmployee_Name1, 1)
merge ""Employee"" using (select ""Id"", ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"" where ((""Id"" is null) or not exists (select 1 from ""Employee"" where (""Employee"".""Id"" = ""EmployeeLookup"".""Id"")))) as i (""Id"",""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"") where not exists (select 1 from ""Employee"" inner join @insertedEmployee ""insertedEmployee"" on (""Employee"".""Id"" = ""insertedEmployee"".""Id"") where (""EmployeeLookup"".""Id"" = ""insertedEmployee"".""Id""));
declare @AddressLookup table(""Id"" int, ""StreetAddress"" nvarchar(max), ""City"" nvarchar(max), ""State"" nvarchar(max), ""_index"" int)
insert @AddressLookup(""Id"", ""StreetAddress"", ""City"", ""State"", ""_index"") values(@employeesEmployee_Addresses_Id0, @employeesEmployee_Addresses_StreetAddress0, @employeesEmployee_Addresses_City0, @employeesEmployee_Addresses_State0, 0), (@employeesEmployee_Addresses_Id1, @employeesEmployee_Addresses_StreetAddress1, @employeesEmployee_Addresses_City1, @employeesEmployee_Addresses_State1, 1)
merge ""Address"" using (select ""Id"", ""StreetAddress"", ""City"", ""State"", ""_index"" from @AddressLookup ""AddressLookup"" where ((""Id"" is null) or not exists (select 1 from ""Address"" where (""Address"".""Id"" = ""AddressLookup"".""Id"")))) as i (""Id"",""StreetAddress"",""City"",""State"",""_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StreetAddress"", ""City"", ""State"") values(""i"".""Id"", ""i"".""StreetAddress"", ""i"".""City"", ""i"".""State"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedAddress(""Id"", ""_index"");
update ""AddressLookup"" set ""Id"" = ""insertedAddress"".""Id"" from @AddressLookup ""AddressLookup"" inner join @insertedAddress ""insertedAddress"" on (""AddressLookup"".""_index"" = ""insertedAddress"".""_index"");
update ""Address"" set ""StreetAddress"" = ""AddressLookup"".""StreetAddress"", ""City"" = ""AddressLookup"".""City"", ""State"" = ""AddressLookup"".""State"" from ""Address"" inner join @AddressLookup ""AddressLookup"" on (""AddressLookup"".""Id"" = ""Address"".""Id"") where not exists (select 1 from ""Address"" inner join @insertedAddress ""insertedAddress"" on (""Address"".""Id"" = ""insertedAddress"".""Id"") where (""AddressLookup"".""Id"" = ""insertedAddress"".""Id""));
declare @LocationLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @LocationLookup(""Id"", ""Name"", ""_index"") values(@employeesLocation_Id0, @employeesLocation_Name0, 0), (@employeesLocation_Id1, @employeesLocation_Name1, 1)
merge ""Location"" using (select ""Id"", ""Name"", ""_index"" from @LocationLookup ""LocationLookup"" where ((""Id"" is null) or not exists (select 1 from ""Location"" where (""Location"".""Id"" = ""LocationLookup"".""Id"")))) as i (""Id"",""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""Name"") values(""i"".""Id"", ""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedLocation(""Id"", ""_index"");
update ""LocationLookup"" set ""Id"" = ""insertedLocation"".""Id"" from @LocationLookup ""LocationLookup"" inner join @insertedLocation ""insertedLocation"" on (""LocationLookup"".""_index"" = ""insertedLocation"".""_index"");
update ""Location"" set ""Name"" = ""LocationLookup"".""Name"" from ""Location"" inner join @LocationLookup ""LocationLookup"" on (""LocationLookup"".""Id"" = ""Location"".""Id"") where not exists (select 1 from ""Location"" inner join @insertedLocation ""insertedLocation"" on (""Location"".""Id"" = ""insertedLocation"".""Id"") where (""LocationLookup"".""Id"" = ""insertedLocation"".""Id""));
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int, ""LocationId_index"" int)
insert @WorkLogLookup(""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""LocationId_index"") values(@employeesId0, @employeesStartDate0, @employeesEndDate0, 0, 0, 0), (@employeesId1, @employeesStartDate1, @employeesEndDate1, 1, 1, 1)
merge ""WorkLog"" using (select ""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"", ""LocationId_index"" from @WorkLogLookup ""WorkLogLookup"" where ((""Id"" is null) or not exists (select 1 from ""WorkLog"" where (""WorkLog"".""Id"" = ""WorkLogLookup"".""Id"")))) as i (""Id"",""StartDate"",""EndDate"",""_index"",""EmployeeId_index"",""LocationId_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StartDate"", ""EndDate"", ""EmployeeId"", ""LocationId"") values(""i"".""Id"", ""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index"")), (select ""Id"" from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""_index"" = ""i"".""LocationId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");
update ""WorkLog"" set ""StartDate"" = ""WorkLogLookup"".""StartDate"", ""EndDate"" = ""WorkLogLookup"".""EndDate"", ""EmployeeId"" = (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""WorkLogLookup"".""EmployeeId_index"")), ""LocationId"" = (select ""Id"" from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""_index"" = ""WorkLogLookup"".""LocationId_index"")) from ""WorkLog"" inner join @WorkLogLookup ""WorkLogLookup"" on (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"") where not exists (select 1 from ""WorkLog"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLog"".""Id"" = ""insertedWorkLog"".""Id"") where (""WorkLogLookup"".""Id"" = ""insertedWorkLog"".""Id""));
declare @EmployeeAddressLookup table(""Id"", ""EmployeeId"", ""AddressId"", ""_index"" int, ""AddressId_index"" int, ""EmployeeId_index"" int)
insert @EmployeeAddressLookup(""EmployeeId"", ""AddressId"", ""_index"", ""AddressId_index"", ""EmployeeId_index"") values(@EmployeeAddress_EmployeeId0, @EmployeeAddress_AddressId0, 0, 0, 0), (@EmployeeAddress_EmployeeId1, @EmployeeAddress_AddressId1, 1, 1, 1)
merge ""EmployeeAddress"" using (select ""_index"", ""AddressId_index"", ""EmployeeId_index"" from (select * from (select ""AddressLookup"".""Id"" ""AddressLookupAddressId"", ""EmployeeLookup"".""Id"" ""EmployeeLookupEmployeeId"", ""EmployeeAddressLookup"".""AddressId_index"", ""EmployeeAddressLookup"".""EmployeeId_index"", ""EmployeeAddressLookup"".""_index"", ROW_NUMBER() over(partition by ""AddressLookup"".""Id"", ""EmployeeLookup"".""Id"" order by ""AddressLookup"".""Id"", ""EmployeeLookup"".""Id"") ""SigQLRowNumber"" from @EmployeeAddressLookup ""EmployeeAddressLookup"" inner join @AddressLookup ""AddressLookup"" on (""AddressLookup"".""_index"" = ""EmployeeAddressLookup"".""AddressId_index"") inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""_index"" = ""EmployeeAddressLookup"".""EmployeeId_index"")) SigQLM2MRowNumberLookupQuery where not exists (select * from (select ROW_NUMBER() over(partition by ""Id"", ""EmployeeId"", ""AddressId"" order by ""Id"", ""EmployeeId"", ""AddressId"") ""SigQLRowNumber"", ""Id"", ""EmployeeId"", ""AddressId"" from ""EmployeeAddress"") EmployeeAddressM2MRowNumberLookup where ((""SigQLM2MRowNumberLookupQuery"".""SigQLRowNumber"" = ""EmployeeAddressM2MRowNumberLookup"".""SigQLRowNumber"") and (""SigQLM2MRowNumberLookupQuery"".""AddressLookupAddressId"" = ""EmployeeAddressM2MRowNumberLookup"".""AddressId"") and (""SigQLM2MRowNumberLookupQuery"".""EmployeeLookupEmployeeId"" = ""EmployeeAddressM2MRowNumberLookup"".""EmployeeId"")))) SigQLM2MMultiplicityQuery) as i (""_index"",""AddressId_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""AddressId"", ""EmployeeId"") values((select ""Id"" from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""_index"" = ""i"".""AddressId_index"")), (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""AddressId"", ""inserted"".""EmployeeId"", ""i"".""_index"" into @insertedEmployeeAddress(""AddressId"", ""EmployeeId"", ""_index"");
update ""EmployeeAddressLookup"" set ""AddressId"" = ""insertedEmployeeAddress"".""AddressId"", ""EmployeeId"" = ""insertedEmployeeAddress"".""EmployeeId"" from @EmployeeAddressLookup ""EmployeeAddressLookup"" inner join @insertedEmployeeAddress ""insertedEmployeeAddress"" on (""EmployeeAddressLookup"".""_index"" = ""insertedEmployeeAddress"".""_index"");", sql);
        }

        #endregion

        #region Sync
        
        [TestMethod]
        public void SyncOneToManyNavigationProperty_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.SyncEmployeeWithWorkLogs(
                         new Employee.SyncFieldsWithWorkLogs()
                         {
                             Id = 1,
                             Name = "Kyle",
                             WorkLogs = new []
                             {
                                 new WorkLog.SyncFields() { Id = 1, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.SyncFields() { StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         }));

            AssertSqlEqual(@"declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@employeesId0, @employeesName0, 0)
merge ""Employee"" using (select ""Id"", ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"" where ((""Id"" is null) or not exists (select 1 from ""Employee"" where (""Employee"".""Id"" = ""EmployeeLookup"".""Id"")))) as i (""Id"",""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"") where not exists (select 1 from ""Employee"" inner join @insertedEmployee ""insertedEmployee"" on (""Employee"".""Id"" = ""insertedEmployee"".""Id"") where (""EmployeeLookup"".""Id"" = ""insertedEmployee"".""Id""));
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int)
insert @WorkLogLookup(""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"") values(@employeesWorkLogs_Id0, @employeesWorkLogs_StartDate0, @employeesWorkLogs_EndDate0, 0, 0), (@employeesWorkLogs_Id1, @employeesWorkLogs_StartDate1, @employeesWorkLogs_EndDate1, 1, 0)
merge ""WorkLog"" using (select ""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"" from @WorkLogLookup ""WorkLogLookup"" where ((""Id"" is null) or not exists (select 1 from ""WorkLog"" where (""WorkLog"".""Id"" = ""WorkLogLookup"".""Id"")))) as i (""Id"",""StartDate"",""EndDate"",""_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StartDate"", ""EndDate"", ""EmployeeId"") values(""i"".""Id"", ""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");
update ""WorkLog"" set ""StartDate"" = ""WorkLogLookup"".""StartDate"", ""EndDate"" = ""WorkLogLookup"".""EndDate"", ""EmployeeId"" = (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""WorkLogLookup"".""EmployeeId_index"")) from ""WorkLog"" inner join @WorkLogLookup ""WorkLogLookup"" on (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"") where not exists (select 1 from ""WorkLog"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLog"".""Id"" = ""insertedWorkLog"".""Id"") where (""WorkLogLookup"".""Id"" = ""insertedWorkLog"".""Id""));
delete ""WorkLog"" from ""WorkLog"" where (exists (select 1 from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""Id"" = ""WorkLog"".""EmployeeId"")) and not exists (select 1 from @WorkLogLookup ""WorkLogLookup"" where (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"")))", sql);
        }

        [TestMethod]
        public void SyncManyToManyNavigationProperty_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.SyncManyToManyEmployeeWithAddresses(
                         new Employee.SyncFieldsWithAddresses()
                         {
                             Id = 1,
                             Name = "Kyle",
                             Addresses = new []
                             {
                                 new Address.UpsertFields() { Id = 1, StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
                                 new Address.UpsertFields() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
                             }
                         }));

            AssertSqlEqual(@"declare @insertedEmployeeAddress table(""AddressId"", ""EmployeeId"", ""_index"" int)
declare @insertedAddress table(""Id"" int, ""_index"" int)
declare @insertedEmployee table(""Id"" int, ""_index"" int)
declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@employeesId0, @employeesName0, 0)
merge ""Employee"" using (select ""Id"", ""Name"", ""_index"" from @EmployeeLookup ""EmployeeLookup"" where ((""Id"" is null) or not exists (select 1 from ""Employee"" where (""Employee"".""Id"" = ""EmployeeLookup"".""Id"")))) as i (""Id"",""Name"",""_index"") on (1 = 0)
 when not matched then
 insert (""Name"") values(""i"".""Name"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedEmployee(""Id"", ""_index"");
update ""EmployeeLookup"" set ""Id"" = ""insertedEmployee"".""Id"" from @EmployeeLookup ""EmployeeLookup"" inner join @insertedEmployee ""insertedEmployee"" on (""EmployeeLookup"".""_index"" = ""insertedEmployee"".""_index"");
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"") where not exists (select 1 from ""Employee"" inner join @insertedEmployee ""insertedEmployee"" on (""Employee"".""Id"" = ""insertedEmployee"".""Id"") where (""EmployeeLookup"".""Id"" = ""insertedEmployee"".""Id""));
declare @AddressLookup table(""Id"" int, ""StreetAddress"" nvarchar(max), ""City"" nvarchar(max), ""State"" nvarchar(max), ""_index"" int)
insert @AddressLookup(""Id"", ""StreetAddress"", ""City"", ""State"", ""_index"") values(@employeesAddresses_Id0, @employeesAddresses_StreetAddress0, @employeesAddresses_City0, @employeesAddresses_State0, 0), (@employeesAddresses_Id1, @employeesAddresses_StreetAddress1, @employeesAddresses_City1, @employeesAddresses_State1, 1)
merge ""Address"" using (select ""Id"", ""StreetAddress"", ""City"", ""State"", ""_index"" from @AddressLookup ""AddressLookup"" where ((""Id"" is null) or not exists (select 1 from ""Address"" where (""Address"".""Id"" = ""AddressLookup"".""Id"")))) as i (""Id"",""StreetAddress"",""City"",""State"",""_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StreetAddress"", ""City"", ""State"") values(""i"".""Id"", ""i"".""StreetAddress"", ""i"".""City"", ""i"".""State"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedAddress(""Id"", ""_index"");
update ""AddressLookup"" set ""Id"" = ""insertedAddress"".""Id"" from @AddressLookup ""AddressLookup"" inner join @insertedAddress ""insertedAddress"" on (""AddressLookup"".""_index"" = ""insertedAddress"".""_index"");
update ""Address"" set ""StreetAddress"" = ""AddressLookup"".""StreetAddress"", ""City"" = ""AddressLookup"".""City"", ""State"" = ""AddressLookup"".""State"" from ""Address"" inner join @AddressLookup ""AddressLookup"" on (""AddressLookup"".""Id"" = ""Address"".""Id"") where not exists (select 1 from ""Address"" inner join @insertedAddress ""insertedAddress"" on (""Address"".""Id"" = ""insertedAddress"".""Id"") where (""AddressLookup"".""Id"" = ""insertedAddress"".""Id""));
declare @EmployeeAddressLookup table(""Id"", ""EmployeeId"", ""AddressId"", ""_index"" int, ""AddressId_index"" int, ""EmployeeId_index"" int)
insert @EmployeeAddressLookup(""EmployeeId"", ""AddressId"", ""_index"", ""AddressId_index"", ""EmployeeId_index"") values(@EmployeeAddress_EmployeeId0, @EmployeeAddress_AddressId0, 0, 0, 0), (@EmployeeAddress_EmployeeId1, @EmployeeAddress_AddressId1, 1, 1, 0)
merge ""EmployeeAddress"" using (select ""_index"", ""AddressId_index"", ""EmployeeId_index"" from (select * from (select ""AddressLookup"".""Id"" ""AddressLookupAddressId"", ""EmployeeLookup"".""Id"" ""EmployeeLookupEmployeeId"", ""EmployeeAddressLookup"".""AddressId_index"", ""EmployeeAddressLookup"".""EmployeeId_index"", ""EmployeeAddressLookup"".""_index"", ROW_NUMBER() over(partition by ""AddressLookup"".""Id"", ""EmployeeLookup"".""Id"" order by ""AddressLookup"".""Id"", ""EmployeeLookup"".""Id"") ""SigQLRowNumber"" from @EmployeeAddressLookup ""EmployeeAddressLookup"" inner join @AddressLookup ""AddressLookup"" on (""AddressLookup"".""_index"" = ""EmployeeAddressLookup"".""AddressId_index"") inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""_index"" = ""EmployeeAddressLookup"".""EmployeeId_index"")) SigQLM2MRowNumberLookupQuery where not exists (select * from (select ROW_NUMBER() over(partition by ""Id"", ""EmployeeId"", ""AddressId"" order by ""Id"", ""EmployeeId"", ""AddressId"") ""SigQLRowNumber"", ""Id"", ""EmployeeId"", ""AddressId"" from ""EmployeeAddress"") EmployeeAddressM2MRowNumberLookup where ((""SigQLM2MRowNumberLookupQuery"".""SigQLRowNumber"" = ""EmployeeAddressM2MRowNumberLookup"".""SigQLRowNumber"") and (""SigQLM2MRowNumberLookupQuery"".""AddressLookupAddressId"" = ""EmployeeAddressM2MRowNumberLookup"".""AddressId"") and (""SigQLM2MRowNumberLookupQuery"".""EmployeeLookupEmployeeId"" = ""EmployeeAddressM2MRowNumberLookup"".""EmployeeId"")))) SigQLM2MMultiplicityQuery) as i (""_index"",""AddressId_index"",""EmployeeId_index"") on (1 = 0)
 when not matched then
 insert (""AddressId"", ""EmployeeId"") values((select ""Id"" from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""_index"" = ""i"".""AddressId_index"")), (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""i"".""EmployeeId_index""))) output ""inserted"".""AddressId"", ""inserted"".""EmployeeId"", ""i"".""_index"" into @insertedEmployeeAddress(""AddressId"", ""EmployeeId"", ""_index"");
update ""EmployeeAddressLookup"" set ""AddressId"" = ""insertedEmployeeAddress"".""AddressId"", ""EmployeeId"" = ""insertedEmployeeAddress"".""EmployeeId"" from @EmployeeAddressLookup ""EmployeeAddressLookup"" inner join @insertedEmployeeAddress ""insertedEmployeeAddress"" on (""EmployeeAddressLookup"".""_index"" = ""insertedEmployeeAddress"".""_index"");
; with SigQL__DeleteEmployeeAddress as (
select ""EmployeeAddress"".* from (select *, ROW_NUMBER() over(partition by ""AddressId"", ""EmployeeId"" order by ""AddressId"", ""EmployeeId"") ""SigQL__Occurrence"" from ""EmployeeAddress"") EmployeeAddress where (exists (select 1 from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""Id"" = ""EmployeeAddress"".""EmployeeId"")) and not exists (select 1 from (select *, ROW_NUMBER() over(partition by ""AddressId"", ""EmployeeId"" order by ""AddressId"", ""EmployeeId"") ""SigQL__Occurrence"" from @EmployeeAddressLookup) EmployeeAddressLookup where ((""EmployeeAddressLookup"".""AddressId"" = ""EmployeeAddress"".""AddressId"") and (""EmployeeAddressLookup"".""EmployeeId"" = ""EmployeeAddress"".""EmployeeId"") and (""EmployeeAddress"".""SigQL__Occurrence"" = ""EmployeeAddressLookup"".""SigQL__Occurrence""))))
)
delete from ""SigQL__DeleteEmployeeAddress""
", sql);
        }

        [TestMethod]
        public void SyncOneToManyNavigationProperty_DeletesLeastDependentTablesFirst_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.SyncAddressesWithLocationsWithWorkLogs(
                new Address.SyncFieldsWithLocationsWithWorkLogs()
                {
                    Id = 1,
                    StreetAddress = "1011 fake st",
                    City = "Charlotte",
                    State = "NC",
                    Locations = new List<Location.UpsertWithWorkLogs>()
                    {
                        new Location.UpsertWithWorkLogs()
                        {
                            Name = "Postal Service",
                            WorkLogs = new List<WorkLog.UpsertFields>()
                            {
                                new WorkLog.UpsertFields()
                                {
                                    StartDate = new DateTime(2024, 03, 01),
                                    EndDate = new DateTime(2024, 04, 01)
                                }
                            }
                        }
                    }
                }));

            AssertSqlEqual(@"declare @insertedWorkLog table(""Id"" int, ""_index"" int)
declare @insertedLocation table(""Id"" int, ""_index"" int)
declare @insertedAddress table(""Id"" int, ""_index"" int)
declare @AddressLookup table(""Id"" int, ""StreetAddress"" nvarchar(max), ""City"" nvarchar(max), ""State"" nvarchar(max), ""_index"" int)
insert @AddressLookup(""Id"", ""StreetAddress"", ""City"", ""State"", ""_index"") values(@employeesId0, @employeesStreetAddress0, @employeesCity0, @employeesState0, 0)
merge ""Address"" using (select ""Id"", ""StreetAddress"", ""City"", ""State"", ""_index"" from @AddressLookup ""AddressLookup"" where ((""Id"" is null) or not exists (select 1 from ""Address"" where (""Address"".""Id"" = ""AddressLookup"".""Id"")))) as i (""Id"",""StreetAddress"",""City"",""State"",""_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StreetAddress"", ""City"", ""State"") values(""i"".""Id"", ""i"".""StreetAddress"", ""i"".""City"", ""i"".""State"") output ""inserted"".""Id"", ""i"".""_index"" into @insertedAddress(""Id"", ""_index"");
update ""AddressLookup"" set ""Id"" = ""insertedAddress"".""Id"" from @AddressLookup ""AddressLookup"" inner join @insertedAddress ""insertedAddress"" on (""AddressLookup"".""_index"" = ""insertedAddress"".""_index"");
update ""Address"" set ""StreetAddress"" = ""AddressLookup"".""StreetAddress"", ""City"" = ""AddressLookup"".""City"", ""State"" = ""AddressLookup"".""State"" from ""Address"" inner join @AddressLookup ""AddressLookup"" on (""AddressLookup"".""Id"" = ""Address"".""Id"") where not exists (select 1 from ""Address"" inner join @insertedAddress ""insertedAddress"" on (""Address"".""Id"" = ""insertedAddress"".""Id"") where (""AddressLookup"".""Id"" = ""insertedAddress"".""Id""));
declare @LocationLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int, ""AddressId_index"" int)
insert @LocationLookup(""Id"", ""Name"", ""_index"", ""AddressId_index"") values(@employeesLocations_Id0, @employeesLocations_Name0, 0, 0)
merge ""Location"" using (select ""Id"", ""Name"", ""_index"", ""AddressId_index"" from @LocationLookup ""LocationLookup"" where ((""Id"" is null) or not exists (select 1 from ""Location"" where (""Location"".""Id"" = ""LocationLookup"".""Id"")))) as i (""Id"",""Name"",""_index"",""AddressId_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""Name"", ""AddressId"") values(""i"".""Id"", ""i"".""Name"", (select ""Id"" from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""_index"" = ""i"".""AddressId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedLocation(""Id"", ""_index"");
update ""LocationLookup"" set ""Id"" = ""insertedLocation"".""Id"" from @LocationLookup ""LocationLookup"" inner join @insertedLocation ""insertedLocation"" on (""LocationLookup"".""_index"" = ""insertedLocation"".""_index"");
update ""Location"" set ""Name"" = ""LocationLookup"".""Name"", ""AddressId"" = (select ""Id"" from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""_index"" = ""LocationLookup"".""AddressId_index"")) from ""Location"" inner join @LocationLookup ""LocationLookup"" on (""LocationLookup"".""Id"" = ""Location"".""Id"") where not exists (select 1 from ""Location"" inner join @insertedLocation ""insertedLocation"" on (""Location"".""Id"" = ""insertedLocation"".""Id"") where (""LocationLookup"".""Id"" = ""insertedLocation"".""Id""));
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""LocationId_index"" int)
insert @WorkLogLookup(""Id"", ""StartDate"", ""EndDate"", ""_index"", ""LocationId_index"") values(@employeesLocations_WorkLogs_Id0, @employeesLocations_WorkLogs_StartDate0, @employeesLocations_WorkLogs_EndDate0, 0, 0)
merge ""WorkLog"" using (select ""Id"", ""StartDate"", ""EndDate"", ""_index"", ""LocationId_index"" from @WorkLogLookup ""WorkLogLookup"" where ((""Id"" is null) or not exists (select 1 from ""WorkLog"" where (""WorkLog"".""Id"" = ""WorkLogLookup"".""Id"")))) as i (""Id"",""StartDate"",""EndDate"",""_index"",""LocationId_index"") on (1 = 0)
 when not matched then
 insert (""Id"", ""StartDate"", ""EndDate"", ""LocationId"") values(""i"".""Id"", ""i"".""StartDate"", ""i"".""EndDate"", (select ""Id"" from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""_index"" = ""i"".""LocationId_index""))) output ""inserted"".""Id"", ""i"".""_index"" into @insertedWorkLog(""Id"", ""_index"");
update ""WorkLogLookup"" set ""Id"" = ""insertedWorkLog"".""Id"" from @WorkLogLookup ""WorkLogLookup"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLogLookup"".""_index"" = ""insertedWorkLog"".""_index"");
update ""WorkLog"" set ""StartDate"" = ""WorkLogLookup"".""StartDate"", ""EndDate"" = ""WorkLogLookup"".""EndDate"", ""LocationId"" = (select ""Id"" from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""_index"" = ""WorkLogLookup"".""LocationId_index"")) from ""WorkLog"" inner join @WorkLogLookup ""WorkLogLookup"" on (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"") where not exists (select 1 from ""WorkLog"" inner join @insertedWorkLog ""insertedWorkLog"" on (""WorkLog"".""Id"" = ""insertedWorkLog"".""Id"") where (""WorkLogLookup"".""Id"" = ""insertedWorkLog"".""Id""));
delete ""WorkLog"" from ""WorkLog"" where (exists (select 1 from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""Id"" = ""WorkLog"".""LocationId"")) and not exists (select 1 from @WorkLogLookup ""WorkLogLookup"" where (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"")))
delete ""WorkLog"" from ""Location"" inner join ""WorkLog"" on (""Location"".""Id"" = ""WorkLog"".""LocationId"") where (exists (select 1 from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""Id"" = ""Location"".""AddressId"")) and not exists (select 1 from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""Id"" = ""Location"".""Id"")))
delete ""Location"" from ""Location"" where (exists (select 1 from @AddressLookup ""AddressLookup"" where (""AddressLookup"".""Id"" = ""Location"".""AddressId"")) and not exists (select 1 from @LocationLookup ""LocationLookup"" where (""LocationLookup"".""Id"" = ""Location"".""Id"")))", sql);
        }

        #endregion Sync

        #region UpdateByKey

        [TestMethod]
        public void UpdateByKey_ViaMethodParams_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateByKeyEmployeeViaMethodParams(1, "bob"));

            AssertSqlEqual(@"declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@id, @name, 0)
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"");", sql);
        }

        [TestMethod]
        public void UpdateByKeyMultipleWithOneToManyNavigationProperty_Void_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateByKeyMultipleEmployeesWithWorkLogs(
                    new Employee.UpdateByKeyFieldsWithWorkLogs[]
                    {
                         new Employee.UpdateByKeyFieldsWithWorkLogs()
                         {
                             Id = 1,
                             Name = "bah",
                             WorkLogs = new []
                             {
                                 new WorkLog.UpdateByKeyFields() { Id = 1, StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.UpdateByKeyFields() { Id = 2, StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         },
                         new Employee.UpdateByKeyFieldsWithWorkLogs()
                         {
                             Id = 2,
                             Name = "2bah",
                             WorkLogs = new []
                             {
                                 new WorkLog.UpdateByKeyFields() { Id = 3, StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2) },
                                 new WorkLog.UpdateByKeyFields() { Id = 4, StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2) }
                             }
                         }
                    }));

            AssertSqlEqual(@"declare @EmployeeLookup table(""Id"" int, ""Name"" nvarchar(max), ""_index"" int)
insert @EmployeeLookup(""Id"", ""Name"", ""_index"") values(@employeesId0, @employeesName0, 0), (@employeesId1, @employeesName1, 1)
update ""Employee"" set ""Name"" = ""EmployeeLookup"".""Name"" from ""Employee"" inner join @EmployeeLookup ""EmployeeLookup"" on (""EmployeeLookup"".""Id"" = ""Employee"".""Id"");
declare @WorkLogLookup table(""Id"" int, ""StartDate"" nvarchar(max), ""EndDate"" nvarchar(max), ""_index"" int, ""EmployeeId_index"" int)
insert @WorkLogLookup(""Id"", ""StartDate"", ""EndDate"", ""_index"", ""EmployeeId_index"") values(@employeesWorkLogs_Id0, @employeesWorkLogs_StartDate0, @employeesWorkLogs_EndDate0, 0, 0), (@employeesWorkLogs_Id1, @employeesWorkLogs_StartDate1, @employeesWorkLogs_EndDate1, 1, 0), (@employeesWorkLogs_Id2, @employeesWorkLogs_StartDate2, @employeesWorkLogs_EndDate2, 2, 1), (@employeesWorkLogs_Id3, @employeesWorkLogs_StartDate3, @employeesWorkLogs_EndDate3, 3, 1)
update ""WorkLog"" set ""StartDate"" = ""WorkLogLookup"".""StartDate"", ""EndDate"" = ""WorkLogLookup"".""EndDate"", ""EmployeeId"" = (select ""Id"" from @EmployeeLookup ""EmployeeLookup"" where (""EmployeeLookup"".""_index"" = ""WorkLogLookup"".""EmployeeId_index"")) from ""WorkLog"" inner join @WorkLogLookup ""WorkLogLookup"" on (""WorkLogLookup"".""Id"" = ""WorkLog"".""Id"");", sql);
        }

        #endregion UpdateByKey

        #region Update 

        [TestMethod]
        public void Update_Void_WithNoFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateAllEmployees("bob"));

            AssertSqlEqual("update \"Employee\" set \"Name\" = @name from \"Employee\";", sql);
        }

        [TestMethod]
        public void Update_Void_WithParamFilter_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateEmployeeById("bob", 1));

            AssertSqlEqual("update \"Employee\" set \"Name\" = @name from \"Employee\" where (\"Employee\".\"Id\" = @id);", sql);
        }

        [TestMethod]
        public void Update_Void_WithMultipleSetFields_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateAllWorkLogsStartDateAndEndDate(DateTime.Today, DateTime.Today));

            AssertSqlEqual("update \"WorkLog\" set \"StartDate\" = @startDate, \"EndDate\" = @endDate from \"WorkLog\";", sql);
        }

        [TestMethod]
        public void Update_Void_WithSetFieldsClass_ReturnsExpectedSql()
        {
            var sql = GetSqlForCall(() => this.monolithicRepository.UpdateAllWorkLogsStartDateAndEndDateSetClass(new WorkLog.SetDateFields()
            {
                StartDate = DateTime.Today,
                EndDate = DateTime.Today
            }));

            AssertSqlEqual("update \"WorkLog\" set \"StartDate\" = @workLogDatesStartDate, \"EndDate\" = @workLogDatesEndDate from \"WorkLog\";", sql);
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
        public void WhenParentAttributeNestedOutputTableNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingParentAttributeNestedTableName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database table for type UnknownSqlIdentifierTable.NestedWithId. Table UnknownSqlIdentifierTable does not exist.",
                        ex.Message);
                    throw;
                }
            });
        }

        [TestMethod]
        public void WhenParentOutputTableNotExists_ThrowsMeaningfulException()
        {
            Assert.ThrowsException<InvalidIdentifierException>(() =>
            {
                var methodInfo = typeof(IMonolithicRepository).GetMethod(nameof(IMonolithicRepository.INVALID_NonExistingParentTableName));
                try
                {
                    GetSqlFor(methodInfo);
                }
                catch (InvalidIdentifierException ex)
                {
                    Assert.AreEqual("Unable to identify matching database table for type UnknownTable.NestedWithId. Table UnknownTable does not exist.",
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
                    Assert.AreEqual("Unable to identify matching database column for parameter locationId with [ViaRelation(\"Employee->WorkLog\", \"NonExistent\")]. Column NonExistent does not exist in table WorkLog.",
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
                    Assert.AreEqual("Unable to identify matching database table for parameter locationId with relational path \"Employee->NonExistent\". Table NonExistent does not exist.",
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
                    Assert.AreEqual("Unable to identify matching database foreign key for parameter locationId with [ViaRelation(\"Employee->Location\", \"Id\")]. No foreign key between Employee and Location could be found.",
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
