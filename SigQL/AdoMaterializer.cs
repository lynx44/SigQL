using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using SigQL.Extensions;
using SigQL.Schema;

namespace SigQL
{
    public interface IQueryMaterializer
    {
        Task<object> MaterializeAsync(SqlMethodInvocation methodInvocation,
            IEnumerable<ParameterArg> methodArgs);
        Task<object> MaterializeAsync(Type outputType, PreparedSqlStatement sqlStatement);

        Task<object> MaterializeAsync(Type outputType, string commandText);
        Task<T> MaterializeAsync<T>(PreparedSqlStatement sqlStatement);
        Task<T> MaterializeAsync<T>(string commandText);
        Task<T> MaterializeAsync<T>(string commandText, IDictionary<string, object> parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null);
        Task<T> MaterializeAsync<T>(string commandText, object parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null);

        object Materialize(SqlMethodInvocation methodInvocation,
            IEnumerable<ParameterArg> methodArgs);
        object Materialize(Type outputType, PreparedSqlStatement sqlStatement);

        object Materialize(Type outputType, string commandText);
        T Materialize<T>(PreparedSqlStatement sqlStatement);
        T Materialize<T>(string commandText);
        T Materialize<T>(string commandText, IDictionary<string, object> parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null);
        T Materialize<T>(string commandText, object parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null);
    }

    public class AdoMaterializer : IQueryMaterializer
    {
        private readonly IQueryExecutor queryExecutor;
        private readonly Action<PreparedSqlStatement> sqlLogger;

        public AdoMaterializer(IQueryExecutor queryExecutor, Action<PreparedSqlStatement> sqlLogger = null)
        {
            this.queryExecutor = queryExecutor;
            this.sqlLogger = sqlLogger;
        }

        public Task<object> MaterializeAsync(SqlMethodInvocation methodInvocation,
            IEnumerable<ParameterArg> methodArgs)
        {
            var statement = methodInvocation.SqlStatement.GetPreparedStatement(methodArgs);

            var targetTablePrimaryKey = methodInvocation.SqlStatement.TargetTablePrimaryKey;
            var tablePrimaryKeyDefinitions = methodInvocation.SqlStatement.TablePrimaryKeyDefinitions;
            var returnType = methodInvocation.SqlStatement.ReturnType;

            return MaterializeAsync(statement, targetTablePrimaryKey, tablePrimaryKeyDefinitions, returnType);
        }
        
        public Task<object> MaterializeAsync(Type outputType, PreparedSqlStatement sqlStatement)
        {
            return MaterializeAsync(sqlStatement, new EmptyTableKeyDefinition(), sqlStatement.PrimaryKeyColumns?.ToGroup() ?? new ConcurrentDictionary<string, IEnumerable<string>>(), outputType);
        }

        public Task<object> MaterializeAsync(Type outputType, string commandText)
        {
            return MaterializeAsync(outputType, new PreparedSqlStatement() {CommandText = commandText, Parameters = new Dictionary<string, object>() });
        }

        public async Task<T> MaterializeAsync<T>(PreparedSqlStatement sqlStatement)
        {
            return (T) (await MaterializeAsync(typeof(T), sqlStatement));
        }

        public Task<T> MaterializeAsync<T>(string commandText, object parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null)
        {
            var preparedSqlStatement = new PreparedSqlStatement(commandText, parameters.ToDictionary(), primaryKeys);
            return MaterializeAsync<T>(preparedSqlStatement);
        }

        public Task<T> MaterializeAsync<T>(string commandText, IDictionary<string, object> parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null)
        {
            var preparedSqlStatement = new PreparedSqlStatement(commandText, parameters, primaryKeys);
            return MaterializeAsync<T>(preparedSqlStatement);
        }

        public Task<T> MaterializeAsync<T>(string commandText)
        {
            return MaterializeAsync<T>(commandText, new { });
        }

        private class EmptyTableKeyDefinition : ITableKeyDefinition
        {
            public IEnumerable<IColumnDefinition> Columns { get; set; }

            public EmptyTableKeyDefinition()
            {
                this.Columns = new List<IColumnDefinition>();
            }
        }

