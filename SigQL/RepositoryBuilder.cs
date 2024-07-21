using System;
using System.Collections.Generic;
using System.Linq;
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
            if (tProxy.IsClass && tProxy.IsAbstract)
            {
                return new Castle.DynamicProxy.ProxyGenerator().CreateClassProxy(
                    tProxy,
                    new ProxyGenerationOptions(),
                    new MethodQueryInterceptor(this.queryExecutor, databaseConfiguration, this.queryMaterializer, options.PluralizationHelper, this.sqlLogger)
                );
            }

            return new Castle.DynamicProxy.ProxyGenerator().CreateInterfaceProxyWithoutTarget(tProxy,
                new MethodQueryInterceptor(this.queryExecutor, databaseConfiguration, this.queryMaterializer, options.PluralizationHelper, this.sqlLogger)
            );
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
            if (tProxy.IsInterface)
            {
                return Build(tProxy);
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
                new MethodQueryInterceptor(this.queryExecutor, databaseConfiguration, this.queryMaterializer, options.PluralizationHelper, this.sqlLogger)
            );
        }

        internal class MethodQueryInterceptor : IInterceptor
        {
            private readonly IDatabaseConfiguration databaseConfiguration;
            private readonly IQueryMaterializer materializer;
            private readonly IPluralizationHelper pluralizationHelper;
            private readonly Action<PreparedSqlStatement> sqlLogger;
            private readonly IQueryExecutor queryExecutor;

            public MethodQueryInterceptor(
                IQueryExecutor queryExecutor, 
                IDatabaseConfiguration databaseConfiguration,
                IQueryMaterializer materializer,
                IPluralizationHelper pluralizationHelper,
                Action<PreparedSqlStatement> sqlLogger = null)
            {
                this.databaseConfiguration = databaseConfiguration;
                this.materializer = materializer;
                this.pluralizationHelper = pluralizationHelper;
                this.sqlLogger = sqlLogger;
                this.queryExecutor = queryExecutor;
            }

            public void Intercept(IInvocation invocation)
            {
                var methodParser = new MethodParser(new SqlStatementBuilder(), databaseConfiguration, pluralizationHelper);
                var sqlStatement = methodParser.SqlFor(invocation.Method);
                var methodArgs = invocation.Method.GetParameters().Select((p, i) => new ParameterArg() { Parameter = p, Value = invocation.Arguments[i] });
                if (OutputFactory.UnwrapType(sqlStatement.ReturnType) != typeof(void))
                {
                    var returnValue = this.materializer.MaterializeAsync(
                        new SqlMethodInvocation() { SqlStatement = sqlStatement },
                        methodArgs);
                    if (sqlStatement.ReturnType.IsTask())
                    {
                        var convertedTaskReturnValue = new TaskConverter(sqlStatement.ReturnType.GetGenericArguments().FirstOrDefault()).ConvertReturnType(returnValue);
                        invocation.ReturnValue = convertedTaskReturnValue;
                    }
                    else
                    {
                        invocation.ReturnValue = returnValue.GetAwaiter().GetResult();
                    }
                }
                else
                {
                    var statement = sqlStatement.GetPreparedStatement(methodArgs);
                    this.sqlLogger?.Invoke(statement);
                    var taskResult = this.queryExecutor.ExecuteNonQueryAsync(statement.CommandText, statement.Parameters);
                    if (sqlStatement.ReturnType.IsTask())
                    {
                        invocation.ReturnValue = taskResult;
                    }
                    else
                    {
                        taskResult.GetAwaiter().GetResult();
                    }
                }
            }
        }
    }

    public class RepositoryBuilderOptions
    {
        public RepositoryBuilderOptions()
        {
            this.PluralizationHelper = DefaultPluralizationHelper.Instance;
        }

        public IPluralizationHelper PluralizationHelper { get; set; }
    }
}
