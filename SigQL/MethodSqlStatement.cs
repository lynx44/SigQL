using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Types;

namespace SigQL
{
    public class MethodSqlStatement
    {
        public MethodSqlStatement()
        {
            this.Parameters = new List<ParameterPath>();
            this.Tokens = new List<TokenPath>();
        }

        internal const int OpenJsonParameterThreshold = 2000;

        public PreparedSqlStatement GetPreparedStatement(IEnumerable<ParameterArg> parameterValues)
        {
            var additionalSqlParameters = this.ParseTokens(parameterValues);
            var parameters = this.GetParameters(parameterValues);
            var allParameters = MergeParameters(parameters, additionalSqlParameters);

            if (allParameters.Count > OpenJsonParameterThreshold)
            {
                ConvertLargeCollectionsToOpenJson(allParameters);
            }

            if (allParameters.Count > OpenJsonParameterThreshold)
            {
                ConvertLargeLookupInsertsToOpenJson(allParameters);
            }

            foreach (var key in allParameters.Keys.ToList())
            {
                if (allParameters[key] == null)
                {
                    allParameters[key] = DBNull.Value;
                }
            }
            return new PreparedSqlStatement()
            {
                CommandText = string.Join("\r\n", this.CommandAst.Select(this.SqlBuilder.Build)),
                Parameters = allParameters,
                CommandTimeout = this.CommandTimeout,
                PrimaryKeyColumns = new PrimaryKeyQuerySpecifierCollection((this.TablePrimaryKeyDefinitions?.SelectMany(pk => pk.Value.Select(c => new PrimaryKeyQuerySpecifier(pk.Key, c)).ToList()) ?? new List<PrimaryKeyQuerySpecifier>()).ToList())
            };
        }

        private void ConvertLargeCollectionsToOpenJson(Dictionary<string, object> allParameters)
        {
            var collectionTokens = this.Tokens
                .Where(t => t.InPredicate != null)
                .Select(t =>
                {
                    var prefix = t.SqlParameterName;
                    var matchingParams = allParameters.Keys
                        .Where(k => k.StartsWith(prefix) && k.Length > prefix.Length && char.IsDigit(k[prefix.Length]))
                        .ToList();
                    return new { Token = t, ParamKeys = matchingParams, Count = matchingParams.Count };
                })
                .Where(t => t.Count > 0)
                .OrderByDescending(t => t.Count)
                .ToList();

            foreach (var collectionToken in collectionTokens)
            {
                if (allParameters.Count <= OpenJsonParameterThreshold)
                    break;

                var values = collectionToken.ParamKeys
                    .OrderBy(k => int.Parse(k.Substring(collectionToken.Token.SqlParameterName.Length)))
                    .Select(k => allParameters[k])
                    .ToList();

                foreach (var key in collectionToken.ParamKeys)
                {
                    allParameters.Remove(key);
                }

                var jsonParamName = collectionToken.Token.SqlParameterName + "_json";
                allParameters[jsonParamName] = JsonSerializer.Serialize(values);

                var sqlType = collectionToken.Token.ElementType.GetSqlTypeName();
                var openJsonNode = new OpenJsonSelect
                {
                    ParameterName = jsonParamName,
                    CastType = sqlType
                };

                collectionToken.Token.InPredicate.SetArgs(openJsonNode);
            }
        }

        private void ConvertLargeLookupInsertsToOpenJson(Dictionary<string, object> allParameters)
        {
            if (this.CommandAst == null) return;

            var statements = this.CommandAst.ToList();
            var declaresByName = statements.OfType<DeclareStatement>()
                .Where(d => d.Parameter?.Name != null && d.DataType?.Type?.Value == "table")
                .ToDictionary(d => d.Parameter.Name, d => d);

            var candidates = statements.OfType<Insert>()
                .Select(ins => new { Insert = ins, TableVar = GetInsertTargetParameterName(ins) })
                .Where(c => c.TableVar != null && declaresByName.ContainsKey(c.TableVar))
                .Where(c => c.Insert.ValuesList?.Args != null && c.Insert.ValuesList.Args.Any())
                .Select(c => new
                {
                    c.Insert,
                    c.TableVar,
                    Declare = declaresByName[c.TableVar],
                    ParamCount = CountInsertParameters(c.Insert)
                })
                .Where(c => c.ParamCount > 0)
                .OrderByDescending(c => c.ParamCount)
                .ToList();

            foreach (var candidate in candidates)
            {
                if (allParameters.Count <= OpenJsonParameterThreshold)
                    break;

                RewriteLookupInsertAsOpenJson(candidate.Insert, candidate.TableVar, candidate.Declare, allParameters);
            }
        }

