using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.SqlServer.Tests.Data;
using SigQL.SqlServer.Tests.Infrastructure;
using SigQL.Tests.Common.Databases.Labor;
using SigQL.Types;

namespace SigQL.SqlServer.Tests
{
    [TestClass]
    public class RepositoryTests
    {
        private IMonolithicRepository monolithicRepository;
        private IWorkLogRepository_IRepository workLogRepositoryWithIRepository;
        private IDbConnection laborDbConnection;
        private IWorkLogRepository workLogRepository;
        private LaborDbContext laborDbContext;
        private AbstractRepository abstractRepository;
        private RepositoryBuilder repositoryBuilder;
        private List<PreparedSqlStatement> sqlStatements;

        [TestInitialize]
        public void Setup()
        {
            laborDbConnection = TestSettings.LaborDbConnection;
            var sqlConnection = (laborDbConnection as SqlConnection);
            DatabaseHelpers.DropAllObjects(sqlConnection);
            
            this.laborDbContext = new LaborDbContext();
            laborDbContext.Database.Migrate();
            sqlStatements = new List<PreparedSqlStatement>();

            var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(sqlConnection.DataSource, sqlConnection.Database);
            repositoryBuilder = new RepositoryBuilder(new SqlQueryExecutor(() => laborDbConnection), sqlDatabaseConfiguration, statement =>
            {
                Console.WriteLine(statement.CommandText);
                sqlStatements.Add(statement);
            });
            this.workLogRepositoryWithIRepository = repositoryBuilder.Build<IWorkLogRepository_IRepository>();
            this.workLogRepository = repositoryBuilder.Build<IWorkLogRepository>();
            this.monolithicRepository = repositoryBuilder.Build<IMonolithicRepository>();
            this.abstractRepository = repositoryBuilder.Build<AbstractRepository>();
        }

        [TestCleanup]
        public void Teardown()
        {
        }

        [TestMethod]
        public void GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = workLogRepositoryWithIRepository.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }

        [TestMethod]
        public void CountWorkLogs()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.CountWorkLogs();

