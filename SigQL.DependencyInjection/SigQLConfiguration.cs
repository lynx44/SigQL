using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace SigQL.DependencyInjection
{
    /// <summary>
    /// The configuration accumulated by <see cref="SigQLBuilder"/> during startup. It is registered
    /// as a singleton so repeated AddSigQL calls compose instead of competing, and it is read when
    /// the <see cref="RepositoryBuilder"/> is first resolved — after all configuration has run, so
    /// the order of the builder calls does not matter.
    /// </summary>
    internal class SigQLConfiguration
    {
        public Func<IServiceProvider, Action<PreparedSqlStatement>> SqlLoggerFactory { get; set; }

        public IList<Action<RepositoryBuilderOptions, IServiceProvider>> OptionsConfigurators { get; } =
            new List<Action<RepositoryBuilderOptions, IServiceProvider>>();

        public ServiceLifetime RepositoryLifetime { get; set; } = ServiceLifetime.Scoped;

        public RepositoryBuilderOptions BuildOptions(IServiceProvider serviceProvider)
        {
            var options = new RepositoryBuilderOptions()
            {
                // a fallback for code that resolves the RepositoryBuilder itself and calls
                // Build<T>() without a resolver; registered repositories pass their own scope
                ServiceResolver = serviceProvider.GetService
            };

            foreach (var configure in OptionsConfigurators)
            {
                configure(options, serviceProvider);
            }

            return options;
        }
    }
}
