using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            return Materialize(outputType, new PreparedSqlStatement() {CommandText = commandText, Parameters = new Dictionary<string, object>() });
        }

        public T Materialize<T>(PreparedSqlStatement sqlStatement)
        {
            return (T) Materialize(typeof(T), sqlStatement);
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

        private class EmptyTableKeyDefinition : ITableKeyDefinition
        {
            public IEnumerable<IColumnDefinition> Columns { get; set; }

            public EmptyTableKeyDefinition()
            {
                this.Columns = new List<IColumnDefinition>();
            }
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

            if (returnType.IsCollectionType())
            {
                var collectionType = returnType;
                if (collectionType.IsGenericType)
                {
                    if (collectionType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) ||
                        collectionType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>))
                    {
                        var asReadOnlyMethod =
                            typeof(List<>).MakeGenericType(collectionType.GenericTypeArguments.First()).GetMethod(nameof(List<object>.AsReadOnly), BindingFlags.Instance | BindingFlags.Public);
                        var list = MakeGenericList(rootOutputType, outputInvocations);
                        result = asReadOnlyMethod.Invoke(list, null);
                    }

                    if (collectionType.GetGenericTypeDefinition() == typeof(IList<>) ||
                        collectionType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        result = MakeGenericList(rootOutputType, outputInvocations);

                    }

                    if (collectionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        result = CastToGenericEnumerable(rootOutputType, outputInvocations);
                    }
                }
                else if(returnType.IsArray)
                {
                    var toArrayMethod =
                        typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray), BindingFlags.Static | BindingFlags.Public);
                    var toArrayMethodForType = toArrayMethod.MakeGenericMethod(rootOutputType);
                    var enumerable = CastToGenericEnumerable(rootOutputType, outputInvocations);
                    result = toArrayMethodForType.Invoke(null, new[] { enumerable });
                }
            }
            else
            {
                result = outputInvocations.SingleOrDefault();
            }

            return result;
        }

        private static object MakeGenericList(Type rootOutputType, List<object> outputInvocations)
        {
            object result;
            var toListMethod =
                typeof(Enumerable).GetMethod(nameof(Enumerable.ToList), BindingFlags.Static | BindingFlags.Public);
            var toListMethodForType = toListMethod.MakeGenericMethod(rootOutputType);
            var enumerable = CastToGenericEnumerable(rootOutputType, outputInvocations);
            result = toListMethodForType.Invoke(null, new[] {enumerable});
            return result;
        }

        private static object CastToGenericEnumerable(Type rootOutputType, List<object> outputInvocations)
        {
            object result;
            var castMethod =
                typeof(Enumerable).GetMethod(nameof(Enumerable.Cast), BindingFlags.Static | BindingFlags.Public);
            var castMethodForType = castMethod.MakeGenericMethod(rootOutputType);
            result = castMethodForType.Invoke(null, new[] {outputInvocations});
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
    }
}
