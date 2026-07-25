using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using SigQL.Exceptions;
using SigQL.Types.Attributes;

namespace SigQL.Utilities
{
    /// <summary>
    /// Supports repository members that supply their own implementation instead of having their
    /// SQL generated: default interface methods, virtual methods on abstract repository classes,
    /// and abstract properties marked with <see cref="InjectAttribute"/>.
    /// </summary>
    internal static class CustomMethodInvoker
    {
        private static readonly ConcurrentDictionary<MethodInfo, Func<object, object[], object>> InvokerCache =
            new ConcurrentDictionary<MethodInfo, Func<object, object[], object>>();

        /// <summary>
        /// Returns the property backing an abstract [Inject] getter, or null when the method is
        /// not one.
        /// </summary>
        internal static PropertyInfo GetInjectedProperty(MethodInfo method)
        {
            if (!method.IsSpecialName || !method.Name.StartsWith("get_"))
            {
                return null;
            }

            var property = method.DeclaringType
                ?.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(p => p.GetGetMethod(true) == method);

            return property?.GetCustomAttribute<InjectAttribute>() != null ? property : null;
        }

        internal static bool HasInjectedParameters(MethodInfo method)
        {
            return method.GetParameters().Any(p => p.GetCustomAttribute<InjectAttribute>() != null);
        }

        /// <summary>
        /// Copies the caller's arguments, replacing each [Inject] parameter with a resolved service.
        /// </summary>
        internal static object[] ResolveArguments(MethodInfo method, object[] arguments, Func<Type, object> serviceResolver)
        {
            var parameters = method.GetParameters();
            var resolved = (object[]) arguments.Clone();

            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].GetCustomAttribute<InjectAttribute>() == null)
                {
                    continue;
                }

                if (!parameters[i].HasDefaultValue)
                {
                    throw new InvalidAttributeException(typeof(InjectAttribute), new MemberInfo[] { method },
                        $"Parameter \"{parameters[i].Name}\" of method \"{method.DeclaringType?.Name}.{method.Name}\" is marked with [Inject] but is not optional. " +
                        "Give it a default value (for example \"= null\") so callers can omit it.");
                }

                resolved[i] = ResolveService(parameters[i].ParameterType, method, serviceResolver);
            }

            return resolved;
        }

        internal static object ResolveService(Type serviceType, MemberInfo member, Func<Type, object> serviceResolver)
        {
            if (serviceResolver == null)
            {
                throw new InvalidOperationException(
                    $"\"{member.DeclaringType?.Name}.{member.Name}\" requires the {serviceType.Name} service, but no service resolver is configured. " +
                    $"Set {nameof(RepositoryBuilderOptions)}.{nameof(RepositoryBuilderOptions.ServiceResolver)}, or pass a resolver to RepositoryBuilder.Build.");
            }

            var service = serviceResolver(serviceType);
            if (service == null)
            {
                throw new InvalidOperationException(
                    $"The service resolver returned null for {serviceType.Name}, required by \"{member.DeclaringType?.Name}.{member.Name}\".");
            }

            if (!serviceType.IsInstanceOfType(service))
            {
                throw new InvalidOperationException(
                    $"The service resolver returned {service.GetType().Name} for {serviceType.Name}, required by \"{member.DeclaringType?.Name}.{member.Name}\".");
            }

            return service;
        }

        /// <summary>
        /// Invokes a default interface method's body. Castle DynamicProxy cannot Proceed() to a
        /// default implementation on a proxy without a target, so this dispatches with a
        /// non-virtual call, which lands in the default body instead of re-entering the proxy.
        /// Calls the body makes back onto itself still route through the proxy, so a custom
        /// method can compose generated ones.
        /// </summary>
        internal static object InvokeDefaultInterfaceMethod(object proxy, MethodInfo method, object[] arguments)
        {
            return InvokerCache.GetOrAdd(method, BuildInvoker)(proxy, arguments);
        }

        private static Func<object, object[], object> BuildInvoker(MethodInfo method)
        {
            var dynamicMethod = new DynamicMethod(
                $"SigQL_DefaultInterfaceMethod_{method.DeclaringType?.Name}_{method.Name}",
                typeof(object),
                new[] { typeof(object), typeof(object[]) },
                typeof(CustomMethodInvoker).Module,
                skipVisibility: true);

            var il = dynamicMethod.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, method.DeclaringType);

            var parameters = method.GetParameters();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType.IsByRef)
                {
                    throw new NotSupportedException(
                        $"Default interface method \"{method.DeclaringType?.Name}.{method.Name}\" declares the ref/out parameter \"{parameters[i].Name}\", which SigQL cannot dispatch to. " +
                        "Move the method to an abstract repository class, or return the value instead.");
                }

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(parameters[i].ParameterType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass,
                    parameters[i].ParameterType);
            }

            // a non-virtual call is what reaches the default body rather than the proxy override
            il.Emit(OpCodes.Call, method);

            if (method.ReturnType == typeof(void))
            {
                il.Emit(OpCodes.Ldnull);
            }
            else if (method.ReturnType.IsValueType)
            {
                il.Emit(OpCodes.Box, method.ReturnType);
            }

            il.Emit(OpCodes.Ret);

            return (Func<object, object[], object>) dynamicMethod.CreateDelegate(typeof(Func<object, object[], object>));
        }
    }
}
