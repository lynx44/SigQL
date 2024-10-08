using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.Exceptions;
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

            ConfigureSigQL();
        }

        private void ConfigureSigQL()
        {
            var sqlConnection = (laborDbConnection as SqlConnection);
            var sqlDatabaseConfiguration = new SqlDatabaseConfiguration(sqlConnection.ConnectionString);
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
        public void GetWorkLogs_WithAliasedColumnName_ReturnsExpected()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog()).ToList();

            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetWithAliasedColumnName(2);

            Assert.AreEqual(2, actual.WorkLogId);
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
        public async Task GetWorkLogsAsync_ReturnsExpectedEmployees()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Name = "James" + i } }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = await monolithicRepository.GetWorkLogsAsync();

            AreEquivalent(new List<string>()
            {
                "James1",
                "James2",
                "James3"
            }, actual.Select(w => w.Employee.Name));
        }

        [TestMethod]
        public void OrderByRelation_ReturnsExpectedOrder()
        {
            var expected = Enumerable.Range(1, 3).Select(i => new EFWorkLog() { Employee = new EFEmployee() { Name = "James" + (4 - i) } }).ToList();
            this.laborDbContext.WorkLog.AddRange(expected);
            this.laborDbContext.SaveChanges();
            var actual = monolithicRepository.GetOrderedWorkLogsWithDynamicOrderByRelationCanonicalDataType(new OrderByRelation(nameof(WorkLog) + "->" + nameof(Employee), nameof(Employee.Name), OrderByDirection.Ascending));

            AreSame(new List<string>()
            {
                "James1",
                "James2",
                "James3"
            }, actual.Select(w => w.Employee.Name));
            AreSame(new List<int>()
            {
                3,
                2,
                1
            }, actual.Select(w => w.Id));
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
        public void MismatchingColumnType_ThrowsException()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = DateTime.Today });
            this.laborDbContext.SaveChanges();
            try
            {
                this.monolithicRepository.INVALID_MismatchingColumnType();
            }
            catch (InvalidTypeException ex)
            {
                Assert.AreEqual("Unable to convert INVALID_MismatchingColumnType.Id. Object of type 'System.Int32' cannot be converted to type 'System.String'.", ex.Message);
                Assert.IsNotNull(ex.InnerException);
                Assert.AreEqual(typeof(ArgumentException), ex.InnerException.GetType());
            }
        }

        [TestMethod]
        public void Get_MismatchingPKCase()
        {
            var allEmployees = new[] { 1, 2, 3, 4, 5 }.Select(i => new EFEmployee() { Name = "Name" + i }).ToList();
            this.laborDbContext.Employee.AddRange(allEmployees);
            this.laborDbContext.SaveChanges();
            var expected = allEmployees.FirstOrDefault(e => e.Id == 3);
            var actual = this.monolithicRepository.GetEmployeeMismatchingPKCase(3);

            Assert.AreEqual(expected.Id, actual.ID);
        }

        [TestMethod]
        public void Get_NestedMismatchingPKCase()
        {
            var workLog = new EFWorkLog()
            {
                Employee = new EFEmployee()
                {
                    Name = "mark"
                }
            };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.GetWorkLogWithEmployeeMismatchingPKCase(1);

            Assert.AreEqual(1, actual.Employee.ID);
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
        public void GetWithListNavigationProperty()
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
            var actual = this.monolithicRepository.GetEmployeesWithListAddresses();
            
            var addressIds = employee.Addresses.Select(a => a.Id).ToList();

            AreEquivalent(addressIds, actual.First().Addresses.Select(a => a.Id).ToList());
        }

        [TestMethod]
        public void GetWithIListNavigationProperty()
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
            var actual = this.monolithicRepository.GetEmployeesWithIListAddresses();
            
            var addressIds = employee.Addresses.Select(a => a.Id).ToList();

            AreEquivalent(addressIds, actual.First().Addresses.Select(a => a.Id).ToList());
        }

        [TestMethod]
        public void GetWithReadOnlyCollectionNavigationProperty()
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
            var actual = this.monolithicRepository.GetEmployeesWithReadOnlyCollectionAddresses();
            
            var addressIds = employee.Addresses.Select(a => a.Id).ToList();

            AreEquivalent(addressIds, actual.First().Addresses.Select(a => a.Id).ToList());
        }

        [TestMethod]
        public void GetWithIReadOnlyCollectionNavigationProperty()
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
            var actual = this.monolithicRepository.GetEmployeesWithIReadOnlyCollectionAddresses();
            
            var addressIds = employee.Addresses.Select(a => a.Id).ToList();

            AreEquivalent(addressIds, actual.First().Addresses.Select(a => a.Id).ToList());
        }

        [TestMethod]
        public void GetWithArrayNavigationProperty()
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
            var actual = this.monolithicRepository.GetEmployeesWithArrayAddresses();
            
            var addressIds = employee.Addresses.Select(a => a.Id).ToList();

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
        public void GetWithAliasedOneToManyCollection_ReturnsExpectedCollection()
        {
            var employee1 = new EFEmployee()
            {
                Name = "Name1"
            };
            var employee2 = new EFEmployee()
            {
                Name = "Name2"
            };

            var workLog1 = new EFWorkLog()
            {
                Employee = employee1,
                StartDate = new DateTime(2022, 1, 1),
                EndDate = new DateTime(2022, 2, 2)
            };
            var workLog2 = new EFWorkLog()
            {
                Employee = employee1,
                StartDate = new DateTime(2022, 3, 3),
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog3 = new EFWorkLog()
            {
                Employee = employee1,
                StartDate = new DateTime(2022, 3, 3),
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog4 = new EFWorkLog()
            {
                Employee = employee2,
                StartDate = new DateTime(2022, 3, 3),
                EndDate = new DateTime(2022, 4, 4)
            };
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.WorkLog.Add(workLog4);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetEmployeesWithAliasedWorkLogs();

            Assert.AreEqual(2, actual.Count());
            Assert.AreEqual(3, actual.First().WorkLogs.Count());
            Assert.AreEqual(1, actual.Last().WorkLogs.Count());
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
        public void GetWithJoinRelationAttribute()
        {
            var employee = new EFEmployee() { Name = "Name" };
            var workLog = new EFWorkLog()
            {
                Employee = employee, 
                StartDate = new DateTime(2022, 1, 1), 
                EndDate = new DateTime(2022, 2, 2)
            };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWithJoinRelationAttribute().First();
            
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Employee.Id, actual.View.EmployeeId);
            Assert.AreEqual(expected.Employee.Name, actual.View.EmployeeName);
            Assert.AreEqual(expected.StartDate, actual.View.StartDate);
            Assert.AreEqual(expected.EndDate, actual.View.EndDate);
            Assert.AreEqual(expected.Id, actual.View.WorkLogId);
        }
        
        [TestMethod]
        public void GetWithMultipleJoinRelationAttributes()
        {
            var employee = new EFEmployee() { Name = "Name" };
            var workLog = new EFWorkLog()
            {
                Employee = employee, 
                StartDate = new DateTime(2022, 1, 1), 
                EndDate = new DateTime(2022, 2, 2)
            };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWithMultipleJoinRelationAttributes().First();
            
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Employee.Id, actual.View.EmployeeId);
            Assert.AreEqual(expected.Employee.Name, actual.View.EmployeeName);
            Assert.AreEqual(expected.StartDate, actual.View.StartDate);
            Assert.AreEqual(expected.EndDate, actual.View.EndDate);
            Assert.AreEqual(expected.Id, actual.View.WorkLogId);
            Assert.IsTrue(actual.WorkLogs.Any());
        }
        
        [TestMethod]
        public void GetWithJoinRelationAttributeAndFilterToSameTable()
        {
            var employee = new EFEmployee() { Name = "Name" };
            var workLog = new EFWorkLog()
            {
                Employee = employee, 
                StartDate = new DateTime(2022, 1, 1), 
                EndDate = new DateTime(2022, 2, 2)
            };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWithJoinRelationAttributeAndFilterToSameTable(1).First();
            
            Assert.AreEqual(expected.Id, actual.Id);
        }
        
        [TestMethod]
        public void GetWithJoinRelationAttributeOnViewWithTableNavigationProperty_ReturnsExpectedCollection()
        {
            var employee1 = new EFEmployee() { Name = "Name1", Addresses = new List<EFAddress>()
            {
                new EFAddress() { City = "1" },
                new EFAddress() { City = "2" },
                new EFAddress() { City = "3" }
                }
            };
            var employee2 = new EFEmployee() { Name = "Name2", Addresses = new List<EFAddress>()
            {
                new EFAddress() { City = "4" },
                new EFAddress() { City = "5" },
                new EFAddress() { City = "6" }
                }
            };
            
            var workLog1 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 1, 1), 
                EndDate = new DateTime(2022, 2, 2)
            };
            var workLog2 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog3 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog4 = new EFWorkLog()
            {
                Employee = employee2, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.WorkLog.Add(workLog4);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetWithJoinRelationAttributeOnViewWithTableNavigationCollection();
            
            Assert.AreEqual(4, actual.Count());
        }
        
        [TestMethod]
        public void GetJoinRelationAttributeMultiTableRelationalPath_ReturnsExpectedCollection()
        {
            var employee1 = new EFEmployee() { Name = "Name1", Addresses = new List<EFAddress>()
            {
                new EFAddress() { City = "1" },
                new EFAddress() { City = "2" },
                new EFAddress() { City = "3" }
                }
            };
            var employee2 = new EFEmployee() { Name = "Name2", Addresses = new List<EFAddress>()
            {
                new EFAddress() { City = "4" },
                new EFAddress() { City = "5" }
                }
            };
            
            var workLog1 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 1, 1), 
                EndDate = new DateTime(2022, 2, 2)
            };
            var workLog2 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog3 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog4 = new EFWorkLog()
            {
                Employee = employee2, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.WorkLog.Add(workLog4);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetJoinAttributeWithMultiTableRelationalPathEF();
            
            Assert.AreEqual(4, actual.Count());
            Assert.AreEqual(3, actual.First().Addresses.Count());
            Assert.AreEqual(2, actual.Last().Addresses.Count());
        }
        
        [TestMethod]
        public void GetWithJoinRelationAttributeOnTableWithViewNavigationProperty_ReturnsExpectedCollection()
        {
            var employee1 = new EFEmployee() { Name = "Name1", Addresses = new List<EFAddress>()
            {
                new EFAddress() { City = "1" },
                new EFAddress() { City = "2" },
                new EFAddress() { City = "3" }
                }
            };
            var employee2 = new EFEmployee() { Name = "Name2", Addresses = new List<EFAddress>()
            {
                new EFAddress() { City = "4" },
                new EFAddress() { City = "5" },
                new EFAddress() { City = "6" }
                }
            };
            
            var workLog1 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 1, 1), 
                EndDate = new DateTime(2022, 2, 2)
            };
            var workLog2 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog3 = new EFWorkLog()
            {
                Employee = employee1, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            var workLog4 = new EFWorkLog()
            {
                Employee = employee2, 
                StartDate = new DateTime(2022, 3, 3), 
                EndDate = new DateTime(2022, 4, 4)
            };
            this.laborDbContext.WorkLog.Add(workLog1);
            this.laborDbContext.WorkLog.Add(workLog2);
            this.laborDbContext.WorkLog.Add(workLog3);
            this.laborDbContext.WorkLog.Add(workLog4);
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetWithJoinRelationAttributeOnTableWithViewNavigationCollection();
            
            Assert.AreEqual(2, actual.Count());
            Assert.AreEqual(3, actual.First().View.Count());
            Assert.AreEqual(1, actual.Last().View.Count());
        }
        
        [TestMethod]
        public void GetWithJoinRelationAttributeMismatchingKeyCase()
        {
            var employee = new EFEmployee() { Name = "Name" };
            var workLog = new EFWorkLog()
            {
                Employee = employee, 
                StartDate = new DateTime(2022, 1, 1), 
                EndDate = new DateTime(2022, 2, 2)
            };
            this.laborDbContext.WorkLog.Add(workLog);
            this.laborDbContext.SaveChanges();
            var expected = workLog;
            var actual = this.monolithicRepository.GetWithJoinRelationAttributeMismatchingKeyCase().First();
            
            //Assert.AreEqual(expected.Employee.Id, actual.Employee.Id);
            //Assert.AreEqual(expected.Employee.Name, actual.Employee.Name);
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
        public void NotStartsWith()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => !e.Name.StartsWith("J")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithNotStartsWith("J").Select(e => e.Id);

            Assert.AreEqual(1, actual.Count());
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
        public void NotContains()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => !e.Name.Contains("a")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithNotContains("a").Select(e => e.Id);

            Assert.AreEqual(1, actual.Count());
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
        public void NotEndsWith()
        {
            this.laborDbContext.Employee.AddRange(
                new EFEmployee() { Name = "Joe" },
                new EFEmployee() { Name = "Jake" },
                new EFEmployee() { Name = "Jam" },
                new EFEmployee() { Name = "Kaylee" }
            );
            this.laborDbContext.SaveChanges();

            var expected = laborDbContext.Employee.Where(e => !e.Name.EndsWith("e")).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetEmployeesByNameWithNotEndsWith("e").Select(e => e.Id);

            Assert.AreEqual(1, actual.Count());
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
        public void InParameter_PluralParameterName()
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
            var actual = this.monolithicRepository.GetWorkLogsWithAnyIdPlural(expected.ToList()).Select(e => e.Id);
            
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
        public void InParameter_WhenInViaRelationProperty_ReturnsExpected()
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
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNamesViaRelation(new WorkLog.GetEmployeeNamesViaRelation() { Names = expectedNames}).Select(wl => wl.Id);
        
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected, actual);
        }

        [TestMethod]
        public void InParameter_WhenInViaRelationPropertyNullCollection_ReturnsExpected()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" }},
                new EFWorkLog(),
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Jam" }},
                new EFWorkLog()
            );
            this.laborDbContext.SaveChanges();

            var expectedNames = new List<string>() {"Jake", "Kaylee"};
            var expected = laborDbContext.WorkLog.Where(wl => expectedNames.Contains(wl.Employee.Name)).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNamesViaRelation(new WorkLog.GetEmployeeNamesViaRelation() { Names = null }).Select(wl => wl.Id);
        
            Assert.AreEqual(4, actual.Count());
        }

        [TestMethod]
        public void InParameter_WhenInViaRelationPropertyEmptyCollection_ReturnsExpected()
        {
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" }},
                new EFWorkLog(),
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Jam" }},
                new EFWorkLog()
            );
            this.laborDbContext.SaveChanges();

            var expectedNames = new List<string>() {"Jake", "Kaylee"};
            var expected = laborDbContext.WorkLog.Where(wl => expectedNames.Contains(wl.Employee.Name)).Select(e => e.Id).ToList();
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNamesViaRelation(new WorkLog.GetEmployeeNamesViaRelation() { Names = new List<string>() }).Select(wl => wl.Id);
        
            Assert.AreEqual(4, actual.Count());
        }

        [TestMethod]
        public void InParameter_ViaNestedRelation_NullCollection_ReturnsExpectedSql()
        {
            var seattleAddress = new EFAddress() { City = "Seattle" };
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Seattle"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "San Francisco"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Dallas"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Seattle" } }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { seattleAddress }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Dallas" } }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { seattleAddress }}}
            );
            this.laborDbContext.SaveChanges();
            
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNamesAndAddressCitiesViaRelationEF(new WorkLog.GetEmployeeNamesAndAddressCitiesViaRelationEF()
                { EmployeeNames = null, AddressCities = new List<string>() { "Seattle" } }).Select(wl => wl.Id);
        
            Assert.AreEqual(4, actual.Count());
        }

        [TestMethod]
        public void InParameter_ViaNestedRelation_EmptyCollection_ReturnsExpectedSql()
        {
            var seattleAddress = new EFAddress() { City = "Seattle" };
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Seattle"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "San Francisco"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Dallas"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Seattle" } }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { seattleAddress }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Dallas" } }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { seattleAddress }}}
            );
            this.laborDbContext.SaveChanges();
            
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNamesAndAddressCitiesViaRelationEF(new WorkLog.GetEmployeeNamesAndAddressCitiesViaRelationEF()
                { EmployeeNames = new List<string>(), AddressCities = new List<string>() { "Seattle" } }).Select(wl => wl.Id);
        
            Assert.AreEqual(4, actual.Count());
        }

        [TestMethod]
        public void InParameter_ViaNestedRelation_BothCollectionsPopulated_ReturnsExpectedSql()
        {
            var seattleAddress = new EFAddress() { City = "Seattle" };
            this.laborDbContext.WorkLog.AddRange(
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Bob" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Seattle"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Bob" , Addresses = new List<EFAddress>() { new EFAddress() { City = "San Francisco"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Bob" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Dallas"}}}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Seattle" } }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Bob" , Addresses = new List<EFAddress>() { seattleAddress }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { new EFAddress() { City = "Dallas" } }}},
                new EFWorkLog() { Employee = new EFEmployee() { Name = "Joe" , Addresses = new List<EFAddress>() { seattleAddress }}}
            );
            this.laborDbContext.SaveChanges();
            
            var actual = this.monolithicRepository.GetWorkLogsByEmployeeNamesAndAddressCitiesViaRelationEF(new WorkLog.GetEmployeeNamesAndAddressCitiesViaRelationEF()
                { EmployeeNames = new List<string>() { "Bob" }, AddressCities = new List<string>() { "Seattle" } }).Select(wl => wl.Id);
        
            Assert.AreEqual(2, actual.Count());
        }

        [TestMethod]
        public void InParameter_CompositeKey_ReturnsExpected()
        {
            this.laborDbContext.Address.AddRange(
                new EFAddress()
                {
                    City = "Seattle",
                    State = "WA"
                },
                new EFAddress()
                {
                    City = "Tacoma",
                    State = "WA"
                },
                new EFAddress()
                {
                    City = "Los Angeles",
                    State = "CA"
                },
                new EFAddress()
                {
                    City = "Concord",
                    State = "MA"
                },
                new EFAddress()
                {
                    City = "Concord",
                    State = "WV"
                });
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.Address.Where(a =>
                a.City == "Seattle" && a.State == "WA" ||
                a.City == "Concord" && a.State == "MA" ||
                a.City == "San Diego" && a.State == "CA").ToList();

            var actual = this.monolithicRepository.GetInWithCompositeKeys(new List<Address.CityAndState>()
            {
                new Address.CityAndState()
                {
                    City = "Seattle",
                    State = "WA"
                },
                new Address.CityAndState()
                {
                    City = "Concord",
                    State = "MA"
                },
                new Address.CityAndState()
                {
                    City = "San Diego",
                    State = "CA"
                }
            });

            Assert.AreEqual(2, actual.Count());
            AreEquivalent(expected.Select(e => e.Id), actual.Select(a => a.Id));
        }

        [TestMethod]
        public void Where_OrGroupByTwoColumnsOfSameTable_WhenTwoParameterValuesMatch()
        {
            var expected1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2)
            };
            var expected2 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 1, 1)
            };
            var expected3 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 1, 1)
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                expected1,
                expected2,
                expected3,
                new EFWorkLog()
                {
                    StartDate = new DateTime(2000, 3, 3),
                    EndDate = new DateTime(2000, 3, 3)
                }
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupByTwoColumnsOfSameTable(new DateTime(2000, 1, 1), new DateTime(2000, 1, 1));

            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { expected1, expected2, expected3 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_OrGroupByTwoColumnsOfSameTable_WhenTwoParameterValuesDiffer()
        {
            var expected1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2)
            };
            var expected2 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 3, 3)
            };
            var expected3 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 2, 2)
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                expected1,
                new EFWorkLog()
                {
                    StartDate = new DateTime(2000, 2, 2),
                    EndDate = new DateTime(2000, 1, 1)
                },
                new EFWorkLog()
                {
                    StartDate = new DateTime(2000, 2, 2),
                    EndDate = new DateTime(2000, 3, 3)
                },
                new EFWorkLog()
                {
                    StartDate = new DateTime(2000, 3, 3),
                    EndDate = new DateTime(2000, 1, 1)
                },
                expected2,
                expected3
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupByTwoColumnsOfSameTable(new DateTime(2000, 1, 1), new DateTime(2000, 2, 2));

            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new [] { expected1, expected2, expected3 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_OrGroupByTwoGroupsForColumnsOfSameTable()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 1, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 1, 1),
                Employee = employee1
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee2
            };
            var workLog5 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 3, 3),
                Employee = employee1
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4,
                workLog5
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupByTwoGroupsForColumnsOfSameTable(
                new DateTime(2000, 1, 1), 
                new DateTime(2000, 2, 2),
                workLog4.Id,
                employee1.Id);

            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog3, workLog4 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_OrGroupByTwoColumnsOfAdjacentTablesViaRelation()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var location1 = new EFLocation()
            {
                Name = "seattle"
            };
            var location2 = new EFLocation()
            {
                Name = "new york"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1,
                Location = location1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 1, 1),
                Employee = employee2,
                Location = location1
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 1, 1),
                Employee = employee1,
                Location = location2
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee2,
                Location = location2
            };
            var workLog5 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 3, 3),
                Employee = employee1,
                Location = location1
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4,
                workLog5
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupByTwoColumnsOfAdjacentTablesViaRelation(new DateTime(2000, 1, 1), "joe", "new york");
            Assert.AreEqual(4, actual.Count());
            AreEquivalent(new[] { workLog1, workLog2, workLog3, workLog4 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_MultipleOrGroupByWithAdjacentTablesViaRelation()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var location1 = new EFLocation()
            {
                Name = "seattle"
            };
            var location2 = new EFLocation()
            {
                Name = "new york"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1,
                Location = location1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 1, 1),
                Employee = employee2,
                Location = location1
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 1, 1),
                Employee = employee1,
                Location = location2
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee2,
                Location = location2
            };
            var workLog5 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 3, 3),
                EndDate = new DateTime(2000, 3, 3),
                Employee = employee1,
                Location = location1
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4,
                workLog5
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.MultipleOrGroupByWithAdjacentTablesViaRelation(
                new DateTime(2000, 1, 1), 
                "joe", 
                employee1.Id,
                "new york");
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog3, workLog4 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_OrGroupWithClassFilter()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee1
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee2
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupWithClassFilter(
                new WorkLog.BetweenDates()
                {
                    StartDate = new DateTime(2000, 1, 1),
                    EndDate = new DateTime(2001, 1, 1)
                },
                employee2.Id);
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog2, workLog4 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }


        [TestMethod]
        public void Where_OrGroupInClassFilter()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee1
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee2
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupInClassFilter(
                new WorkLog.OrColumns()
                {
                    StartDate = new DateTime(2000, 1, 1),
                    EmployeeId = employee2.Id
                });
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog2, workLog4 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_OrGroupInTwoClassFilters()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee2
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 2),
                Employee = employee2
            };
            var workLog5 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog6 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4,
                workLog5,
                workLog6,
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupInTwoClassFilters(
                new WorkLog.OrColumns()
                {
                    StartDate = new DateTime(2000, 1, 1),
                    EmployeeId = employee2.Id
                },
                new WorkLog.OrColumns2()
                {
                    Id = workLog3.Id,
                    EndDate = new DateTime(2000, 2, 2)
                });
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog3, workLog5 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_OrGroupWithTwoClassFilters()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(1999, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee2
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupWithTwoClassFilters(
                new WorkLog.BetweenDates()
                {
                    StartDate = new DateTime(2000, 1, 1),
                    EndDate = new DateTime(2000, 2, 2)
                },
                new WorkLog.IdAndEmployeeId()
                {
                    Id = workLog4.Id,
                    EmployeeId = employee2.Id
                });
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog2, workLog4 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_OrGroupNestedNavigationClassFilter()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var employee3 = new EFEmployee()
            {
                Name = "bill"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2010, 1, 1),
                EndDate = new DateTime(2010, 2, 1),
                Employee = employee3
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee1
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupNestedNavigationClassFilter(
                new WorkLog.NestedOrColumns()
                {
                    Employee = new Employee.OrColumns()
                    {
                        Id = employee2.Id,
                        Name = "bill"
                    }
                });
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(new[] { workLog2, workLog3 }.Select(wl => wl.Id), actual.Select(wl => wl.Id));
        }

        [TestMethod]
        public void Where_TwoOrGroupNestedNavigationClassFilter()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var employee3 = new EFEmployee()
            {
                Name = "bill"
            };
            var employee4 = new EFEmployee()
            {
                Name = "dave"
            };
            var employee5 = new EFEmployee()
            {
                Name = "mike"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 1),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 1),
                Employee = employee3
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee4
            };
            var workLog5 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 2),
                Employee = employee5
            };
            var workLog6 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2222, 2, 2),
                Employee = employee2
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4,
                workLog5,
                workLog6
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.TwoOrGroupNestedNavigationClassFilter(
                new Employee.NestedWithTwoOrGroups()
                {
                    WorkLog = new WorkLog.TwoOrGroups()
                    {
                        StartDate = new DateTime(2000, 1, 1),
                        EndDate = new DateTime(2000, 2, 2),
                        EmployeeId = employee2.Id,
                        Id = workLog3.Id
                    }
                });
            Assert.AreEqual(1, actual.Count());
            AreEquivalent(new[] { employee2 }.Select(e => e.Id), actual.Select(e => e.Id));
        }

        [TestMethod]
        public void Where_OrGroupWithColumnAndNavigationClassFilter()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var employee3 = new EFEmployee()
            {
                Name = "bill"
            };
            var employee4 = new EFEmployee()
            {
                Name = "dave"
            };
            var employee5 = new EFEmployee()
            {
                Name = "mike"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 1),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 1),
                Employee = employee3
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee4
            };
            var workLog5 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 2),
                Employee = employee5
            };
            var workLog6 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2222, 2, 2),
                Employee = employee2
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4,
                workLog5,
                workLog6
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupWithColumnAndNavigationClassFilter(
                workLog1.Id,
                new WorkLog.GetByEmployeeNameFilter()
                {
                    Employee = new Employee.EmployeeNameFilter()
                    {
                        Name = "joe"
                    }
                });
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog2, workLog6 }.Select(wl => wl.Id), actual.Select(e => e.Id));
        }

        [TestMethod]
        public void Where_OrGroupClassWithColumnAndNavigationClass()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var employee3 = new EFEmployee()
            {
                Name = "bill"
            };
            var employee4 = new EFEmployee()
            {
                Name = "dave"
            };
            var employee5 = new EFEmployee()
            {
                Name = "mike"
            };
            var workLog1 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2000, 2, 1),
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 1),
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 1),
                Employee = employee3
            };
            var workLog4 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2000, 2, 2),
                Employee = employee4
            };
            var workLog5 = new EFWorkLog()
            {
                StartDate = new DateTime(2222, 1, 1),
                EndDate = new DateTime(2222, 2, 2),
                Employee = employee5
            };
            var workLog6 = new EFWorkLog()
            {
                StartDate = new DateTime(2000, 1, 1),
                EndDate = new DateTime(2222, 2, 2),
                Employee = employee2
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4,
                workLog5,
                workLog6
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupClassWithColumnAndNavigationClass(
                new WorkLog.OrGroupClassWithColumnAndNavigationClass()
                {
                    Id = workLog1.Id,
                    Employee = new Employee.EmployeeNameFilter()
                    {
                        Name = "joe"
                    }
                });
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog2, workLog6 }.Select(wl => wl.Id), actual.Select(e => e.Id));
        }

        [TestMethod]
        public void Where_OrGroupForTwoNestedNavigationClassFilters()
        {
            var employee1 = new EFEmployee()
            {
                Name = "bob"
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe"
            };
            var employee3 = new EFEmployee()
            {
                Name = "bill"
            };
            var location1 = new EFLocation()
            {
                Name = "bob's burgers"
            };
            var location2 = new EFLocation()
            {
                Name = "sally's sandwiches"
            };
            var workLog1 = new EFWorkLog()
            {
                Employee = employee1,
                Location = location1
            };
            var workLog2 = new EFWorkLog()
            {
                Employee = employee2,
                Location = location2
            };
            var workLog3 = new EFWorkLog()
            {
                Employee = employee1,
                Location = location2
            };
            var workLog4 = new EFWorkLog()
            {
                Employee = employee2,
                Location = location1
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3,
                workLog4
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupForTwoNestedNavigationClassFilters(
                new WorkLog.TwoNestedNavigationClassFilters()
                {
                    Employee = new Employee.EmployeeNameFilter()
                    {
                        Name = "bob"
                    },
                    Location = new Location.LocationName()
                    {
                        Name = "bob's burgers"
                    }
                });
            Assert.AreEqual(3, actual.Count());
            AreEquivalent(new[] { workLog1, workLog3, workLog4 }.Select(wl => wl.Id), actual.Select(e => e.Id));
        }

        [TestMethod]
        public void Where_OrGroupWithColumnAndNestedNavigationClassFilter()
        {
            var address1 = new EFAddress()
            {
                StreetAddress = "123 fake st"
            };
            var address2 = new EFAddress()
            {
                StreetAddress = "234 nonsense ave"
            };
            var address3 = new EFAddress()
            {
                StreetAddress = "345 blah dr"
            };
            var employee1 = new EFEmployee()
            {
                Name = "bob",
                Addresses = new List<EFAddress>()
                {
                    address1
                }
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe",
                Addresses = new List<EFAddress>()
                {
                    address2
                }
            };
            var employee3 = new EFEmployee()
            {
                Name = "bill",
                Addresses = new List<EFAddress>()
                {
                    address3
                }
            };
            var workLog1 = new EFWorkLog()
            {
                Employee = employee1
            };
            var workLog2 = new EFWorkLog()
            {
                Employee = employee2
            };
            var workLog3 = new EFWorkLog()
            {
                Employee = employee3
            };
            var efWorkLogs = new List<EFWorkLog>()
            {
                workLog1,
                workLog2,
                workLog3
            };
            this.laborDbContext.WorkLog.AddRange(efWorkLogs);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupWithColumnAndNestedNavigationClassFilter(
                new WorkLog.NestedColumnAndNavigationClassFilter()
                {
                    Employee = new Employee.ColumnOrNavigationClassFilter()
                    {
                        Name = "joe",
                        Address = new Address.AddressStreetAddress()
                        {
                            StreetAddress = "123 fake st"
                        }
                    }
                });
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(new[] { workLog1, workLog2 }.Select(wl => wl.Id), actual.Select(e => e.Id));
        }

        [TestMethod]
        public void Where_OrGroupWithParameterColumnAndNavigationManyToManyClassFilter()
        {
            var address1 = new EFAddress()
            {
                StreetAddress = "123 fake st"
            };
            var address2 = new EFAddress()
            {
                StreetAddress = "234 nonsense ave"
            };
            var address3 = new EFAddress()
            {
                StreetAddress = "345 blah dr"
            };
            var employee1 = new EFEmployee()
            {
                Name = "bob",
                Addresses = new List<EFAddress>()
                {
                    address1
                }
            };
            var employee2 = new EFEmployee()
            {
                Name = "joe",
                Addresses = new List<EFAddress>()
                {
                    address2
                }
            };
            var employee3 = new EFEmployee()
            {
                Name = "bill",
                Addresses = new List<EFAddress>()
                {
                    address3
                }
            };
            var efEmployees = new List<EFEmployee>()
            {
                employee1,
                employee2,
                employee3
            };
            this.laborDbContext.Employee.AddRange(efEmployees);
            this.laborDbContext.SaveChanges();

            var actual = this.monolithicRepository.OrGroupWithParameterColumnAndNavigationManyToManyClassFilter(
                "joe",
                new Employee.StreetAddressFilter()
                {
                    Address = new Address.StreetAddressFilter()
                    {
                        StreetAddress = "123 fake st"
                    }
                });
            Assert.AreEqual(2, actual.Count());
            AreEquivalent(new[] { employee1, employee2 }.Select(e => e.Id), actual.Select(e => e.Id));
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
        public void OrderBy()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 3, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2020, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2019, 1, 1)});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { StartDate = new DateTime(2022, 1, 1)});
            this.laborDbContext.SaveChanges();
            var expected = this.laborDbContext.WorkLog.OrderBy(w => w.StartDate).Select(wl => wl.Id).ToList();
            var actual = this.monolithicRepository.GetOrderedWorkLogs(OrderByDirection.Ascending).Select(wl => wl.Id).ToList();

            AreSame(expected, actual);
        }

        [TestMethod]
        public void OrderByNonProjectedNavigationTable()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "4" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "3" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "5" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "1" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "2" }});
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetOrderedWorkLogsByNonProjectedNavigationTable(OrderByDirection.Ascending).Select(wl => wl.Id).ToList();

            AreSame(new [] { 4,5,2,1,3}, actual);
        }

        [TestMethod]
        public void OrderByProjectedNavigationTable()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "4" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "3" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "5" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "1" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "2" }});
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetOrderedWorkLogsByClassFilterNavigationProperty(new WorkLog.OrderByDirectionEmployeeName() { Employee = new Employee.EmployeeNameOrder() { Name = OrderByDirection.Ascending } }).ToList();

            AreSame(new [] { 4,5,2,1,3}, actual.Select(wl => wl.Id));
            AreSame(new[] { "1", "2", "3", "4", "5" }, actual.Select(wl => wl.Employee.Name));
        }

        [TestMethod]
        public void OrderByProjectedNavigationTableWithMultipleJoins()
        {
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "4" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "3" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "5" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "1" }});
            this.laborDbContext.WorkLog.Add(new EFWorkLog() { Employee = new EFEmployee() { Name = "2" }});
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetOrderedWorkLogsByClassFilterNavigationPropertyCanonicalType(new WorkLog.OrderByDirectionEmployeeName() { Employee = new Employee.EmployeeNameOrder() { Name = OrderByDirection.Ascending } }).ToList();

            AreSame(new [] { 4,5,2,1,3}, actual.Select(wl => wl.Id));
            AreSame(new[] { "1", "2", "3", "4", "5" }, actual.Select(wl => wl.Employee.Name));
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
        public void Offset_ManyToOneOrdering()
        {
            for (int i = 0; i < 5; i++)
            {
                var workLog = new EFWorkLog() { StartDate = new DateTime(2021, 1, 1).AddDays(new Random(i).Next(0, 20))};
                var employee = new EFEmployee() { Name = $"Bob{(i % 2 == 0 ? i : i + 10)}"};
                workLog.Employee = employee;
                
                this.laborDbContext.WorkLog.Add(workLog);
            }
            this.laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.GetNextWorkLogsWithOrder(1, new List<IOrderBy>() { new OrderBy(nameof(Employee), nameof(Employee.Name)) }).Select(w => w.Id).ToList();

            AreSame(new []{ 2, 4, 3, 5 }, actual);
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
        public void InsertSingle_Params_OutputIds()
        {
            var result = monolithicRepository.InsertEmployeeWithAttributeTableNameWithValuesByParamsOutputId("bob");

            Assert.AreEqual(1, result.Id);
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
        public void InsertMultiple_Void_ValuesWithOneToManyNavigationTables_ReturnsExpected()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);
            var actual = this.laborDbContext.Employee.Include(e => e.WorkLogs).ToList();

            Assert.AreEqual(2, actual.Count);
            AreSame(actual.Select(p => p.Name).ToList(), insertFields.Select(e => e.Name).ToList());
            actual.ForEach(employee => Assert.AreEqual(2, employee.WorkLogs.Count));
        }

        [TestMethod]
        public void InsertMultiple_Void_ValuesWithManyToOneNavigationTables_ReturnsExpected()
        {
            var insertFields = new[]
            {
                new WorkLog.InsertFieldsWithEmployee()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee = 
                        new Employee.InsertFields()
                            { Name = "Mike" }
                },
                new WorkLog.InsertFieldsWithEmployee()
                {
                    StartDate = new DateTime(2021, 3, 1), 
                    EndDate = new DateTime(2021, 1, 2),
                    Employee = 
                        new Employee.InsertFields()
                            { Name = "Lester" }
                }
            };
            this.monolithicRepository.InsertMultipleWorkLogsWithEmployees(insertFields);
            var actual = this.laborDbContext.Employee.Include(e => e.WorkLogs).ToList();

            Assert.AreEqual(2, actual.Count);
            AreSame(actual.Select(p => p.Name).ToList(), insertFields.Select(wl => wl.Employee.Name).ToList());
            actual.ForEach(employee => Assert.AreEqual(1, employee.WorkLogs.Count));
        }

        [TestMethod]
        public void InsertMultiple_Void_ValuesWithManyToOneAdjacentAndNestedManyToManyNavigationTables_ReturnsExpected()
        {
            var insertFields = new[]
            {
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                        {
                            Name = "Mike",
                            Addresses = new Address.InsertFields[]
                            {
                                new Address.InsertFields()
                                {
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "456 fake st",
                                    City = "Portland",
                                    State = "OR"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "567 fake st",
                                    City = "San Diego",
                                    State = "CA"
                                }
                            }

                        },
                    Location = new Location.Insert()
                    {
                        Name = "Ice Queen"
                    }
                },
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 3, 1), 
                    EndDate = new DateTime(2021, 1, 2),
                    Employee = 
                        new Employee.InsertFieldsWithAddress()
                            { 
                                Name = "Lester", 
                                Addresses = new Address.InsertFields[]
                                {
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "234 fake st", 
                                        City = "New York",
                                        State = "NY"
                                    },
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "345 fake st", 
                                        City = "Manchester",
                                        State = "NH"
                                    }
                                }

                            },
                    Location = new Location.Insert()
                    {
                        Name = "Burger Hut"
                    }
                }
            };
            this.monolithicRepository.InsertMultipleWorkLogsWithAdjacentAndNestedRelations(insertFields);
            var actual = this.laborDbContext.WorkLog
                    .Include(wl => wl.Employee)
                    .ThenInclude(e => e.Addresses)
                    .Include(wl => wl.Location)
                    //.Include(e => e.WorkLogs.Select(wl => wl.Location))
                    .ToList();

            Assert.AreEqual(2, actual.Count);
            AreSame(actual.Select(p => p.Employee.Name).ToList(), insertFields.Select(wl => wl.Employee.Name).ToList());
            Assert.AreEqual(2, actual.Count);

            var efWorkLog1 = actual.First();
            Assert.AreEqual(new DateTime(2021, 1, 1), efWorkLog1.StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), efWorkLog1.EndDate);

                var efEmployee1 = efWorkLog1.Employee;
                Assert.AreEqual("Mike", efEmployee1.Name);
                Assert.AreEqual(3, efEmployee1.Addresses.Count);
                
                    var efEmployee1Address1 = efEmployee1.Addresses.First();
                    Assert.AreEqual("123 fake st", efEmployee1Address1.StreetAddress);
                    Assert.AreEqual("Pennsylvania", efEmployee1Address1.City);
                    Assert.AreEqual("PA", efEmployee1Address1.State);
                    
                    var efEmployee1Address2 = efEmployee1.Addresses.Skip(1).First();
                    Assert.AreEqual("456 fake st", efEmployee1Address2.StreetAddress);
                    Assert.AreEqual("Portland", efEmployee1Address2.City);
                    Assert.AreEqual("OR", efEmployee1Address2.State);

                    var efEmployee1Address3 = efEmployee1.Addresses.Skip(2).First();
                    Assert.AreEqual("567 fake st", efEmployee1Address3.StreetAddress);
                    Assert.AreEqual("San Diego", efEmployee1Address3.City);
                    Assert.AreEqual("CA", efEmployee1Address3.State);

                var efLocation1 = efWorkLog1.Location;
                Assert.AreEqual("Ice Queen", efLocation1.Name);

            var efWorkLog2 = actual.Last();
            Assert.AreEqual(new DateTime(2021, 3, 1), efWorkLog2.StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), efWorkLog2.EndDate);

                var efEmployee2 = efWorkLog2.Employee;
                Assert.AreEqual("Lester", efEmployee2.Name);
                Assert.AreEqual(2, efEmployee2.Addresses.Count);
                
                    var efEmployee2Address1 = efEmployee2.Addresses.First();
                    Assert.AreEqual("234 fake st", efEmployee2Address1.StreetAddress);
                    Assert.AreEqual("New York", efEmployee2Address1.City);
                    Assert.AreEqual("NY", efEmployee2Address1.State);
                    
                    var efEmployee2Address2 = efEmployee2.Addresses.Skip(1).First();
                    Assert.AreEqual("345 fake st", efEmployee2Address2.StreetAddress);
                    Assert.AreEqual("Manchester", efEmployee2Address2.City);
                    Assert.AreEqual("NH", efEmployee2Address2.State);

                var efLocation2 = efWorkLog2.Location;
                Assert.AreEqual("Burger Hut", efLocation2.Name);

        }

        [TestMethod]
        public void InsertMultiple_ReturnResult_ValuesWithManyToOneAdjacentAndNestedManyToManyNavigationTables_ReturnsExpected()
        {
            var insertFields = new[]
            {
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                        {
                            Name = "Mike",
                            Addresses = new Address.InsertFields[]
                            {
                                new Address.InsertFields()
                                {
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "456 fake st",
                                    City = "Portland",
                                    State = "OR"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "567 fake st",
                                    City = "San Diego",
                                    State = "CA"
                                }
                            }

                        },
                    Location = new Location.Insert()
                    {
                        Name = "Ice Queen"
                    }
                },
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 3, 1), 
                    EndDate = new DateTime(2021, 1, 2),
                    Employee = 
                        new Employee.InsertFieldsWithAddress()
                            { 
                                Name = "Lester", 
                                Addresses = new Address.InsertFields[]
                                {
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "234 fake st", 
                                        City = "New York",
                                        State = "NY"
                                    },
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "345 fake st", 
                                        City = "Manchester",
                                        State = "NH"
                                    }
                                }

                            },
                    Location = new Location.Insert()
                    {
                        Name = "Burger Hut"
                    }
                }
            };
            var actual = this.monolithicRepository.InsertMultipleWorkLogsWithAdjacentAndNestedRelationsAndReturnResult(insertFields);

            Assert.AreEqual(2, actual.Count());
            AreSame(actual.Select(p => p.Employee.Name).ToList(), insertFields.Select(wl => wl.Employee.Name).ToList());
            Assert.AreEqual(2, actual.Count());

            var efWorkLog1 = actual.First();
            Assert.AreEqual(new DateTime(2021, 1, 1), efWorkLog1.StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), efWorkLog1.EndDate);

                var efEmployee1 = efWorkLog1.Employee;
                Assert.AreEqual("Mike", efEmployee1.Name);
                Assert.AreEqual(3, efEmployee1.Addresses.Count());
                
                    var efEmployee1Address1 = efEmployee1.Addresses.First();
                    Assert.AreEqual("123 fake st", efEmployee1Address1.StreetAddress);
                    Assert.AreEqual("Pennsylvania", efEmployee1Address1.City);
                    Assert.AreEqual("PA", efEmployee1Address1.State);
                    
                    var efEmployee1Address2 = efEmployee1.Addresses.Skip(1).First();
                    Assert.AreEqual("456 fake st", efEmployee1Address2.StreetAddress);
                    Assert.AreEqual("Portland", efEmployee1Address2.City);
                    Assert.AreEqual("OR", efEmployee1Address2.State);

                    var efEmployee1Address3 = efEmployee1.Addresses.Skip(2).First();
                    Assert.AreEqual("567 fake st", efEmployee1Address3.StreetAddress);
                    Assert.AreEqual("San Diego", efEmployee1Address3.City);
                    Assert.AreEqual("CA", efEmployee1Address3.State);

                var efLocation1 = efWorkLog1.Location;
                Assert.AreEqual("Ice Queen", efLocation1.Name);

            var efWorkLog2 = actual.Last();
            Assert.AreEqual(new DateTime(2021, 3, 1), efWorkLog2.StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), efWorkLog2.EndDate);

                var efEmployee2 = efWorkLog2.Employee;
                Assert.AreEqual("Lester", efEmployee2.Name);
                Assert.AreEqual(2, efEmployee2.Addresses.Count());
                
                    var efEmployee2Address1 = efEmployee2.Addresses.First();
                    Assert.AreEqual("234 fake st", efEmployee2Address1.StreetAddress);
                    Assert.AreEqual("New York", efEmployee2Address1.City);
                    Assert.AreEqual("NY", efEmployee2Address1.State);
                    
                    var efEmployee2Address2 = efEmployee2.Addresses.Skip(1).First();
                    Assert.AreEqual("345 fake st", efEmployee2Address2.StreetAddress);
                    Assert.AreEqual("Manchester", efEmployee2Address2.City);
                    Assert.AreEqual("NH", efEmployee2Address2.State);

                var efLocation2 = efWorkLog2.Location;
                Assert.AreEqual("Burger Hut", efLocation2.Name);

        }

        [TestMethod]
        public void InsertWithPropertyListReturnType_ReturnsExpected()
        {
            var insertFields = new[]
            {
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                        {
                            Name = "Mike",
                            Addresses = new Address.InsertFields[]
                            {
                                new Address.InsertFields()
                                {
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "456 fake st",
                                    City = "Portland",
                                    State = "OR"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "567 fake st",
                                    City = "San Diego",
                                    State = "CA"
                                }
                            }

                        },
                    Location = new Location.Insert()
                    {
                        Name = "Ice Queen"
                    }
                },
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 3, 1), 
                    EndDate = new DateTime(2021, 1, 2),
                    Employee = 
                        new Employee.InsertFieldsWithAddress()
                            { 
                                Name = "Lester", 
                                Addresses = new Address.InsertFields[]
                                {
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "234 fake st", 
                                        City = "New York",
                                        State = "NY"
                                    },
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "345 fake st", 
                                        City = "Manchester",
                                        State = "NH"
                                    }
                                }

                            },
                    Location = new Location.Insert()
                    {
                        Name = "Burger Hut"
                    }
                }
            };
            var actual = this.monolithicRepository.InsertMultipleWorkLogsWithAdjacentAndNestedRelationsAndReturnResult(insertFields);

            Assert.AreEqual(2, actual.Count());
            AreSame(actual.Select(p => p.Employee.Name).ToList(), insertFields.Select(wl => wl.Employee.Name).ToList());
            Assert.AreEqual(2, actual.Count());

            var efWorkLog1 = actual.First();
            Assert.AreEqual(new DateTime(2021, 1, 1), efWorkLog1.StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), efWorkLog1.EndDate);

                var efEmployee1 = efWorkLog1.Employee;
                Assert.AreEqual("Mike", efEmployee1.Name);
                Assert.AreEqual(3, efEmployee1.Addresses.Count());
                
                    var efEmployee1Address1 = efEmployee1.Addresses.First();
                    Assert.AreEqual("123 fake st", efEmployee1Address1.StreetAddress);
                    Assert.AreEqual("Pennsylvania", efEmployee1Address1.City);
                    Assert.AreEqual("PA", efEmployee1Address1.State);
                    
                    var efEmployee1Address2 = efEmployee1.Addresses.Skip(1).First();
                    Assert.AreEqual("456 fake st", efEmployee1Address2.StreetAddress);
                    Assert.AreEqual("Portland", efEmployee1Address2.City);
                    Assert.AreEqual("OR", efEmployee1Address2.State);

                    var efEmployee1Address3 = efEmployee1.Addresses.Skip(2).First();
                    Assert.AreEqual("567 fake st", efEmployee1Address3.StreetAddress);
                    Assert.AreEqual("San Diego", efEmployee1Address3.City);
                    Assert.AreEqual("CA", efEmployee1Address3.State);

                var efLocation1 = efWorkLog1.Location;
                Assert.AreEqual("Ice Queen", efLocation1.Name);

            var efWorkLog2 = actual.Last();
            Assert.AreEqual(new DateTime(2021, 3, 1), efWorkLog2.StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), efWorkLog2.EndDate);

                var efEmployee2 = efWorkLog2.Employee;
                Assert.AreEqual("Lester", efEmployee2.Name);
                Assert.AreEqual(2, efEmployee2.Addresses.Count());
                
                    var efEmployee2Address1 = efEmployee2.Addresses.First();
                    Assert.AreEqual("234 fake st", efEmployee2Address1.StreetAddress);
                    Assert.AreEqual("New York", efEmployee2Address1.City);
                    Assert.AreEqual("NY", efEmployee2Address1.State);
                    
                    var efEmployee2Address2 = efEmployee2.Addresses.Skip(1).First();
                    Assert.AreEqual("345 fake st", efEmployee2Address2.StreetAddress);
                    Assert.AreEqual("Manchester", efEmployee2Address2.City);
                    Assert.AreEqual("NH", efEmployee2Address2.State);

                var efLocation2 = efWorkLog2.Location;
                Assert.AreEqual("Burger Hut", efLocation2.Name);

        }

        // test insert when empty collections are passed

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

        [TestMethod]
        public void Upsert_Multiple()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

            this.monolithicRepository.UpsertMultipleEmployeesWithWorkLogs(
                new Employee.UpsertFieldsWithWorkLogs[]
                {
                    new Employee.UpsertFieldsWithWorkLogs()
                    {
                        Id = 1,
                        Name = "Kyle",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpsertFields()
                                {Id = 1, StartDate = new DateTime(2022, 1, 1), EndDate = new DateTime(2022, 1, 2)},
                            new WorkLog.UpsertFields()
                                {StartDate = new DateTime(2022, 2, 1), EndDate = new DateTime(2022, 2, 2)},
                            new WorkLog.UpsertFields()
                                {StartDate = new DateTime(2022, 3, 1), EndDate = new DateTime(2022, 3, 2)}
                        }
                    },
                    new Employee.UpsertFieldsWithWorkLogs()
                    {
                        Name = "Geno",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpsertFields()
                                {Id = 2, StartDate = new DateTime(2022, 4, 1), EndDate = new DateTime(2022, 4, 2)},
                            new WorkLog.UpsertFields()
                                {StartDate = new DateTime(2022, 5, 1), EndDate = new DateTime(2022, 5, 2)}
                        }
                    }
                });

            var actual = laborDbContext.Employee.Include(e => e.WorkLogs).ToList();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(3, actualEmployee1.WorkLogs.Count);
            Assert.AreEqual(new DateTime(2022, 1, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).StartDate);
            Assert.AreEqual(new DateTime(2022, 1, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).EndDate);
            Assert.AreEqual(new DateTime(2022, 2, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 5).StartDate);
            Assert.AreEqual(new DateTime(2022, 2, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 5).EndDate);
            Assert.AreEqual(new DateTime(2022, 3, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 6).StartDate);
            Assert.AreEqual(new DateTime(2022, 3, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 6).EndDate);
            var actualEmployee2 = actual.Single(e => e.Id == 3);
            Assert.AreEqual(2, actualEmployee2.WorkLogs.Count);
            Assert.AreEqual(new DateTime(2022, 4, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 2).StartDate);
            Assert.AreEqual(new DateTime(2022, 4, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 2).EndDate);
            Assert.AreEqual(new DateTime(2022, 5, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 7).StartDate);
            Assert.AreEqual(new DateTime(2022, 5, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 7).EndDate);
        }

        [TestMethod]
        public void Upsert_Multiple_OutputIds()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

            var result = this.monolithicRepository.UpsertMultipleEmployeesWithWorkLogs_OutputIds(
                new Employee.UpsertFieldsWithWorkLogs[]
                {
                    new Employee.UpsertFieldsWithWorkLogs()
                    {
                        Id = 1,
                        Name = "Kyle",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpsertFields()
                                {Id = 1, StartDate = new DateTime(2022, 1, 1), EndDate = new DateTime(2022, 1, 2)},
                            new WorkLog.UpsertFields()
                                {StartDate = new DateTime(2022, 2, 1), EndDate = new DateTime(2022, 2, 2)},
                            new WorkLog.UpsertFields()
                                {StartDate = new DateTime(2022, 3, 1), EndDate = new DateTime(2022, 3, 2)}
                        }
                    },
                    new Employee.UpsertFieldsWithWorkLogs()
                    {
                        Name = "Geno",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpsertFields()
                                {Id = 2, StartDate = new DateTime(2022, 4, 1), EndDate = new DateTime(2022, 4, 2)},
                            new WorkLog.UpsertFields()
                                {StartDate = new DateTime(2022, 5, 1), EndDate = new DateTime(2022, 5, 2)}
                        }
                    }
                });
            
            AreEquivalent(new [] { 1, 3 }, result.Select(c => c.Id));
        }
        
        [TestMethod]
        public void UpsertMultiple_ReturnResult_ValuesWithManyToOneAdjacentAndNestedManyToManyNavigationTables_ReturnsExpected()
        {
            var insertFields = new[]
            {
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                        {
                            Name = "Mike",
                            Addresses = new Address.InsertFields[]
                            {
                                new Address.InsertFields()
                                {
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "456 fake st",
                                    City = "Portland",
                                    State = "OR"
                                },
                                new Address.InsertFields()
                                {
                                    StreetAddress = "567 fake st",
                                    City = "San Diego",
                                    State = "CA"
                                }
                            }

                        },
                    Location = new Location.Insert()
                    {
                        Name = "Ice Queen"
                    }
                },
                new WorkLog.InsertFieldsWithEmployeeAndLocation()
                {
                    StartDate = new DateTime(2021, 3, 1),
                    EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.InsertFieldsWithAddress()
                            {
                                Name = "Lester",
                                Addresses = new Address.InsertFields[]
                                {
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "234 fake st",
                                        City = "New York",
                                        State = "NY"
                                    },
                                    new Address.InsertFields()
                                    {
                                        StreetAddress = "345 fake st",
                                        City = "Manchester",
                                        State = "NH"
                                    }
                                }

                            },
                    Location = new Location.Insert()
                    {
                        Name = "Burger Hut"
                    }
                }
            };
            monolithicRepository.InsertMultipleWorkLogsWithAdjacentAndNestedRelations(insertFields);

            var upsertFields = new[]
            {
                new WorkLog.UpsertFieldsWithEmployeeAndLocation()
                {
                    Id = 1,
                    StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2),
                    Employee =
                        new Employee.UpsertFieldsWithAddress()
                        {
                            Id = 1,
                            Name = "Mike",
                            Addresses = new []
                            {
                                new Address.UpsertFields()
                                {
                                    Id = 1,
                                    StreetAddress = "123 fake st",
                                    City = "Pennsylvania",
                                    State = "PA"
                                },
                                new Address.UpsertFields()
                                {
                                    Id = 2,
                                    StreetAddress = "456 fake st",
                                    City = "Portland",
                                    State = "OR"
                                }
                            }

                        },
                    Location = new Location.Upsert()
                    {
                        Name = "Eleven Til Seven"
                    }
                },
                new WorkLog.UpsertFieldsWithEmployeeAndLocation()
                {
                    Id = 2,
                    StartDate = new DateTime(2022, 3, 1),
                    EndDate = new DateTime(2022, 1, 2),
                    Employee =
                        new Employee.UpsertFieldsWithAddress()
                            {
                                Id = 2,
                                Name = "Lester",
                                Addresses = new []
                                {
                                    new Address.UpsertFields()
                                    {
                                        Id = 3,
                                        StreetAddress = "567 fake st",
                                        City = "San Diego",
                                        State = "CA"
                                    },
                                    new Address.UpsertFields()
                                    {
                                        Id = 4,
                                        StreetAddress = "234 fake st",
                                        City = "New York",
                                        State = "NY"
                                    },
                                    new Address.UpsertFields()
                                    {
                                        Id = 5,
                                        StreetAddress = "345 fake st",
                                        City = "Manchester",
                                        State = "NH"
                                    },
                                    new Address.UpsertFields()
                                    {
                                        StreetAddress = "789 sesame st",
                                        City = "New Jersey",
                                        State = "NJ"
                                    }
                                }

                            },
                    Location = new Location.Upsert()
                    {
                        Id = 2,
                        Name = "Taco Hut"
                    }
                }
            };
            this.monolithicRepository.UpsertMultipleWorkLogsWithAdjacentAndNestedRelations(upsertFields);
            var actual = laborDbContext.WorkLog
                .Include(wl => wl.Employee)
                .ThenInclude(e => e.Addresses)
                .Include(wl => wl.Location).ToList();

            Assert.AreEqual(2, actual.Count());
            AreSame(actual.Select(p => p.Employee.Name).ToList(), upsertFields.Select(wl => wl.Employee.Name).ToList());
            Assert.AreEqual(2, actual.Count());

            var efWorkLog1 = actual.First();
            Assert.AreEqual(new DateTime(2021, 1, 1), efWorkLog1.StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), efWorkLog1.EndDate);

            var efEmployee1 = efWorkLog1.Employee;
            Assert.AreEqual("Mike", efEmployee1.Name);
            Assert.AreEqual(3, efEmployee1.Addresses.Count());

            var efEmployee1Address1 = efEmployee1.Addresses.First();
            Assert.AreEqual("123 fake st", efEmployee1Address1.StreetAddress);
            Assert.AreEqual("Pennsylvania", efEmployee1Address1.City);
            Assert.AreEqual("PA", efEmployee1Address1.State);

            var efEmployee1Address2 = efEmployee1.Addresses.Skip(1).First();
            Assert.AreEqual("456 fake st", efEmployee1Address2.StreetAddress);
            Assert.AreEqual("Portland", efEmployee1Address2.City);
            Assert.AreEqual("OR", efEmployee1Address2.State);

            var efLocation1 = efWorkLog1.Location;
            Assert.AreEqual(3, efLocation1.Id);
            Assert.AreEqual("Eleven Til Seven", efLocation1.Name);

            var efWorkLog2 = actual.Last();
            Assert.AreEqual(new DateTime(2022, 3, 1), efWorkLog2.StartDate);
            Assert.AreEqual(new DateTime(2022, 1, 2), efWorkLog2.EndDate);

            var efEmployee2 = efWorkLog2.Employee;
            Assert.AreEqual("Lester", efEmployee2.Name);
            Assert.AreEqual(4, efEmployee2.Addresses.Count());

            var efEmployee2Address1 = efEmployee2.Addresses.Single(a => a.Id == 3);
            Assert.AreEqual("567 fake st", efEmployee2Address1.StreetAddress);
            Assert.AreEqual("San Diego", efEmployee2Address1.City);
            Assert.AreEqual("CA", efEmployee2Address1.State);

            var efEmployee2Address2 = efEmployee2.Addresses.Single(a => a.Id == 4);
            Assert.AreEqual("234 fake st", efEmployee2Address2.StreetAddress);
            Assert.AreEqual("New York", efEmployee2Address2.City);
            Assert.AreEqual("NY", efEmployee2Address2.State);

            var efEmployee2Address3 = efEmployee2.Addresses.Single(a => a.Id == 5);
            Assert.AreEqual("345 fake st", efEmployee2Address3.StreetAddress);
            Assert.AreEqual("Manchester", efEmployee2Address3.City);
            Assert.AreEqual("NH", efEmployee2Address3.State);

            var efEmployee2Address4 = efEmployee2.Addresses.Single(a => a.Id == 6);
            Assert.AreEqual("789 sesame st", efEmployee2Address4.StreetAddress);
            Assert.AreEqual("New Jersey", efEmployee2Address4.City);
            Assert.AreEqual("NJ", efEmployee2Address4.State);

            var efLocation2 = efWorkLog2.Location;
            Assert.AreEqual("Taco Hut", efLocation2.Name);

        }
        
        [TestMethod]
        public void Upsert_Single_NullKey_InsertsRow()
        {
            this.monolithicRepository.UpsertEmployeeViaMethodParams(null, "bob");

            var actual = laborDbContext.Employee.AsNoTracking().Single();

            Assert.AreEqual("bob", actual.Name);
        }
        
        [TestMethod]
        public void Upsert_Single_NonExistingKey_InsertsRow()
        {
            this.monolithicRepository.UpsertEmployeeViaMethodParams(2, "bob");

            var actual = laborDbContext.Employee.AsNoTracking().Single();

            Assert.AreEqual(1, actual.Id);
            Assert.AreEqual("bob", actual.Name);
        }
        
        [TestMethod]
        public void Upsert_Single_ExistingKey_UpdatesRow()
        {
            laborDbContext.Employee.Add(new EFEmployee() { Name = "bob" });
            laborDbContext.SaveChanges();
            this.monolithicRepository.UpsertEmployeeViaMethodParams(1, "joe");

            var actual = laborDbContext.Employee.AsNoTracking().Single();

            Assert.AreEqual(1, actual.Id);
            Assert.AreEqual("joe", actual.Name);
        }
        
        [TestMethod]
        public void Upsert_Single_Update_ReturnValue()
        {
            laborDbContext.Employee.Add(new EFEmployee() { Name = "bob" });
            laborDbContext.SaveChanges();
            var actual = this.monolithicRepository.UpsertEmployeeViaMethodParamsReturnValue(1, "joe");

            Assert.AreEqual(1, actual.Id);
            Assert.AreEqual("joe", actual.Name);
        }

        [TestMethod]
        public void Upsert_Single_Insert_ReturnValue()
        {
            var actual = this.monolithicRepository.UpsertEmployeeViaMethodParamsReturnValue(2, "bob");

            Assert.AreEqual(1, actual.Id);
            Assert.AreEqual("bob", actual.Name);
        }
        
        [TestMethod]
        public void Sync_SingleCollectionNavigationProperty()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

            this.monolithicRepository.SyncEmployeeWithWorkLogs(
                    new Employee.SyncFieldsWithWorkLogs()
                    {
                        Id = 1,
                        Name = "Kyle",
                        WorkLogs = new[]
                        {
                            new WorkLog.SyncFields()
                                {Id = 1, StartDate = new DateTime(2022, 1, 1), EndDate = new DateTime(2022, 1, 2)},
                            new WorkLog.SyncFields()
                                {StartDate = new DateTime(2022, 2, 1), EndDate = new DateTime(2022, 2, 2)}
                        }
                    });

            var actual = laborDbContext.Employee.Include(e => e.WorkLogs).ToList();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(2, actualEmployee1.WorkLogs.Count);
            Assert.AreEqual(new DateTime(2022, 1, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).StartDate);
            Assert.AreEqual(new DateTime(2022, 1, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).EndDate);
            Assert.AreEqual(new DateTime(2022, 2, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 5).StartDate);
            Assert.AreEqual(new DateTime(2022, 2, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 5).EndDate);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.WorkLogs.Count);
            Assert.AreEqual(new DateTime(2021, 3, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).EndDate);
            Assert.AreEqual(new DateTime(2021, 4, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).StartDate);
            Assert.AreEqual(new DateTime(2021, 2, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).EndDate);
        }
        
        [TestMethod]
        public async Task Sync_SingleCollectionNavigationPropertyAsync()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

            await this.monolithicRepository.SyncEmployeeWithWorkLogsAsync(
                    new Employee.SyncFieldsWithWorkLogs()
                    {
                        Id = 1,
                        Name = "Kyle",
                        WorkLogs = new[]
                        {
                            new WorkLog.SyncFields()
                                {Id = 1, StartDate = new DateTime(2022, 1, 1), EndDate = new DateTime(2022, 1, 2)},
                            new WorkLog.SyncFields()
                                {StartDate = new DateTime(2022, 2, 1), EndDate = new DateTime(2022, 2, 2)}
                        }
                    });

            var actual = laborDbContext.Employee.Include(e => e.WorkLogs).ToList();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(2, actualEmployee1.WorkLogs.Count);
            Assert.AreEqual(new DateTime(2022, 1, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).StartDate);
            Assert.AreEqual(new DateTime(2022, 1, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).EndDate);
            Assert.AreEqual(new DateTime(2022, 2, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 5).StartDate);
            Assert.AreEqual(new DateTime(2022, 2, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 5).EndDate);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.WorkLogs.Count);
            Assert.AreEqual(new DateTime(2021, 3, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).StartDate);
            Assert.AreEqual(new DateTime(2021, 1, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).EndDate);
            Assert.AreEqual(new DateTime(2021, 4, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).StartDate);
            Assert.AreEqual(new DateTime(2021, 2, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).EndDate);
        }
        
        //[TestMethod]
        //public void Sync_IdOnly_SingleCollectionNavigationProperty()
        //{
        //    var insertFields = new Employee.InsertFieldsWithWorkLogs[]
        //    {
        //        new Employee.InsertFieldsWithWorkLogs()
        //        {
        //            Name = "Mike",
        //            WorkLogs = new[]
        //            {
        //                new WorkLog.DataFields()
        //                    {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
        //                new WorkLog.DataFields()
        //                    {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
        //            }
        //        },
        //        new Employee.InsertFieldsWithWorkLogs()
        //        {
        //            Name = "Lester",
        //            WorkLogs = new[]
        //            {
        //                new WorkLog.DataFields()
        //                    {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
        //                new WorkLog.DataFields()
        //                    {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
        //            }
        //        }
        //    };
        //    this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

        //    this.monolithicRepository.SyncEmployeeIdWithWorkLogIds(
        //            new Employee.SyncIdsWithWorkLogIds()
        //            {
        //                Id = 1,
        //                WorkLogs = new[]
        //                {
        //                    new WorkLog.WorkLogIdPoco()
        //                        {Id = 1},
        //                    new WorkLog.WorkLogIdPoco()
        //                        {Id = 3}
        //                }
        //            });

        //    var actual = laborDbContext.Employee.Include(e => e.WorkLogs).ToList();

        //    Assert.IsTrue(actual.Any(e => e.Name == "Mike"));
        //    Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
        //    var actualEmployee1 = actual.Single(e => e.Id == 1);
        //    Assert.AreEqual("Mike", actualEmployee1.Name);
        //    Assert.AreEqual(2, actualEmployee1.WorkLogs.Count);
        //    Assert.AreEqual(new DateTime(2022, 1, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).StartDate);
        //    Assert.AreEqual(new DateTime(2022, 1, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).EndDate);
        //    Assert.AreEqual(new DateTime(2022, 2, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 3).StartDate);
        //    Assert.AreEqual(new DateTime(2022, 2, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 3).EndDate);
        //}

        [TestMethod]
        public void Sync_ManyToManyPrimaryKeyFromForeignKeyNavigationProperty()
        {
            var insertFields = new EFEmployee[]
            {
                new EFEmployee()
                {
                    Name = "Mike",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
                        new EFAddress() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
                    }
                },
                new EFEmployee()
                {
                    Name = "Lester",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "678 fake st", City = "Orlando", State = "FL" },
                        new EFAddress() { StreetAddress = "910 fake st", City = "Atlanta", State = "GA" }
                    }
                }
            };

            laborDbContext.Employee.AddRange(insertFields);
            laborDbContext.SaveChanges();

            this.monolithicRepository.SyncManyToManyEmployeeWithAddresses(
                    new Employee.SyncFieldsWithAddresses()
                    {
                        Id = 1,
                        Name = "Kyle",
                        Addresses = new[]
                        {
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { StreetAddress = "2020 fake st", City = "New York", State = "NY" }
                        }
                    });

            laborDbContext.ChangeTracker.Clear();
            var actual = this.monolithicRepository.GetSyncManyToManyEmployeeWithAddresses();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(2, actualEmployee1.Addresses.Count());
            Assert.AreEqual("789 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 1).StreetAddress);
            Assert.AreEqual("Los Angeles", actualEmployee1.Addresses.First(wl => wl.Id == 1).City);
            Assert.AreEqual("CA", actualEmployee1.Addresses.First(wl => wl.Id == 1).State);
            Assert.AreEqual("2020 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 5).StreetAddress);
            Assert.AreEqual("New York", actualEmployee1.Addresses.First(wl => wl.Id == 5).City);
            Assert.AreEqual("NY", actualEmployee1.Addresses.First(wl => wl.Id == 5).State);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.Addresses.Count());
            Assert.AreEqual("678 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 3).StreetAddress);
            Assert.AreEqual("Orlando", actualEmployee2.Addresses.First(wl => wl.Id == 3).City);
            Assert.AreEqual("FL", actualEmployee2.Addresses.First(wl => wl.Id == 3).State);
            Assert.AreEqual("910 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 4).StreetAddress);
            Assert.AreEqual("Atlanta", actualEmployee2.Addresses.First(wl => wl.Id == 4).City);
            Assert.AreEqual("GA", actualEmployee2.Addresses.First(wl => wl.Id == 4).State);
        }

        [TestMethod]
        public void Sync_ManyToManyNoPrimaryKeyNavigationProperty()
        {
            var sqlCommand = new SqlCommand(@"ALTER TABLE EFAddressEFEmployee DROP CONSTRAINT PK_EFAddressEFEmployee;", (this.laborDbConnection as SqlConnection));
            DatabaseHelpers.RunCommand(sqlCommand);
            ConfigureSigQL();

            var insertFields = new EFEmployee[]
            {
                new EFEmployee()
                {
                    Name = "Mike",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
                        new EFAddress() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
                    }
                },
                new EFEmployee()
                {
                    Name = "Lester",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "678 fake st", City = "Orlando", State = "FL" },
                        new EFAddress() { StreetAddress = "910 fake st", City = "Atlanta", State = "GA" }
                    }
                }
            };

            laborDbContext.Employee.AddRange(insertFields);
            laborDbContext.SaveChanges();

            this.monolithicRepository.SyncManyToManyEmployeeWithAddresses(
                    new Employee.SyncFieldsWithAddresses()
                    {
                        Id = 1,
                        Name = "Kyle",
                        Addresses = new[]
                        {
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { StreetAddress = "2020 fake st", City = "New York", State = "NY" }
                        }
                    });

            laborDbContext.ChangeTracker.Clear();

            var actual = this.monolithicRepository.GetSyncManyToManyEmployeeWithAddresses();
            var efAddressEfEmployees = this.monolithicRepository.GetEFAddressEFEmployees();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(3, efAddressEfEmployees.Count(a => a.EmployeesId == 1));
            Assert.AreEqual(2, efAddressEfEmployees.Count(a => a.AddressesId == 1 && a.EmployeesId == 1));
            //Assert.AreEqual(3, efAddressEfEmployees.Where(e => e.EmployeesId == actualEmployee1.Id).Count());
            //Assert.AreEqual(2, efAddressEfEmployees.Where(e => e.EmployeesId == actualEmployee1.Id).Where(a => a.AddressesId == 1).Count());
            Assert.AreEqual("789 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 1).StreetAddress);
            Assert.AreEqual("Los Angeles", actualEmployee1.Addresses.First(wl => wl.Id == 1).City);
            Assert.AreEqual("CA", actualEmployee1.Addresses.First(wl => wl.Id == 1).State);
            Assert.AreEqual("2020 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 5).StreetAddress);
            Assert.AreEqual("New York", actualEmployee1.Addresses.First(wl => wl.Id == 5).City);
            Assert.AreEqual("NY", actualEmployee1.Addresses.First(wl => wl.Id == 5).State);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.Addresses.Count());
            Assert.AreEqual("678 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 3).StreetAddress);
            Assert.AreEqual("Orlando", actualEmployee2.Addresses.First(wl => wl.Id == 3).City);
            Assert.AreEqual("FL", actualEmployee2.Addresses.First(wl => wl.Id == 3).State);
            Assert.AreEqual("910 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 4).StreetAddress);
            Assert.AreEqual("Atlanta", actualEmployee2.Addresses.First(wl => wl.Id == 4).City);
            Assert.AreEqual("GA", actualEmployee2.Addresses.First(wl => wl.Id == 4).State);
        }

        [TestMethod]
        public void Sync_ManyToManyIndependentPrimaryKeyNavigationProperty()
        {
            var sqlCommand = new SqlCommand("ALTER TABLE EFAddressEFEmployee DROP CONSTRAINT PK_EFAddressEFEmployee;alter table [EFAddressEFEmployee] add Id int identity primary key;", (this.laborDbConnection as SqlConnection));
            DatabaseHelpers.RunCommand(sqlCommand);
            ConfigureSigQL();

            var insertFields = new EFEmployee[]
            {
                new EFEmployee()
                {
                    Name = "Mike",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
                        new EFAddress() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
                    }
                },
                new EFEmployee()
                {
                    Name = "Lester",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "678 fake st", City = "Orlando", State = "FL" },
                        new EFAddress() { StreetAddress = "910 fake st", City = "Atlanta", State = "GA" }
                    }
                }
            };

            laborDbContext.Employee.AddRange(insertFields);
            laborDbContext.SaveChanges();

            this.monolithicRepository.SyncManyToManyEmployeeWithAddresses(
                    new Employee.SyncFieldsWithAddresses()
                    {
                        Id = 1,
                        Name = "Kyle",
                        Addresses = new[]
                        {
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { StreetAddress = "2020 fake st", City = "New York", State = "NY" }
                        }
                    });

            laborDbContext.ChangeTracker.Clear();

            var actual = this.monolithicRepository.GetSyncManyToManyEmployeeWithAddresses();
            var efAddressEfEmployees = this.monolithicRepository.GetEFAddressEFEmployees();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(3, efAddressEfEmployees.Count(a => a.EmployeesId == 1));
            Assert.AreEqual(2, efAddressEfEmployees.Count(a => a.AddressesId == 1 && a.EmployeesId == 1));
            Assert.AreEqual("789 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 1).StreetAddress);
            Assert.AreEqual("Los Angeles", actualEmployee1.Addresses.First(wl => wl.Id == 1).City);
            Assert.AreEqual("CA", actualEmployee1.Addresses.First(wl => wl.Id == 1).State);
            Assert.AreEqual("2020 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 5).StreetAddress);
            Assert.AreEqual("New York", actualEmployee1.Addresses.First(wl => wl.Id == 5).City);
            Assert.AreEqual("NY", actualEmployee1.Addresses.First(wl => wl.Id == 5).State);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.Addresses.Count());
            Assert.AreEqual("678 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 3).StreetAddress);
            Assert.AreEqual("Orlando", actualEmployee2.Addresses.First(wl => wl.Id == 3).City);
            Assert.AreEqual("FL", actualEmployee2.Addresses.First(wl => wl.Id == 3).State);
            Assert.AreEqual("910 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 4).StreetAddress);
            Assert.AreEqual("Atlanta", actualEmployee2.Addresses.First(wl => wl.Id == 4).City);
            Assert.AreEqual("GA", actualEmployee2.Addresses.First(wl => wl.Id == 4).State);
        }

        [TestMethod]
        public void Sync_ManyToMany_AllowsDeletionOfDuplicatedKey()
        {
            var sqlCommand = new SqlCommand(@"ALTER TABLE EFAddressEFEmployee DROP CONSTRAINT PK_EFAddressEFEmployee;", (this.laborDbConnection as SqlConnection));
            DatabaseHelpers.RunCommand(sqlCommand);
            ConfigureSigQL();

            var insertFields = new EFEmployee[]
            {
                new EFEmployee()
                {
                    Name = "Mike",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
                        new EFAddress() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
                    }
                },
                new EFEmployee()
                {
                    Name = "Lester",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "678 fake st", City = "Orlando", State = "FL" },
                        new EFAddress() { StreetAddress = "910 fake st", City = "Atlanta", State = "GA" }
                    }
                }
            };

            laborDbContext.Employee.AddRange(insertFields);
            laborDbContext.SaveChanges();

            this.monolithicRepository.SyncManyToManyEmployeeWithAddresses(
                    new Employee.SyncFieldsWithAddresses()
                    {
                        Id = 1,
                        Name = "Kyle",
                        Addresses = new[]
                        {
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { StreetAddress = "2020 fake st", City = "New York", State = "NY" }
                        }
                    });

            this.monolithicRepository.SyncManyToManyEmployeeWithAddresses(
                    new Employee.SyncFieldsWithAddresses()
                    {
                        Id = 1,
                        Name = "Kyle",
                        Addresses = new[]
                        {
                            new Address.UpsertFields() { Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA" },
                            new Address.UpsertFields() { StreetAddress = "2020 fake st", City = "New York", State = "NY" }
                        }
                    });
            
            var actual = this.monolithicRepository.GetSyncManyToManyEmployeeWithAddresses();
            var efAddressEfEmployees = this.monolithicRepository.GetEFAddressEFEmployees();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(2, efAddressEfEmployees.Count(a => a.EmployeesId == 1));
            Assert.AreEqual(1, efAddressEfEmployees.Count(a => a.AddressesId == 1 && a.EmployeesId == 1));
            Assert.AreEqual("789 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 1).StreetAddress);
            Assert.AreEqual("Los Angeles", actualEmployee1.Addresses.First(wl => wl.Id == 1).City);
            Assert.AreEqual("CA", actualEmployee1.Addresses.First(wl => wl.Id == 1).State);
            Assert.AreEqual("2020 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 6).StreetAddress);
            Assert.AreEqual("New York", actualEmployee1.Addresses.First(wl => wl.Id == 6).City);
            Assert.AreEqual("NY", actualEmployee1.Addresses.First(wl => wl.Id == 6).State);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.Addresses.Count());
            Assert.AreEqual("678 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 3).StreetAddress);
            Assert.AreEqual("Orlando", actualEmployee2.Addresses.First(wl => wl.Id == 3).City);
            Assert.AreEqual("FL", actualEmployee2.Addresses.First(wl => wl.Id == 3).State);
            Assert.AreEqual("910 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 4).StreetAddress);
            Assert.AreEqual("Atlanta", actualEmployee2.Addresses.First(wl => wl.Id == 4).City);
            Assert.AreEqual("GA", actualEmployee2.Addresses.First(wl => wl.Id == 4).State);
        }

        [TestMethod]
        public void Sync_ManyToMany_EmptyCollection_ClearsRows()
        {
            var sqlCommand = new SqlCommand(@"ALTER TABLE EFAddressEFEmployee DROP CONSTRAINT PK_EFAddressEFEmployee;", (this.laborDbConnection as SqlConnection));
            DatabaseHelpers.RunCommand(sqlCommand);
            ConfigureSigQL();

            var insertFields = new EFEmployee[]
            {
                new EFEmployee()
                {
                    Name = "Mike",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
                        new EFAddress() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
                    }
                }
            };

            laborDbContext.Employee.AddRange(insertFields);
            laborDbContext.SaveChanges();

            this.monolithicRepository.SyncManyToManyEmployeeWithAddresses(
                    new Employee.SyncFieldsWithAddresses()
                    {
                        Id = 1,
                        Name = "Kyle",
                        Addresses = new List<Address.UpsertFields>()
                    });
            
            var actual = this.monolithicRepository.GetSyncManyToManyEmployeeWithAddresses();

            Assert.AreEqual(0, actual.Single().Addresses.Count());
        }

        [TestMethod]
        public void Sync_IdClassesOnly_ManyToManyPrimaryKeyFromForeignKeyNavigationProperty()
        {
            var insertFields = new EFEmployee[]
            {
                new EFEmployee()
                {
                    Name = "Mike",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
                        new EFAddress() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
                    }
                },
                new EFEmployee()
                {
                    Name = "Lester",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "678 fake st", City = "Orlando", State = "FL" },
                        new EFAddress() { StreetAddress = "910 fake st", City = "Atlanta", State = "GA" }
                    }
                }
            };

            laborDbContext.Employee.AddRange(insertFields);
            laborDbContext.SaveChanges();

            this.monolithicRepository.SyncEmployeeWithAddressId(
                    new Employee.SyncWithAddressId()
                    {
                        Id = 1,
                        Addresses = new[]
                        {
                            new Address.AddressId() { Id = 1 },
                            new Address.AddressId() { Id = 3 }
                        }
                    });

            laborDbContext.ChangeTracker.Clear();
            var actual = this.monolithicRepository.GetSyncManyToManyEmployeeWithAddresses();

            Assert.IsTrue(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Mike", actualEmployee1.Name);
            Assert.AreEqual(2, actualEmployee1.Addresses.Count());
            Assert.AreEqual("123 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 1).StreetAddress);
            Assert.AreEqual("Seattle", actualEmployee1.Addresses.First(wl => wl.Id == 1).City);
            Assert.AreEqual("WA", actualEmployee1.Addresses.First(wl => wl.Id == 1).State);
            Assert.AreEqual("678 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 3).StreetAddress);
            Assert.AreEqual("Orlando", actualEmployee1.Addresses.First(wl => wl.Id == 3).City);
            Assert.AreEqual("FL", actualEmployee1.Addresses.First(wl => wl.Id == 3).State);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.Addresses.Count());
            Assert.AreEqual("678 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 3).StreetAddress);
            Assert.AreEqual("Orlando", actualEmployee2.Addresses.First(wl => wl.Id == 3).City);
            Assert.AreEqual("FL", actualEmployee2.Addresses.First(wl => wl.Id == 3).State);
            Assert.AreEqual("910 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 4).StreetAddress);
            Assert.AreEqual("Atlanta", actualEmployee2.Addresses.First(wl => wl.Id == 4).City);
            Assert.AreEqual("GA", actualEmployee2.Addresses.First(wl => wl.Id == 4).State);
        }

        //[TestMethod]
        //public void Sync_IdViaRelation_ManyToManyPrimaryKeyFromForeignKeyNavigationProperty()
        //{
        //    var insertFields = new EFEmployee[]
        //    {
        //        new EFEmployee()
        //        {
        //            Name = "Mike",
        //            Addresses = new[]
        //            {
        //                new EFAddress() { StreetAddress = "123 fake st", City = "Seattle", State = "WA" },
        //                new EFAddress() { StreetAddress = "345 fake st", City = "Portland", State = "OR" }
        //            }
        //        },
        //        new EFEmployee()
        //        {
        //            Name = "Lester",
        //            Addresses = new[]
        //            {
        //                new EFAddress() { StreetAddress = "678 fake st", City = "Orlando", State = "FL" },
        //                new EFAddress() { StreetAddress = "910 fake st", City = "Atlanta", State = "GA" }
        //            }
        //        }
        //    };

        //    laborDbContext.Employee.AddRange(insertFields);
        //    laborDbContext.SaveChanges();

        //    this.monolithicRepository.SyncEmployeeWithAddressIdViaRelation(
        //            new Employee.SyncWithAddressIdViaRelationEF()
        //            {
        //                Id = 1,
        //                AddressIds = new[]
        //                {
        //                    1,
        //                    3
        //                }
        //            });

        //    laborDbContext.ChangeTracker.Clear();
        //    var actual = this.monolithicRepository.GetSyncManyToManyEmployeeWithAddresses();

        //    Assert.IsTrue(actual.Any(e => e.Name == "Mike"));
        //    Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
        //    var actualEmployee1 = actual.Single(e => e.Id == 1);
        //    Assert.AreEqual("Mike", actualEmployee1.Name);
        //    Assert.AreEqual(2, actualEmployee1.Addresses.Count());
        //    Assert.AreEqual("123 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 1).StreetAddress);
        //    Assert.AreEqual("Seattle", actualEmployee1.Addresses.First(wl => wl.Id == 1).City);
        //    Assert.AreEqual("WA", actualEmployee1.Addresses.First(wl => wl.Id == 1).State);
        //    Assert.AreEqual("678 fake st", actualEmployee1.Addresses.First(wl => wl.Id == 3).StreetAddress);
        //    Assert.AreEqual("Orlando", actualEmployee1.Addresses.First(wl => wl.Id == 3).City);
        //    Assert.AreEqual("FL", actualEmployee1.Addresses.First(wl => wl.Id == 3).State);
        //    var actualEmployee2 = actual.Single(e => e.Id == 2);
        //    Assert.AreEqual(2, actualEmployee2.Addresses.Count());
        //    Assert.AreEqual("678 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 3).StreetAddress);
        //    Assert.AreEqual("Orlando", actualEmployee2.Addresses.First(wl => wl.Id == 3).City);
        //    Assert.AreEqual("FL", actualEmployee2.Addresses.First(wl => wl.Id == 3).State);
        //    Assert.AreEqual("910 fake st", actualEmployee2.Addresses.First(wl => wl.Id == 4).StreetAddress);
        //    Assert.AreEqual("Atlanta", actualEmployee2.Addresses.First(wl => wl.Id == 4).City);
        //    Assert.AreEqual("GA", actualEmployee2.Addresses.First(wl => wl.Id == 4).State);
        //}


        [TestMethod]
        public void Sync_NestedNavigationProperties()
        {
            var insertFields = new EFEmployee[]
            {
                new EFEmployee()
                {
                    Name = "Mike",
                    Addresses = new[]
                    {
                        new EFAddress()
                        {
                            StreetAddress = "123 fake st", City = "Seattle", State = "WA",
                            Locations = new List<EFLocation>()
                            {
                                new EFLocation()
                                {
                                    Name = "Baseball Field"
                                }
                            }
                        },
                        new EFAddress()
                        {
                            StreetAddress = "345 fake st", City = "Portland", State = "OR",
                            Locations = new List<EFLocation>()
                            {
                                new EFLocation()
                                {
                                    Name = "Town Center"
                                }
                            }
                        }
                    }
                },
                new EFEmployee()
                {
                    Name = "Lester",
                    Addresses = new[]
                    {
                        new EFAddress() { StreetAddress = "678 fake st", City = "Orlando", State = "FL", 
                            Locations = new List<EFLocation>()
                            {
                                new EFLocation()
                                {
                                    Name = "Mall"
                                }
                            }},
                        new EFAddress()
                        {
                            StreetAddress = "910 fake st", City = "Atlanta", State = "GA",
                            Locations = new List<EFLocation>()
                            {
                                new EFLocation()
                                {
                                    Name = "City Hall"
                                }
                            }
                        }
                    }
                }
            };

            laborDbContext.Employee.AddRange(insertFields);
            laborDbContext.SaveChanges();

            this.monolithicRepository.SyncEmployeeWithAddressesAndLocations(
                    new Employee.SyncFieldsWithAddressesAndLocations()
                    {
                        Id = 1,
                        Name = "Kyle",
                        Addresses = new[]
                        {
                            new Address.UpsertWithLocation()
                            {
                                Id = 1, StreetAddress = "789 fake st", City = "Los Angeles", State = "CA",
                                Locations = new List<Location.Upsert>()
                                {
                                    new Location.Upsert()
                                    {
                                        Id = 1,
                                        Name = "Basketball Court"
                                    },
                                    new Location.Upsert()
                                    {
                                        Name = "Town Square"
                                    }
                                }
                            },
                            new Address.UpsertWithLocation()
                            {
                                StreetAddress = "2020 fake st", City = "New York", State = "NY",
                                Locations = new List<Location.Upsert>()
                                {
                                    new Location.Upsert()
                                    {
                                        Name = "Billiards Room"
                                    }
                                }
                            }
                        }
                    });
            
            var actual = this.monolithicRepository.GetSyncEmployeeWithAddressesAndLocations();

            Assert.IsFalse(actual.Any(e => e.Name == "Mike"));
            Assert.IsTrue(actual.Any(e => e.Name == "Lester"));
            var actualEmployee1 = actual.Single(e => e.Id == 1);
            Assert.AreEqual("Kyle", actualEmployee1.Name);
            Assert.AreEqual(2, actualEmployee1.Addresses.Count());
            Assert.AreEqual(2, actualEmployee1.Addresses.First(a => a.Id == 1).Locations.Count);
            Assert.AreEqual("Basketball Court", actualEmployee1.Addresses.First(a => a.Id == 1).Locations.First().Name);
            Assert.AreEqual("Town Square", actualEmployee1.Addresses.First(a => a.Id == 1).Locations.Last().Name);
            Assert.AreEqual(1, actualEmployee1.Addresses.First(a => a.Id == 5).Locations.Count);
            Assert.AreEqual("Billiards Room", actualEmployee1.Addresses.First(a => a.Id == 5).Locations.First().Name);
            var actualEmployee2 = actual.Single(e => e.Id == 2);
            Assert.AreEqual(2, actualEmployee2.Addresses.Count());
            Assert.AreEqual(1, actualEmployee2.Addresses.First(a => a.Id == 3).Locations.Count);
            Assert.AreEqual("Mall", actualEmployee2.Addresses.First(a => a.Id == 3).Locations.First().Name);
            Assert.AreEqual("City Hall", actualEmployee2.Addresses.First(a => a.Id == 4).Locations.First().Name);
        }

        [TestMethod]
        public void Sync_DeletesLeastDependentTablesFirst()
        {
            this.monolithicRepository.SyncAddressesWithLocationsWithWorkLogs(
                    new Address.SyncFieldsWithLocationsWithWorkLogs()
                    {
                        StreetAddress = "789 fake st", City = "Los Angeles", State = "CA",
                        Locations = new List<Location.UpsertWithWorkLogs>()
                        {
                            new Location.UpsertWithWorkLogs()
                            {
                                Name = "Mall",
                                WorkLogs = new List<WorkLog.UpsertFields>()
                                {
                                    new WorkLog.UpsertFields()
                                    {
                                        StartDate = new DateTime(2024, 01, 01),
                                        EndDate = new DateTime(2024, 02, 01)
                                    }
                                }
                            }
                        }
                    });

            
            this.monolithicRepository.SyncAddressesWithLocationsWithWorkLogs(
                    new Address.SyncFieldsWithLocationsWithWorkLogs()
                    {
                        Id = 1,
                        StreetAddress = "1011 fake st", City = "Charlotte", State = "NC",
                        Locations = new List<Location.UpsertWithWorkLogs>()
                        {
                            new Location.UpsertWithWorkLogs()
                            {
                                Name = "Postal Service",
                                WorkLogs = new List<WorkLog.UpsertFields>()
                                {
                                    new WorkLog.UpsertFields()
                                    {
                                        StartDate = new DateTime(2024, 03, 01),
                                        EndDate = new DateTime(2024, 04, 01)
                                    }
                                }
                            }
                        }
                    });
            
            var actual = this.monolithicRepository.GetSyncAddressesWithLocationsWithWorkLogs();

            Assert.AreEqual(1, actual.Count());
            var actualAddress = actual.Single();
            Assert.AreEqual(1, actualAddress.Locations.Count);
            Assert.AreEqual(2, actualAddress.Locations.Single().Id);
            Assert.AreEqual(1, actualAddress.Locations.Single().WorkLogs.Count);
            Assert.AreEqual(2, actualAddress.Locations.Single().WorkLogs.Single().Id);
        }

        [TestMethod]
        public void Sync_ProcessesDeleteOneRelationship()
        {
            this.monolithicRepository.SyncLocationWithAddress(
                    new Location.UpsertWithAddress()
                    {
                            Name = "Mall",
                            Address = new Address.UpsertFields()
                            {
                                StreetAddress = "789 fake st",
                                City = "Los Angeles",
                                State = "CA",
                            }
                    });
            
            var actual = this.monolithicRepository.GetSyncLocationWithAddress();

            Assert.AreEqual(1, actual.Count());
            var actualLocation = actual.Single();
            Assert.AreEqual(1, actualLocation.Address.Id);
        }

        [TestMethod]
        public void Sync_ProcessesNestedOneRelationship()
        {
            this.monolithicRepository.SyncWorkLogWithLocationWithAddress(
                new WorkLog.UpsertWithLocationWithAddress()
                {
                    StartDate = new DateTime(2024, 1, 1),
                    EndDate = new DateTime(2024, 2, 1),
                    Location = new Location.UpsertWithAddress()
                    {
                        Name = "Mall",
                        Address = new Address.UpsertFields()
                        {
                            StreetAddress = "789 fake st",
                            City = "Los Angeles",
                            State = "CA",
                        }
                    }
                });
            
            var actual = this.monolithicRepository.GetSyncWorkLogWithLocationWithAddress();

            Assert.AreEqual(1, actual.Count());
            var actualWorklog = actual.Single();
            Assert.AreEqual(1, actualWorklog.Location.Id);
            Assert.AreEqual(1, actualWorklog.Location.Address.Id);
        }

        [TestMethod]
        public void Sync_DoesNotDeleteNestedOneRelationship()
        {
            this.monolithicRepository.SyncWorkLogWithLocationWithAddress(
                new WorkLog.UpsertWithLocationWithAddress()
                {
                    StartDate = new DateTime(2024, 1, 1),
                    EndDate = new DateTime(2024, 2, 1),
                    Location = new Location.UpsertWithAddress()
                    {
                        Name = "Mall",
                        Address = new Address.UpsertFields()
                        {
                            StreetAddress = "789 fake st",
                            City = "Los Angeles",
                            State = "CA",
                        }
                    }
                });

            this.monolithicRepository.SyncWorkLogWithLocationWithAddress(
                new WorkLog.UpsertWithLocationWithAddress()
                {
                    Id = 1,
                    StartDate = new DateTime(2024, 2, 1),
                    EndDate = new DateTime(2024, 3, 1),
                    Location = new Location.UpsertWithAddress()
                    {
                        Name = "Mall 2",
                        Address = new Address.UpsertFields()
                        {
                            StreetAddress = "2789 fake st",
                            City = "2Los Angeles",
                            State = "2CA",
                        }
                    }
                });
            
            var actual = this.monolithicRepository.GetSyncWorkLogWithLocationWithAddress();
            
            Assert.AreEqual(1, actual.Count());
            var actualWorklog = actual.Single();
            Assert.AreEqual(2, actualWorklog.Location.Id);
            Assert.AreEqual(2, actualWorklog.Location.Address.Id);
            Assert.AreEqual(1, laborDbContext.WorkLog.Count());
            Assert.AreEqual(2, laborDbContext.Location.Count());
            Assert.AreEqual(2, laborDbContext.Address.Count());
        }

        [TestMethod]
        public void Sync_AllowsEmptyLists()
        {
            this.monolithicRepository.SyncAddressesWithLocationsWithWorkLogs(
                    new Address.SyncFieldsWithLocationsWithWorkLogs()
                    {
                        StreetAddress = "789 fake st", City = "Los Angeles", State = "CA",
                        Locations = new List<Location.UpsertWithWorkLogs>()
                        {
                            new Location.UpsertWithWorkLogs()
                            {
                                Name = "Mall",
                                WorkLogs = new List<WorkLog.UpsertFields>()
                                {
                                    new WorkLog.UpsertFields()
                                    {
                                        StartDate = new DateTime(2024, 01, 01),
                                        EndDate = new DateTime(2024, 02, 01)
                                    }
                                }
                            }
                        }
                    });

            
            this.monolithicRepository.SyncAddressesWithLocationsWithWorkLogs(
                    new Address.SyncFieldsWithLocationsWithWorkLogs()
                    {
                        Id = 1,
                        StreetAddress = "1011 fake st", City = "Charlotte", State = "NC",
                        Locations = new List<Location.UpsertWithWorkLogs>()
                        {
                            new Location.UpsertWithWorkLogs()
                            {
                                Name = "Postal Service",
                                WorkLogs = new List<WorkLog.UpsertFields>()
                                {
                                }
                            }
                        }
                    });
            
            var actual = this.monolithicRepository.GetSyncAddressesWithLocationsWithWorkLogs();

            Assert.AreEqual(1, actual.Count());
            var actualAddress = actual.Single();
            Assert.AreEqual(1, actualAddress.Locations.Count);
            Assert.AreEqual(2, actualAddress.Locations.Single().Id);
            Assert.AreEqual(0, actualAddress.Locations.Single().WorkLogs.Count);
        }

        [TestMethod]
        public void UpdateByKey_Single_InsertsRow()
        {
            laborDbContext.Employee.Add(new EFEmployee() { Name = "bill" });
            laborDbContext.SaveChanges();
            this.monolithicRepository.UpdateByKeyEmployeeViaMethodParams(1, "bob");

            var actual = laborDbContext.Employee.AsNoTracking().Single();

            Assert.AreEqual("bob", actual.Name);
        }

        [TestMethod]
        public void UpdateByKey_Multiple()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

            this.monolithicRepository.UpdateByKeyMultipleEmployeesWithWorkLogs(
                new Employee.UpdateByKeyFieldsWithWorkLogs[]
                {
                    new Employee.UpdateByKeyFieldsWithWorkLogs()
                    {
                        Id = 1,
                        Name = "Bob",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpdateByKeyFields()
                                {Id = 1, StartDate = new DateTime(2022, 9, 1), EndDate = new DateTime(2022, 9, 2)},
                            new WorkLog.UpdateByKeyFields()
                                {Id = 2, StartDate = new DateTime(2022, 10, 1), EndDate = new DateTime(2022, 10, 2)}
                        }
                    },
                    new Employee.UpdateByKeyFieldsWithWorkLogs()
                    {
                        Id = 2,
                        Name = "Joe",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpdateByKeyFields()
                                {Id = 3, StartDate = new DateTime(2022, 11, 1), EndDate = new DateTime(2022, 11, 2)},
                            new WorkLog.UpdateByKeyFields()
                                {Id = 4, StartDate = new DateTime(2022, 12, 1), EndDate = new DateTime(2022, 12, 2)}
                        }
                    }
                });

            var actual = laborDbContext.Employee.Include(e => e.WorkLogs).ToList();
            var actualEmployee1 = actual.First(e => e.Id == 1);
            Assert.AreEqual("Bob", actualEmployee1.Name);
            Assert.AreEqual(new DateTime(2022, 9, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).StartDate);
            Assert.AreEqual(new DateTime(2022, 9, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).EndDate);
            Assert.AreEqual(new DateTime(2022, 10, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 2).StartDate);
            Assert.AreEqual(new DateTime(2022, 10, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 2).EndDate);
            var actualEmployee2 = actual.First(e => e.Id == 2);
            Assert.AreEqual("Joe", actualEmployee2.Name);
            Assert.AreEqual(new DateTime(2022, 11, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).StartDate);
            Assert.AreEqual(new DateTime(2022, 11, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).EndDate);
            Assert.AreEqual(new DateTime(2022, 12, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).StartDate);
            Assert.AreEqual(new DateTime(2022, 12, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).EndDate);
        }

        [TestMethod]
        public void UpdateByKey_MovesNavigationProperty()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

            this.monolithicRepository.UpdateByKeyMultipleEmployeesWithWorkLogs(
                new Employee.UpdateByKeyFieldsWithWorkLogs[]
                {
                    new Employee.UpdateByKeyFieldsWithWorkLogs()
                    {
                        Id = 1,
                        Name = "Bob",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpdateByKeyFields()
                                {Id = 1, StartDate = new DateTime(2022, 9, 1), EndDate = new DateTime(2022, 9, 2)}
                        }
                    },
                    new Employee.UpdateByKeyFieldsWithWorkLogs()
                    {
                        Id = 2,
                        Name = "Joe",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpdateByKeyFields()
                                {Id = 3, StartDate = new DateTime(2022, 11, 1), EndDate = new DateTime(2022, 11, 2)},
                            new WorkLog.UpdateByKeyFields()
                                {Id = 4, StartDate = new DateTime(2022, 12, 1), EndDate = new DateTime(2022, 12, 2)},
                            new WorkLog.UpdateByKeyFields()
                                {Id = 2, StartDate = new DateTime(2022, 10, 1), EndDate = new DateTime(2022, 10, 2)}
                        }
                    }
                });

            var actual = laborDbContext.Employee.Include(e => e.WorkLogs).ToList();
            var actualEmployee1 = actual.First(e => e.Id == 1);
            Assert.AreEqual("Bob", actualEmployee1.Name);
            Assert.AreEqual(new DateTime(2022, 9, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).StartDate);
            Assert.AreEqual(new DateTime(2022, 9, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).EndDate);
            var actualEmployee2 = actual.First(e => e.Id == 2);
            Assert.AreEqual("Joe", actualEmployee2.Name);
            Assert.AreEqual(new DateTime(2022, 10, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 2).StartDate);
            Assert.AreEqual(new DateTime(2022, 10, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 2).EndDate);
            Assert.AreEqual(new DateTime(2022, 11, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).StartDate);
            Assert.AreEqual(new DateTime(2022, 11, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).EndDate);
            Assert.AreEqual(new DateTime(2022, 12, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).StartDate);
            Assert.AreEqual(new DateTime(2022, 12, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).EndDate);
        }

        [TestMethod]
        public void UpdateByKey_DoesNotDeleteNavigationProperty()
        {
            var insertFields = new Employee.InsertFieldsWithWorkLogs[]
            {
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Mike",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 1, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 2, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                },
                new Employee.InsertFieldsWithWorkLogs()
                {
                    Name = "Lester",
                    WorkLogs = new[]
                    {
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 3, 1), EndDate = new DateTime(2021, 1, 2)},
                        new WorkLog.DataFields()
                            {StartDate = new DateTime(2021, 4, 1), EndDate = new DateTime(2021, 2, 2)}
                    }
                }
            };
            this.monolithicRepository.InsertMultipleEmployeesWithWorkLogs(insertFields);

            this.monolithicRepository.UpdateByKeyMultipleEmployeesWithWorkLogs(
                new Employee.UpdateByKeyFieldsWithWorkLogs[]
                {
                    new Employee.UpdateByKeyFieldsWithWorkLogs()
                    {
                        Id = 1,
                        Name = "Bob",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpdateByKeyFields()
                                {Id = 1, StartDate = new DateTime(2022, 9, 1), EndDate = new DateTime(2022, 9, 2)}
                        }
                    },
                    new Employee.UpdateByKeyFieldsWithWorkLogs()
                    {
                        Id = 2,
                        Name = "Joe",
                        WorkLogs = new[]
                        {
                            new WorkLog.UpdateByKeyFields()
                                {Id = 3, StartDate = new DateTime(2022, 11, 1), EndDate = new DateTime(2022, 11, 2)},
                            new WorkLog.UpdateByKeyFields()
                                {Id = 4, StartDate = new DateTime(2022, 12, 1), EndDate = new DateTime(2022, 12, 2)}
                        }
                    }
                });

            var actual = laborDbContext.Employee.Include(e => e.WorkLogs).ToList();
            var actualEmployee1 = actual.First(e => e.Id == 1);
            Assert.AreEqual("Bob", actualEmployee1.Name);
            Assert.AreEqual(new DateTime(2022, 9, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).StartDate);
            Assert.AreEqual(new DateTime(2022, 9, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 1).EndDate);
            Assert.AreEqual(new DateTime(2021, 2, 1), actualEmployee1.WorkLogs.First(wl => wl.Id == 2).StartDate);
            Assert.AreEqual(new DateTime(2021, 2, 2), actualEmployee1.WorkLogs.First(wl => wl.Id == 2).EndDate);
            var actualEmployee2 = actual.First(e => e.Id == 2);
            Assert.AreEqual("Joe", actualEmployee2.Name);
            Assert.AreEqual(new DateTime(2022, 11, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).StartDate);
            Assert.AreEqual(new DateTime(2022, 11, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 3).EndDate);
            Assert.AreEqual(new DateTime(2022, 12, 1), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).StartDate);
            Assert.AreEqual(new DateTime(2022, 12, 2), actualEmployee2.WorkLogs.First(wl => wl.Id == 4).EndDate);
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

        //[TestMethod]
        //public void JoinTest()
        //{
        //    var workLog = new EFWorkLog() { StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1) };
        //    var employee = new EFEmployee() { Name = "Joe" };
        //    workLog.Employee = employee;

        //    var otherWorkLog = new EFWorkLog() { StartDate = DateTime.Today.AddDays(5), EndDate = DateTime.Today.AddDays(6) };
        //    var otherEmployee = new EFEmployee() { Name = "Bob" };
        //    otherWorkLog.Employee = otherEmployee;

        //    this.laborDbContext.WorkLog.Add(workLog);
        //    this.laborDbContext.WorkLog.Add(otherWorkLog);
        //    this.laborDbContext.SaveChanges();

        //    var efWorkLogs = this.laborDbContext.WorkLog.Include(wl => wl.Employee).ThenInclude(e  => e.Addresses).OrderBy(wl => wl.Employee.Addresses.Select(a => a.City)).Skip(5).ToList();

        //}

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
