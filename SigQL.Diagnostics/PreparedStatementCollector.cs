using System.Collections.Generic;
using Castle.DynamicProxy;
using SigQL.Schema;

namespace SigQL.Diagnostics
{
    public class PreparedStatementCollectorFactory
    {
        private readonly IDatabaseConfiguration databaseConfiguration;

        public PreparedStatementCollectorFactory(IDatabaseConfiguration databaseConfiguration)
        {
            this.databaseConfiguration = databaseConfiguration;
        }

        public TProxy Build<TProxy>(IList<PreparedSqlStatement> collectedStatements)
            where TProxy : class
        {
            return new Castle.DynamicProxy.ProxyGenerator().CreateInterfaceProxyWithoutTarget<TProxy>(
                new PreparedStatementInterceptor(this.databaseConfiguration, collectedStatements)
            );
        }

        internal class PreparedStatementInterceptor : IInterceptor
        {
            private readonly IDatabaseConfiguration databaseConfiguration;
            private readonly IList<PreparedSqlStatement> collectedStatements;

            public PreparedStatementInterceptor(IDatabaseConfiguration databaseConfiguration,
                IList<PreparedSqlStatement> collectedStatements)
            {
                this.databaseConfiguration = databaseConfiguration;
                this.collectedStatements = collectedStatements;
            }

            public void Intercept(IInvocation invocation)
            {
                var preparedSqlStatementBuilder = new PreparedSqlStatementBuilder(this.databaseConfiguration);
                var statement = preparedSqlStatementBuilder.Build(invocation);
                this.collectedStatements.Add(statement);
            }
        }
    }
        
}
