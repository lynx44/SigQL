using System;
using System.Collections;
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
        private MethodSqlStatement BuildInsertStatement(InsertSpec insertSpec, List<ParameterPath> parameterPaths)
        {
            var targetTableType = insertSpec.UnwrappedReturnType;

            var valuesListClause = new ValuesListClause();
            var statement = new List<AstNode>();
            var tablePrimaryKeyDefinitions = new ConcurrentDictionary<string, ITableKeyDefinition>();

            var insertColumnList = insertSpec.ColumnParameters.Select(cp => 
                new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = cp.Column.Name })).ToList();
            
            var tokens = new List<TokenPath>();
            var multipleInsertParameters = insertSpec.ColumnParameters.Where(c =>
            {
                return this.databaseResolver.IsTableOrTableProjection(c.ParameterPath.Parameter.ParameterType) && TableEqualityComparer.Default.Equals(
                           this.databaseResolver.DetectTable(c.ParameterPath.Parameter.ParameterType),
                           insertSpec.Table);
            }).ToList();
            if (multipleInsertParameters.Any(p => p.ParameterPath.Parameter != insertSpec.ColumnParameters.First().ParameterPath.Parameter))
            {
                throw new InvalidOperationException($"Only one parameter can represent multiple inserts for target table {insertSpec.Table.Name}");
            }
            if (!multipleInsertParameters.Any(p => p.ParameterPath.Parameter.ParameterType.IsCollectionType()) && insertSpec.RootMethodInfo.ReturnType == typeof(void))
            {
                valuesListClause.SetArgs(
                    new ValuesList().SetArgs(
                        insertSpec.ColumnParameters.Select(cp => 
                            new NamedParameterIdentifier()
                            {
                                Name = cp.ParameterPath.SqlParameterName
                            })
                    )
                );
                statement.Add(new Insert()
                {
                    Object = new TableIdentifier().SetArgs(new RelationalTable() { Label = insertSpec.Table.Name }),
                    ColumnList = 
                        insertColumnList,
                    ValuesList = valuesListClause
                });
            } 
            else
            {
                var mergeIndexColumnName = "_index";
                var mergeTableAlias = "i";

                var insertColumnParameter = multipleInsertParameters.FirstOrDefault();
                

                var lookupParameterTableName = $"{insertSpec.RelationalPrefix}{insertSpec.Table.Name}Lookup";
                var declareLookupParameterStatement = new DeclareStatement()
                {
                    Parameter = new NamedParameterIdentifier() { Name = lookupParameterTableName },
                    DataType = new DataType() { Type = new Literal() { Value = "table" } }
                        .SetArgs(
                            insertSpec.ColumnParameters.Select(c =>
                                new ColumnDeclaration().SetArgs(
                                    new RelationalColumn() { Label = c.Column.Name },
                                    new DataType() { Type = new Literal() { Value = c.Column.DataTypeDeclaration } }
                                )
                            ).Concat(new ColumnDeclaration().SetArgs(
                                new RelationalColumn() { Label = mergeIndexColumnName },
                                new DataType() { Type = new Literal() { Value = "int" } }
                            ).AsEnumerable())
                        )
                };
                statement.Insert(0, declareLookupParameterStatement);
                
                var mergeValuesParametersList = new ValuesListClause();
                var lookupParameterTableInsert = new Insert()
                {
                    Object = new TableIdentifier().SetArgs(new NamedParameterIdentifier() { Name = lookupParameterTableName }),
                    ColumnList = insertSpec.ColumnParameters.Select(c =>
                        new ColumnIdentifier().SetArgs(
                            new RelationalColumn() {Label = c.Column.Name}
                        )).AppendOne(new ColumnIdentifier().SetArgs(
                        new RelationalColumn() {Label = mergeIndexColumnName }
                    )).ToList(),
                    ValuesList = mergeValuesParametersList 
                };

                statement.Add(lookupParameterTableInsert);

                var merge = new Merge()
                {
                    Table = new TableIdentifier().SetArgs(new RelationalTable() {Label = insertSpec.Table.Name}),
                    Using = new MergeUsing()
                    {
                        Values = 
                            new Select()
                            {
                                SelectClause = new SelectClause().SetArgs(
                                    insertSpec.ColumnParameters.Select(c =>
                                        new ColumnIdentifier().SetArgs(
                                            new RelationalColumn() { Label = c.Column.Name }
                                        )
                                    ).AppendOne(new ColumnIdentifier().SetArgs(
                                            new RelationalColumn() { Label = mergeIndexColumnName }
                                    ))),
                                FromClause = 
                                    new FromClause().SetArgs(
                                        new FromClauseNode().SetArgs(
                                            new TableIdentifier().SetArgs(
                                                new NamedParameterIdentifier() { Name = lookupParameterTableName})))
                            },
                        As = new TableAliasDefinition() {Alias = mergeTableAlias}
                            .SetArgs(
                                insertSpec.ColumnParameters.Select(cp =>
                                    new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() {Label = cp.Column.Name }
                                    )
                                ).AppendOne(
                                    new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() { Label = mergeIndexColumnName })
                                )
                            )
                    },
                    On = new EqualsOperator().SetArgs(
                        new Literal() {Value = "1"},
                        new Literal() {Value = "0"}
                    ),
                    WhenNotMatched = new WhenNotMatched()
                    {
                        Insert = new MergeInsert()
                        {
                            ColumnList = insertColumnList,
                            ValuesList = valuesListClause
                        }
                    }
                };

                valuesListClause.SetArgs(
                    new ValuesList().SetArgs(
                        insertSpec.ColumnParameters.Select(cp =>
                            new ColumnIdentifier().SetArgs(
                                new RelationalTable() { Label = mergeTableAlias }, 
                                new RelationalColumn() { Label = cp.Column.Name }
                                )
                        )
                    )
                );

                var tokenPath = new TokenPath(insertColumnParameter.ParameterPath.Argument)
                {
                    SqlParameterName = insertColumnParameter.ParameterPath.SqlParameterName,
                    UpdateNodeFunc = (parameterValue, tokenPath) =>
                    {
                        var enumerable = parameterValue is IEnumerable ? parameterValue as IEnumerable : parameterValue.AsEnumerable();
                        var sqlParameters = new Dictionary<string, object>();
                        var allItems = enumerable?.Cast<object>();
                        if (allItems != null && allItems.Any())
                        {
                            mergeValuesParametersList.SetArgs(allItems.Select((item, i) =>
                            {
                                return new ValuesList().SetArgs(
                                    insertSpec.ColumnParameters.Select(cp =>
                                    {
                                        if(i == 0)
                                            parameterPaths.RemoveAll(p => p.SqlParameterName == cp.ParameterPath.SqlParameterName);
                                        var sqlParameterName = $"{insertSpec.RelationalPrefix}{cp.ParameterPath.SqlParameterName}{i}";
                                        var parameterValue = MethodSqlStatement.GetValueForParameterPath(item, cp.ParameterPath.Properties);
                                        sqlParameters[sqlParameterName] = parameterValue;
                                        return new NamedParameterIdentifier()
                                        {
                                            Name = sqlParameterName
                                        };
                                    }).Cast<AstNode>().AppendOne(new Literal() { Value = i.ToString() })
                                );
                            }));
                        }
                        else
                        {
                            throw new ArgumentException($"Unable to insert items for {insertSpec.Table.Name} (via method {insertSpec.RootMethodInfo.Name}) from null or empty list.");
                        }

                        return sqlParameters;
                    }
                };
                
                tokens.Add(tokenPath);
                statement.Add(merge);

                if (insertSpec.ReturnType != typeof(void))
                {
                    var outputParameterTableName = $"inserted{insertSpec.RelationalPrefix}{insertSpec.Table.Name}";
                    var declareOutputParameterStatement = new DeclareStatement()
                    {
                        Parameter = new NamedParameterIdentifier() { Name = outputParameterTableName },
                        DataType = new DataType() { Type = new Literal() { Value = "table" } }
                            .SetArgs(
                                insertSpec.Table.PrimaryKey.Columns.Select(c =>
                                    new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() { Label = c.Name },
                                        new DataType() { Type = new Literal() { Value = c.DataTypeDeclaration } }
                                    )
                                ).Concat(new ColumnDeclaration().SetArgs(
                                    new RelationalColumn() { Label = mergeIndexColumnName },
                                    new DataType() { Type = new Literal() { Value = "int" } }
                                ).AsEnumerable())
                            )
                    };
                    statement.Insert(0, declareOutputParameterStatement);

                    var insertedTableName = "inserted";
                    merge.WhenNotMatched.Insert.Output = new OutputClause()
                    {
                        Into = new IntoClause() {Object = new NamedParameterIdentifier() {Name = outputParameterTableName}}.SetArgs(
                            insertSpec.Table.PrimaryKey.Columns.Select(c => 
                            new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = c.Name}))
                                .AppendOne(
                                    new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = mergeIndexColumnName })))
                    }.SetArgs(
                        insertSpec.Table.PrimaryKey.Columns.Select(c =>
                        new ColumnIdentifier().SetArgs(new RelationalTable() {Label = insertedTableName},
                            new RelationalColumn() {Label = c.Name}))
                            .AppendOne(
                                new ColumnIdentifier().SetArgs(
                                    new RelationalTable() { Label = mergeTableAlias },
                                    new RelationalColumn() { Label = mergeIndexColumnName }
                                    ))
                        );

                    var selectClauseBuilder = new SelectClauseBuilder(this.databaseResolver);
                    var resolvedSelectClause = selectClauseBuilder.Build(targetTableType);
                    var fromClauseRelations = resolvedSelectClause.FromClauseRelations;
                    var selectClause = resolvedSelectClause.Ast;

                    var fromClauseNode = BuildFromClause(fromClauseRelations, new List<IArgument>());
                    
                    var primaryTable = fromClauseRelations.TargetTable;
                    var outputParameterTableSelectAlias = "i";
                    fromClauseNode.SetArgs(fromClauseNode.Args.AppendOne(new InnerJoin()
                    {
                        RightNode =
                            new TableIdentifier().SetArgs(new Alias() { Label = outputParameterTableSelectAlias }.SetArgs(new NamedParameterIdentifier() {Name = outputParameterTableName }))
                    }.SetArgs(
                        primaryTable.PrimaryKey.Columns.Select(pks =>
                            new AndOperator().SetArgs(
                                new EqualsOperator().SetArgs(
                                    new ColumnIdentifier().SetArgs(new RelationalTable() {Label = fromClauseRelations.Alias},
                                        new RelationalColumn() {Label = pks.Name}),
                                    new ColumnIdentifier().SetArgs(new RelationalTable() { Label = outputParameterTableSelectAlias }, new RelationalColumn() {Label = pks.Name})
                                )))
                    )));
                    var fromClause = new FromClause().SetArgs(fromClauseNode);

                    var selectStatement = new Select()
                    {
                        SelectClause = selectClause,
                        FromClause = fromClause,
                        OrderByClause = new OrderByClause().SetArgs(
                            primaryTable.PrimaryKey.Columns.Select(pks => 
                                    new OrderByIdentifier().SetArgs(
                                        new ColumnIdentifier().SetArgs(new RelationalTable() { Label = outputParameterTableSelectAlias }, new RelationalColumn() { Label = mergeIndexColumnName })
                                    )
                                )
                            )
                    };

                    statement.Add(selectStatement);
                }

                var manyTables = insertSpec.TableRelations.NavigationTables.Where(nt =>
                    TableEqualityComparer.Default.Equals(nt.ForeignKeyToParent.PrimaryKeyTable, insertSpec.Table)).ToList();

                var manyTableInsertSpecs = manyTables.Select(t => new InsertSpec()
                {
                    Table = t.TargetTable,
                    TableRelations = t,
                    ColumnParameters = t.ForeignKeyToParent.KeyPairs.Select(c => new InsertColumnParameter()
                    {
                        Column = c.ForeignTableColumn,
                        ParameterPath = new ParameterPath(t.Argument)
                        {
                            SqlParameterName = $"{insertSpec.RelationalPrefix}{t.TargetTable.Name}"
                        }
                    }).ToList(),
                    RelationalPrefix = insertSpec.Table.Name,
                    ReturnType = typeof(void),
                    UnwrappedReturnType = typeof(void),
                    RootMethodInfo = insertSpec.RootMethodInfo
                }).ToList();

                // parameterPaths.AddRange(manyTableInsertSpecs.SelectMany(m => m.ColumnParameters.Select(c => c.ParameterPath)).ToList());
                var methodSqlStatements = manyTableInsertSpecs.Select(tis =>
                    BuildInsertStatement(tis, parameterPaths)).ToList();

                statement.AddRange(methodSqlStatements.SelectMany(mst => mst.CommandAst));
                tokens.AddRange(methodSqlStatements.SelectMany(mst => mst.Tokens));
            }

            var sqlStatement = new MethodSqlStatement()
            {
                CommandAst = statement,
                SqlBuilder = this.builder,
                ReturnType = insertSpec.ReturnType,
                UnwrappedReturnType = targetTableType,
                Parameters = parameterPaths,
                Tokens = tokens,
                TargetTablePrimaryKey = insertSpec.Table.PrimaryKey,
                TablePrimaryKeyDefinitions = tablePrimaryKeyDefinitions
            };

            return sqlStatement;
        }

        private bool IsInsertMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(InsertAttribute), false)?.Any()).GetValueOrDefault(false);
        }

        private InsertSpec GetInsertSpec(MethodInfo methodInfo)
        {
            throw new NotImplementedException();
            //var insertAttribute = methodInfo.GetCustomAttributes(typeof(InsertAttribute), false).Cast<InsertAttribute>().FirstOrDefault();
            //if (insertAttribute != null)
            //{
            //    var insertSpec = new InsertSpec();
            //    if (!string.IsNullOrEmpty(insertAttribute.TableName))
            //    {
            //        insertSpec.Table = this.databaseConfiguration.Tables.FindByName(insertAttribute.TableName);
            //    }
               
            //    var methodParameters = methodInfo.GetParameters();
            //    var tableTypeParameters = methodParameters.Where(p => this.databaseResolver.IsTableOrTableProjection(OutputFactory.UnwrapType(p.ParameterType)));
            //    if (tableTypeParameters.Any())
            //    {
            //        if (methodParameters.Length > 1 || tableTypeParameters.Count() > 1)
            //        {
            //            throw new InvalidOperationException("Only one table representation parameter is supported.");
            //        }
                    
            //        var parameterInfo = tableTypeParameters.Single();
            //        var tableRelations = this.databaseResolver.BuildTableRelations(this.databaseResolver.ToArgumentContainer(parameterInfo.ParameterType), TableRelationsColumnSource.Parameters);
            //        insertSpec.ColumnParameters = tableRelations.ProjectedColumns.SelectMany(pc =>
            //            pc.Arguments.All.Select(arg =>
            //                new InsertColumnParameter()
            //                {
            //                    Column = pc,
            //                    ParameterPath = new ParameterPath(arg)
            //                    {
            //                        SqlParameterName = pc.Name
            //                    }
            //                })
            //            ).ToList();
            //        if (insertSpec.Table == null)
            //        {
            //            insertSpec.Table = tableRelations.TargetTable;
            //        }

            //        insertSpec.TableRelations = tableRelations;
            //    }
            //    else
            //    {
            //        insertSpec.ColumnParameters = 
            //            methodParameters.AsArguments(this.databaseResolver)
            //                .Select(p => 
            //                    new InsertColumnParameter()
            //                    {
            //                        Column = insertSpec.Table.Columns.FindByName(p.Name),
            //                        ParameterPath = new ParameterPath(p)
            //                        {
            //                            SqlParameterName = p.Name
            //                        }
            //                    }
            //                ).ToList();
            //    }

            //    insertSpec.ReturnType = methodInfo.ReturnType;
            //    insertSpec.UnwrappedReturnType = OutputFactory.UnwrapType(methodInfo.ReturnType);
            //    insertSpec.RootMethodInfo = methodInfo;

            //    return insertSpec;
            //}

             
            
            //return null;
        }


        private class InsertSpec
        {
            public ITableDefinition Table { get; set; }
            public IList<InsertColumnParameter> ColumnParameters { get; set; }
            public TableRelations TableRelations { get; set; }
            public Type ReturnType { get; set; }
            public Type UnwrappedReturnType { get; set; }
            public MethodInfo RootMethodInfo { get; set; }
            public string RelationalPrefix { get; set; }
        }

        private class InsertColumnParameter
        {
            public IColumnDefinition Column { get; set; }
            public ParameterPath ParameterPath { get; set; }
        }
    }
}
