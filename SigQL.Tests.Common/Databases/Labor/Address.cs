﻿using System.Collections.Generic;
using SigQL.Types.Attributes;

namespace SigQL.Tests.Common.Databases.Labor
{
    public class Address
    {
        public int Id { get; set; }
        public string StreetAddress { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public IEnumerable<Location> Locations { get; set; }
        public IEnumerable<Employee> Employees { get; set; }
        public AddressClassification Classification { get; set; }

        public interface IAddressIdWithLocations
        {
            int Id { get; }
            IEnumerable<Location.ILocationFields> Locations { get; }
        }

        public interface IAddressId
        {
            int Id { get;}
        }

        public interface IAddressFields
        {
            int Id { get;}
            string StreetAddress { get; }
        }

        public class AddressFieldsPoco
        {
            public int Id { get; set; }
            public string StreetAddress { get; set; }
        }

        public interface IId
        {
            int Id { get;}
        }

        public interface IAddressWithClassification
        {
            AddressClassification Classification { get; }
        }

        public class StreetAddressFilter
        {
            public string StreetAddress { get; set; }
        }

        public class StreetAddressFilterWithAlias
        {
            [Column(nameof(StreetAddress))] 
            public string AddressLine1 { get; set; }
        }

        public interface IStreetAddressCoordinates        
        {
            IEnumerable<StreetAddressCoordinate.ICoordinates> Coordinates { get; }
        }

        public class InsertFields
        {
            public string StreetAddress { get; set; }
            public string City { get; set; }
            public string State { get; set; }
        }
    }

    public enum AddressClassification
    {
        Home,
        Work
    }
}