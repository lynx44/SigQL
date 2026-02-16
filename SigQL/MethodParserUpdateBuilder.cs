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
                    new RelationalTable() { Label = primaryTable.Name }, tableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.WhereClauseExcludingSet).AsEnumerable(), parameterPaths,
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
                            BuildSetValueExpression(c, primaryTable.Name)
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

        private AstNode BuildSetValueExpression(UpdateColumnParameter c, string targetTableName)
        {
            var paramNode = new NamedParameterIdentifier()
            {
                Name = c.ParameterPath.SqlParameterName
            };

            if (c.IgnoreIfNullOrEmpty)
            {
                return new Function() { Name = "IsNull" }.SetArgs(
                    new Function() { Name = "NullIf" }.SetArgs(
                        paramNode,
                        new Literal() { Value = "''" }
                    ),
                    new ColumnIdentifier().SetArgs(new RelationalTable() { Label = targetTableName }, new RelationalColumn() { Label = c.Column.Name })
                );
            }

            if (c.IgnoreIfNull)
            {
                return new Function() { Name = "IsNull" }.SetArgs(
                    paramNode,
                    new ColumnIdentifier().SetArgs(new RelationalTable() { Label = targetTableName }, new RelationalColumn() { Label = c.Column.Name })
                );
            }

            return paramNode;
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
                    var parentIgnoreIfNull = c.GetCustomAttribute<IgnoreIfNullAttribute>() != null;
                    var parentIgnoreIfNullOrEmpty = c.GetCustomAttribute<IgnoreIfNullOrEmptyAttribute>() != null;
                    if (this.databaseResolver.IsTableOrTableProjection(c.Type))
                    {
                        return c.ClassProperties.Select(pr => new UpdateColumnParameter()
                        {
                            Column = updateSpec.Table.Columns.FindByName(pr.Name),
                            ParameterPath = new ParameterPath(pr)
                            {
                                SqlParameterName = $"{c.Name}{pr.Name}"
                            },
                            IgnoreIfNull = parentIgnoreIfNull || pr.GetCustomAttribute<IgnoreIfNullAttribute>() != null,
                            IgnoreIfNullOrEmpty = parentIgnoreIfNullOrEmpty || pr.GetCustomAttribute<IgnoreIfNullOrEmptyAttribute>() != null
                        });
                    }
                    return new UpdateColumnParameter()
                    {
                        Column = updateSpec.Table.Columns.FindByName(c.Name),
                        ParameterPath = new ParameterPath(c)
                        {
                            SqlParameterName = c.Name
                        },
                        IgnoreIfNull = parentIgnoreIfNull,
                        IgnoreIfNullOrEmpty = parentIgnoreIfNullOrEmpty
                    }.AsEnumerable();
                }).ToList();

                var filterParameters = arguments.Except(setParameters).ToList();

                // Handle mixed class parameters: classes containing both [Set] and non-[Set] properties
                foreach (var filterParam in filterParameters)
                {
                    if (this.databaseResolver.IsTableOrTableProjection(filterParam.Type))
                    {
                        var setProperties = filterParam.ClassProperties
                            .Where(cp => cp.GetCustomAttribute<SetAttribute>() != null)
                            .ToList();

                        if (setProperties.Any())
                        {
                            var parentIgnoreIfNull = filterParam.GetCustomAttribute<IgnoreIfNullAttribute>() != null;
                            var parentIgnoreIfNullOrEmpty = filterParam.GetCustomAttribute<IgnoreIfNullOrEmptyAttribute>() != null;

                            foreach (var setProp in setProperties)
                            {
                                updateSpec.SetColumnParameters.Add(new UpdateColumnParameter()
                                {
                                    Column = updateSpec.Table.Columns.FindByName(setProp.Name),
                                    ParameterPath = new ParameterPath(setProp)
                                    {
                                        SqlParameterName = $"{filterParam.Name}{setProp.Name}"
                                    },
                                    IgnoreIfNull = parentIgnoreIfNull || setProp.GetCustomAttribute<IgnoreIfNullAttribute>() != null,
                                    IgnoreIfNullOrEmpty = parentIgnoreIfNullOrEmpty || setProp.GetCustomAttribute<IgnoreIfNullOrEmptyAttribute>() != null
                                });
                            }
                        }
                    }
                }

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
            public bool IgnoreIfNull { get; set; }
            public bool IgnoreIfNullOrEmpty { get; set; }
        }
    }
}
