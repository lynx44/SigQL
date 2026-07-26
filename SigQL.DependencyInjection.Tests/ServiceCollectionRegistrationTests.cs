using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SigQL.DependencyInjection.Tests.Infrastructure;
using SigQL.Schema;
using SigQL.Tests.Common.Databases.Labor;

namespace SigQL.DependencyInjection.Tests
{
    /// <summary>
    /// Registration and resolution behavior. Proxies are built without contacting a database, so
    /// these run against stub services; queries themselves are covered by the integration tests in
    /// SigQL.SqlServer.Tests.
    /// </summary>
    [TestClass]
    public class ServiceCollectionRegistrationTests
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
        public void AddSigQL_RegistersTheRepositoryBuilder()
        {
            AddSigQL();

            using var provider = services.BuildServiceProvider();

            Assert.IsNotNull(provider.GetService<RepositoryBuilder>());
        }

        [TestMethod]
        public void AddSigQL_RegistersTheMaterializer_SoCustomMethodsCanInjectIt()
        {
            AddSigQL();

            using var provider = services.BuildServiceProvider();

            Assert.IsInstanceOfType(provider.GetService<IQueryMaterializer>(), typeof(AdoMaterializer));
        }

        [TestMethod]
        public void AddRepository_ResolvesAProxyForAnInterface()
        {
            AddSigQL().AddRepository<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();

            var repository = provider.GetService<IWorkLogRepository>();

            Assert.IsNotNull(repository);
            Assert.IsInstanceOfType(repository, typeof(IWorkLogRepository));
        }

        [TestMethod]
        public void AddRepository_ResolvesAProxyForAnAbstractClass()
        {
            AddSigQL().AddRepository<AbstractRepository>();

            using var provider = services.BuildServiceProvider();

            Assert.IsInstanceOfType(provider.GetService<AbstractRepository>(), typeof(AbstractRepository));
        }

        [TestMethod]
        public void AddRepository_RejectsAConcreteClass()
        {
            var builder = AddSigQL();

            var exception = Assert.ThrowsException<ArgumentException>(() => builder.AddRepository<ConcreteRepository>());

            StringAssert.Contains(exception.Message, "interfaces or public abstract classes");
        }

        [TestMethod]
        public void AddRepositoriesFromAssemblyContaining_RegistersEveryRepositoryByConvention()
        {
            AddSigQL().AddRepositoriesFromAssemblyContaining<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.IsNotNull(scope.ServiceProvider.GetService<IWorkLogRepository>());
            Assert.IsNotNull(scope.ServiceProvider.GetService<IMonolithicRepository>());
            Assert.IsNotNull(scope.ServiceProvider.GetService<ICustomImplementationRepository>());
            Assert.IsNotNull(scope.ServiceProvider.GetService<AbstractRepository>());
        }

        [TestMethod]
        public void AddRepositoriesFromAssemblyContaining_RegistersInterfacesMarkedWithIRepository()
        {
            AddSigQL().AddRepositoriesFromAssemblyContaining<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.IsNotNull(scope.ServiceProvider.GetService<IWorkLogRepository_IRepository>());
        }

        [TestMethod]
        public void AddRepositoriesFromAssemblyContaining_SkipsTypesThatCannotBeProxied()
        {
            AddSigQL().AddRepositoriesFromAssemblyContaining<IWorkLogRepository>();

            Assert.IsFalse(services.Any(d => d.ServiceType == typeof(WorkLogSummarizer)));
            Assert.IsFalse(services.Any(d => d.ServiceType == typeof(WorkLog)));
        }

