using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.DependencyInjection.Tests.Infrastructure;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.DependencyInjection.Tests
{
    /// <summary>
    /// An abstract repository class that implements an interface. Custom method bodies live on the
    /// class, so the interface has to resolve to the class's proxy — an interface proxy would have
    /// no implementation to call, and SigQL would generate a query for the custom method instead.
    /// </summary>
    [TestClass]
    public class AbstractClassInterfaceTests
    {
        private IServiceCollection services;

        [TestInitialize]
        public void Setup()
        {
            services = new ServiceCollection();
        }

        private SigQLBuilder AddSigQL()
        {
            return services.AddSigQL()
                .UseDatabase(sp => new StubDatabaseConfiguration(), sp => new StubQueryExecutor());
        }

        [TestMethod]
        public void ResolvingTheInterface_ReturnsTheAbstractClassProxy()
        {
            AddSigQL().AddRepositories(typeof(IWorkLogInterfacedRepository), typeof(WorkLogInterfacedRepository));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.IsInstanceOfType(
                scope.ServiceProvider.GetService<IWorkLogInterfacedRepository>(),
                typeof(WorkLogInterfacedRepository));
        }

        [TestMethod]
        public void ResolvingTheInterface_RunsTheCustomImplementationOnTheClass()
        {
            AddSigQL().AddRepositories(typeof(IWorkLogInterfacedRepository), typeof(WorkLogInterfacedRepository));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var repository = scope.ServiceProvider.GetService<IWorkLogInterfacedRepository>();

            Assert.AreEqual("custom implementation", repository.Describe());
        }

        [TestMethod]
        public void TheInterfaceAndTheClass_ResolveToTheSameInstance()
        {
            AddSigQL().AddRepositories(typeof(IWorkLogInterfacedRepository), typeof(WorkLogInterfacedRepository));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.AreSame(
                scope.ServiceProvider.GetService<IWorkLogInterfacedRepository>(),
                scope.ServiceProvider.GetService<WorkLogInterfacedRepository>());
        }

        [TestMethod]
        public void AssemblyScan_PairsTheInterfaceWithItsAbstractClass()
        {
            AddSigQL().AddRepositoriesFromAssemblyContaining<IWorkLogInterfacedRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var repository = scope.ServiceProvider.GetService<IWorkLogInterfacedRepository>();

            Assert.IsInstanceOfType(repository, typeof(WorkLogInterfacedRepository));
            Assert.AreEqual("custom implementation", repository.Describe());
        }

        [TestMethod]
        public void RegisteringTheInterfaceAlone_BuildsAnInterfaceProxy()
        {
            // without the class in the registration there is no implementation to run, which is
            // why AddRepositories is the way to register a paired interface
            AddSigQL().AddRepository<IWorkLogInterfacedRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var repository = scope.ServiceProvider.GetService<IWorkLogInterfacedRepository>();

            Assert.IsNotInstanceOfType(repository, typeof(WorkLogInterfacedRepository));
        }
    }
}
