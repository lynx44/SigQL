using System;
using System.Collections.Generic;
using System.Linq;

namespace SigQL.DependencyInjection
{
    /// <summary>
    /// A single service registration produced by a scan. <see cref="ServiceType"/> is what callers
    /// inject; <see cref="ImplementationType"/> is the type SigQL builds a proxy for. They differ
    /// only when an abstract repository class implements a repository interface, in which case the
    /// interface resolves to the proxy built for the class.
    /// </summary>
    internal class RepositoryRegistration
    {
        public RepositoryRegistration(Type serviceType, Type implementationType)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
        }

        public Type ServiceType { get; }
        public Type ImplementationType { get; }
        public bool IsForwarded => ServiceType != ImplementationType;
    }

    internal static class RepositoryDiscovery
    {
        /// <summary>
        /// Pairs each abstract repository class with the repository interfaces it implements, so
        /// resolving either one yields the same proxy, and returns the standalone interfaces as
        /// their own registrations.
        /// </summary>
        internal static IList<RepositoryRegistration> Discover(IEnumerable<Type> candidates)
        {
            var repositoryTypes = candidates.Where(RepositoryConventions.IsProxyable).Distinct().ToList();
            var abstractClasses = repositoryTypes.Where(t => t.IsClass).ToList();
            var interfaces = repositoryTypes.Where(t => t.IsInterface).ToList();

            var registrations = new List<RepositoryRegistration>();
            var implementedInterfaces = new Dictionary<Type, Type>();

            foreach (var abstractClass in abstractClasses)
            {
                registrations.Add(new RepositoryRegistration(abstractClass, abstractClass));

                foreach (var implemented in abstractClass.GetInterfaces().Where(interfaces.Contains))
                {
                    if (implementedInterfaces.TryGetValue(implemented, out var alreadyMappedTo))
                    {
                        throw new InvalidOperationException(
                            $"Both \"{alreadyMappedTo.FullName}\" and \"{abstractClass.FullName}\" implement the repository interface \"{implemented.FullName}\", so SigQL cannot tell which one \"{implemented.Name}\" should resolve to. " +
                            "Register the interface explicitly with AddRepository, or exclude one of the classes from the scan.");
                    }

                    implementedInterfaces.Add(implemented, abstractClass);
                    registrations.Add(new RepositoryRegistration(implemented, abstractClass));
                }
            }

            registrations.AddRange(interfaces
                .Where(i => !implementedInterfaces.ContainsKey(i))
                .Select(i => new RepositoryRegistration(i, i)));

            return registrations;
        }
    }
}
