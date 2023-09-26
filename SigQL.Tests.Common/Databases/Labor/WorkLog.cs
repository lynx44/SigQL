using System;
using System.Collections.Generic;
using SigQL.Types;
using SigQL.Types.Attributes;

namespace SigQL.Tests.Common.Databases.Labor
{
    public class WorkLog : WorkLog.IWorkLogId
    {
        public int Id { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? EmployeeId { get; set; }
        public Employee Employee { get; set; }
        public int? LocationId { get; set; }
        public Location Location { get; set; }

        public interface ICount : IWorkLogId
        {
        }

        public interface IWorkLogId
        {
            int Id { get; set; }
        }

        public interface ILocationId
        {
            int LocationId { get; set; }
        }

        public interface IStartDate
        {
            DateTime StartDate { get; set; }
        }

        public interface IEndDate
        {
            DateTime EndDate { get; set; }
        }

        public interface IEmployeeId
        {
            int EmployeeId { get; set; }
        }

        public interface IFields
        {
            int Id { get; }
            DateTime? StartDate { get; }
            DateTime? EndDate { get; }
            int? EmployeeId { get; }
            int? LocationId { get; }
        }

        public interface IAliasedWorkLogId
        {
            [Column(nameof(WorkLog.Id))] int WorkLogId { get; }
        }

        public interface ILocationIdAndEmployeeId
        {
            int EmployeeId { get; set; }
            int LocationId { get; set; }
        }

        public interface IUpdateFields
        {
            DateTime StartDate { get; set; }
            DateTime EndDate { get; set; }
            int EmployeeId { get; set; }
            int LocationId { get; set; }
        }

        public interface IFilterFields
        {
            int Id { get; set; }
            DateTime StartDate { get; set; }
            DateTime EndDate { get; set; }
            int EmployeeId { get; set; }
            int LocationId { get; set; }
        }

        public interface IWorkLogWithEmployee
        {
            int Id { get; set; }
            Employee.IEmployeeFields Employee { get; set; }
        }

        public interface IWorkLogWithEmployeeNames
        {
            int Id { get; set; }
            Employee.IName EmployeeNames { get; set; }
        }

        public class FilterWithOffset
        {
            [Offset] public int Offset { get; set; }
        }

        public class FilterWithFetch
        {
            [Fetch] public int Fetch { get; set; }
        }

        public class FilterWithOffsetFetch
        {
            [Offset] public int Offset { get; set; }
            [Fetch] public int Fetch { get; set; }
        }

        public class FilterWithFetchAndParameter
        {
            [Fetch] public int Fetch { get; set; }
            public int Id { get; set; }
        }

        public class FilterWithOffsetAndParameter
        {
            [Offset] public int Offset { get; set; }
            public DateTime StartDate { get; set; }
        }

        public class LocationWorkLogFilter
        {
            public Location.ILocationId Location { get; set; }
        }

        public interface WorkLogEmployeeNameLocationNameResult
        {
            int Id { get; set; }
            DateTime StartDate { get; set; }
            DateTime EndDate { get; set; }
            string LocationName { get; set; }
        }

        public interface IWorkLogWithEmployeeAndLocation
        {
            int Id { get; set; }
            Employee.IEmployeeFields Employee { get; set; }
            Location.ILocationFields Location { get; set; }
        }

        public interface IWorkLogWithEmployeeWithAddress
        {
            int Id { get; set; }
            Employee.IEmployeeWithAddresses Employee { get; set; }
        }

        public class WorkLogWithEmployeeWithAddressPoco
        {
            public int Id { get; set; }
            public Employee.EmployeeWithAddressesPoco Employee { get; set; }
        }

        public interface IWorkLogWithLocationAndLocationAddress
        {
            int Id { get; set; }
            Location.ILocationWithAddress Location { get; set; }
        }

        public class GetByEmployeeNameFilter
        {
            public Employee.EmployeeNameFilter Employee { get; set; }
        }
        public class GetByEmployeeNameFilterWithOffset
        {
            [Offset] public int Offset { get; set; }
            public Employee.EmployeeNameFilter Employee { get; set; }
        }
        public class GetByStartDateAndEmployeeNameFilterWithOffsetFetch
        {
            [Offset] public int Offset { get; set; }
            [Fetch] public int Fetch { get; set; }
            public DateTime StartDate { get; set; }
            public Employee.EmployeeNameFilter Employee { get; set; }
        }

        public class GetLikeEmployeeNameFilter
        {
            public Employee.EmployeeNameLikeFilter Employee { get; set; }
        }

        public class GetEmployeeNamesInFilter
        {
            public Employee.EmployeeNamesInFilter Employee { get; set; }
        }

        public class GetEmployeeNamesViaRelation
        {
            [IgnoreIfNullOrEmpty, ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Labor.Employee.Name))]
            public IEnumerable<string> Names { get; set; }
        }