        private static string GetInsertTargetParameterName(Insert insert)
        {
            var tableIdentifier = insert.Object as TableIdentifier;
            var namedParam = tableIdentifier?.Args?.OfType<NamedParameterIdentifier>().FirstOrDefault();
            return namedParam?.Name;
        }

        private static int CountInsertParameters(Insert insert)
        {
            return insert.ValuesList.Args
                .OfType<ValuesList>()
                .Sum(row => row.Args?.OfType<NamedParameterIdentifier>().Count() ?? 0);
        }

        private static Dictionary<string, string> GetColumnSqlTypes(DeclareStatement declare)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (declare?.DataType?.Args == null) return result;
            foreach (var colDecl in declare.DataType.Args.OfType<ColumnDeclaration>())
            {
                var colArgs = colDecl.Args?.ToList();
                if (colArgs == null || colArgs.Count < 2) continue;
                var col = colArgs[0] as RelationalColumn;
                var dataType = colArgs[1] as DataType;
                var typeLiteral = dataType?.Type?.Value;
                if (col?.Label != null && typeLiteral != null)
                {
                    result[col.Label] = typeLiteral;
                }
            }
            return result;
        }

        private static List<string> GetInsertColumnNames(Insert insert)
        {
            var names = new List<string>();
            foreach (var col in insert.ColumnList)
            {
                var ci = col as ColumnIdentifier;
                var rc = ci?.Args?.OfType<RelationalColumn>().FirstOrDefault();
                names.Add(rc?.Label);
            }
            return names;
        }

