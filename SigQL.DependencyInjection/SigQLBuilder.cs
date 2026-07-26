using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SigQL.Schema;
using SigQL.SqlServer;

namespace SigQL.DependencyInjection
{
    /// <summary>
    /// Configures the SigQL services and repository registrations added to an
    /// <see cref="IServiceCollection"/>. Obtained from
    /// <c>services.AddSigQL()</c>.
    /// </summary>
    public class SigQLBuilder
    {
        private readonly SigQLConfiguration configuration;

        internal SigQLBuilder(IServiceCollection services, SigQLConfiguration configuration)
        {
            this.Services = services;
            this.configuration = configuration;
        }

        /// <summary>
        /// The service collection being configured, for registrations SigQL does not cover.
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        /// Reads the schema from, and runs queries against, the given SQL Server connection string.
        /// Registers <see cref="IDatabaseConfiguration"/> and <see cref="IQueryExecutor"/> as
        /// singletons. The schema is read the first time a repository is resolved, not at startup.
        /// </summary>
        public SigQLBuilder UseSqlServer(string connectionString)
        {
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            return UseSqlServer(sp => connectionString);
        }

        /// <summary>
        /// SQL Server, with the connection string resolved from the service provider — for example
        /// from IConfiguration.
        /// </summary>
        public SigQLBuilder UseSqlServer(Func<IServiceProvider, string> connectionStringFactory)
        {
            if (connectionStringFactory == null) throw new ArgumentNullException(nameof(connectionStringFactory));

            return UseDatabase(
                sp => new SqlDatabaseConfiguration(connectionStringFactory(sp)),
                sp =>
                {
                    var connectionString = connectionStringFactory(sp);
                    return new SqlQueryExecutor(() => new SqlConnection(connectionString));
                });
        }

        /// <summary>
        /// Uses an explicitly constructed schema and query executor, for databases or connection
        /// handling that <see cref="UseSqlServer(string)"/> does not cover.
        /// </summary>
        public SigQLBuilder UseDatabase(
            Func<IServiceProvider, IDatabaseConfiguration> databaseConfigurationFactory,
            Func<IServiceProvider, IQueryExecutor> queryExecutorFactory)
        {
            if (databaseConfigurationFactory == null) throw new ArgumentNullException(nameof(databaseConfigurationFactory));
            if (queryExecutorFactory == null) throw new ArgumentNullException(nameof(queryExecutorFactory));

            Services.TryAddSingleton(databaseConfigurationFactory);
            Services.TryAddSingleton(queryExecutorFactory);

            return this;
        }

        /// <summary>
        /// Logs every SQL statement SigQL executes.
        /// </summary>
        public SigQLBuilder LogSqlWith(Action<PreparedSqlStatement> sqlLogger)
        {
            if (sqlLogger == null) throw new ArgumentNullException(nameof(sqlLogger));

            return LogSqlWith(sp => sqlLogger);
        }

        /// <summary>
        /// Logs every SQL statement SigQL executes, using a logger resolved from the service
        /// provider.
        /// </summary>
        public SigQLBuilder LogSqlWith(Func<IServiceProvider, Action<PreparedSqlStatement>> sqlLoggerFactory)
        {
            configuration.SqlLoggerFactory = sqlLoggerFactory ?? throw new ArgumentNullException(nameof(sqlLoggerFactory));

            return this;
        }

        /// <summary>
        /// Configures the options the repositories are built with — the pluralization helper or the
        /// foreign key resolver, for example.
        /// </summary>
        public SigQLBuilder ConfigureOptions(Action<RepositoryBuilderOptions> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            return ConfigureOptions((options, sp) => configure(options));
        }

        /// <summary>
        /// Configures the repository options with access to the service provider, for options that
        /// depend on other services:
        /// <c>(options, sp) => options.ForeignKeyResolver = new ConventionForeignKeyResolver(sp.GetRequiredService&lt;IDatabaseConfiguration&gt;())</c>.
        /// </summary>
        public SigQLBuilder ConfigureOptions(Action<RepositoryBuilderOptions, IServiceProvider> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            configuration.OptionsConfigurators.Add(configure);

            return this;
        }

        /// <summary>
        /// The lifetime used by repository registrations that follow. Scoped by default, so that
        /// [Inject] members receive the services of the request they are resolved in.
        /// </summary>
        public SigQLBuilder WithLifetime(ServiceLifetime lifetime)
        {
            configuration.RepositoryLifetime = lifetime;

            return this;
        }

        /// <summary>
        /// Registers a single repository interface or abstract class.
        /// </summary>
        public SigQLBuilder AddRepository<TRepository>(ServiceLifetime? lifetime = null)
            where TRepository : class
        {
            return AddRepository(typeof(TRepository), lifetime);
        }