            Assert.AreEqual(expected.Count, actual.Count);
        }
        
        [TestMethod]
        public void GetWorkLogs_AvoidsStackOverflow()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }
        
        [TestMethod]
        public void GetWorkLogs_ReturnsExpectedEmployees()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Name = "James" + i }}).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            AreEquivalent(new List<string>()
            {
                "James1",
                "James2",
                "James3"
            }, actual.Select(w => w.Employee.Name));
        }

        [TestMethod]
        public void GetWorkLogs_ReturnsExpectedEmployeeAddresses()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Addresses = new List<EFAddress>() { new EFAddress() { StreetAddress = "street" + i + "-1" }, new EFAddress() { StreetAddress = "street" + i + "-2" } } } }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            AreEquivalent(new List<string>()
            {
                "street1-1",
                "street1-2",
                "street2-1",
                "street2-2",
                "street3-1",
                "street3-2",
            }, actual.SelectMany(w => w.Employee.Addresses).Select(a => a.StreetAddress));
        }

        [TestMethod]
        public void GetWorkLogs_ReturnsExpectedEmployeeAddressesLocation()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Addresses = new List<EFAddress>() { new EFAddress() { Locations = new List<EFLocation>() { new EFLocation() { Name = $"WL{i}_Employee_Addresses_Locations1"}, new EFLocation() { Name = $"WL{i}_Employee_Addresses_Locations2"} }} } } }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            AreEquivalent(new List<string>()
            {
                "WL1_Employee_Addresses_Locations1",
                "WL1_Employee_Addresses_Locations2",
                "WL2_Employee_Addresses_Locations1",
                "WL2_Employee_Addresses_Locations2",
                "WL3_Employee_Addresses_Locations1",
                "WL3_Employee_Addresses_Locations2",
            }, actual.SelectMany(w => w.Employee.Addresses.SelectMany(a => a.Locations).Select(l => l.Name)));
        }

        [TestMethod]
        public void GetWorkLogs_EmployeeAddressesLocationAddress_ReturnsNull()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Addresses = new List<EFAddress>() { new EFAddress() { Locations = new List<EFLocation>() { new EFLocation() { Address = new EFAddress() } }} } } }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            Assert.IsTrue(actual.All(wl => wl.Employee.Addresses.All(a => a.Locations.All(l => l.Address == null))));
        }

        [TestMethod]
        public void GetWorkLogs_EmployeeAddressesEmployees_ReturnsNull()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Addresses = new List<EFAddress>() { new EFAddress() { Employees = new List<EFEmployee>() { new EFEmployee() }} } } }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            Assert.IsTrue(actual.All(wl => wl.Employee.Addresses.All(a => a.Employees == null)));
        }

        [TestMethod]
        public void GetWorkLogs_ReturnsExpectedLocations()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Location = new EFLocation() { Name = "Location" + i } }).ToList();

            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            AreEquivalent(new List<string>()
            {
                "Location1",
                "Location2",
                "Location3"
            }, actual.Select(w => w.Location.Name));
        }
        
        [TestMethod]
        public void GetWorkLogs_ReturnsExpectedLocationAddress()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Location = new EFLocation() { Address = new EFAddress() { StreetAddress = $"WL{i}_Loc_Address_StreetAddress" }} }).ToList();

            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            AreEquivalent(new List<string>()
            {
                "WL1_Loc_Address_StreetAddress",
                "WL2_Loc_Address_StreetAddress",
                "WL3_Loc_Address_StreetAddress"
            }, actual.Select(w => w.Location.Address.StreetAddress));
        }
        
        [TestMethod]
        public void GetWorkLogs_ReturnsExpectedLocationAddressEmployee()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Location = new EFLocation() { Address = new EFAddress() { Employees = new List<EFEmployee>() { new EFEmployee() { Name = $"WL{i}_Employee1" }, new EFEmployee() { Name = $"WL{i}_Employee2" }  }}} }).ToList();

            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            AreEquivalent(new List<string>()
            {
                "WL1_Employee1",
                "WL1_Employee2",
                "WL2_Employee1",
                "WL2_Employee2",
                "WL3_Employee1",
                "WL3_Employee2",
            }, actual.SelectMany(w => w.Location.Address.Employees.Select(e => e.Name)));
        }
        
        [TestMethod]
        public void GetWorkLogs_LocationAddressLocation_ReturnsNull()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Location = new EFLocation() { Address = new EFAddress() { Locations = new List<EFLocation>() { new EFLocation() }}} }).ToList();

            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            Assert.IsTrue(actual.All(wl => wl.Location.Address.Locations == null));
        }
        
        [TestMethod]
        public void GetWorkLogs_LocationAddressEmployeeAddress_ReturnsNull()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Location = new EFLocation() { Address = new EFAddress() { Employees = new List<EFEmployee>() { new EFEmployee() { Addresses = new List<EFAddress>() { new EFAddress() }} } } } }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

            Assert.IsTrue(actual.All(wl => wl.Location.Address.Employees.All(e => e.Addresses == null)));
        }
        
        [TestMethod]
        public void GetWorkLogs_DoesNotMaterializeDescendentEmployees()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Addresses = new List<EFAddress>() { new EFAddress() { Employees = new List<EFEmployee>() { new EFEmployee() }}}}}).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWorkLogs();

           Assert.IsTrue(actual.All(wl => wl.Employee.Addresses.All(a => a.Employees == null)));
        }

        [TestMethod]
        public void GetAllIds_ViaInnerProjection()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = workLogRepository.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }

        [TestMethod]
        public void GetMultipleBaseFields_ViaInnerProjection()
        {
            var expected = new[] {1, 2, 3, 4, 5}.Select(i => new EFEmployee() { Name = "Name" + i }).ToList();
            this.laborDbContext.Employee.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetAllEmployeeFields();
        
            AreEquivalent(expected.Select(e => new { Id = e.Id, Name = e.Name }), actual.Select(e => new { Id = e.Id, Name = e.Name }));
        }

        [TestMethod]
        public void GetMultipleBaseFields_ViaInnerProjection_WhenNoRecords_ReturnsEmpty()
        {
            var actual = this.monolithicRepository.GetAllEmployeeFields();
        
            Assert.AreEqual(0, actual.Count());
        }

        [TestMethod]
        public void GetSingle()
        {
            var allEmployees = new[] {1, 2, 3, 4, 5}.Select(i => new EFEmployee() { Name = "Name" + i }).ToList();
            this.laborDbContext.Employee.AddRange(allEmployees);
            this.laborDbContext.SaveChanges();
            var expected = allEmployees.FirstOrDefault(e => e.Id == 3);
            var actual = this.monolithicRepository.Get(3);

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Name, actual.Name);
        }

        [TestMethod]
        public void GetSingle_WhenNoResults_ReturnsNull()
        {
            var allEmployees = new[] {1, 2}.Select(i => new EFEmployee() { Name = "Name" + i }).ToList();
            this.laborDbContext.Employee.AddRange(allEmployees);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.Get(6);

            Assert.IsNull(actual);
        }

        [TestMethod]
        public void GetSingle_WhenMultipleResults_ThrowsException()
        {
            var allEmployees = new[] {1, 2}.Select(i => new EFEmployee() { Name = "Name" }).ToList();
            this.laborDbContext.Employee.AddRange(allEmployees);
            this.laborDbContext.SaveChanges();
            Assert.ThrowsException<InvalidOperationException>(() => this.monolithicRepository.GetByName("Name"));
        }

        [TestMethod]
        public void GetWithEnumProperty()
        {
            var addAddress = new EFAddress() { Classification = AddressClassification.Work };
            this.laborDbContext.Address.AddRange(addAddress);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetAddressesWithEnumClassification();

            Assert.AreEqual(1, actual.Count());
            Assert.AreEqual(AddressClassification.Work, actual.First().Classification);
        }

        [TestMethod]
        public void GetSingle_WhenColumnIsNull_ReturnsNull()
        {
            var expected = new EFEmployee() { Name = null };
            this.laborDbContext.Employee.Add(expected);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.Get(expected.Id);

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(null, actual.Name);
        }

        [TestMethod]
        public void GetSingleViaSpecifiedColumnNameParameter()
        {
            var allEmployees = new[] {1, 2, 3, 4, 5}.Select(i => new EFEmployee() { Name = "Name" + i }).ToList();
            this.laborDbContext.Employee.AddRange(allEmployees);
            this.laborDbContext.SaveChanges();
            var expected = allEmployees.FirstOrDefault(e => e.Id == 3);
            var actual = this.monolithicRepository.GetWithSpecifiedColumnName(3);

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Name, actual.Name);
        }

        [TestMethod]
        public void GetSingleViaFilterWithSpecifiedColumnNameParameter()
        {
            var allEmployees = new[] {1, 2, 3, 4, 5}.Select(i => new EFEmployee() { Name = "Name" + i }).ToList();
            this.laborDbContext.Employee.AddRange(allEmployees);
            this.laborDbContext.SaveChanges();
            var expected = allEmployees.FirstOrDefault(e => e.Id == 3);
            var actual = this.monolithicRepository.GetWithFilterSpecifiedColumnName(new Employee.EmployeeIdFilter() { EmployeeId = 3});

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Name, actual.Name);
        }

        [TestMethod]
        public void GetSingleViaNavigationPropertyWithSpecifiedColumnNameParameter()
        {
            var allEmployees = new[] {1, 2, 3, 4, 5}.Select(i => 
                new EFEmployee()
                {
                    Name = "Name" + i, 
                    Addresses = new List<EFAddress>()
                    {
                        new EFAddress() { StreetAddress = i + " Fake St" }
                    }
                }).ToList();
            this.laborDbContext.Employee.AddRange(allEmployees);
            this.laborDbContext.SaveChanges();
            var expected = allEmployees.FirstOrDefault(e => e.Id == 3);
            var actual = this.monolithicRepository.GetWithFilterNestedSpecifiedColumnName(
                new Employee.EmployeeAddressWithNestedColumnAliasFilter()
                {
                    Address = new Address.StreetAddressFilterWithAlias()
                    {
                        AddressLine1 = "3 Fake St"
                    }
                });

            Assert.AreEqual(expected.Id, actual.Id);
        }

        [TestMethod]
        public void GetWithSingleNavigationProperty()
        {
            var employee = new EFEmployee() { Name = "Name"};
            var workLog = new EFWorkLog() { Employee = employee };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWorkLogWithEmployee();

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Employee.Id, actual.Employee.Id);
            Assert.AreEqual(expected.Employee.Name, actual.Employee.Name);
        }

        [TestMethod]
        public void GetWithSingleNavigationProperty_SecondCallCached()
        {
            var employee = new EFEmployee() { Name = "Name"};
            var workLog = new EFWorkLog() { Employee = employee };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetWorkLogWithEmployee();

            var employeeObj = actual.Employee;
            Assert.AreEqual(employeeObj, actual.Employee);
        }

        [TestMethod]
        public void GetWithCollectionNavigationProperty_SecondCallCached()
        {
            var employee = new EFEmployee() { Name = "A Name", Addresses = new List<EFAddress>()
            {
                new EFAddress()
                {
                    StreetAddress = "123 fake st"
                }
            }};
            this.laborDbContext.Employee.Add(employee);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetEmployeesWithAddresses();
            
            var addressIds = employee.Addresses.Select(a => a.Id).ToList();

            AreEquivalent(addressIds, actual.First().Addresses.Select(a => a.Id).ToList());
            AreEquivalent(addressIds, actual.First().Addresses.Select(a => a.Id).ToList());
        }
        
        [TestMethod]
        public void GetWithSingleNavigationProperty_WhenJoinRowNull_ReturnsNull()
        {
            var workLog = new EFWorkLog();
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWorkLogWithEmployee();
            
            Assert.IsNull(actual.Employee);
        }

        [TestMethod]
        public void GetWithAdjacentNavigationProperties()
        {
            var location = new EFLocation() {Name = "A Location"};
            var employee = new EFEmployee() { Name = "A Name" };
            var workLog = new EFWorkLog() { Employee = employee, Location = location };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWorkLogWithEmployeeAndLocation();

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Employee.Id, actual.Employee.Id);
            Assert.AreEqual(expected.Employee.Name, actual.Employee.Name);
            Assert.AreEqual(expected.Location.Id, actual.Location.Id);
            Assert.AreEqual(expected.Location.Name, actual.Location.Name);
        }

        [TestMethod]
        public void GetWithAdjacentNavigationProperties_WhenJoinColumnsNull_ReturnsNullProperties()
        {
            var workLog = new EFWorkLog();
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWorkLogWithEmployeeAndLocation();

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.IsNull(actual.Employee);
            Assert.IsNull(actual.Location);
        }

        [TestMethod]
        public void GetWithAdjacentNavigationProperties_WhenFirstJoinNull_ReturnsNullProperty()
        {
            var location = new EFLocation() { Name = "A Location" };
            var workLog = new EFWorkLog() { Location = location };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWorkLogWithEmployeeAndLocation();

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.IsNull(actual.Employee);
            Assert.AreEqual(expected.Location.Id, actual.Location.Id);
            Assert.AreEqual(expected.Location.Name, actual.Location.Name);
        }

        [TestMethod]
        public void GetWithAdjacentNavigationProperties_WhenSecondJoinNull_ReturnsNullProperty()
        {
            var employee = new EFEmployee() { Name = "A Name" };
            var workLog = new EFWorkLog() { Employee = employee };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWorkLogWithEmployeeAndLocation();

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Employee.Id, actual.Employee.Id);
            Assert.AreEqual(expected.Employee.Name, actual.Employee.Name);
            Assert.IsNull(actual.Location);
        }

        [TestMethod]
        public void GetWithNestedNavigationProperties()
        {
            var address = new EFAddress() {StreetAddress = "123 Fake St"};
            var location = new EFLocation() { Name = "A Location", Address = address };
            var workLog = new EFWorkLog() { Location = location };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWorkLogWithLocationAndLocationAddress();

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Location.Id, actual.Location.Id);
            Assert.AreEqual(expected.Location.Name, actual.Location.Name);
            Assert.AreEqual(expected.Location.Address.Id, actual.Location.Address.Id);
            Assert.AreEqual(expected.Location.Address.StreetAddress, actual.Location.Address.StreetAddress);
        }

        [TestMethod]
        public void GetWithEnumerableNavigationProperty()
        {
            var address = new EFAddress() {StreetAddress = "123 Fake St"};
            address.Locations = new List<EFLocation>();
            address.Locations.Add(new EFLocation() { Name = "Location 1", Address = address });
            address.Locations.Add(new EFLocation() { Name = "Location 2", Address = address });
            address.Locations.Add(new EFLocation() { Name = "Location 3", Address = address });
            this.laborDbContext.Address.Add(address);
            this.laborDbContext.SaveChanges();
            var expected = address;
            var actual = this.monolithicRepository.GetAddressWithLocations();

            Assert.AreEqual(expected.Id, actual.Id);
            AreEquivalent(expected.Locations.Select(l => new { l.Name, l.Id }), actual.Locations.Select(l => new { l.Name, l.Id }));
        }

        [TestMethod]
        public void GetWithEnumerableNavigationProperty_WhenNullJoin_ReturnsEmptyCollection()
        {
            var address = new EFAddress() {StreetAddress = "123 Fake St"};
            this.laborDbContext.Address.Add(address);
            this.laborDbContext.SaveChanges();
            var expected = address;
            var actual = this.monolithicRepository.GetAddressWithLocations();

            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(0, actual.Locations.Count());
        }

        [TestMethod]
        public void GetSingleRowWithManyToManyEnumerableNavigationProperty()
        {
            var employee = new EFEmployee() { Name = "A Name" };
            employee.Addresses = new List<EFAddress>();
            employee.Addresses.Add(new EFAddress() {StreetAddress = "123 Fake St"});
            employee.Addresses.Add(new EFAddress() {StreetAddress = "345 Fake St"});
            employee.Addresses.Add(new EFAddress() {StreetAddress = "567 Fake St"});
            this.laborDbContext.Employee.Add(employee);
            this.laborDbContext.SaveChanges();
            var expected = employee;
            var actual = this.monolithicRepository.GetEmployeeWithAddresses();

            Assert.AreEqual(expected.Id, actual.Id);
            AreEquivalent(expected.Addresses.Select(l => new { l.StreetAddress, l.Id }), actual.Addresses.Select(l => new { l.StreetAddress, l.Id }));
        }

        [TestMethod]
        public void GetSingleRowWithManyToManyEnumerableNavigationProperty_WithEmptyNavigationCollection()
        {
            var employee = new EFEmployee() { Name = "A Name" };
            this.laborDbContext.Employee.Add(employee);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetEmployeeWithAddresses();
            
            Assert.IsFalse(actual.Addresses.Any());
        }

        [TestMethod]
        public void GetMultipleRowsWithManyToManyEnumerableNavigationProperty_WithEmptyNavigationCollection()
        {
            var employee = new EFEmployee() { Name = "A Name" };
            this.laborDbContext.Employee.Add(employee);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetEmployeesWithAddresses();

            // ensure this does not throw
            actual.SelectMany(e => e.Addresses).Select(a => a.Id).ToList();
        }

        [TestMethod]
        public void GetViaMultipleWhereParameters()
        {
            var workLog = new EFWorkLog() { StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
            var employee = new EFEmployee();
            var location = new EFLocation() {Name = "Location 1"};
            workLog.Employee = employee;
            workLog.Location = location;
            
            var otherWorkLog = new EFWorkLog() { StartDate = DateTime.Today.AddDays(5), EndDate = DateTime.Today.AddDays(6) };
            var otherEmployee = new EFEmployee();
            var otherLocation = new EFLocation() {Name = "Location 1"};
            otherWorkLog.Employee = otherEmployee;
            otherWorkLog.Location = otherLocation;

            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.WorkLog.Add(otherWorkLog);
            this.laborDbContext.SaveChanges();
            var expected = employee;
            var actual = this.monolithicRepository.GetWorkLogByLocationIdAndEmployeeId(workLog.LocationId.Value, workLog.EmployeeId.Value);

            Assert.AreEqual(expected.Id, actual.Id);
        }

        [TestMethod]
        public void GetViaNestedNavigationPropertyParameter()
        {
            var workLog = new EFWorkLog() { StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
            var employee = new EFEmployee() { Name = "Joe" };
            workLog.Employee = employee;
            
            var otherWorkLog = new EFWorkLog() { StartDate = DateTime.Today.AddDays(5), EndDate = DateTime.Today.AddDays(6) };
            var otherEmployee = new EFEmployee() { Name = "Bob" };
            otherWorkLog.Employee = otherEmployee;

            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.WorkLog.Add(otherWorkLog);
            this.laborDbContext.SaveChanges();
            var expected = employee;
            var actual = this.monolithicRepository.GetWorkLogByEmployeeName(new WorkLog.GetByEmployeeNameFilter() { Employee = new Employee.EmployeeNameFilter() { Name = "Joe" }});

            Assert.AreEqual(expected.Id, actual.Id);
        }

        [TestMethod]
        public void GetManyToManyViaNestedNavigationPropertyParameter()
        {
            var employee = new EFEmployee() { Name = "Joe" };
            employee.Addresses = new List<EFAddress>() { new EFAddress() { StreetAddress = "123 fake st"} };

            
            var otherEmployee = new EFEmployee() { Name = "John" };
            otherEmployee.Addresses = new List<EFAddress>() { new EFAddress() { StreetAddress = "456 fake st"} };

            this.laborDbContext.Employee.Add(employee);
            this.laborDbContext.Employee.Add(otherEmployee);
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.Employee.First(e => e.Addresses.Any(a => a.StreetAddress == "123 fake st"));
            var actual = this.monolithicRepository.GetEmployeeByStreetAddress(new Employee.StreetAddressFilter() { Address = new Address.StreetAddressFilter() { StreetAddress = "123 fake st" }});

            Assert.AreEqual(expected.Id, actual.Id);
        }

        [TestMethod]
        public void LikeByParameter()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
                );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name.StartsWith("J") && e.Name.EndsWith("e")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithLike(Like.FromUnsafeRawValue("J%e")).Select(e => e.Id);
            
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void IgnoreIfNull_WhenNull()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
                );
            this.laborDbContext.SaveChanges();

            string name = null;
            var expected = laborDbContext.Employee.Where(e => name == null || e.Name == name).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameIgnoreIfNull(name).Select(e => e.Id);
            
            Assert.AreEqual(4, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void IgnoreIfNull_WhenPopulated()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
                );
            this.laborDbContext.SaveChanges();

            string name = "Jake";
            var expected = laborDbContext.Employee.Where(e => e.Name == name).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameIgnoreIfNull(name).Select(e => e.Id);
            
            Assert.AreEqual(1, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void IgnoreIfNullOrEmpty_WhenNull()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            string name = null;
            var expected = laborDbContext.Employee.Where(e => name == null || e.Name == name).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameIgnoreIfNullOrEmptyString(name).Select(e => e.Id);
            
            Assert.AreEqual(4, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void IgnoreIfNullOrEmpty_WhenEmpty()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            string name = "";
            var expected = laborDbContext.Employee.Where(e => name == "" || e.Name == name).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameIgnoreIfNullOrEmptyString(name).Select(e => e.Id);
            
            Assert.AreEqual(4, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void StartsWith()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
                );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name.StartsWith("J")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithStartsWith("J").Select(e => e.Id);
            
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void StartsWith_WhenNull_ReturnsNoResults()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
                );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name.StartsWith(null)).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithStartsWith(null).Select(e => e.Id);
            
            Assert.AreEqual(0, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void Contains()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name.Contains("a")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithContains("a").Select(e => e.Id);
            
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void Contains_WhenNull_ReturnsNoResults()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name.Contains(null)).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithContains(null).Select(e => e.Id);
            
            Assert.AreEqual(0, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void EndsWith()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name.EndsWith("e")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithEndsWith("e").Select(e => e.Id);
            
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void EndsWith_WhenNull_ReturnsNoResults()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name.EndsWith(null)).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithEndsWith(null).Select(e => e.Id);
            
            Assert.AreEqual(0, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void LikeViaNestedNavigationPropertyParameter()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" }},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Jake" }},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Jam" }},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Kaylee" }}
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Where(wl => wl.Employee.Name.StartsWith("J") && wl.Employee.Name.EndsWith("e")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNameWithLike(new WorkLog.GetLikeEmployeeNameFilter() { Employee = new Employee.EmployeeNameLikeFilter() { Name = Like.FromUnsafeRawValue("J%e")}}).Select(wl => wl.Id);
        
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Skip(1).Take(1).Select(e => e.Id).ToList().Concat(laborDbContext.WorkLog.Skip(3).Take(2).Select(e => e.Id).ToList()).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithAnyId(expected.Cast<int?>().ToList()).Select(e => e.Id);
            
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_WhenNull_ReturnsNoResults()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.GetWorkLogsWithAnyId(null).Select(e => e.Id);
            
            Assert.AreEqual(0, actual.Count());
        }

        [TestMethod]
        public void InParameter_IgnoreIfNull_WhenNull_ReturnsResults()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNull(null).Select(e => e.Id);
            
            Assert.AreEqual(5, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_IgnoreIfNull_WithItems_ReturnsResults()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Skip(1).Take(2).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNull(expected).Select(e => e.Id);
            
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_IgnoreIfNullOrEmpty_WhenEmpty_ReturnsAllResults()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(new List<int>()).Select(e => e.Id);
            
            Assert.AreEqual(5, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_IgnoreIfNullOrEmpty_WhenNull_ReturnsAllResults()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(null).Select(e => e.Id);
            
            Assert.AreEqual(5, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_IgnoreIfNullOrEmpty_WithItems_ReturnsExpectedResults()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Skip(1).Take(2).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(expected).Select(e => e.Id);
            
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_WhenEmptyIgnoreIfNullOrEmpty_ReturnsResults()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog(),
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { },
                new EFWorkLog() { }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithAnyIdIgnoreIfNullOrEmpty(new List<int>()).Select(e => e.Id);
            
            Assert.AreEqual(5, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_WhenInNestedNavigationProperty_ReturnsExpected()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" }},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Jake" }},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Jam" }},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Kaylee" }}
            );
            this.laborDbContext.SaveChanges();

            var expectedNames = new List<string>() {"Jake", "Kaylee"};
            var expected = laborDbContext.WorkLog.Where(wl => expectedNames.Contains(wl.Employee.Name)).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNamesWithIn(new WorkLog.GetEmployeeNamesInFilter() { Employee = new Employee.EmployeeNamesInFilter() { Name = expectedNames }}).Select(wl => wl.Id);
        
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void GreaterThanParameter()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 03) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 01) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 02) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 05) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 04) }
            );
            this.laborDbContext.SaveChanges();

            var targetDate = new DateTime(2021, 01, 03);
            var expected = laborDbContext.WorkLog.Where(wl => wl.StartDate > targetDate).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsGreaterThanStartDate(targetDate).Select(e => e.Id);
            
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void GreaterThanOrEqualParameter()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 03) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 01) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 02) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 05) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 04) }
            );
            this.laborDbContext.SaveChanges();

            var targetDate = new DateTime(2021, 01, 03);
            var expected = laborDbContext.WorkLog.Where(wl => wl.StartDate >= targetDate).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsGreaterThanOrEqualToStartDate(targetDate).Select(e => e.Id);
            
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void GreaterThanAndLessThanParametersForSameColumn()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 03) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 01) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 02) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 05) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 04) }
            );
            this.laborDbContext.SaveChanges();

            var startRange = new DateTime(2021, 01, 02);
            var endRange = new DateTime(2021, 01, 04);
            var expected = laborDbContext.WorkLog.Where(wl => wl.StartDate >= startRange && wl.StartDate <= endRange).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsBetweenDatesViaAlias(startRange, endRange).Select(e => e.Id);
            
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void LessThanParameter()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 03) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 01) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 02) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 05) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 04) }
            );
            this.laborDbContext.SaveChanges();

            var targetDate = new DateTime(2021, 01, 03);
            var expected = laborDbContext.WorkLog.Where(wl => wl.StartDate < targetDate).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsLessThanStartDate(targetDate).Select(e => e.Id);
            
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void LessThanOrEqualParameter()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 03) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 01) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 02) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 05) },
                new EFWorkLog() { StartDate = new DateTime(2021, 01, 04) }
            );
            this.laborDbContext.SaveChanges();

            var targetDate = new DateTime(2021, 01, 03);
            var expected = laborDbContext.WorkLog.Where(wl => wl.StartDate <= targetDate).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsLessThanOrEqualToStartDate(targetDate).Select(e => e.Id);
            
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(expected, actual);
        }

        // consider if this behavior is desired
        // [TestMethod]
        // public void GetViaDirectNavigationPropertyParameter()
        // {
        //     var workLog = new EFWorkLog() { StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
        //     var employee = new EFEmployee() { Name = "Joe" };
        //     workLog.Employee = employee;
        //     
        //     var otherWorkLog = new EFWorkLog() { StartDate = DateTime.Today.AddDays(5), EndDate = DateTime.Today.AddDays(6) };
        //     var otherEmployee = new EFEmployee() { Name = "Bob" };
        //     otherWorkLog.Employee = otherEmployee;
        //
        //     this.laborDbContext.WorkLog.Add(workLog);
        //     this.laborDbContext.WorkLog.Add(otherWorkLog);
        //     this.laborDbContext.SaveChanges();
        //     var expected = employee;
        //     var actual = this.monolithicRepository.GetWorkLogByEmployeeNameDirect(new Employee.EmployeeNameFilter() { Name = "Joe" });
        //
        //     Assert.AreEqual(expected.Id, actual.Id);
        // }

        [TestMethod]
        public void GetOrderedViaNullMethodParameter()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1)});
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderBy(w => w.StartDate).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsAttribute().Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaAscendingOrderByDirectionMethodParameter()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1)});
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderBy(w => w.StartDate).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsAttribute(OrderByDirection.Ascending).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingOrderByDirectionMethodParameter()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1)});
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderByDescending(w => w.StartDate).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsAttribute(OrderByDirection.Descending).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDynamicOrderByMethodParameter()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderBy(w => w.StartDate).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderBy(new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate))).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingDynamicOrderByMethodParameter()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderByDescending(w => w.StartDate).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicOrderBy(new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Descending)).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingDynamicEnumerableOrderByMethodParameter()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderBy(w => w.StartDate).ThenByDescending(w => w.Id).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
                new List<OrderBy>()
                {
                    new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Ascending),
                    new OrderBy(nameof(WorkLog), nameof(WorkLog.Id), OrderByDirection.Descending)
                }).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingDynamicEnumerableOrderByMethodParameter_EmptyCollection()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
                new List<OrderBy>()).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingDynamicEnumerableOrderByMethodParameter_NullCollection()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderBy(
                null).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingDynamicEnumerableOrderByViaClassFilterProperty()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderBy(w => w.StartDate).ThenByDescending(w => w.Id).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderByViaClassFilter(
                new WorkLog.DynamicOrderByEnumerable()
                {
                    OrderBys = new List<OrderBy>()
                    {
                        new OrderBy(nameof(WorkLog), nameof(WorkLog.StartDate), OrderByDirection.Ascending),
                        new OrderBy(nameof(WorkLog), nameof(WorkLog.Id), OrderByDirection.Descending)
                    }
                }).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingDynamicEnumerableOrderByViaClassFilterProperty_EmptyCollection()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderByViaClassFilter(
                new WorkLog.DynamicOrderByEnumerable()
                {
                    OrderBys = new List<OrderBy>()
                }).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaDescendingDynamicEnumerableOrderByViaClassFilterProperty_NullCollection()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1) });
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1) });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogsWithDynamicEnumerableOrderByViaClassFilter(
                new WorkLog.DynamicOrderByEnumerable()
                {
                    OrderBys = null
                }).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void GetOrderedViaNestedNavigationProperty()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog()
            {
                StartDate = new DateTime(2020, 1, 1),
                Employee = new EFEmployee()
                {
                    Addresses = new List<EFAddress>()
                    {
                        new EFAddress(),
                        new EFAddress()
                    }
                }
            });

            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.Address.OrderByDescending(a => a.Id).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsOrderedByAddressId(OrderByDirection.Descending).ToList();

            AreSame(expected, actual.Select(w => w.Employee).SelectMany(e => e.Addresses.Select(a => a.Id)).ToList());
        }

        [TestMethod]
        public void GetNestedPoco()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog()
            {
                StartDate = new DateTime(2020, 1, 1),
                Employee = new EFEmployee()
                {
                    Addresses = new List<EFAddress>()
                    {
                        new EFAddress(),
                        new EFAddress()
                    }
                }
            });

            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.Address.OrderByDescending(a => a.Id).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsOrderedByAddressIdPocoReturn(OrderByDirection.Descending).ToList();

            AreSame(expected, actual.Select(w => w.Employee).SelectMany(e => e.Addresses.Select(a => a.Id)).ToList());
        }

        [TestMethod]
        public void GetPocoWithClrOnlyProperty()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog()
            {
                StartDate = new DateTime(2020, 1, 1)
            });

            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsWithClrOnlyProperty().ToList();

            AreSame(expected, actual.Select(w => w.Id).ToList());
            AreSame(expected.Select(id => $"example{id}"), actual.Select(w => w.ClrOnlyProperty).ToList());
        }

        [TestMethod]
        public void Offset_DefaultOrdering()
        {
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog() { };
                var employee = new EFEmployee();
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Skip(2).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetNextWorkLogs(2).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void Offset_SpecifiedOrdering()
        {
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog() { StartDate = new DateTime(2021, 1, 1).AddDays(new Random(i).Next(0, 20))};
                var employee = new EFEmployee();
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderBy(wl => wl.StartDate).Skip(2).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetNextWorkLogsWithOrder(2).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void Offset_WithPrimaryTableFilter()
        {
            for (int i = 0; i < 10; i++)
            {
                var workLog = new EFWorkLog() { StartDate = new DateTime(2021, 1, 1).AddDays(new Random(i + 7).Next(0, 2))};
                var employee = new EFEmployee();
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var searchDate = new DateTime(2021, 1, 1);
            var expected = this.laborDbContext.WorkLog.Where(wl => wl.StartDate == searchDate).Skip(2).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetNextWorkLogsWithPrimaryTableFilter(2, searchDate).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void Fetch_WithPrimaryTableFilter()
        {
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog() { };
                var employee = new EFEmployee();
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Take(2).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.TakeWorkLogs(2).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void OffsetFetch_WithPrimaryTableFilter()
        {
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog() { };
                var employee = new EFEmployee();
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Skip(3).Take(2).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.SkipTakeWorkLogs(3, 2).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void OffsetFetch_WithPrimaryAndNavigationTableFilter()
        {
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog()
                {
                    StartDate = new DateTime(2022, 1, 1)
                };
                var employee = new EFEmployee()
                {
                    Name = "Employee" + (i % 2)
                };
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.SkipTakeWorkLogsByStartDateAndEmployeeName(new WorkLog.GetByStartDateAndEmployeeNameFilterWithOffsetFetch()
            {
                Offset = 1,
                Fetch = 1,
                Employee = new Employee.EmployeeNameFilter()
                {
                    Name = "Employee1"
                },
                StartDate = new DateTime(2022, 1, 1)
            }).Select(wl => wl.Id).ToList();

            Assert.AreEqual(1, actual.Count);
            AreSame(new int[] { 4 }, actual);
        }

        [TestMethod]
        public void ViaAttribute_ManyToOne()
        {
            var location1 = new EFLocation();
            var location2 = new EFLocation();
            this.laborDbContext.Location.Add(location1);
            this.laborDbContext.Location.Add(location2);
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog() { Location = i % 2 == 0 ? location1 : location2 };
                var employee = new EFEmployee();
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.Employee.Where(e => e.WorkLogs.Any(wl => wl.LocationId == location1.Id)).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeeIdsForWorkLogLocationId(location1.Id).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void ViaAttribute_OneToMany()
        {
            var workLog1 = new EFWorkLog();
            var employee1 = new EFEmployee() { Name = "Joe" };
            workLog1.Employee = employee1;

            var workLog2 = new EFWorkLog();
            var employee2 = new EFEmployee() { Name = "John" };
            workLog2.Employee = employee2;

            var workLog3 = new EFWorkLog();
            var employee3 = new EFEmployee() { Name = "John" };
            workLog3.Employee = employee3;
            
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Where(wl => wl.Employee.Name == "John").Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogIdsForEmployeeName("John").Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void ViaAttribute_OneToMany_WithDifferingParameterName()
        {
            var workLog1 = new EFWorkLog();
            var employee1 = new EFEmployee() { Name = "Joe" };
            workLog1.Employee = employee1;

            var workLog2 = new EFWorkLog();
            var employee2 = new EFEmployee() { Name = "John" };
            workLog2.Employee = employee2;

            var workLog3 = new EFWorkLog();
            var employee3 = new EFEmployee() { Name = "John" };
            workLog3.Employee = employee3;
            
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Where(wl => wl.Employee.Name == "John").Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogIdsForEmployeeNameWithDifferingParameterName("John").Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void ViaRelationManyToMany_WithIntermediateTableSpecified()
        {
            var employee1 = new EFEmployee();
            var address1 = new EFAddress() { StreetAddress = "456 Melrose" };
            address1.Employees = new List<EFEmployee>()
            {
                employee1
            };

            var employee2 = new EFEmployee();
            var address2 = new EFAddress() { StreetAddress = "123 Fake St" };
            address2.Employees = new List<EFEmployee>()
            {
                employee2
            };

            var employee3 = new EFEmployee();
            var address3 = new EFAddress() { StreetAddress = "123 Fake St" };
            address3.Employees = new List<EFEmployee>()
            {
                employee3
            };

            this.laborDbContext.Address.Add(address1);
            this.laborDbContext.Address.Add(address2);
            this.laborDbContext.Address.Add(address3);
            this.laborDbContext.SaveChanges();

            var expected = this.laborDbContext.Employee.Where(e => e.Addresses.Any(a => a.StreetAddress == "123 Fake St")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.EF_GetEmployeeIdsForStreetAddress("123 Fake St").Select(e => e.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void ViaAttributeClassFilter_ManyToOne()
        {
            var location1 = new EFLocation();
            var location2 = new EFLocation();
            this.laborDbContext.Location.Add(location1);
            this.laborDbContext.Location.Add(location2);
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog() { Location = i % 2 == 0 ? location1 : location2 };
                var employee = new EFEmployee();
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.Employee.Where(e => e.WorkLogs.Any(wl => wl.LocationId == location1.Id)).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeeIdsForWorkLogLocationIdClassFilter(
                new Employee.WorkLogLocationIdFilterViaRelation()
                {
                    LocationId = location1.Id
                }).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void ViaAttributeClassFilter_OneToMany()
        {
            var workLog1 = new EFWorkLog();
            var employee1 = new EFEmployee() { Name = "Joe" };
            workLog1.Employee = employee1;

            var workLog2 = new EFWorkLog();
            var employee2 = new EFEmployee() { Name = "John" };
            workLog2.Employee = employee2;

            var workLog3 = new EFWorkLog();
            var employee3 = new EFEmployee() { Name = "John" };
            workLog3.Employee = employee3;
            
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Where(wl => wl.Employee.Name == "John").Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogIdsForEmployeeNameViaClassFilter(
                new WorkLog.EmployeeNameViaRelationFilter()
                {
                    Name = "John"
                }).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void ViaAttributeClassFilter_OneToMany_WithDifferingParameterName()
        {
            var workLog1 = new EFWorkLog();
            var employee1 = new EFEmployee() { Name = "Joe" };
            workLog1.Employee = employee1;

            var workLog2 = new EFWorkLog();
            var employee2 = new EFEmployee() { Name = "John" };
            workLog2.Employee = employee2;

            var workLog3 = new EFWorkLog();
            var employee3 = new EFEmployee() { Name = "John" };
            workLog3.Employee = employee3;
            
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.Where(wl => wl.Employee.Name == "John").Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogIdsForEmployeeNameWithDifferingParameterNameViaClassFilter(
                new WorkLog.EmployeeNameFilterWithAliasViaRelation()
                {
                    TheEmployeeName = "John"
                }).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void ViaRelationManyToManyClassFilter_WithIntermediateTableSpecified()
        {
            var employee1 = new EFEmployee();
            var address1 = new EFAddress() { StreetAddress = "456 Melrose" };
            address1.Employees = new List<EFEmployee>()
            {
                employee1
            };

            var employee2 = new EFEmployee();
            var address2 = new EFAddress() { StreetAddress = "123 Fake St" };
            address2.Employees = new List<EFEmployee>()
            {
                employee2
            };

            var employee3 = new EFEmployee();
            var address3 = new EFAddress() { StreetAddress = "123 Fake St" };
            address3.Employees = new List<EFEmployee>()
            {
                employee3
            };

            this.laborDbContext.Address.Add(address1);
            this.laborDbContext.Address.Add(address2);
            this.laborDbContext.Address.Add(address3);
            this.laborDbContext.SaveChanges();

            var expected = this.laborDbContext.Employee.Where(e => e.Addresses.Any(a => a.StreetAddress == "123 Fake St")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.EF_GetEmployeeIdsForStreetAddressViaClassFilter(
                    new Employee.EFStreetAddressFilterViaRelation()
                    {
                        StreetAddress = "123 Fake St"
                    }
                ).Select(e => e.Id).ToList();

            AreSame(expected, actual);
        }

        #region View 

        [TestMethod]
        public void GetView()
        {
            var workLogs = new[] { 1, 2 }.Select(i => new EFWorkLog()
            {
                Employee = new EFEmployee() { Name = "EmployeeName" + i }, 
                StartDate = new DateTime(2021, 1, i + 1), 
                EndDate = new DateTime(2021, 1, i + 2)
            }).ToList();
            this.laborDbContext.WorkLog.AddRange(workLogs);
            this.laborDbContext.SaveChanges();
            var result = this.monolithicRepository.GetWorkLogEmployeeView();

            var expected = workLogs.Select(wl => new { WorkLogId = wl.Id, wl.StartDate, wl.EndDate, EmployeeId = wl.Employee.Id, EmployeeName = wl.Employee.Name }).ToList();
            var actual = result.Select(v => new { v.WorkLogId, v.StartDate, v.EndDate, v.EmployeeId, v.EmployeeName }).ToList();
            CustomAssert.AreEquivalent<dynamic>(expected, actual);
        }

        #endregion

        #region Function
        
        [TestMethod]
        public void TableValuedFunction()
        {
            var efEmployees = Enumerable.Range(1, 2).Select(i => new EFEmployee() { }).ToList();
            this.laborDbContext.Employee.AddRange(efEmployees);
            this.laborDbContext.SaveChanges();
            var workLogs = new [] {
                new EFWorkLog()
                {
                    Employee = efEmployees[0] 
                },
                new EFWorkLog()
                {
                    Employee = efEmployees[1] 
                },
                new EFWorkLog()
                {
                    Employee = efEmployees[0] 
                },
                new EFWorkLog()
                {
                    Employee = efEmployees[1] 
                }
            };
            foreach (var workLog in workLogs)
            {
                this.laborDbContext.WorkLog.Add(workLog);
                this.laborDbContext.SaveChanges();
            }
            
            var actual = monolithicRepository.itvf_GetWorkLogsByEmployeeId(1);

            AreEquivalent(new [] { 1, 3 }, actual.Select(wl => wl.Id));
        }
        
        [TestMethod]
        public void TableValuedFunction_WithFilterParam()
        {
            var efEmployees = Enumerable.Range(1, 2).Select(i => new EFEmployee() { }).ToList();
            this.laborDbContext.Employee.AddRange(efEmployees);
            this.laborDbContext.SaveChanges();
            var workLogs = new [] {
                new EFWorkLog()
                {
                    Employee = efEmployees[0],
                    StartDate = new DateTime(2021, 1, 1)
                },
                new EFWorkLog()
                {
                    Employee = efEmployees[1],
                    StartDate = new DateTime(2021, 1, 1)
                },
                new EFWorkLog()
                {
                    Employee = efEmployees[0],
                    StartDate = new DateTime(2021, 3, 1)
                },
                new EFWorkLog()
                {
                    Employee = efEmployees[1],
                    StartDate = new DateTime(2021, 3, 1)
                }
            };
            foreach (var workLog in workLogs)
            {
                this.laborDbContext.WorkLog.Add(workLog);
                this.laborDbContext.SaveChanges();
            }
            
            var actual = monolithicRepository.itvf_GetWorkLogsByEmployeeId(1, new DateTime(2021, 2, 1));

            AreEquivalent(new [] { 3 }, actual.Select(wl => wl.Id));
        }

        #endregion Function

        #region Insert

        [TestMethod]
        public void InsertSingle_Void_ValuesByParam_ReturnsExpected()
        {
            this.monolithicRepository.InsertEmployeeWithAttributeTableNameWithValuesByParams("Joleen");
            var actual = this.laborDbContext.Employee.ToList();

            Assert.AreEqual(1, actual.Count);
            Assert.AreEqual("Joleen", actual.First().Name);
        }

        [TestMethod]
        public void InsertSingle_Void_ValuesBySingleClassInstance_ReturnsExpected()
        {
            this.monolithicRepository.InsertEmployeeWithAttributeWithValuesByDetectedClass(new Employee.InsertFields() { Name = "Josh" });
            var actual = this.laborDbContext.Employee.ToList();

            Assert.AreEqual(1, actual.Count);
            Assert.AreEqual("Josh", actual.First().Name);
        }

        [TestMethod]
        public void InsertSingle_OutputId_ValuesBySingleClassInstance_ReturnsExpected()
        {
            var inserted = this.monolithicRepository.InsertEmployeeWithAttributeWithValuesByDetectedClassReturnId(new Employee.InsertFields() { Name = "Josh" });
            var actual = this.laborDbContext.Employee.Single(e => e.Id == inserted.Id);
            
            Assert.AreEqual("Josh", actual.Name);
        }

        [TestMethod]
        public void InsertMultiple_Void_ValuesByDetectedClassInstance_ReturnsExpected()
        {
            var insertFields = new List<Employee.InsertFields>() 
            {
                new Employee.InsertFields() { Name = "Josh" },
                new Employee.InsertFields() { Name = "James" },
                new Employee.InsertFields() { Name = "John" }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithAttributeWithValuesByDetectedClass(insertFields);
            var actual = this.laborDbContext.Employee.ToList();

            Assert.AreEqual(3, actual.Count);
            AreSame(actual.Select(p => p.Name).ToList(), insertFields.Select(e => e.Name).ToList());
        }

        [TestMethod]
        public void InsertMultiple_Void_ValuesByDetectedMultiPropertyClassInstance_ReturnsExpected()
        {
            var insertParameters = new Address.InsertFields[]
            {
                new Address.InsertFields() {StreetAddress = "123 fake", City = "Seattle", State = "WA"},
                new Address.InsertFields() {StreetAddress = "456 fake", City = "Portland", State = "OR"}
            };
            this.monolithicRepository.InsertMultipleAddressesWithAttributeWithValuesByDetectedClass(
                insertParameters);
            var actual = this.laborDbContext.Address.ToList();

            Assert.AreEqual(2, actual.Count);
            AreSame(actual.Select(e => new { e.StreetAddress, e.City, e.State }).ToList(), insertParameters.Select(e => new { e.StreetAddress, e.City, e.State }).ToList());
        }

        [TestMethod]
        public void InsertMultiple_OutputIds_ReturnsExpected()
        {
            var insertFields = new List<Employee.InsertFields>() 
            {
                new Employee.InsertFields() { Name = "Josh" },
                new Employee.InsertFields() { Name = "James" },
                new Employee.InsertFields() { Name = "John" }
            };
            var output = this.monolithicRepository.InsertMultipleEmployeesAndReturnIds(insertFields);
            var actual = this.laborDbContext.Employee.ToList();

            Assert.AreEqual(3, actual.Count);
            AreSame(actual.Select(p => p.Id).ToList(), output.Select(e => e.Id).ToList());
        }

        #endregion Insert

        #region Delete

        [TestMethod]
        public void Delete_WithSingleFilterParameter_DeletesExpectedRows()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Joe" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => e.Name != "Joe").Select(e => e.Id).ToList();
            this.monolithicRepository.DeleteEmployeeWithAttributeTableNameWithValuesByParams("Joe");
            var actual = laborDbContext.Employee.Select(e => e.Id).ToList();
            
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        // todo
        // [TestMethod]
        // public void Delete_WithNullFilterParameter_DeletesExpectedRows()
        // {
        //     this.laborDbContext.Employee.AddRange(
        //         new EFEmployee() { Name = "Joe" },
        //         new EFEmployee() { Name = "Jake" },
        //         new EFEmployee() { Name = "Jam" },
        //         new EFEmployee() { Name = "Joe" },
        //         new EFEmployee() { Name = null }
        //     );
        //     this.laborDbContext.SaveChanges();
        //
        //     var expected = laborDbContext.Employee.Where(e => e.Name != null).Select(e => e.Id).ToList();
        //     this.monolithicRepository.DeleteEmployeeWithAttributeTableNameWithValuesByParams(null);
        //     var actual = laborDbContext.Employee.Select(e => e.Id).ToList();
        //     
        //     Assert.AreEqual(4, actual.Count());
        //     AreEquivalent(expected, actual);
        // }

        #endregion Delete

        #region Update

        [TestMethod]
        public void Update_SingleSetField_WithNoFilter()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Joe" }
            );
            this.laborDbContext.SaveChanges();

            this.monolithicRepository.UpdateAllEmployees("James");
            var actualNames = laborDbContext.Employee.Select(e => e.Name).ToList();

            Assert.AreEqual(4, actualNames.Count());
            AreEquivalent(Enumerable.Range(0, 4).Select(j => "James"), actualNames);
        }

        [TestMethod]
        public void Update_SingleSetField_WithFilterField()
        {
            var employee = new EFEmployee() { Name = "Jam" };
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                employee,
                new EFEmployee() { Name = "Joe" }
            );
            this.laborDbContext.SaveChanges();

            this.monolithicRepository.UpdateEmployeeById("James", employee.Id);
            var actualName = laborDbContext.Employee.Where(e => e.Id == employee.Id).Select(e => e.Name).FirstOrDefault();
            
            Assert.AreEqual("James", actualName);
        }

        [TestMethod]
        public void Update_MultipleSetFields()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                new EFWorkLog() { StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)}
            );
            this.laborDbContext.SaveChanges();

            this.monolithicRepository.UpdateAllWorkLogsStartDateAndEndDate(new DateTime(2022, 2, 2), new DateTime(2022, 2, 3));
            var actual = laborDbContext.WorkLog.Select(wl => new { StartDate = wl.StartDate.Value, EndDate = wl.EndDate.Value }).ToList();
            
            CustomAssert.AreEquivalent<dynamic>(Enumerable.Range(0, 2).Select(i => new { StartDate = new DateTime(2022, 2, 2), EndDate = new DateTime(2022, 2, 3) }), actual);
        }

        [TestMethod]
        public void Update_MultipleSetFieldsAsClass()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                new EFWorkLog() { StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)}
            );
            this.laborDbContext.SaveChanges();

            this.monolithicRepository.UpdateAllWorkLogsStartDateAndEndDateSetClass(new WorkLog.SetDateFields()
            {
                StartDate = new DateTime(2022, 2, 2),
                EndDate = new DateTime(2022, 2, 3)
            });
            var actual = laborDbContext.WorkLog.Select(wl => new { StartDate = wl.StartDate.Value, EndDate = wl.EndDate.Value }).ToList();
            
            CustomAssert.AreEquivalent<dynamic>(Enumerable.Range(0, 2).Select(i => new { StartDate = new DateTime(2022, 2, 2), EndDate = new DateTime(2022, 2, 3) }), actual);
        }

        #endregion

        #region Abstract Class

        [TestMethod]
        public void AbstractClass_GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = abstractRepository.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }

        [TestMethod]
        public void AbstractClassWithParams_GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var abstractRepositoryWithConstructorArgs = repositoryBuilder.Build<AbstractRepositoryWithConstructorArgs>(t => t == typeof(int) ? (object)988 : "abstracttest");
            var actual = abstractRepositoryWithConstructorArgs.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }

        [TestMethod]
        public void AbstractClassConstructorArgs_GetEmployee_UsesInjectedDependencies()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var abstractRepositoryWithConstructorArgs = repositoryBuilder.Build<AbstractRepositoryWithConstructorArgs>(t => t == typeof(int) ? (object)988 : "abstracttest");
            var actual = abstractRepositoryWithConstructorArgs.Get(1);

            Assert.AreEqual(988, actual.Id);
            Assert.AreEqual("abstracttest", actual.Name);
        }
        
        [TestMethod]
        public void BuildViaTypeParameter_GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var abstractRepositoryWithConstructorArgs = (AbstractRepository) repositoryBuilder.Build(typeof(AbstractRepository));
            var actual = abstractRepositoryWithConstructorArgs.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }
        
        [TestMethod]
        public void BuildViaTypeParameter_Interface_GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var abstractRepositoryWithConstructorArgs = (IWorkLogRepository) repositoryBuilder.Build(typeof(IWorkLogRepository));
            var actual = abstractRepositoryWithConstructorArgs.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }
        
        [TestMethod]
        public void BuildViaTypeParameterWithConstructorArgs_GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var abstractRepositoryWithConstructorArgs = (AbstractRepositoryWithConstructorArgs) repositoryBuilder.Build(typeof(AbstractRepositoryWithConstructorArgs), t => t == typeof(int) ? (object)988 : "abstracttest");
            var actual = abstractRepositoryWithConstructorArgs.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }
        
        [TestMethod]
        public void BuildInterfaceViaTypeParameterConstructorResolver_GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var abstractRepositoryWithConstructorArgs = (IWorkLogRepository) repositoryBuilder.Build(typeof(IWorkLogRepository), t => t == typeof(int) ? (object)988 : "abstracttest");
            var actual = abstractRepositoryWithConstructorArgs.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }
        
        [TestMethod]
        public void BuildParameterlessAbstractClassViaTypeParameterConstructorResolver_GetAllIds()
        {
            var expected = Enumerable.Range(1, 5).Select(i => new EFWorkLog() { }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();

            var abstractRepositoryWithConstructorArgs = (AbstractRepository) repositoryBuilder.Build(typeof(AbstractRepository), t => t == typeof(int) ? (object)988 : "abstracttest");
            var actual = abstractRepositoryWithConstructorArgs.GetAllIds().Select(e => e.Id);

            AreEquivalent(expected.Select(w => w.Id), actual);
        }

        #endregion Abstract Class

        #region Alternate Connections

        [TestMethod]
        public void UsernameConnection()
        {
            laborDbConnection = TestSettings.LaborDbConnectionWithUserCredentials;
            var sqlConnection = (laborDbConnection as SqlConnection);
            
            var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(TestSettings.LaborDbConnectionWithUserCredentials.ConnectionString);
            repositoryBuilder = new RepositoryBuilder(new SqlQueryExecutor(() => laborDbConnection), sqlDatabaseConfiguration, statement => sqlStatements.Add(statement));
            this.monolithicRepository = repositoryBuilder.Build<IMonolithicRepository>();

            GetAllIds();
        }

        #endregion

        // [TestMethod]
        // public void JoinTest()
        // {
        //     var workLog = new EFWorkLog() { StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
        //     var employee = new EFEmployee() { Name = "Joe" };
        //     workLog.Employee = employee;
        //     
        //     var otherWorkLog = new EFWorkLog() { StartDate = DateTime.Today.AddDays(5), EndDate = DateTime.Today.AddDays(6) };
        //     var otherEmployee = new EFEmployee() { Name = "Bob" };
        //     otherWorkLog.Employee = otherEmployee;
        //
        //     this.laborDbContext.WorkLog.Add(workLog);
        //     this.laborDbContext.WorkLog.Add(otherWorkLog);
        //     this.laborDbContext.SaveChanges();
        //
        //     // var efWorkLogs = this.laborDbContext.Employee.Where(wl => wl.WorkLogs.Any(wl => wl.StartDate > DateTime.Today)).Include(wl => wl.WorkLogs).Skip(1).Take(2).ToList();
        //     
        // }

        // [TestMethod]
        // public void CompositeKeyTests()
        // {
        //     var efCompositeKeyTable = new EFCompositeKeyTable()
        //     {
        //         FirstName = "joe",
        //         LastName = "smith",
        //         EFCompositeForeignKeyTables = new List<EFCompositeForeignKeyTable>()
        //         {
        //             new EFCompositeForeignKeyTable()
        //         }
        //     };
        //     this.laborDbContext.CompositeKeyTable.Add(efCompositeKeyTable);
        //     this.laborDbContext.SaveChanges();
        //
        //     this.laborDbContext.CompositeKeyTable.Where(c => c.EFCompositeForeignKeyTables.Any(b => b.Id == 1)).Skip(1)
        //         .ToList();
        // }

        private void AreEquivalent<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            CollectionAssert.AreEquivalent(expected.ToList(), actual.ToList());
        }

        private void AreSame<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            if (expected.Count() != actual.Count() || 
                !expected.Select((item, i) => (actual.ElementAt(i)?.Equals(item)).GetValueOrDefault(false)).All(b => b))
            {
                Assert.Fail($"Collections are not equal. {(expected.Count() != actual.Count() ? "The number of items do not match. " : null)}Expected: [{string.Join(",", expected)}]. Actual: [{string.Join(",", actual)}]");
            }
        }
    }
}
