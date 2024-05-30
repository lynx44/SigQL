using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SigQL.Types;
using SigQL.Types.Attributes;

namespace SigQL.Tests.Common.Databases.Labor
{
    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public IEnumerable<Address> Addresses { get; set; }
        
        public interface IName
        {
            string Name { get; }
        }

        public class EmployeeNameFilter
        {
            public string Name { get; set; }
        }

        public class EmployeeNameNotFilter
        {
            [Not] public string Name { get; set; }
        }

        public class EmployeeNameLikeFilter
        {
            public Like Name { get; set; }
        }

        public interface IEmployeeId
        {
            int Id { get; set; }
        }

        public interface IEmployeeID_MismatchingCase
        {
            int ID { get; set; }
        }

        public interface IEmployeeFields
        {
            int Id { get; set; }
            string Name { get; set; }
        }

        public class InsertFields
        {
            public string Name { get; set; }
        }

        public class InsertFieldsWithAddress
        {
            public string Name { get; set; }
            public IEnumerable<Address.InsertFields> Addresses  { get; set; }
        }

        public class InsertFieldsWithListAddresses
        {
            public string Name { get; set; }
            public List<Address.InsertFields> Addresses  { get; set; }
        }

        public class UpsertFieldsWithAddress
        {
            public int? Id { get; set; }
            public string Name { get; set; }
            public IEnumerable<Address.UpsertFields> Addresses  { get; set; }
        }

        public class InsertFieldsWithWorkLogs
        {
            public string Name { get; set; }
            public IEnumerable<WorkLog.DataFields> WorkLogs { get; set; }
        }

        public class UpdateByKeyFieldsWithWorkLogs
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public IEnumerable<WorkLog.UpdateByKeyFields> WorkLogs { get; set; }
        }

        public class UpsertFieldsWithWorkLogs
        {
            public int? Id { get; set; }
            public string Name { get; set; }
            public IEnumerable<WorkLog.UpsertFields> WorkLogs { get; set; }
        }

        public interface IEmployeeWithAddresses
        {
            int Id { get; set; }
            IEnumerable<Address.IAddressFields> Addresses { get; set; }
        }

        public interface IEmployeeWithListAddresses
        {
            int Id { get; set; }
            List<Address.IAddressFields> Addresses { get; set; }
        }

        public interface IEmployeeWithIListAddresses
        {
            int Id { get; set; }
            IList<Address.IAddressFields> Addresses { get; set; }
        }

        public interface IEmployeeWithReadOnlyCollectionAddresses
        {
            int Id { get; set; }
            ReadOnlyCollection<Address.IAddressFields> Addresses { get; set; }
        }

        public interface IEmployeeWithIReadOnlyCollectionAddresses
        {
            int Id { get; set; }
            IReadOnlyCollection<Address.IAddressFields> Addresses { get; set; }
        }

        public interface IEmployeeWithArrayAddresses
        {
            int Id { get; set; }
            Address.IAddressFields[] Addresses { get; set; }
        }

        public class EmployeeWithAddressesPoco
        {
            public int Id { get; set; }
            public IEnumerable<Address.AddressFieldsPoco> Addresses { get; set; }
        }

        public class StreetAddressFilter
        {
            public Address.StreetAddressFilter Address { get; set; }
        }

        public class EmployeeNamesInFilter
        {
            public IEnumerable<string> Name { get; set; }
        }

        public class IdFilter
        {
            public int Id { get; set; }
        }

        public class EmployeeIdFilter
        {
            [Column(nameof(Id))] public int EmployeeId { get; set; }
        }


        public class EmployeeAddressWithNestedColumnAliasFilter
        {
            public Address.StreetAddressFilterWithAlias Address { get; set; }
        }

        public class EmployeeWithClrOnlyProperty
        {
            public int Id { get; set; }
            [ClrOnly]
            public string ClrOnlyProperty => $"example{Id}";
        }

        public class StreetAddressFilterViaRelation

        {
            [ViaRelation(nameof(Employee) + "->" + nameof(EmployeeAddress) + "->" + nameof(Address), nameof(Address.StreetAddress))]
            public string StreetAddress { get; set; }
        }

        public class EFStreetAddressFilterViaRelation

        {
            [ViaRelation(nameof(Employee) + "->EFAddressEFEmployee->" + nameof(Address), nameof(Address.StreetAddress))]
            public string StreetAddress { get; set; }
        }

        public class WorkLogLocationIdFilterViaRelation
        {
            [ViaRelation(nameof(Employee) + "->" + nameof(WorkLog), nameof(WorkLog.LocationId))]
            public int LocationId { get; set; }
        }
        
        public class EmployeeNameOrder
        {
            public OrderByDirection Name { get; set; }
        }
        
        public class DynamicOrderBy
        {
            public IEnumerable<IOrderBy> Order { get; set; }
        }

        public interface IEmployeeToWorkLogView
        {
            [JoinRelation("Employee(Id)->(EmployeeId)WorkLogEmployeeView")]
            IEnumerable<WorkLogEmployeeView.IDataFields> View { get; }
        }

        public interface EmployeeToAddressJoinRelationAttribute
        {
            [JoinRelation("Employee(Id)->(EmployeesId)EFAddressEFEmployee(AddressesId)->(Id)Address")]
            IEnumerable<Address.IAddressFields> Addresses { get; }
        }

        public interface IEmployeeWithAliasedWorkLogs
        {
            IEnumerable<MyWorkLog> WorkLogs { get; }
        }
        
        public class InsertEmployeeTwice
        {
            public string Name { get; set; }
            public WorkLog.InsertFieldsWithEmployee WorkLog { get; set; }
        }
    }

    [SqlIdentifier(nameof(Employee))]
    public class MyEmployee
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    [SqlIdentifier(nameof(Employee))]
    public class MyEmployeeIdFilter
    {
        public int Id { get; set; }
    }

    public class Employees
    {
        public interface IEmployeeFields
        {
            int Id { get; set; }
            string Name { get; set; }
        }
    }

    // not yet used/supported
    // public class EmployeeInsertFields
    // {
    //     public string Name { get; set; }
    // }
}

namespace SigQL.Tests.Common.Databases.Labor.Alt
{
    public class WorkLog
    {
        public int Id { get; set; }
        public Employee Employee { get; set; }
    }

    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public IEnumerable<Address> Addresses { get; set; }
    }

    public class Address
    {
        public int Id { get; set; }
        public string StreetAddress { get; set; }
        public string City { get; set; }
        public string State { get; set; }
    }
}