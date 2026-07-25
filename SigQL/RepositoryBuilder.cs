using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Utilities;

namespace SigQL
{
    public class RepositoryBuilder
    {
        private readonly IQueryExecutor queryExecutor;
        private readonly IDatabaseConfiguration databaseConfiguration;
        private readonly IQueryMaterializer queryMaterializer;
        private readonly RepositoryBuilderOptions options;
        private readonly Action<PreparedSqlStatement> sqlLogger;

        public RepositoryBuilder(
            IQueryExecutor queryExecutor, 
            IDatabaseConfiguration databaseConfiguration, 
            Action<PreparedSqlStatement> sqlLogger = null) :
            this(queryExecutor, databaseConfiguration, new AdoMaterializer(queryExecutor, sqlLogger), new RepositoryBuilderOptions(), sqlLogger)
        {
        }

        public RepositoryBuilder(
            IQueryExecutor queryExecutor, 
            IDatabaseConfiguration databaseConfiguration,
            IQueryMaterializer queryMaterializer,
            Action<PreparedSqlStatement> sqlLogger = null) : 
            this(queryExecutor, databaseConfiguration, queryMaterializer, new RepositoryBuilderOptions(), sqlLogger)
        {
        }

        public RepositoryBuilder(
            IQueryExecutor queryExecutor, 
            IDatabaseConfiguration databaseConfiguration,
            IQueryMaterializer queryMaterializer,
            RepositoryBuilderOptions options,
            Action<PreparedSqlStatement> sqlLogger = null)
        {
            this.queryExecutor = queryExecutor;
            this.databaseConfiguration = databaseConfiguration;
            this.queryMaterializer = queryMaterializer;
            this.options = options;
            this.sqlLogger = sqlLogger;
        }

        public TProxy Build<TProxy>()
            where TProxy : class
        {
            return (TProxy) CreateProxy(typeof(TProxy));
        }

        private object CreateProxy(Type tProxy)
        {
            return CreateProxy(tProxy, options.ServiceResolver);
        }

        private object CreateProxy(Type tProxy, Func<Type, object> serviceResolver)
        {
            if (tProxy.IsClass && tProxy.IsAbstract)
            {
                return new Castle.DynamicProxy.ProxyGenerator().CreateClassProxy(
                    tProxy,
                    new ProxyGenerationOptions(),
                    CreateInterceptor(serviceResolver)
                );
            }

            return new Castle.DynamicProxy.ProxyGenerator().CreateInterfaceProxyWithoutTarget(tProxy,
                CreateInterceptor(serviceResolver)
            );
        }

        private MethodQueryInterceptor CreateInterceptor(Func<Type, object> serviceResolver)
        {
            return new MethodQueryInterceptor(this.queryExecutor, databaseConfiguration, this.queryMaterializer,
                options.PluralizationHelper, options.ForeignKeyResolver, serviceResolver ?? options.ServiceResolver, this.sqlLogger);
        }

        public TProxy Build<TProxy>(Func<Type, object> constructorParameterResolver)
            where TProxy : class
        {
            return (TProxy) Build(typeof(TProxy), constructorParameterResolver);
        }

        public object Build(Type type)
        {
            return CreateProxy(type);
        }

        public object Build(Type tProxy, Func<Type, object> constructorParameterResolver)
        {
            // the same delegate doubles as the service resolver for [Inject] members
            if (tProxy.IsInterface)
            {
                return CreateProxy(tProxy, constructorParameterResolver);
            }

            object[] constructorArguments = null;
            var constructorWithArgs = tProxy.GetConstructors().FirstOrDefault(c => c.GetParameters().Any());
            if (constructorWithArgs != null)
            {
                var defaultConstructor = constructorWithArgs;
                var parameterTypes = defaultConstructor.GetParameters().Select(p => p.ParameterType).ToList();
                constructorArguments = parameterTypes.Select(t => constructorParameterResolver(t)).ToArray();
            }

            return new Castle.DynamicProxy.ProxyGenerator().CreateClassProxy(
                tProxy,
                new ProxyGenerationOptions(),
                constructorArguments,
                CreateInterceptor(constructorParameterResolver)
            );
        }

        internal class MethodQueryInterceptor : IInterceptor
        {
            private readonly IDatabaseConfiguration databaseConfiguration;
            private readonly IQueryMaterializer materializer;
            private readonly IPluralizationHelper pluralizationHelper;
            private readonly IForeignKeyResolver foreignKeyResolver;
            private readonly Func<Type, object> serviceResolver;
            private readonly Action<PreparedSqlStatement> sqlLogger;
            private readonly IQueryExecutor queryExecutor;

            public MethodQueryInterceptor(
                IQueryExecutor queryExecutor,
                IDatabaseConfiguration databaseConfiguration,
                IQueryMaterializer materializer,
                IPluralizationHelper pluralizationHelper,
                IForeignKeyResolver foreignKeyResolver,
                Func<Type, object> serviceResolver = null,
                Action<PreparedSqlStatement> sqlLogger = null)
            {
                this.databaseConfiguration = databaseConfiguration;
                this.materializer = materializer;
                this.pluralizationHelper = pluralizationHelper;
                this.foreignKeyResolver = foreignKeyResolver ?? DefaultForeignKeyResolver.Instance;
                this.serviceResolver = serviceResolver;
                this.sqlLogger = sqlLogger;
                this.queryExecutor = queryExecutor;
            }

