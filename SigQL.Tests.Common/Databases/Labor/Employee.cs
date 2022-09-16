using System.Collections.Generic;
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

        public interface IEmployeeFields
        {
            int Id { get; set; }
            string Name { get; set; }
        }

        public class InsertFields
        {
            public string Name { get; set; }
        }

        public class InsertFieldsWithWorkLogs
        {
            public string Name { get; set; }
            public IEnumerable<WorkLog.DataFields> WorkLogs { get; set; }
        }

        public interface IEmployeeWithAddresses
        {
            int Id { get; set; }
            IEnumerable<Address.IAddressFields> Addresses { get; set; }
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
            [ViaRelation(nameof(Employee) + "->" + nameof(EmployeeAddress) + "->" + nameof(Address) + "." + nameof(Address.StreetAddress))]
            public string StreetAddress { get; set; }
        }

        public class EFStreetAddressFilterViaRelation

        {
            [ViaRelation(nameof(Employee) + "->EFAddressEFEmployee->" + nameof(Address) + "." + nameof(Address.StreetAddress))]
            public string StreetAddress { get; set; }
        }

        public class WorkLogLocationIdFilterViaRelation
        {
            [ViaRelation(nameof(Employee) + "->" + nameof(WorkLog) + "." + nameof(WorkLog.LocationId))]
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
    }

    // not yet used/supported
    // public class EmployeeInsertFields
    // {
    //     public string Name { get; set; }
    // }
}