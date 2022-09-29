using System;
using System.Collections.Generic;
using SigQL.Types;
using SigQL.Types.Attributes;

namespace SigQL.Tests.Common.Databases.Labor
{
    public interface IMonolithicRepository
    {
        IEnumerable<Employee.IEmployeeFields> GetAllEmployeeFields();
        Employee.IEmployeeFields Get(int id);
        IEnumerable<WorkLog> GetWorkLogs();
        WorkLog.IAliasedWorkLogId GetWithAliasedColumnName(int id);
        MyWorkLog GetWithSqlIdentifierAttribute();
        MyWorkLogWithEmployee GetNavigationPropertyWithSqlIdentifierAttribute();
        Employee.IEmployeeFields GetByName(string name);
        Employee.IEmployeeFields GetByFilter(Employee.IdFilter filter);
        Employee.IEmployeeFields GetByFilterWithSqlIdentifierAttribute(MyEmployeeIdFilter filter);
        Employee.IEmployeeFields GetWithSpecifiedColumnName([Column(nameof(Employee.Id))] int employeeId);
        Employee.IEmployeeFields GetWithFilterSpecifiedColumnName(Employee.EmployeeIdFilter filter);
        Employee.IEmployeeId GetWithFilterNestedSpecifiedColumnName(Employee.EmployeeAddressWithNestedColumnAliasFilter filter);
        IEnumerable<Address.IAddressWithClassification> GetAddressesWithEnumClassification();
        Employee.IEmployeeFields GetNot([Not] int id);
        Employee.IEmployeeFields GetByNameNot([Not] string name);
        Employee.IEmployeeFields GetByNameFilter(Employee.EmployeeNameFilter filter);
        Employee.IEmployeeFields GetByNameFilterNot(Employee.EmployeeNameNotFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsWithAnyIdNotIn([Not] IEnumerable<int?> id);
        IEnumerable<Employee.IEmployeeFields> GetClrOnly([ClrOnly] int id);
        IEnumerable<WorkLog.IWorkLogId> GetClrOnlyMixedWithColumnParams([ClrOnly] int id, int employeeId);
        IEnumerable<Employee.IEmployeeFields> GetClrOnlyParameterClass([ClrOnly] Employee.IdFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetClrOnlyFilterClassProperty(WorkLog.ClrOnlyFilter filter);
        Employees.IEmployeeFields GetEmployeesPlural(int id);
        EmployeeStatus.IId GetEmployeeStatusSingular(int id);



        // joins
        WorkLog.IWorkLogWithEmployee GetWorkLogWithEmployee();
        WorkLog.IWorkLogWithEmployeeNames GetWorkLogWithEmployeeNames();
        WorkLog.IWorkLogWithEmployeeAndLocation GetWorkLogWithEmployeeAndLocation();
        IEnumerable<WorkLog.IWorkLogWithEmployeeAndLocation> GetWorkLogsWithEmployeeAndLocation();
        WorkLog.IWorkLogWithLocationAndLocationAddress GetWorkLogWithLocationAndLocationAddress();
        Address.IAddressIdWithLocations GetAddressWithLocations();
        Employee.IEmployeeWithAddresses GetEmployeeWithAddresses();
        IEnumerable<Employee.IEmployeeWithAddresses> GetEmployeesWithAddresses();
        IEnumerable<Address.IStreetAddressCoordinates> GetAddressWithStreetAddress();
        IEnumerable<Address.IStreetAddressCoordinates> GetAddressWithStreetAddressFetch([Fetch] int limit);

        // where
        WorkLog.IWorkLogId GetWorkLogByLocationIdAndEmployeeId(int locationId, int employeeId);
        WorkLog.IWorkLogId GetWorkLogByEmployeeName(WorkLog.GetByEmployeeNameFilter filter);
        Employee.IEmployeeId GetEmployeeByStreetAddress(Employee.StreetAddressFilter filter);
        // WorkLog.IWorkLogId GetWorkLogByEmployeeNameDirect(Employee.EmployeeNameFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsAttribute([Column(nameof(WorkLog.StartDate))] OrderByDirection direction = OrderByDirection.Ascending);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogs(OrderByDirection startDate = OrderByDirection.Ascending);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogs(OrderByDirection startDate = OrderByDirection.Ascending, OrderByDirection endDate = OrderByDirection.Ascending, OrderByDirection employeeId = OrderByDirection.Ascending);
        IEnumerable<WorkLog.IWorkLogWithEmployee> GetOrderedWorkLogs([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] OrderByDirection employeeName, [Column(nameof(WorkLog.StartDate))] OrderByDirection direction, IOrderBy dynamicOrderBy);
        IEnumerable<WorkLog.IWorkLogWithEmployee> GetOrderedWorkLogsByClassFilterNavigationProperty(WorkLog.OrderByDirectionEmployeeName filter);
        IEnumerable<WorkLog> GetOrderedWorkLogsByClassFilterNavigationPropertyCanonicalType(WorkLog.OrderByDirectionEmployeeName filter);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsByNonProjectedNavigationTable([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] OrderByDirection employeeName);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsMultiple(OrderByDirection startDate = OrderByDirection.Ascending, OrderByDirection endDate = OrderByDirection.Ascending);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsViaClassFilter(WorkLog.OrderByDirectionStartDate filter);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsViaClassFilterMultiple(WorkLog.OrderByDirectionStartDateEndDate filter);
        IEnumerable<WorkLog.IWorkLogId> INVALID_GetOrderedWorkLogs(OrderByDirection theStartDate = OrderByDirection.Ascending);
        //IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsGenericType<TOrder>(OrderBy<TOrder> direction = null);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsWithDynamicOrderBy(IOrderBy order);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetOrderedWorkLogsWithDynamicOrderByRelation(IOrderBy order);
        IEnumerable<WorkLog> GetOrderedWorkLogsWithDynamicOrderByRelationCanonicalDataType(IOrderBy order);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsWithDynamicEnumerableOrderBy(IEnumerable<IOrderBy> orders);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsWithDynamicEnumerableOrderByViaClassFilter(WorkLog.DynamicOrderByEnumerable filter);
        IEnumerable<WorkLog.IWorkLogId> GetOrderedWorkLogsWithDynamicEnumerableOrderByViaNavigationClassFilter(WorkLog.NavigationDynamicOrderByEnumerable filter);
        IEnumerable<WorkLog.IWorkLogWithEmployeeWithAddress> GetWorkLogsOrderedByAddressId([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee) + "->EFAddressEFEmployee->" + nameof(Address), nameof(Address.Id))] OrderByDirection addressIdSortOrder = OrderByDirection.Ascending);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithLike(Like name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithNotLike([Not] Like name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithStartsWith([StartsWith] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithContains([Contains] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithEndsWith([EndsWith] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithNotStartsWith([Not, StartsWith] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithNotContains([Not, Contains] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameWithNotEndsWith([Not, EndsWith] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameIgnoreIfNull([IgnoreIfNull] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeesByNameIgnoreIfNullOrEmptyString([IgnoreIfNullOrEmpty] string name);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsByEmployeeNameWithLike(WorkLog.GetLikeEmployeeNameFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsWithAnyId(IEnumerable<int?> id);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsWithAnyIdIgnoreIfNull([IgnoreIfNull] IEnumerable<int> id);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty([IgnoreIfNullOrEmpty] IEnumerable<int> id);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsByEmployeeNamesWithIn(WorkLog.GetEmployeeNamesInFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsGreaterThanStartDate([GreaterThan] DateTime startDate);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsGreaterThanOrEqualToStartDate([GreaterThanOrEqual] DateTime startDate);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsGreaterThanStartDateClassFilter(WorkLog.StartDateGreaterThanFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsGreaterThanOrEqualToStartDateClassFilter(WorkLog.StartDateGreaterThanOrEqualFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsBetweenDatesViaAlias([Column(nameof(WorkLog.StartDate)), GreaterThanOrEqual] DateTime startDate, [Column(nameof(WorkLog.StartDate)), LessThanOrEqual] DateTime endDate);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsLessThanStartDate([LessThan] DateTime startDate);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsLessThanOrEqualToStartDate([LessThanOrEqual] DateTime startDate);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsLessThanStartDateClassFilter(WorkLog.StartDateLessThanFilter filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogsLessThanOrEqualToStartDateClassFilter(WorkLog.StartDateLessThanOrEqualFilter filter);
        IEnumerable<Employee.IEmployeeId> GetEmployeeIdsForWorkLogLocationId([ViaRelation(nameof(Employee) + "->" + nameof(WorkLog), nameof(WorkLog.LocationId))] int locationId);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogIdsForEmployeeName([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string name);
        IEnumerable<Employee.IEmployeeId> GetEmployeeIdsForStreetAddress([ViaRelation(nameof(Employee) + "->" + nameof(EmployeeAddress) + "->" + nameof(Address), nameof(Address.StreetAddress))] string streetAddress);
        IEnumerable<Employee.IEmployeeId> EF_GetEmployeeIdsForStreetAddress([ViaRelation(nameof(Employee) + "->EFAddressEFEmployee->" + nameof(Address), nameof(Address.StreetAddress))] string streetAddress);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogIdsForEmployeeNameWithDifferingParameterName([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))] string theEmployeeName);
        IEnumerable<Employee.IEmployeeId> GetEmployeeIdsForWorkLogLocationIdClassFilter(Employee.WorkLogLocationIdFilterViaRelation filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogIdsForEmployeeNameViaClassFilter(WorkLog.EmployeeNameViaRelationFilter filter);
        IEnumerable<Employee.IEmployeeId> GetEmployeeIdsForStreetAddressViaClassFilter(Employee.StreetAddressFilterViaRelation filter);
        IEnumerable<Employee.IEmployeeId> EF_GetEmployeeIdsForStreetAddressViaClassFilter(Employee.EFStreetAddressFilterViaRelation filter);
        IEnumerable<WorkLog.IWorkLogId> GetWorkLogIdsForEmployeeNameWithDifferingParameterNameViaClassFilter(WorkLog.EmployeeNameFilterWithAliasViaRelation filter);

        /// <summary>
        /// This is illegal. The interface T specified in the OrderBy<T> definition must only contain one column,
        /// which is the sort column for the query
        /// </summary>
        //IEnumerable<WorkLog.IWorkLogId> ILLEGAL_GetOrderedWorkLogs(OrderBy<WorkLog.ILocationIdAndEmployeeId> direction);
        IEnumerable<Labor.WorkLog.IInvalidColumn> INVALID_NonExistingColumnName();
        IEnumerable<Labor.NonExistingTable.IId> INVALID_NonExistingTableName();
        IEnumerable<Labor.WorkLog.IInvalidNestedColumn> INVALID_NonExistingNestedTableName();
        IEnumerable<WorkLog.IWorkLogId> INVALID_NonExistingParameterColumnName(int nonExistent);
        IEnumerable<WorkLog.IWorkLogId> INVALID_NonExistingParameterColumnNameWithAlias([Column("nonExistent")] int id);
        IEnumerable<WorkLog.IWorkLogId> INVALID_NonExistingPropertyColumnName(WorkLog.IInvalidColumn args);
        IEnumerable<WorkLog.IWorkLogId> INVALID_NonExistingPropertyColumnNameWithAlias(WorkLog.IInvalidColumnWithAlias args);
        IEnumerable<WorkLog.IWorkLogId> INVALID_NonExistingPropertyTableName(WorkLog.IInvalidNestedColumn args);
        IEnumerable<WorkLog.IWorkLogId> INVALID_NonExistingPropertyForeignKey(WorkLog.IInvalidAddressRelation args);
        IEnumerable<Employee.IEmployeeId> INVALID_NonExistingViaRelationColumnName([ViaRelation(nameof(Employee) + "->" + nameof(WorkLog), "NonExistent")] int locationId);
        IEnumerable<Employee.IEmployeeId> INVALID_NonExistingViaRelationTableName([ViaRelation(nameof(Employee) + "->NonExistent", "Id")] int locationId);
        IEnumerable<Employee.IEmployeeId> INVALID_NonExistingViaRelationForeignKey([ViaRelation(nameof(Employee) + "->" + nameof(Location), nameof(Location.Id))] int locationId);
        //IEnumerable<Labor.WorkLog.IWorkLogId> INVALID_NonExistingDirectParameterColumnName();

        // offset
        //IEnumerable<WorkLog.IWorkLogId> TakeWorkLogsOnly(Fetch take);
        //IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogs(Offset skip);
        //IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsWithOrder(Offset skip, OrderBy<WorkLog.IStartDate> order = null);
        //IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsWithPrimaryTableFilter(Offset skip, DateTime startDate);
        //IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsWithNavigationTableFilter(Offset skip, WorkLog.GetByEmployeeNameFilter filter);
        //IEnumerable<WorkLog.IWorkLogWithEmployeeNames> TakeWorkLogs(Fetch take);
        //IEnumerable<WorkLog.IWorkLogWithEmployeeNames> SkipTakeWorkLogs(Offset skip, Fetch take);
        //IEnumerable<DiagnosticLog.IFields> FetchDiagnosticLogs(Fetch take);

        IEnumerable<WorkLog.IWorkLogId> TakeWorkLogsOnly([Fetch] int take);
        IEnumerable<WorkLog.IWorkLogId> TakeWorkLogsOnlyWithFilter([Fetch] int take, int id);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogs([Offset] int skip);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsViaClassFilter(WorkLog.FilterWithOffset filter);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsViaClassFilterAndParameter(WorkLog.FilterWithOffsetAndParameter filter);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsWithNavigationTableFilterWithOffset(WorkLog.GetByEmployeeNameFilterWithOffset filter);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsWithOrder([Offset] int skip, [Column(nameof(WorkLog.StartDate))] OrderByDirection order = OrderByDirection.Ascending);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsWithPrimaryTableFilter([Offset] int skip, DateTime startDate);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> GetNextWorkLogsWithNavigationTableFilter([Offset] int skip, WorkLog.GetByEmployeeNameFilter filter);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> TakeWorkLogs([Fetch] int take);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> SkipTakeWorkLogs([Offset] int skip, [Fetch] int take);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> TakeWorkLogsViaClassFilter(WorkLog.FilterWithFetch filter);
        IEnumerable<WorkLog.IWorkLogWithEmployeeNames> SkipTakeWorkLogsViaClassFilter(WorkLog.FilterWithOffsetFetch filter);
        IEnumerable<WorkLog.IWorkLogId> TakeWorkLogsOnlyViaClassFilter(WorkLog.FilterWithFetch filter);
        IEnumerable<WorkLog.IWorkLogId> TakeWorkLogsOnlyWithFilterViaClassFilter(WorkLog.FilterWithFetchAndParameter filter);

        IEnumerable<Labor.WorkLog.IWorkLogId> SkipTakeWorkLogsByStartDateAndEmployeeName(
            WorkLog.GetByStartDateAndEmployeeNameFilterWithOffsetFetch filter);
        ICountResult<WorkLog.IWorkLogId> CountWorkLogs();
        IEnumerable<DiagnosticLog.IFields> FetchDiagnosticLogs([Fetch] int take);

        IEnumerable<WorkLog.IWorkLogToView> GetWithJoinRelationAttribute();
        IEnumerable<WorkLogEmployeeView.IDataFieldsWithWorkLogs> GetWithJoinRelationAttributeOnViewWithTableNavigationCollection();
        IEnumerable<Employee.IEmployeeToWorkLogView> GetWithJoinRelationAttributeOnTableWithViewNavigationCollection();
        IEnumerable<Address.IEmployeeToWorkLogView> GetWithNestedJoinRelationAttribute();
        IEnumerable<WorkLog.IWorkLogToViewMismatchingCase> GetWithJoinRelationAttributeMismatchingKeyCase();

        // view
        IEnumerable<WorkLogEmployeeView.IFields> GetWorkLogEmployeeView();

        // functions
        IEnumerable<itvf_GetWorkLogsByEmployeeId.IId> itvf_GetWorkLogsByEmployeeId([Parameter] int empId);
        IEnumerable<itvf_GetWorkLogsByEmployeeId.IId> itvf_GetWorkLogsByEmployeeId([Parameter] int empId, [GreaterThan] DateTime startDate);
        // not yet supported
        //IEnumerable<itvf_GetWorkLogsByEmployeeId.IId> itvf_GetWorkLogsByEmployeeIdWithClassParameters(itvf_GetWorkLogsByEmployeeId.Parameters parameters);

        // poco
        IEnumerable<WorkLog.WorkLogWithEmployeeWithAddressPoco> GetWorkLogsOrderedByAddressIdPocoReturn([ViaRelation(nameof(WorkLog) + "->" + nameof(Employee) + "->EFAddressEFEmployee->" + nameof(Address), nameof(Address.Id))] OrderByDirection addressIdSortOrder = OrderByDirection.Ascending);
        IEnumerable<WorkLog.WorkLogIdPocoWithClrOnlyProperty> GetWorkLogsWithClrOnlyProperty();
        IEnumerable<WorkLog.WorkLogIdPocoWithNestedClrOnlyProperty> GetWorkLogsWithNestedClrOnlyProperty();

        // insert
        [Insert(TableName = nameof(Employee))]
        void InsertEmployeeWithAttributeTableNameWithValuesByParams(string name);
        [Insert(TableName = nameof(Employee))]
        Employee.IEmployeeId InsertEmployeeWithAttributeTableNameWithValuesByParamsOutputId(string name);
        [Insert]
        void InsertEmployeeWithAttributeWithValuesByDetectedClass(Employee.InsertFields values);
        [Insert]
        Employee.IEmployeeId InsertEmployeeWithAttributeWithValuesByDetectedClassReturnId(Employee.InsertFields values);
        [Insert]
        void InsertMultipleEmployeesWithAttributeWithValuesByDetectedClass(IEnumerable<Employee.InsertFields> employees);
        [Insert]
        IEnumerable<Employee.IEmployeeId> InsertMultipleEmployeesAndReturnIds(IEnumerable<Employee.InsertFields> employees);
        [Insert]
        void InsertMultipleAddressesWithAttributeWithValuesByDetectedClass(IEnumerable<Address.InsertFields> addresses);
        [Insert]
        void InsertMultipleEmployeesWithWorkLogs(IEnumerable<Employee.InsertFieldsWithWorkLogs> employees);
        // not yet supported
        // [Insert(TableName = nameof(Employee))]
        // void InsertEmployeeWithAttributeWithValuesByUnknownClass(EmployeeInsertFields values);

        //update
        [Update(TableName = nameof(Employee))]
        void UpdateAllEmployees([Set] string name);
        [Update(TableName = nameof(WorkLog))]
        void UpdateAllWorkLogsStartDateAndEndDate([Set] DateTime startDate, [Set] DateTime endDate);
        [Update(TableName = nameof(WorkLog))]
        void UpdateAllWorkLogsStartDateAndEndDateSetClass([Set] WorkLog.SetDateFields workLogDates);
        // todo
        //[Update(TableName = nameof(WorkLog))]
        //void UpdateAllWorkLogsStartDateAndEndDateSetAndFilterClass(WorkLog.SetDatesWithIdFilter workLog);
        [Update(TableName = nameof(Employee))]
        void UpdateEmployeeById([Set] string name, int id);

        // delete
        [Delete(TableName = nameof(Employee))]
        void DeleteEmployeeWithAttributeTableNameWithValuesByParams(string name);
    }
}