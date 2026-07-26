using System;
using System.Reflection;
using SigQL.Types;

namespace SigQL.DependencyInjection
{
    /// <summary>
    /// The rules used to decide which types in an assembly are SigQL repositories.
    /// </summary>
    public static class RepositoryConventions
    {
        /// <summary>
        /// True when SigQL is able to build a proxy for this type at all: a public interface, or a
        /// public abstract class. Concrete classes are excluded — SigQL has nothing to generate for
        /// them, and proxying one would silently intercept a working implementation.
        /// </summary>
        public static bool IsProxyable(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (!type.IsPublic && !type.IsNestedPublic)
            {
                return false;
            }

            if (type.IsGenericTypeDefinition)
            {
                return false;
            }

            // the marker interfaces themselves are not repositories
            if (type == typeof(IRepository) ||
                (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRepository<>)))
            {
                return false;
            }

            return type.IsInterface || (type.IsClass && type.IsAbstract);
        }

        /// <summary>
        /// The default filter applied when scanning an assembly: a type either implements
        /// <see cref="IRepository"/> or its name ends in "Repository".
        /// </summary>
        public static bool IsRepository(Type type)
        {
            if (!IsProxyable(type))
            {
                return false;
            }

            return typeof(IRepository).IsAssignableFrom(type) ||
                   type.Name.EndsWith("Repository", StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns every type in the assembly, tolerating assemblies whose types cannot all be
        /// loaded (a missing optional reference, for example) rather than failing the scan.
        /// </summary>
        internal static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loaded = new System.Collections.Generic.List<Type>();
                foreach (var type in ex.Types)
                {
                    if (type != null)
                    {
                        loaded.Add(type);
                    }
                }

                return loaded.ToArray();
            }
        }
    }
}