        public class GetEmployeeNamesAndAddressCitiesViaRelation
        {
            [IgnoreIfNullOrEmpty, ViaRelation(nameof(WorkLog) + "->" + nameof(Labor.Employee), nameof(Labor.Employee.Name))]
            public IEnumerable<string> EmployeeNames { get; set; }
            [IgnoreIfNullOrEmpty, ViaRelation(nameof(WorkLog) + "->" + nameof(Labor.Employee) + "->" + nameof(EmployeeAddress) + "->" + nameof(Address), nameof(Labor.Address.City))]
            public IEnumerable<string> AddressCities { get; set; }
        }

        public class GetEmployeeNamesAndEmployeeIdsViaRelation
        {
            [IgnoreIfNullOrEmpty, ViaRelation(nameof(WorkLog) + "->" + nameof(Labor.Employee), nameof(Labor.Employee.Name))]
            public IEnumerable<string> EmployeeNames { get; set; }
            [IgnoreIfNullOrEmpty, ViaRelation(nameof(WorkLog) + "->" + nameof(Labor.Employee), nameof(Labor.Employee.Id))]
            public IEnumerable<int> EmployeeIds { get; set; }
        }

        public class GetEmployeeNamesAndAddressCitiesViaRelationEF
        {
            [IgnoreIfNullOrEmpty, ViaRelation(nameof(WorkLog) + "->" + nameof(Labor.Employee), nameof(Labor.Employee.Name))]
            public IEnumerable<string> EmployeeNames { get; set; }
            [IgnoreIfNullOrEmpty, ViaRelation(nameof(WorkLog) + "->" + nameof(Labor.Employee) + "->EFAddressEFEmployee->" + nameof(Address), nameof(Labor.Address.City))]
            public IEnumerable<string> AddressCities { get; set; }
        }

        public class DataFields
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        public class UpdateByKeyFields
        {
            public int Id { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        public class UpsertFields
        {
            public int? Id { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
        }

        public class SetDateFields
        {
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
        }

        public class SetDatesWithIdFilter
        {
            [Set]
            public DateTime StartDate { get; set; }
            [Set]
            public DateTime EndDate { get; set; }
            public int Id { get; set; }
        }

        public interface IInvalidColumn
        {
            int WorkLogId { get; set; }
        }

        public interface IInvalidColumnWithAlias
        {
            [Column("WorkLogId")] int Id { get; set; }
        }

        public interface IInvalidNestedColumn
        {
            IEnumerable<NonExistingTable.IId> NonExistingTable { get; set; }
        }

        public interface IInvalidAddressRelation
        {
            Address.IAddressId Address { get; set; }
        }

        public interface IAddressesEF
        {
            [JoinRelation("WorkLog(EmployeeId)->(Id)Employee(Id)->(EmployeesId)EFAddressEFEmployee(AddressesId)->(Id)Address")]
            IEnumerable<Address.IAddressId> Addresses { get; }
        }

        public interface IAddresses
        {
            [JoinRelation("WorkLog(EmployeeId)->(Id)Employee(Id)->(EmployeeId)EmployeeAddress(AddressId)->(Id)Address")]
            IEnumerable<Address.IAddressId> Addresses { get; }
        }

        public class WorkLogIdPoco
        {
            public int Id { get; set; }
        }

        public class WorkLogIdPocoWithExtraProperty
        {
            public int Id { get; set; }
            public string PayRateExtra { get; set; }
        }

        public class WorkLogIdPocoWithClrOnlyProperty
        {
            public int Id { get; set; }
            [ClrOnly]
            public string ClrOnlyProperty => $"example{Id}";
        }

        public class WorkLogIdPocoWithNestedClrOnlyProperty
        {
            public int Id { get; set; }
            public Employee.EmployeeWithClrOnlyProperty Employee { get; set; }
        }

        public class StartDateGreaterThanFilter
        {
            [GreaterThan]
            public DateTime StartDate { get; set; }
        }

        public class StartDateGreaterThanOrEqualFilter
        {
            [GreaterThanOrEqual]
            public DateTime StartDate { get; set; }
        }

        public class StartDateLessThanFilter
        {
            [LessThan]
            public DateTime StartDate { get; set; }
        }

        public class StartDateLessThanOrEqualFilter
        {
            [LessThanOrEqual]
            public DateTime StartDate { get; set; }
        }
        
        public class ClrOnlyFilter
        {
            [ClrOnly] public int Id { get; set; }
            public int EmployeeId { get; set; }
        }

        public class OrderByDirectionStartDate
        {
            public OrderByDirection StartDate { get; set; }
        }

        public class OrderByDirectionStartDateEndDate
        {
            public OrderByDirection StartDate { get; set; }
            public OrderByDirection EndDate { get; set; }
        }

        public class OrderByDirectionEmployeeName
        {
            public Employee.EmployeeNameOrder Employee { get; set; }
        }
        public class DynamicOrderByEnumerable
        {
            public IEnumerable<IOrderBy> OrderBys { get; set; }
        }

        public class NavigationDynamicOrderByEnumerable
        {
            public Employee.DynamicOrderBy Employee { get; set; }
        }

        public class EmployeeNameViaRelationFilter
        {
            [ViaRelation(nameof(WorkLog) + "->" + nameof(Labor.Employee), nameof(Labor.Employee.Name))]
            public string Name { get; set; }
        }

        public class EmployeeNameFilterWithAliasViaRelation
        {
            [ViaRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name))]
            public string TheEmployeeName { get; set; }
        }

        public interface IWorkLogToView
        {
            int Id { get; }
            [JoinRelation("WorkLog(EmployeeId)->(EmployeeId)WorkLogEmployeeView")]
            WorkLogEmployeeView.IFields View { get; }
        }

        public interface IWorkLogToViewMismatchingCase
        {
            int Id { get; }
            [JoinRelation("WorkLog(EmployeeId)->(EmployeeId)WorkLogEmployeeView")]
            WorkLogEmployeeView.IFieldsMismatchingCase View { get; }
        }

        public interface IWorkLogWithMultipleJoinRelationAttributes
        {
            int Id { get; }
            [JoinRelation("WorkLog(EmployeeId)->(EmployeeId)WorkLogEmployeeView")]
            WorkLogEmployeeView.IFields View { get; }
            [JoinRelation("WorkLog(EmployeeId)->(EmployeeId)WorkLogEmployeeView(WorkLogId)->(Id)WorkLog")]
            IEnumerable<WorkLog.IWorkLogId> WorkLogs { get; }
        }

        public interface IWorkLogToViewToEmployee
        {
            int Id { get; }
            [JoinRelation("WorkLog(EmployeeId)->(EmployeeId)WorkLogEmployeeView(EmployeeId)->(Id)Employee")]
            IEnumerable<Employee.IEmployeeFields> Employee { get; }
        }

        public class InsertFieldsWithEmployee
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public Employee.InsertFields Employee { get; set; }
        }

