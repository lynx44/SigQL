namespace SigQL.Tests.Common.Databases.Labor
{
    public class StreetAddressCoordinate
    {
        public int Id { get; set; }
        public string StreetAddress { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }

        public interface IStreetAddressCoordinateFields
        {
            int Id { get; }
            string StreetAddress { get; }
            string City { get; }
            string State { get; }
            decimal Latitude { get; }
            decimal Longitude { get; }
        }

        public interface ICoordinates
        {
            decimal Latitude { get; }
            decimal Longitude { get; }
        }
    }
}
