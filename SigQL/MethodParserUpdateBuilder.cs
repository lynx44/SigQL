using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Sql;
using SigQL.Types.Attributes;

namespace SigQL
{
    public partial class MethodParser
    {
        private MethodSqlStatement BuildUpdateStatement(UpdateSpec updateSpec)
        {
            var methodInfo = updateSpec.RootMethodInfo;

            var primaryTable = updateSpec.Table;
            var tokens = new List<TokenPath>();
            var parameterPaths = new List<ParameterPath>();
            WhereClause whereClause = null;
            if (updateSpec.FilterParameters.Any())
            {
                var tableRelations = this.databaseResolver.BuildTableRelations(primaryTable, new TableArgument(primaryTable, updateSpec.FilterParameters.AsArguments(this.databaseResolver)), TableRelationsColumnSource.Parameters, new ConcurrentDictionary<string, IEnumerable<string>>());

                whereClause = BuildWhereClauseFromTargetTablePerspective(
                    new RelationalTable() { Label = primaryTable.Name }, tableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.WhereClause).AsEnumerable(), parameterPaths,
                    tokens);
            }

            var statement = new Update()
            {
                Args = new TableIdentifier().SetArgs(new RelationalTable() { Label = primaryTable.Name }).AsEnumerable(),
                SetClause = updateSpec.SetColumnParameters.Select(c =>
                    new SetEqualOperator().SetArgs(
                            new ColumnIdentifier()
                                .SetArgs(new RelationalColumn()
                                {
                                    Label = c.Column.Name
                                }),
                            new NamedParameterIdentifier()
                            {
                                Name = c.ParameterPath.SqlParameterName
                            }
                        )),
                FromClause = new FromClause().SetArgs(new TableIdentifier().SetArgs(new RelationalTable()
                { Label = primaryTable.Name })),
                WhereClause = whereClause
            };

            parameterPaths.AddRange(updateSpec.SetColumnParameters.Select(c => c.ParameterPath).ToList());

            var sqlStatement = new MethodSqlStatement()
            {
                CommandAst = statement.AsEnumerable(),
                SqlBuilder = this.builder,
                ReturnType = methodInfo.ReturnType,
                UnwrappedReturnType = null,
                Parameters = parameterPaths,
                Tokens = tokens,
                TargetTablePrimaryKey = null,
                TablePrimaryKeyDefinitions = null
            };

            return sqlStatement;
        }

        private UpdateSpec GetUpdateSpec(MethodInfo methodInfo)
        {
            var updateAttribute = methodInfo.GetCustomAttributes(typeof(UpdateAttribute), false).Cast<UpdateAttribute>().FirstOrDefault();
            if (updateAttribute != null)
            {
                var updateSpec = new UpdateSpec();
                if (!string.IsNullOrEmpty(updateAttribute.TableName))
                {
                    updateSpec.Table = this.databaseConfiguration.Tables.FindByName(updateAttribute.TableName);
                }

                var arguments = methodInfo.GetParameters().AsArguments(this.databaseResolver);
                var setParameters = arguments.Where(p => p.GetCustomAttribute<SetAttribute>() != null).ToList();
                
                updateSpec.SetColumnParameters = setParameters.SelectMany(c =>
                {
                    if (this.databaseResolver.IsTableOrTableProjection(c.Type))
                    {
                        return c.ClassProperties.Select(pr => new UpdateColumnParameter()
                        {
                            Column = updateSpec.Table.Columns.FindByName(pr.Name),
                            ParameterPath = new ParameterPath(pr)
                            {
                                SqlParameterName = $"{c.Name}{pr.Name}"
                            }
                        });
                    }
                    return new UpdateColumnParameter()
                    {
                        Column = updateSpec.Table.Columns.FindByName(c.Name),
                        ParameterPath = new ParameterPath(c)
                        {
                            SqlParameterName = c.Name
                        }
                    }.AsEnumerable();
                }).ToList();

                var filterParameters = arguments.Except(setParameters).ToList();
                updateSpec.FilterParameters = filterParameters.Select(c => c.GetParameterInfo()).ToList();

                updateSpec.RootMethodInfo = methodInfo;

                return updateSpec;
            }

            return null;
        }

        private class UpdateSpec
        {
            public ITableDefinition Table { get; set; }
            public IList<UpdateColumnParameter> SetColumnParameters { get; set; }
            public IList<ParameterInfo> FilterParameters { get; set; }
            public MethodInfo RootMethodInfo { get; set; }
        }
        
        private class UpdateColumnParameter
        {
            public IColumnDefinition Column { get; set; }
            public ParameterPath ParameterPath { get; set; }
        }
    }
}
