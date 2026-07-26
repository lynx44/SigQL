using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SigQL;
using SigQL.DependencyInjection;
using SigQL.Schema;

// the conventional namespace for registration extensions, so Startup.cs needs no extra using
namespace Microsoft.Extensions.DependencyInjection
{
    public static class SigQLServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the SigQL services. Follow it with UseSqlServer and one of the AddRepositories
        /// methods:
        /// <code>
        /// services.AddSigQL(connectionString)
        ///         .AddRepositoriesFromAssemblyContaining&lt;IEmployeeRepository&gt;();
        /// </code>
        /// Calling it more than once returns a builder over the same configuration.
        /// </summary>
        public static SigQLBuilder AddSigQL(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            var configuration = services
                .FirstOrDefault(d => d.ServiceType == typeof(SigQLConfiguration))
                ?.ImplementationInstance as SigQLConfiguration;

            if (configuration == null)
            {
                configuration = new SigQLConfiguration();
                services.AddSingleton(configuration);
                AddCoreServices(services, configuration);
            }

            return new SigQLBuilder(services, configuration);
        }

        /// <summary>
        /// Adds the SigQL services for a SQL Server connection string. Shorthand for
        /// <c>AddSigQL().UseSqlServer(connectionString)</c>.
        /// </summary>
        public static SigQLBuilder AddSigQL(this IServiceCollection services, string connectionString)
        {
            return services.AddSigQL().UseSqlServer(connectionString);
        }

        /// <summary>
        /// Adds the SigQL services and configures them in one call:
        /// <code>
        /// services.AddSigQL(sigql => sigql
        ///     .UseSqlServer(connectionString)
        ///     .AddRepositoriesFromAssemblyContaining&lt;IEmployeeRepository&gt;());
        /// </code>
        /// </summary>
        public static IServiceCollection AddSigQL(this IServiceCollection services, Action<SigQLBuilder> configure)
        {
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            configure(services.AddSigQL());

            return services;
        }

        private static void AddCoreServices(IServiceCollection services, SigQLConfiguration configuration)
        {
            services.TryAddSingleton<IQueryMaterializer>(sp =>
                new AdoMaterializer(sp.GetRequiredService<IQueryExecutor>(), configuration.SqlLoggerFactory?.Invoke(sp)));

            services.TryAddSingleton(sp =>
            {
                var queryExecutor = sp.GetService<IQueryExecutor>();
                var databaseConfiguration = sp.GetService<IDatabaseConfiguration>();
                if (queryExecutor == null || databaseConfiguration == null)
                {
                    throw new InvalidOperationException(
                        "SigQL has no database configured. Call UseSqlServer(connectionString) on the builder returned by AddSigQL, " +
                        $"or register {nameof(IQueryExecutor)} and {nameof(IDatabaseConfiguration)} yourself.");
                }

                return new RepositoryBuilder(
                    queryExecutor,
                    databaseConfiguration,
                    sp.GetRequiredService<IQueryMaterializer>(),
                    configuration.BuildOptions(sp),
                    configuration.SqlLoggerFactory?.Invoke(sp));
            });
        }
    }
}
