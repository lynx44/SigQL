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

        public class DataFields
        {
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

        public class DynamicOrderByEnumerable
        {
            public IEnumerable<IOrderBy> OrderBys { get; set; }
        }
    }
}