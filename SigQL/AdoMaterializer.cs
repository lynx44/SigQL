using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using SigQL.Extensions;
using SigQL.Schema;

namespace SigQL
{
    public interface IQueryMaterializer
    {
        object Materialize(SqlMethodInvocation methodInvocation,
            IEnumerable<ParameterArg> methodArgs);
        object Materialize(Type outputType, PreparedSqlStatement sqlStatement);
        T Materialize<T>(PreparedSqlStatement sqlStatement);
        T Materialize<T>(string commandText, object parameters);
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

        public object Materialize(SqlMethodInvocation methodInvocation,
            IEnumerable<ParameterArg> methodArgs)
        {
            var statement = methodInvocation.SqlStatement.GetPreparedStatement(methodArgs);

            var targetTablePrimaryKey = methodInvocation.SqlStatement.TargetTablePrimaryKey;
            var tablePrimaryKeyDefinitions = methodInvocation.SqlStatement.TablePrimaryKeyDefinitions;
            var rootOutputType = methodInvocation.SqlStatement.UnwrappedReturnType;
            var returnType = methodInvocation.SqlStatement.ReturnType;

            return Materialize(statement, targetTablePrimaryKey, tablePrimaryKeyDefinitions, returnType);
        }
        
        public object Materialize(Type outputType, PreparedSqlStatement sqlStatement)
        {
            return Materialize(sqlStatement, new EmptyTableKeyDefinition(), new ConcurrentDictionary<string, ITableKeyDefinition>(), outputType);
        }
        
        public T Materialize<T>(PreparedSqlStatement sqlStatement)
        {
            return (T) Materialize(typeof(T), sqlStatement);
        }
        
        public T Materialize<T>(string commandText, object parameters)
        {
            var preparedSqlStatement = new PreparedSqlStatement(commandText, parameters);
            return Materialize<T>(preparedSqlStatement);
        }

        private class EmptyTableKeyDefinition : ITableKeyDefinition
        {
            public IEnumerable<IColumnDefinition> Columns { get; set; }

            public EmptyTableKeyDefinition()
            {
                this.Columns = new List<IColumnDefinition>();
            }
        }

        private object Materialize(PreparedSqlStatement statement, ITableKeyDefinition targetTablePrimaryKey,
            IDictionary<string, ITableKeyDefinition> tablePrimaryKeyDefinitions, Type returnType)
        {
            RowValueCollection rowValueCollection;
            var outputInvocations = new List<object>();
            this.sqlLogger?.Invoke(statement);

            using (var reader = queryExecutor.ExecuteReader(statement.CommandText, statement.Parameters))
            {
                rowValueCollection = ReadRowValues(reader, targetTablePrimaryKey.Columns.Select(c => c.Name).ToList(),
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

            if (returnType.IsCollectionType())
            {
                var collectionType = returnType;
                if (collectionType.IsGenericType)
                {
                    if (collectionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        var castMethod =
                            typeof(Enumerable).GetMethod(nameof(Enumerable.Cast), BindingFlags.Static | BindingFlags.Public);
                        var castMethodForType = castMethod.MakeGenericMethod(rootOutputType);
                        result = castMethodForType.Invoke(null, outputInvocations.AsEnumerable().ToArray());
                    }
                }
            }
            else
            {
                result = outputInvocations.SingleOrDefault();
            }

            return result;
        }

        private RowValueCollection ReadRowValues(IDataReader reader, IEnumerable<string> keyColumnNames,
            IDictionary<string, ITableKeyDefinition> tablePrimaryKeyDefinitions)
        {
            var rowValueCollection = new RowValueCollection();
            int rowNumber = 0;
            while (reader.Read())
            {
                var rowValueDictionary = new Dictionary<string, object>();
                
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
            IDictionary<string, ITableKeyDefinition> tablePrimaryKeyDefinitions, int rowNumber)
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

            // var directRelationships = relationships.Where(p =>
            //     p.ColumnAliasForeignKeyPairs.Any(kp =>
            //         (aliasName == null && !kp.ForeignTableColumnWithAlias.Alias.Contains(".")) || kp.ForeignTableColumnWithAlias.Alias.StartsWith(aliasName + ".")));

            var allNavigationPropertyColumns = rowValues.Where(kvp => kvp.Key.Count(c => c == '.') == 1);
            var navigationPropertySets = allNavigationPropertyColumns.GroupBy(c => c.Key.Substring(0, c.Key.IndexOf(".")));
            foreach (var navigationPropertyColumns in navigationPropertySets)
            {
                var navigationPropertyName = navigationPropertyColumns.Key;
                var qualifiedAliasName = $"{aliasName}{(aliasName != null ? "." : string.Empty)}{navigationPropertyName}";
                var navigationKeyColumnNames = 
                    tablePrimaryKeyDefinitions[qualifiedAliasName].Columns.Select(c => c.Name).ToList();
                var navigationRowValues = new Dictionary<string, object>();
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
    }
}
