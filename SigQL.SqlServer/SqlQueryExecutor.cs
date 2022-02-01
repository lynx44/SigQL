using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;

namespace SigQL.SqlServer
{
    public class SqlQueryExecutor : IQueryExecutor
    {
        private readonly Func<IDbConnection> connectionFactory;

        public SqlQueryExecutor(Func<IDbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory;
        }

        public IDataReader ExecuteReader(string commandText)
        {
            var sqlConnection = (SqlConnection) this.connectionFactory();
            sqlConnection.Open();
            var command = new SqlCommand(commandText, sqlConnection);
            return command.ExecuteReader(CommandBehavior.CloseConnection);
        }

        public IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters)
        {
            var sqlConnection = (SqlConnection) this.connectionFactory();
            sqlConnection.Open();
            var command = GetSqlCommand(sqlConnection, commandText, parameters);

            return command.ExecuteReader(CommandBehavior.CloseConnection);
        }

        public void ExecuteNonQuery(string commandText, IDictionary<string, object> parameters)
        {
            var sqlConnection = (SqlConnection) this.connectionFactory();
            try
            {
                sqlConnection.Open();
                var command = GetSqlCommand(sqlConnection, commandText, parameters);

                command.ExecuteNonQuery();
            }
            finally
            {
                sqlConnection.Close();
            }
        }

        private SqlCommand GetSqlCommand(SqlConnection sqlConnection, string commandText, IDictionary<string, object> parameters)
        {
            var command = new SqlCommand(commandText, sqlConnection);
            if(parameters != null)
                foreach (var parameterKey in parameters.Keys)
                {
                    command.Parameters.AddWithValue($"@{parameterKey}", parameters[parameterKey]);
                }

            return command;
        }
    }
}