        [TestMethod]
        public void AddRepositoriesFromAssembly_HonorsACustomFilter()
        {
            AddSigQL().AddRepositoriesFromAssembly(
                typeof(IWorkLogRepository).Assembly,
                t => t == typeof(IWorkLogRepository));

            Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IWorkLogRepository)));
            Assert.IsFalse(services.Any(d => d.ServiceType == typeof(IMonolithicRepository)));
        }

        [TestMethod]
        public void AddRepositoriesFromNamespaceOf_RegistersOnlyThatNamespace()
        {
            AddSigQL().AddRepositoriesFromNamespaceOf<IWorkLogRepository>();

            Assert.IsTrue(services.Any(d => d.ServiceType == typeof(IWorkLogRepository)));
            Assert.IsFalse(services.Any(d => d.ServiceType == typeof(IScopedTagRepository)));
        }

        [TestMethod]
        public void AddRepositories_ThrowsWhenTwoAbstractClassesImplementTheSameInterface()
        {
            var builder = AddSigQL();

            var exception = Assert.ThrowsException<InvalidOperationException>(() =>
                builder.AddRepositories(typeof(IAmbiguousRepository), typeof(FirstAmbiguousRepository), typeof(SecondAmbiguousRepository)));

            StringAssert.Contains(exception.Message, nameof(IAmbiguousRepository));
        }

        [TestMethod]
        public void ExistingRegistration_IsNotReplacedByTheScan()
        {
            services.AddScoped<IReplaceableRepository, HandWrittenReplaceableRepository>();

            AddSigQL().AddRepositoriesFromAssemblyContaining<IReplaceableRepository>(
                t => t == typeof(IReplaceableRepository));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.AreEqual("hand written", scope.ServiceProvider.GetService<IReplaceableRepository>().Describe());
        }

        [TestMethod]
        public void Repositories_AreScopedByDefault()
        {
            AddSigQL().AddRepository<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var firstScope = provider.CreateScope();
            using var secondScope = provider.CreateScope();

            var first = firstScope.ServiceProvider.GetService<IWorkLogRepository>();

            Assert.AreSame(first, firstScope.ServiceProvider.GetService<IWorkLogRepository>());
            Assert.AreNotSame(first, secondScope.ServiceProvider.GetService<IWorkLogRepository>());
        }

        [TestMethod]
        public void WithLifetime_ChangesTheLifetimeOfSubsequentRegistrations()
        {
            AddSigQL()
                .WithLifetime(ServiceLifetime.Singleton)
                .AddRepository<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var firstScope = provider.CreateScope();
            using var secondScope = provider.CreateScope();

            Assert.AreSame(
                firstScope.ServiceProvider.GetService<IWorkLogRepository>(),
                secondScope.ServiceProvider.GetService<IWorkLogRepository>());
        }

        [TestMethod]
        public void AddRepository_AcceptsAPerRegistrationLifetime()
        {
            AddSigQL().AddRepository<IWorkLogRepository>(ServiceLifetime.Transient);

            using var provider = services.BuildServiceProvider();

            Assert.AreNotSame(
                provider.GetService<IWorkLogRepository>(),
                provider.GetService<IWorkLogRepository>());
        }

        [TestMethod]
        public void InjectedServices_ComeFromTheResolvingScope()
        {
            services.AddScoped<ITagProvider, TagProvider>();
            AddSigQL().AddRepository<IScopedTagRepository>();

            using var provider = services.BuildServiceProvider();
            using var firstScope = provider.CreateScope();
            using var secondScope = provider.CreateScope();

            var firstTag = firstScope.ServiceProvider.GetService<IScopedTagRepository>().GetTag();
            var secondTag = secondScope.ServiceProvider.GetService<IScopedTagRepository>().GetTag();

            Assert.AreEqual(firstScope.ServiceProvider.GetService<ITagProvider>().Tag, firstTag);
            Assert.AreNotEqual(firstTag, secondTag);
        }

        [TestMethod]
        public void ResolvingARepository_WithoutADatabase_ExplainsWhatIsMissing()
        {
            services.AddSigQL().AddRepository<IWorkLogRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var exception = Assert.ThrowsException<InvalidOperationException>(
                () => scope.ServiceProvider.GetService<IWorkLogRepository>());

            StringAssert.Contains(exception.Message, "UseSqlServer");
        }

        [TestMethod]
        public void ConfigureOptions_IsAppliedWhenTheRepositoryBuilderIsCreated()
        {
            IDatabaseConfiguration configurationSeenByOptions = null;

            AddSigQL().ConfigureOptions((options, sp) =>
            {
                configurationSeenByOptions = sp.GetService<IDatabaseConfiguration>();
                options.ForeignKeyResolver = new ConventionForeignKeyResolver(configurationSeenByOptions);
            });

            using var provider = services.BuildServiceProvider();
            provider.GetService<RepositoryBuilder>();

            Assert.IsInstanceOfType(configurationSeenByOptions, typeof(StubDatabaseConfiguration));
        }

        [TestMethod]
        public void ConfigureOptions_RunsRegardlessOfTheOrderItWasCalledIn()
        {
            var configured = false;

            services.AddSigQL().AddRepository<IWorkLogRepository>();
            services.AddSigQL()
                .UseDatabase(sp => new StubDatabaseConfiguration(), sp => new StubQueryExecutor())
                .ConfigureOptions(options => configured = true);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetService<IWorkLogRepository>();

            Assert.IsTrue(configured);
        }

        [TestMethod]
        public void AddSigQL_WithAConfigureDelegate_ReturnsTheServiceCollection()
        {
            var returned = services.AddSigQL(sigql => sigql
                .UseDatabase(sp => new StubDatabaseConfiguration(), sp => new StubQueryExecutor())
                .AddRepository<IWorkLogRepository>());

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();

            Assert.AreSame(services, returned);
            Assert.IsNotNull(scope.ServiceProvider.GetService<IWorkLogRepository>());
        }
    }
}