        private async Task<object> MaterializeAsync(PreparedSqlStatement statement, ITableKeyDefinition targetTablePrimaryKey,
            IDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions, Type returnType)
        {
            RowValueCollection rowValueCollection;
            var outputInvocations = new List<object>();
            this.sqlLogger?.Invoke(statement);

            if (statement.Parameters != null)
            {
                // convert null parameters to DBNull
                foreach (var parameter in statement.Parameters.Where(p => p.Value == null).ToList())
                {
                    if (parameter.Value == null)
                    {
                        statement.Parameters[parameter.Key] = DBNull.Value;
                    }
                }
            }
            

            using (var reader = await queryExecutor.ExecuteReaderAsync(statement.CommandText, statement.Parameters))
            {
                rowValueCollection = ReadRowValues(reader, tablePrimaryKeyDefinitions.Keys.Any(k => k == string.Empty) ? tablePrimaryKeyDefinitions[""].ToList() : new List<string>(),
                    tablePrimaryKeyDefinitions);
            }

            var rootOutputType = OutputFactory.UnwrapType(returnType);
            var orderedRowValues = rowValueCollection.Rows.Values.OrderBy(v => v.RowNumber).ToList();
            foreach (var rowValue in orderedRowValues)
            {
                var target = new RowProjectionBuilder().Build(rootOutputType, rowValue);
                outputInvocations.Add(target);
            }

            object result = outputInvocations;

            if (returnType.IsTask())
            {
                if (returnType.IsGenericType)
                {
                    returnType = returnType.GetGenericArguments().First();
                }
                else
                {
                    returnType = typeof(void);
                }
                
            }
            result = OutputFactory.Cast(result, returnType);

            return result;
        }

        private RowValueCollection ReadRowValues(IDataReader reader, IEnumerable<string> keyColumnNames,
            IDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions)
        {
            var rowValueCollection = new RowValueCollection();
            int rowNumber = 0;
            while (reader.Read())
            {
                var rowValueDictionary = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);
                
                Enumerable.Range(0, reader.FieldCount).ToList().ForEach((i) => rowValueDictionary.Add(reader.GetName(i), reader[i]));

                ReadRow(rowValueDictionary, null, keyColumnNames, rowValueCollection, tablePrimaryKeyDefinitions, rowNumber);
                rowNumber++;
            }

            return rowValueCollection;
        }