            public void Intercept(IInvocation invocation)
            {
                // a member that supplies its own implementation is the user's code, not a query
                if (TryInterceptCustomImplementation(invocation))
                {
                    return;
                }

                var methodParser = new MethodParser(new SqlStatementBuilder(), databaseConfiguration, pluralizationHelper, foreignKeyResolver);
                var sqlStatement = methodParser.SqlFor(invocation.Method);
                var methodArgs = invocation.Method.GetParameters().Select((p, i) => new ParameterArg() { Parameter = p, Value = invocation.Arguments[i] });
                if (OutputFactory.UnwrapType(sqlStatement.ReturnType) != typeof(void))
                {
                    
                    if (sqlStatement.ReturnType.IsTask())
                    {
                        var returnValue = this.materializer.MaterializeAsync(
                            new SqlMethodInvocation() { SqlStatement = sqlStatement },
                            methodArgs);
                        var convertedTaskReturnValue = new TaskConverter(sqlStatement.ReturnType.GetGenericArguments().FirstOrDefault()).ConvertReturnType(returnValue);
                        invocation.ReturnValue = convertedTaskReturnValue;
                    }
                    else
                    {
                        invocation.ReturnValue = this.materializer.Materialize(
                            new SqlMethodInvocation() { SqlStatement = sqlStatement },
                            methodArgs); ;
                    }
                }
                else
                {
                    var statement = sqlStatement.GetPreparedStatement(methodArgs);
                    this.sqlLogger?.Invoke(statement);
                    
                    if (sqlStatement.ReturnType.IsTask())
                    {
                        var taskResult = this.queryExecutor.ExecuteNonQueryAsync(statement.CommandText, statement.Parameters, statement.CommandTimeout);
                        invocation.ReturnValue = taskResult;
                    }
                    else
                    {
                        this.queryExecutor.ExecuteNonQuery(statement.CommandText, statement.Parameters, statement.CommandTimeout);
                    }
                }
            }

            /// <summary>
            /// Handles members that are not generated queries: methods with a body (default
            /// interface methods and virtual methods on abstract repository classes), and
            /// abstract [Inject] properties. Returns false when the member should have its SQL
            /// generated as usual.
            /// </summary>
            private bool TryInterceptCustomImplementation(IInvocation invocation)
            {
                // MethodInvocationTarget is the class implementation for a class proxy, and null
                // for an interface proxy without a target
                var implementation = invocation.MethodInvocationTarget ?? invocation.Method;

                if (!implementation.IsAbstract)
                {
                    InvokeCustomImplementation(invocation, implementation);
                    return true;
                }

                var injectedProperty = CustomMethodInvoker.GetInjectedProperty(invocation.Method);
                if (injectedProperty != null)
                {
                    invocation.ReturnValue = CustomMethodInvoker.ResolveService(
                        injectedProperty.PropertyType, injectedProperty, this.serviceResolver);
                    return true;
                }

                return false;
            }

            private void InvokeCustomImplementation(IInvocation invocation, MethodInfo implementation)
            {
                if (implementation.DeclaringType != null && implementation.DeclaringType.IsInterface)
                {
                    // Castle cannot Proceed() into a default interface method, so dispatch directly
                    var arguments = CustomMethodInvoker.ResolveArguments(
                        invocation.Method, invocation.Arguments, this.serviceResolver);
                    invocation.ReturnValue = CustomMethodInvoker.InvokeDefaultInterfaceMethod(
                        invocation.Proxy, invocation.Method, arguments);
                    return;
                }

                // a virtual method on an abstract repository class: Castle can call the base body
                if (CustomMethodInvoker.HasInjectedParameters(implementation))
                {
                    var arguments = CustomMethodInvoker.ResolveArguments(
                        implementation, invocation.Arguments, this.serviceResolver);
                    for (var i = 0; i < arguments.Length; i++)
                    {
                        invocation.SetArgumentValue(i, arguments[i]);
                    }
                }

                invocation.Proceed();
            }
        }
    }

    public class RepositoryBuilderOptions
    {
        public RepositoryBuilderOptions()
        {
            this.PluralizationHelper = DefaultPluralizationHelper.Instance;
            this.ForeignKeyResolver = DefaultForeignKeyResolver.Instance;
        }

        public IPluralizationHelper PluralizationHelper { get; set; }
        public IForeignKeyResolver ForeignKeyResolver { get; set; }

        /// <summary>
        /// Supplies services for parameters and properties marked with
        /// <see cref="SigQL.Types.Attributes.InjectAttribute"/>. Typically wired to a DI
        /// container, for example <c>t => serviceProvider.GetRequiredService(t)</c>.
        /// </summary>
        public Func<Type, object> ServiceResolver { get; set; }
    }
}