        public class InsertFieldsWithEmployeeAndLocation
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public Employee.InsertFieldsWithAddress Employee { get; set; }
            public Location.Insert Location { get; set; }
        }
        public class UpsertFieldsWithEmployeeAndLocation
        {
            public int? Id { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public Employee.UpsertFieldsWithAddress Employee { get; set; }
            public Location.Upsert Location { get; set; }
        }

        public interface IEmployeeID_MismatchingCase
        {
            Employee.IEmployeeID_MismatchingCase Employee { get; }
        }

        public class INVALID_MismatchingColumnType
        {
            public string Id { get; set; }
        }

        public class BetweenDates
        {
            [GreaterThanOrEqual]
            public DateTime StartDate { get; set; }
            [LessThanOrEqual]
            public DateTime EndDate { get; set; }
        }

        public class IdAndEmployeeId
        {
            public int Id { get; set; }
            public int EmployeeId { get; set; }
        }

        public class OrColumns
        {
            [OrGroup]
            public int EmployeeId { get; set; }
            [OrGroup]
            public DateTime StartDate { get; set; }
        }

        public class OrColumns2
        {
            [OrGroup]
            public int Id { get; set; }
            [OrGroup]
            public DateTime EndDate { get; set; }
        }

        public class NestedOrColumns
        {
            public Employee.OrColumns Employee { get; set; }
        }

        public class TwoOrGroups
        {
            [OrGroup("1")]
            public DateTime StartDate { get; set; }
            [OrGroup("1")]
            public DateTime EndDate { get; set; }
            [OrGroup("2")]
            public int Id { get; set; }
            [OrGroup("2")]
            public int EmployeeId { get; set; }
        }

        public class NestedColumnAndNavigationClassFilter
        {
            public Employee.ColumnOrNavigationClassFilter Employee { get; set; }
        }
        
        public class OrGroupClassWithColumnAndNavigationClass
        {
            [OrGroup]
            public int Id { get; set; }
            [OrGroup]
            public Employee.EmployeeNameFilter Employee { get; set; }
        }

        public class TwoNestedNavigationClassFilters
        {
            [OrGroup]
            public Employee.EmployeeNameFilter Employee { get; set; }
            [OrGroup]
            public Location.LocationName Location { get; set; }
        }
    }

    [SqlIdentifier(nameof(WorkLog))]
    public class MyWorkLog
    {
        public int Id  { get; set; }
        public DateTime StartDate { get; set; }
    }

    [SqlIdentifier(nameof(WorkLog))]
    public class MyWorkLogWithEmployee
    {
        public int Id  { get; set; }
        public MyEmployee Employee { get; set; }
    }

    [SqlIdentifier(nameof(WorkLog))]
    public class WorkLogTable
    {
        public class NestedWithId
        {
            public int Id { get; set; }
        }
    }

    [SqlIdentifier("UnknownTableName")]
    public class UnknownSqlIdentifierTable
    {
        public class NestedWithId
        {
            public int Id { get; set; }
        }
    }

    public class UnknownTable
    {
        public class NestedWithId
        {
            public int Id { get; set; }
        }
    }
}