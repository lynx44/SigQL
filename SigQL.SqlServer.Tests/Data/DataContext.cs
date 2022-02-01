using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.SqlServer.Tests.Data
{
    public class LaborDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        // public LaborDbContext(DbContextOptions<LaborDbContext> options)
        //     : base(options)
        // {
        // }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(TestSettings.LaborConnectionString);
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EFCompositeKeyTable>(t => t.HasKey(t => new {t.FirstName, t.LastName}));
        }

        public DbSet<EFWorkLog> WorkLog { get; set; }
        public DbSet<EFEmployee> Employee { get; set; }
        public DbSet<EFLocation> Location { get; set; }
        public DbSet<EFAddress> Address { get; set; }
        //public DbSet<EFStreetAddressCoordinate> StreetAddressCoordinates { get; set; }
        public DbSet<EFCompositeKeyTable> CompositeKeyTable { get; set; }
        public DbSet<EFCompositeForeignKeyTable> CompositeForeignKeyTable { get; set; }
    }

    public class EFWorkLog : WorkLog.IFields
    {
        public int Id { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? EmployeeId { get; set; }
        public EFEmployee Employee { get; set; }
        public int? LocationId { get; set; }
        public EFLocation Location { get; set; }
    }

    public class EFEmployee : Employee.IEmployeeFields
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ICollection<EFAddress> Addresses { get; set; }
        public ICollection<EFWorkLog> WorkLogs { get; set; }
    }

    public class EFLocation : Location.ILocationFields
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? AddressId { get; set; }
        public EFAddress Address { get; set; }
    }

    public class EFAddress : Address.IAddressFields
    {
        public int Id { get; set; }
        public string StreetAddress { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public ICollection<EFLocation> Locations { get; set; }
        public ICollection<EFEmployee> Employees { get; set; }
        public AddressClassification Classification { get; set; }
    }

    //public class EFStreetAddressCoordinate : StreetAddressCoordinate.IStreetAddressCoordinateFields
    //{
    //    public int Id { get; set; }
    //    public string StreetAddress { get; set; }
    //    public string City { get; set; }
    //    public string State { get; set; }
    //    public decimal Latitude { get; set; }
    //    public decimal Longitude { get; set; }
    //}

    public class EFCompositeKeyTable
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public ICollection<EFCompositeForeignKeyTable> EFCompositeForeignKeyTables { get; set; }
    }

    public class EFCompositeForeignKeyTable
    {
        public int Id { get; set; }
    }
}
