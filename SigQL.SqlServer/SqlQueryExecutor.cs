using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SigQL.SqlServer
{
    public class SqlQueryExecutor : IQueryExecutor
    {
        private readonly Func<IDbConnection> connectionFactory;
        private readonly Func<SqlCommand, SqlCommand> commandAction;

        public SqlQueryExecutor(Func<IDbConnection> connectionFactory)
        {
            this.connectionFactory = connectionFactory;
        }
        public SqlQueryExecutor(Func<IDbConnection> connectionFactory, Func<SqlCommand, SqlCommand> commandAction)
        {
            this.connectionFactory = connectionFactory;
            this.commandAction = commandAction;
        }

        public async Task<IDataReader> ExecuteReaderAsync(string commandText)
        {
            var sqlConnection = (SqlConnection) this.connectionFactory();
            sqlConnection.Open();
            var command = new SqlCommand(commandText, sqlConnection);
            return await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        }

        public async Task<IDataReader> ExecuteReaderAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null)
        {
            var sqlConnection = (SqlConnection) this.connectionFactory();
            sqlConnection.Open();
            var command = GetSqlCommand(sqlConnection, commandText, parameters, commandTimeout);

            return await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        }

        public IDataReader ExecuteReader(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null)
        {
            var sqlConnection = (SqlConnection)this.connectionFactory();
            sqlConnection.Open();
            var command = GetSqlCommand(sqlConnection, commandText, parameters, commandTimeout);

            return command.ExecuteReader(CommandBehavior.CloseConnection);
        }

        public async Task<int> ExecuteNonQueryAsync(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null)
        {
            var sqlConnection = (SqlConnection) this.connectionFactory();
            try
            {
                sqlConnection.Open();
                var command = GetSqlCommand(sqlConnection, commandText, parameters, commandTimeout);

                return await command.ExecuteNonQueryAsync();
            }
            finally
            {
                sqlConnection.Close();
            }
        }

        public int ExecuteNonQuery(string commandText, IDictionary<string, object> parameters, int? commandTimeout = null)
        {
            var sqlConnection = (SqlConnection)this.connectionFactory();
            try
            {
                sqlConnection.Open();
                var command = GetSqlCommand(sqlConnection, commandText, parameters, commandTimeout);

                return command.ExecuteNonQuery();
            }
            finally
            {
                sqlConnection.Close();
            }
        }

        private SqlCommand GetSqlCommand(SqlConnection sqlConnection, string commandText, IDictionary<string, object> parameters, int? commandTimeout = null)
        {
            var command = new SqlCommand(commandText, sqlConnection);
            if(parameters != null)
                foreach (var parameterKey in parameters.Keys)
                {
                    command.Parameters.AddWithValue($"@{parameterKey}", parameters[parameterKey]);
                }

            // Apply the per-method [Command(Timeout = n)] value before the user's commandAction, so a
            // globally configured commandAction remains the final escape hatch and can still override it.
            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = commandTimeout.Value;
            }

            if (commandAction != null)
            {
                command = commandAction(command);
            }

            return command;
        }
    }
}