        /// <remarks>The goal here is to loop over each row and deduplicate each record based on the navigation relationships. the end
        /// result is to create a dictionary-like object that uses the primary key of each table as a key in the dictionary, and the row values
        /// as the properties of the class. For example, an address with locations would look like this:
        ///
        /// Address = { [Key: "Id" = 1] Values(1, "123 Fake Street", Location = { [Key: "Id" = 100] Values(100, "Location 1"), [Key: "Id" = 101] Values(101, "Location 2") }) }
        /// Address = { [Key: "Id" = 2] Values(2, "345 Fake Street", Location = { [Key: "Id" = 100] Values(100, "Location 1"), [Key: "Id" = 200] Values(200, "Location 3") }) }
        /// </remarks>
        private void ReadRow(Dictionary<string, object> rowValues, string aliasName, IEnumerable<string> keyColumnNames,
            RowValueCollection bucket,
            IDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions, int rowNumber)
        {
            RowValues rowValueContainer = null;
            var keys = new RowKey(keyColumnNames.Select(c => new KeyValue(c, rowValues[c])));
            if (!bucket.ContainsKey(keys))
            {
                bucket.Rows[keys] = new RowValues
                {
                    Values = rowValues,
                    RowNumber = rowNumber
                };
            }

            rowValueContainer = bucket.Rows[keys];
            
            var allNavigationPropertyColumns = rowValues.Where(kvp => kvp.Key.Count(c => c == '.') == 1);
            var navigationPropertySets = allNavigationPropertyColumns.GroupBy(c => c.Key.Substring(0, c.Key.IndexOf(".")));
            foreach (var navigationPropertyColumns in navigationPropertySets)
            {
                var navigationPropertyName = navigationPropertyColumns.Key;
                var qualifiedAliasName = $"{aliasName}{(aliasName != null ? "." : string.Empty)}{navigationPropertyName}";
                var navigationKeyColumnNames = 
                    tablePrimaryKeyDefinitions[qualifiedAliasName].ToList();
                var navigationRowValues = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);
                foreach (var keyValuePair in rowValues)
                {
                    // remove parent row values
                    if (keyValuePair.Key.StartsWith($"{navigationPropertyName}."))
                    {
                        var key = keyValuePair.Key;
                        key = TrimStartingPhrase(key, $"{navigationPropertyName}.");
                        navigationRowValues[key] = keyValuePair.Value;
                    }
                }

                if (!rowValueContainer.Relations.ContainsKey(navigationPropertyName))
                {
                    rowValueContainer.Relations[navigationPropertyName] = new RowValueCollection();
                }

                ReadRow(navigationRowValues, qualifiedAliasName, navigationKeyColumnNames, rowValueContainer.Relations[navigationPropertyName], tablePrimaryKeyDefinitions, rowNumber);
            }
        }

        private static string TrimStartingPhrase(string target, string trimString)
        {
            if (string.IsNullOrEmpty(trimString)) return target;

            string result = target;
            if (result.StartsWith(trimString))
            {
                result = result.Substring(trimString.Length);
            }

            return result;
        }

        private object Materialize(PreparedSqlStatement statement, ITableKeyDefinition targetTablePrimaryKey,
            IDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions, Type returnType)
        {
            RowValueCollection rowValueCollection;
            var outputInvocations = new List<object>();
            this.sqlLogger?.Invoke(statement);

            if (statement.Parameters != null)
            {
                // convert null parameters to DBNull
                foreach (var parameter in statement.Parameters.Where(p => p.Value == null).ToList())
                {
                    if (parameter.Value == null)
                    {
                        statement.Parameters[parameter.Key] = DBNull.Value;
                    }
                }
            }


            using (var reader = queryExecutor.ExecuteReader(statement.CommandText, statement.Parameters))
            {
                rowValueCollection = ReadRowValues(reader, tablePrimaryKeyDefinitions.Keys.Any(k => k == string.Empty) ? tablePrimaryKeyDefinitions[""].ToList() : new List<string>(),
                    tablePrimaryKeyDefinitions);
            }

            var rootOutputType = OutputFactory.UnwrapType(returnType);
            var orderedRowValues = rowValueCollection.Rows.Values.OrderBy(v => v.RowNumber).ToList();
            foreach (var rowValue in orderedRowValues)
            {
                var target = new RowProjectionBuilder().Build(rootOutputType, rowValue);
                outputInvocations.Add(target);
            }

            object result = outputInvocations;

            if (returnType.IsTask())
            {
                if (returnType.IsGenericType)
                {
                    returnType = returnType.GetGenericArguments().First();
                }
                else
                {
                    returnType = typeof(void);
                }

            }
            result = OutputFactory.Cast(result, returnType);

            return result;
        }

        public object Materialize(SqlMethodInvocation methodInvocation,
            IEnumerable<ParameterArg> methodArgs)
        {
            var statement = methodInvocation.SqlStatement.GetPreparedStatement(methodArgs);

            var targetTablePrimaryKey = methodInvocation.SqlStatement.TargetTablePrimaryKey;
            var tablePrimaryKeyDefinitions = methodInvocation.SqlStatement.TablePrimaryKeyDefinitions;
            var returnType = methodInvocation.SqlStatement.ReturnType;

            return Materialize(statement, targetTablePrimaryKey, tablePrimaryKeyDefinitions, returnType);
        }

        public object Materialize(Type outputType, PreparedSqlStatement sqlStatement)
        {
            return Materialize(sqlStatement, new EmptyTableKeyDefinition(), sqlStatement.PrimaryKeyColumns?.ToGroup() ?? new ConcurrentDictionary<string, IEnumerable<string>>(), outputType);
        }

        public object Materialize(Type outputType, string commandText)
        {
            return Materialize(outputType, new PreparedSqlStatement() { CommandText = commandText, Parameters = new Dictionary<string, object>() });
        }

        public T Materialize<T>(PreparedSqlStatement sqlStatement)
        {
            return (T)Materialize(typeof(T), sqlStatement);
        }

        public T Materialize<T>(string commandText, object parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null)
        {
            var preparedSqlStatement = new PreparedSqlStatement(commandText, parameters.ToDictionary(), primaryKeys);
            return Materialize<T>(preparedSqlStatement);
        }

        public T Materialize<T>(string commandText, IDictionary<string, object> parameters, PrimaryKeyQuerySpecifierCollection primaryKeys = null)
        {
            var preparedSqlStatement = new PreparedSqlStatement(commandText, parameters, primaryKeys);
            return Materialize<T>(preparedSqlStatement);
        }

        public T Materialize<T>(string commandText)
        {
            return Materialize<T>(commandText, new { });
        }
    }
}
