using System.Linq;
using Castle.DynamicProxy;
using SigQL.Schema;

namespace SigQL.Diagnostics
{
    public class PreparedSqlStatementBuilder
    {
        private readonly IDatabaseConfiguration databaseConfiguration;

        public PreparedSqlStatementBuilder(IDatabaseConfiguration databaseConfiguration)
        {
            this.databaseConfiguration = databaseConfiguration;
        }

        public PreparedSqlStatement Build(IInvocation invocation)
        {
            var methodParser = new MethodParser(new SqlStatementBuilder(), this.databaseConfiguration);
            var sqlStatement = methodParser.SqlFor(invocation.Method);
            var methodArgs = invocation.Method.GetParameters().Select((p, i) => new ParameterArg() { Parameter = p, Value = invocation.Arguments[i] });
            return sqlStatement.GetPreparedStatement(methodArgs);
        }
    }
}
