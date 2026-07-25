using System.Linq;
using Castle.DynamicProxy;
using SigQL.Schema;

namespace SigQL.Diagnostics
{
    public class PreparedSqlStatementBuilder
    {
        private readonly IDatabaseConfiguration databaseConfiguration;
        private readonly IForeignKeyResolver foreignKeyResolver;

        public PreparedSqlStatementBuilder(IDatabaseConfiguration databaseConfiguration, IForeignKeyResolver foreignKeyResolver = null)
        {
            this.databaseConfiguration = databaseConfiguration;
            this.foreignKeyResolver = foreignKeyResolver ?? DefaultForeignKeyResolver.Instance;
        }

        public PreparedSqlStatement Build(IInvocation invocation)
        {
            var methodParser = new MethodParser(new SqlStatementBuilder(), this.databaseConfiguration, DefaultPluralizationHelper.Instance, this.foreignKeyResolver);
            var sqlStatement = methodParser.SqlFor(invocation.Method);
            var methodArgs = invocation.Method.GetParameters().Select((p, i) => new ParameterArg() { Parameter = p, Value = invocation.Arguments[i] });
            return sqlStatement.GetPreparedStatement(methodArgs);
        }
    }
}