        private void RewriteLookupInsertAsOpenJson(Insert insert, string tableVar, DeclareStatement declare,
            Dictionary<string, object> allParameters)
        {
            var columnNames = GetInsertColumnNames(insert);
            var columnTypes = GetColumnSqlTypes(declare);

            // Build JSON payload: one object per row, keyed by column name.
            var rows = new List<Dictionary<string, object>>();
            foreach (var row in insert.ValuesList.Args.OfType<ValuesList>())
            {
                var cells = row.Args.ToList();
                var rowDict = new Dictionary<string, object>();
                for (var i = 0; i < columnNames.Count && i < cells.Count; i++)
                {
                    var name = columnNames[i];
                    if (name == null) continue;
                    rowDict[name] = ExtractCellValue(cells[i], allParameters);
                }
                rows.Add(rowDict);
            }

            // Remove old per-cell parameters that belonged to this insert.
            foreach (var row in insert.ValuesList.Args.OfType<ValuesList>())
            {
                foreach (var np in row.Args.OfType<NamedParameterIdentifier>())
                {
                    allParameters.Remove(np.Name);
                }
            }

            var jsonParamName = tableVar + "_json";
            allParameters[jsonParamName] = JsonSerializer.Serialize(rows);

            var openJsonColumns = columnNames
                .Where(n => n != null)
                .Select(n => new OpenJsonTableColumn
                {
                    Name = n,
                    SqlType = columnTypes.TryGetValue(n, out var t) ? t : "nvarchar(max)"
                })
                .ToList();

            var openJsonTable = new OpenJsonTable
            {
                ParameterName = jsonParamName,
                Columns = openJsonColumns
            };

            // Replace the VALUES (...) body with: SELECT col1, col2, ... FROM openjson(@json) WITH (...)
            insert.ValuesList = null;
            insert.SetArgs(new Select()
            {
                SelectClause = new SelectClause().SetArgs(
                    columnNames.Where(n => n != null).Select(n =>
                        (AstNode)new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = n })
                    ).ToList()
                ),
                FromClause = new FromClause().SetArgs(
                    new FromClauseNode().SetArgs(openJsonTable)
                )
            });
        }

        private static object ExtractCellValue(AstNode cell, Dictionary<string, object> allParameters)
        {
            switch (cell)
            {
                case NamedParameterIdentifier np:
                    if (!allParameters.TryGetValue(np.Name, out var v)) return null;
                    return v is DBNull ? null : v;
                case Literal lit:
                    if (lit.Value == null) return null;
                    if (int.TryParse(lit.Value, out var i)) return i;
                    if (string.Equals(lit.Value, "null", StringComparison.OrdinalIgnoreCase)) return null;
                    return lit.Value;
                default:
                    return null;
            }
        }

        internal IEnumerable<AstNode> CommandAst { get; set; }
        public Type UnwrappedReturnType { get; set; }
        public Type ReturnType { get; set; }
        public IEnumerable<ParameterPath> Parameters { get; set; }
        // these are string literal replacements, for non parameterized variables
        // sent through method arguments. For example, the direction of the order by clause
        public IEnumerable<TokenPath> Tokens { get; set; }
        public ITableKeyDefinition TargetTablePrimaryKey { get; set; }
        public IDictionary<string, IEnumerable<string>> TablePrimaryKeyDefinitions { get; set; }
        internal SqlStatementBuilder SqlBuilder { get; set; }
        internal bool IsTotalCountWithResult { get; set; }
        /// <summary>
        /// The command timeout, in seconds, to apply when executing this statement. Sourced from the
        /// method's [Command(Timeout = n)] attribute. Null means use the provider/global default.
        /// </summary>
        public int? CommandTimeout { get; set; }

        public IDictionary<string, object> GetParameters(IEnumerable<ParameterArg> methodArgs)
        {
            var joinedParams = this.Parameters.Join(methodArgs, pp => pp.Parameter, pa => pa.Parameter, (pp, pa) => new { Path = pp, Arg = pa }).ToList();
            var dbParameters = new Dictionary<string, object>();
            foreach (var joinedParam in joinedParams)
            {
                var parameterName = joinedParam.Path.SqlParameterName;
                object value = GetValueForParameterPath(joinedParam.Arg.Value, joinedParam.Path.Properties);

                dbParameters[parameterName] = value;
            }

            return dbParameters;
        }

        public IDictionary<string, object> ParseTokens(IEnumerable<ParameterArg> parameterArgs)
        {
            var joinedParams = this.Tokens.Join(parameterArgs, pp => pp.Parameter, pa => pa.Parameter, (pp, pa) => new { Path = pp, Arg = pa }).ToList();
            var allParameters = new Dictionary<string, object>();
            foreach (var joinedParam in joinedParams)
            {
                object value = GetValueForParameterPath(joinedParam.Arg.Value, joinedParam.Path.Properties);

                var newParameters = joinedParam.Path.UpdateNodeFunc.Invoke(value, joinedParam.Path, parameterArgs);
                allParameters = MergeParameters(allParameters, newParameters);
            }

            return allParameters;
        }

        private static Dictionary<string, object> MergeParameters(IDictionary<string, object> allParameters, IDictionary<string, object> newParameters)
        {
            var addedParameters = newParameters.Where(p => !allParameters.ContainsKey(p.Key)).ToList();
            var updateParameters = newParameters.Where(p => allParameters.ContainsKey(p.Key)).ToList();
            var concatenatedParameters = allParameters.Concat(addedParameters).ToDictionary(p => p.Key, p => p.Value);
            foreach (var parameter in updateParameters)
            {
                concatenatedParameters[parameter.Key] = parameter.Value;
            }
            return concatenatedParameters;
        }

        internal static object GetValueForParameterPath(object value, IEnumerable<PropertyInfo> propertyPaths)
        {
            if (propertyPaths != null && Enumerable.Any<PropertyInfo>(propertyPaths))
            {
                foreach (var propertyInfo in propertyPaths)
                {
                    value = propertyInfo.GetMethod.Invoke(value, null);
                }
            }

            var parameterProvider = value as IDatabaseParameterValueProvider;
            if (parameterProvider != null)
            {
                value = parameterProvider.SqlValue;
            }

            return value;
        }

        internal static IEnumerable<object> GetFlattenedValuesForCollectionParameterPath(object value, Type valueType, IEnumerable<PropertyInfo> propertyPaths)
        {
            return !propertyPaths.Any() ? value.AsEnumerable() : value.AsEnumerable().SelectMany(v =>
            {
                var propertyInfo = propertyPaths.First();
                return GetFlattenedValuesForCollectionParameterPath(
                    propertyInfo.GetMethod.Invoke(v, null), propertyInfo.PropertyType,
                    propertyPaths.Skip(1).ToList());
            });
            //var values = new List<object>();
            //// this is a non-collection type, just return the value
            //if (!(value?.GetType().IsCollectionType()).GetValueOrDefault(false))
            //{
            //    values.Add(value);

            //    return values;
            //}
            //else if (propertyPaths.Count() == 0)
            //{
            //    // this is likely a primitive collection type, return the values
            //    return values;
            //}

            //if (propertyPaths != null && Enumerable.Any<PropertyInfo>(propertyPaths))
            //{
            //    foreach (var propertyInfo in propertyPaths)
            //    {
            //        value = propertyInfo.GetMethod.Invoke(value, null);
            //    }
            //}

            //var parameterProvider = value as IDatabaseParameterValueProvider;
            //if (parameterProvider != null)
            //{
            //    value = parameterProvider.SqlValue;
            //}

            //return value;
        }
    }
}