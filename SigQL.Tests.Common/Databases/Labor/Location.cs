namespace SigQL.Tests.Common.Databases.Labor
{
    public class Location : Location.ILocationId
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? AddressId { get; set; }
        public Address Address { get; set; }

        public interface ILocationId
        {
            int Id { get; set; }
        }

        public interface ILocationFields
        {
            int Id { get; }
            string Name { get; }
            int? AddressId { get; }
        }

        public class Insert
        {
            public string Name { get; set; }
        }

        public class Upsert
        {
            public int? Id { get; set; }
            public string Name { get; set; }
        }

        public interface ILocationWithAddress
        {
            int Id { get; }
            string Name { get; }
            Address.IAddressFields Address { get; set; }
        }
    }
}