        /// <summary>
        /// Registers a single repository interface or abstract class.
        /// </summary>
        public SigQLBuilder AddRepository(Type repositoryType, ServiceLifetime? lifetime = null)
        {
            if (repositoryType == null) throw new ArgumentNullException(nameof(repositoryType));

            if (!RepositoryConventions.IsProxyable(repositoryType))
            {
                throw new ArgumentException(
                    $"\"{repositoryType.FullName}\" cannot be registered as a SigQL repository. Repositories must be public interfaces or public abstract classes.",
                    nameof(repositoryType));
            }

            return Register(RepositoryDiscovery.Discover(new[] { repositoryType }), lifetime);
        }

        /// <summary>
        /// Registers each of the given repository interfaces and abstract classes, pairing any
        /// abstract class with the repository interfaces it implements.
        /// </summary>
        public SigQLBuilder AddRepositories(params Type[] repositoryTypes)
        {
            return AddRepositories(repositoryTypes, null);
        }

        /// <summary>
        /// Registers each of the given repository interfaces and abstract classes, pairing any
        /// abstract class with the repository interfaces it implements.
        /// </summary>
        public SigQLBuilder AddRepositories(IEnumerable<Type> repositoryTypes, ServiceLifetime? lifetime = null)
        {
            if (repositoryTypes == null) throw new ArgumentNullException(nameof(repositoryTypes));

            var types = repositoryTypes.ToList();
            var invalid = types.FirstOrDefault(t => !RepositoryConventions.IsProxyable(t));
            if (invalid != null)
            {
                throw new ArgumentException(
                    $"\"{invalid.FullName}\" cannot be registered as a SigQL repository. Repositories must be public interfaces or public abstract classes.",
                    nameof(repositoryTypes));
            }

            return Register(RepositoryDiscovery.Discover(types), lifetime);
        }

        /// <summary>
        /// Registers every repository in the assembly that contains <typeparamref name="T"/>.
        /// </summary>
        /// <param name="filter">
        /// Replaces the default convention (implements IRepository, or a name ending in
        /// "Repository"). Interfaces and abstract classes are the only candidates either way.
        /// </param>
        public SigQLBuilder AddRepositoriesFromAssemblyContaining<T>(
            Func<Type, bool> filter = null,
            ServiceLifetime? lifetime = null)
        {
            return AddRepositoriesFromAssembly(typeof(T).Assembly, filter, lifetime);
        }

        /// <summary>
        /// Registers every repository in the assembly.
        /// </summary>
        /// <param name="filter">
        /// Replaces the default convention (implements IRepository, or a name ending in
        /// "Repository"). Interfaces and abstract classes are the only candidates either way.
        /// </param>
        public SigQLBuilder AddRepositoriesFromAssembly(
            Assembly assembly,
            Func<Type, bool> filter = null,
            ServiceLifetime? lifetime = null)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var matches = RepositoryConventions.GetLoadableTypes(assembly)
                .Where(RepositoryConventions.IsProxyable)
                .Where(filter ?? RepositoryConventions.IsRepository);

            return Register(RepositoryDiscovery.Discover(matches), lifetime);
        }

        /// <summary>
        /// Registers every repository declared in the same namespace as <typeparamref name="T"/>.
        /// </summary>
        public SigQLBuilder AddRepositoriesFromNamespaceOf<T>(ServiceLifetime? lifetime = null)
        {
            var repositoryNamespace = typeof(T).Namespace;

            return AddRepositoriesFromAssembly(
                typeof(T).Assembly,
                t => t.Namespace == repositoryNamespace && RepositoryConventions.IsRepository(t),
                lifetime);
        }

        private SigQLBuilder Register(IEnumerable<RepositoryRegistration> registrations, ServiceLifetime? lifetime)
        {
            var repositoryLifetime = lifetime ?? configuration.RepositoryLifetime;

            foreach (var registration in registrations)
            {
                var implementationType = registration.ImplementationType;

                // an existing registration wins, so a repository can be replaced with a hand
                // written implementation without removing it from the scan
                Services.TryAdd(new ServiceDescriptor(
                    registration.ServiceType,
                    registration.IsForwarded
                        ? (Func<IServiceProvider, object>) (sp => sp.GetRequiredService(implementationType))
                        : (sp => BuildRepository(sp, implementationType)),
                    repositoryLifetime));
            }

            return this;
        }

        private static object BuildRepository(IServiceProvider serviceProvider, Type repositoryType)
        {
            // the provider is passed through as the service resolver, so [Inject] members and
            // abstract class constructor arguments come from the same scope the repository
            // was resolved in
            return serviceProvider.GetRequiredService<RepositoryBuilder>()
                .Build(repositoryType, serviceProvider.GetService);
        }
    }
}
