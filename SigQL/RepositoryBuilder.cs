using System;
using System.Linq;
using Castle.DynamicProxy;
using SigQL.Schema;

namespace SigQL
{
    public class RepositoryBuilder
    {
        private readonly IQueryExecutor queryExecutor;
        private readonly IDatabaseConfiguration databaseConfiguration;
        private readonly IQueryMaterializer queryMaterializer;
        private readonly Action<PreparedSqlStatement> sqlLogger;

        public RepositoryBuilder(
            IQueryExecutor queryExecutor, 
            IDatabaseConfiguration databaseConfiguration, 
            Action<PreparedSqlStatement> sqlLogger = null)
        {
            this.queryExecutor = queryExecutor;
            this.databaseConfiguration = databaseConfiguration;
            this.sqlLogger = sqlLogger;
            this.queryMaterializer = new AdoMaterializer(queryExecutor, sqlLogger);
        }

        public RepositoryBuilder(
            IQueryExecutor queryExecutor, 
            IDatabaseConfiguration databaseConfiguration,
            IQueryMaterializer queryMaterializer,
            Action<PreparedSqlStatement> sqlLogger = null)
        {
            this.queryExecutor = queryExecutor;
            this.databaseConfiguration = databaseConfiguration;
            this.queryMaterializer = queryMaterializer;
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
                    new MethodQueryInterceptor(this.queryExecutor, databaseConfiguration, this.queryMaterializer, this.sqlLogger)
                );
            }

            return new Castle.DynamicProxy.ProxyGenerator().CreateInterfaceProxyWithoutTarget(tProxy,
                new MethodQueryInterceptor(this.queryExecutor, databaseConfiguration, this.queryMaterializer, this.sqlLogger)
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
                new MethodQueryInterceptor(this.queryExecutor, databaseConfiguration, this.queryMaterializer, this.sqlLogger)
            );
        }

        internal class MethodQueryInterceptor : IInterceptor
        {
            private readonly IDatabaseConfiguration databaseConfiguration;
            private readonly IQueryMaterializer materializer;
            private readonly Action<PreparedSqlStatement> sqlLogger;
            private readonly IQueryExecutor queryExecutor;

            public MethodQueryInterceptor(
                IQueryExecutor queryExecutor, 
                IDatabaseConfiguration databaseConfiguration,
                IQueryMaterializer materializer,
                Action<PreparedSqlStatement> sqlLogger = null)
            {
                this.databaseConfiguration = databaseConfiguration;
                this.materializer = materializer;
                this.sqlLogger = sqlLogger;
                this.queryExecutor = queryExecutor;
            }

            public void Intercept(IInvocation invocation)
            {
                var methodParser = new MethodParser(new SqlStatementBuilder(), databaseConfiguration);
                var sqlStatement = methodParser.SqlFor(invocation.Method);
                var methodArgs = invocation.Method.GetParameters().Select((p, i) => new ParameterArg() { Parameter = p, Value = invocation.Arguments[i] });
                if (sqlStatement.ReturnType != typeof(void))
                {
                    invocation.ReturnValue = this.materializer.Materialize(
                            new SqlMethodInvocation() { SqlStatement = sqlStatement },
                            methodArgs);
                }
                else
                {
                    var statement = sqlStatement.GetPreparedStatement(methodArgs);
                    this.sqlLogger?.Invoke(statement);
                    this.queryExecutor.ExecuteNonQuery(statement.CommandText, statement.Parameters);
                }
            }
        }
    }
}
