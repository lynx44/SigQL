using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.SqlServer.Tests.Data;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.SqlServer.Tests
{
    /// <summary>
    /// Registration through Microsoft.Extensions.DependencyInjection, running against a real
    /// database — the Startup.cs path a consumer of SigQL.DependencyInjection writes.
    /// </summary>
    [TestClass]
    public class DependencyInjectionTests
    {
        private IDbConnection laborDbConnection;
        private LaborDbContext laborDbContext;

        [TestInitialize]
        public void Setup()
        {
            laborDbConnection = TestSettings.LaborDbConnection;
            DatabaseHelpers.DropAllObjects(laborDbConnection as SqlConnection);

            laborDbContext = new LaborDbContext();
            laborDbContext.Database.Migrate();
        }

        private IList<EFWorkLog> InsertWorkLogs(int count)
        {
            var workLogs = Enumerable.Range(1, count).Select(i => new EFWorkLog() { }).ToList();
            laborDbContext.WorkLog.AddRange(workLogs);
            laborDbContext.SaveChanges();
            return workLogs;
        }

        private static ServiceCollection NewServices()
        {
            return new ServiceCollection();
        }

        [TestMethod]
        public void RegisteredRepository_RunsAGeneratedQuery()
        {
            var expected = InsertWorkLogs(3);

            var services = NewServices();
            services.AddSigQL(TestSettings.LaborConnectionString)
                .AddRepositoriesFromAssemblyContaining<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<IWorkLogRepository>();

            CollectionAssert.AreEquivalent(
                expected.Select(w => w.Id).ToList(),
                repository.GetAllIds().Select(w => w.Id).ToList());
        }

        [TestMethod]
        public void ConfigureDelegateOverload_RegistersEverythingInOneCall()
        {
            InsertWorkLogs(2);

            var services = NewServices();
            services.AddSigQL(sigql => sigql
                .UseSqlServer(TestSettings.LaborConnectionString)
                .AddRepositoriesFromNamespaceOf<IWorkLogRepository>());

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.AreEqual(2, scope.ServiceProvider.GetRequiredService<IWorkLogRepository>().GetAllIds().Count());
        }

        [TestMethod]
        public void InterfaceBackedByAnAbstractClass_UsesTheCustomImplementation()
        {
            InsertWorkLogs(4);

            var services = NewServices();
            services.AddSigQL(TestSettings.LaborConnectionString)
                .AddRepositoriesFromAssemblyContaining<IWorkLogInterfacedRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<IWorkLogInterfacedRepository>();

            // the custom body on the abstract class, not a generated query
            Assert.AreEqual("custom implementation", repository.Describe());
            // a custom body composing a generated query
            Assert.AreEqual(4, repository.CountAllIds());
        }

        [TestMethod]
        public void InterfaceBackedByAnAbstractClass_RunsCustomSqlWithTheInjectedMaterializer()
        {
            var workLogs = InsertWorkLogs(5);
            var thirdId = workLogs.Select(w => w.Id).OrderBy(id => id).ElementAt(2);

            var services = NewServices();
            services.AddSigQL(TestSettings.LaborConnectionString)
                .AddRepositoriesFromAssemblyContaining<IWorkLogInterfacedRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<IWorkLogInterfacedRepository>();

            CollectionAssert.AreEquivalent(
                workLogs.Select(w => w.Id).Where(id => id > thirdId).ToList(),
                repository.GetIdsAbove(thirdId).Select(w => w.Id).ToList());
        }

        [TestMethod]
        public void InjectedService_IsResolvedFromTheContainer()
        {
            InsertWorkLogs(3);

            var services = NewServices();
            services.AddScoped<IWorkLogSummarizer, WorkLogSummarizer>();
            services.AddSigQL(TestSettings.LaborConnectionString)
                .AddRepositoriesFromAssemblyContaining<ICustomImplementationRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var repository = scope.ServiceProvider.GetRequiredService<ICustomImplementationRepository>();

            Assert.AreEqual("summarized 3", repository.SummarizeAll());
            Assert.AreEqual("summarized 3", repository.SummarizeViaProperty());
        }

        [TestMethod]
        public void LogSqlWith_ReceivesTheExecutedStatements()
        {
            InsertWorkLogs(1);

            var statements = new List<PreparedSqlStatement>();
            var services = NewServices();
            services.AddSigQL(TestSettings.LaborConnectionString)
                .LogSqlWith(statement => statements.Add(statement))
                .AddRepository<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            scope.ServiceProvider.GetRequiredService<IWorkLogRepository>().GetAllIds().ToList();

            Assert.AreEqual(1, statements.Count);
            StringAssert.Contains(statements.Single().CommandText, "WorkLog");
        }

        [TestMethod]
        public void ConnectionStringFromTheServiceProvider_IsUsed()
        {
            InsertWorkLogs(2);

            var services = NewServices();
            services.AddSingleton(new ConnectionSettings() { ConnectionString = TestSettings.LaborConnectionString });
            services.AddSigQL()
                .UseSqlServer(sp => sp.GetRequiredService<ConnectionSettings>().ConnectionString)
                .AddRepository<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.AreEqual(2, scope.ServiceProvider.GetRequiredService<IWorkLogRepository>().GetAllIds().Count());
        }

        [TestMethod]
        public void SchemaIsReadOnce_AndSharedAcrossScopes()
        {
            InsertWorkLogs(1);

            var services = NewServices();
            services.AddSigQL(TestSettings.LaborConnectionString)
                .AddRepository<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<IWorkLogRepository>().GetAllIds().ToList();
            }

            using (var scope = provider.CreateScope())
            {
                Assert.AreEqual(1, scope.ServiceProvider.GetRequiredService<IWorkLogRepository>().GetAllIds().Count());
            }

            Assert.AreSame(
                provider.GetRequiredService<Schema.IDatabaseConfiguration>(),
                provider.GetRequiredService<Schema.IDatabaseConfiguration>());
        }

        public class ConnectionSettings
        {
            public string ConnectionString { get; set; }
        }
    }
}
