using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SigQL.Exceptions;
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

            var insertTableRelations = insertSpec.InsertTableRelationsCollection.First();
            var insertColumnList = insertTableRelations.ColumnParameters.Select(cp =>
                new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = cp.Column.Name })).ToList();

            var tokens = new List<TokenPath>();
            var multipleInsertParameters = insertTableRelations.ColumnParameters.Where(c =>
            {
                return this.databaseResolver.IsTableOrTableProjection(c.ParameterPath.Parameter.ParameterType) && TableEqualityComparer.Default.Equals(
                           this.databaseResolver.DetectTable(c.ParameterPath.Parameter.ParameterType),
                           insertSpec.Table);
            }).ToList();
            if (multipleInsertParameters.Any(p => p.ParameterPath.Parameter != insertTableRelations.ColumnParameters.First().ParameterPath.Parameter))
            {
                throw new InvalidOperationException($"Only one parameter can represent multiple inserts for target table {insertSpec.Table.Name}");
            }
            if (!multipleInsertParameters.Any(p => p.ParameterPath.Parameter.ParameterType.IsCollectionType()) && insertSpec.RootMethodInfo.ReturnType == typeof(void))
            {
                valuesListClause.SetArgs(
                    new ValuesList().SetArgs(
                        insertTableRelations.ColumnParameters.Select(cp =>
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


                //var lookupParameterTableName = $"{insertSpec.RelationalPrefix}{insertSpec.Table.Name}Lookup";
                var lookupParameterTableName = insertTableRelations.LookupTableName;
                var declareLookupParameterStatement = new DeclareStatement()
                {
                    Parameter = new NamedParameterIdentifier() { Name = lookupParameterTableName },
                    DataType = new DataType() { Type = new Literal() { Value = "table" } }
                        .SetArgs(
                            insertTableRelations.ColumnParameters.Select(c =>
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
                    ColumnList = insertTableRelations.ColumnParameters.Select(c =>
                        new ColumnIdentifier().SetArgs(
                            new RelationalColumn() { Label = c.Column.Name }
                        )).AppendOne(new ColumnIdentifier().SetArgs(
                        new RelationalColumn() { Label = mergeIndexColumnName }
                    )).ToList(),
                    ValuesList = mergeValuesParametersList
                };

                statement.Add(lookupParameterTableInsert);

                var merge = new Merge()
                {
                    Table = new TableIdentifier().SetArgs(new RelationalTable() { Label = insertSpec.Table.Name }),
                    Using = new MergeUsing()
                    {
                        Values =
                            new Select()
                            {
                                SelectClause = new SelectClause().SetArgs(
                                    insertTableRelations.ColumnParameters.Select(c =>
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
                                                new NamedParameterIdentifier() { Name = lookupParameterTableName })))
                            },
                        As = new TableAliasDefinition() { Alias = mergeTableAlias }
                            .SetArgs(
                                insertTableRelations.ColumnParameters.Select(cp =>
                                    new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() { Label = cp.Column.Name }
                                    )
                                ).AppendOne(
                                    new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() { Label = mergeIndexColumnName })
                                )
                            )
                    },
                    On = new EqualsOperator().SetArgs(
                        new Literal() { Value = "1" },
                        new Literal() { Value = "0" }
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
                        insertTableRelations.ColumnParameters.Select(cp =>
                            new ColumnIdentifier().SetArgs(
                                new RelationalTable() { Label = mergeTableAlias },
                                new RelationalColumn() { Label = cp.Column.Name }
                                )
                        )
                    )
                );

                var tokenPath = new TokenPath(insertColumnParameter.ParameterPath.Argument.FindParameter())
                {
                    SqlParameterName = insertColumnParameter.ParameterPath.SqlParameterName,
                    UpdateNodeFunc = (parameterValue, tokenPath) =>
                    {
                        var enumerable = tokenPath.Argument.Type.IsCollectionType() ? parameterValue as IEnumerable : parameterValue.AsEnumerable();
                        var sqlParameters = new Dictionary<string, object>();
                        var allItems = enumerable?.Cast<object>();
                        if (allItems != null && allItems.Any())
                        {
                            mergeValuesParametersList.SetArgs(allItems.Select((item, i) =>
                            {
                                return new ValuesList().SetArgs(
                                    insertTableRelations.ColumnParameters.Select(cp =>
                                    {
                                        if (i == 0)
                                            parameterPaths.RemoveAll(p => p.SqlParameterName == cp.ParameterPath.SqlParameterName);
                                        var sqlParameterName = $"{cp.ParameterPath.SqlParameterName}{i}";
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
                    var outputParameterTableName = insertTableRelations.InsertedTableName;
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

                    var fromClauseNode = BuildFromClause(fromClauseRelations);
                    
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

                var manyTables = insertTableRelations.TableRelations.NavigationTables.Where(nt =>
                    TableEqualityComparer.Default.Equals(nt.ForeignKeyToParent.PrimaryKeyTable, insertSpec.Table)).ToList();

                var manyTableInsertSpecs = manyTables.Select(t => new InsertSpec()
                {
                    Table = t.TargetTable,
                    InsertTableRelationsCollection = new List<InsertTableRelations>()
                    {
                        new InsertTableRelations() {
                            TableRelations = t,
                            ColumnParameters = t.ForeignKeyToParent.KeyPairs.Select(c => new InsertColumnParameter()
                            {
                                Column = c.ForeignTableColumn,
                                ParameterPath = new ParameterPath(t.Argument)
                                {
                                    SqlParameterName = $"{t.TargetTable.Name}"
                                }
                            }).ToList(),
                        }
                    },
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
            var insertAttribute = methodInfo.GetCustomAttributes(typeof(InsertAttribute), false).Cast<InsertAttribute>().FirstOrDefault();
            if (insertAttribute != null)
            {
                var methodParameters = methodInfo.GetParameters();
                var tableTypeParameters = methodParameters.Where(p => this.databaseResolver.IsTableOrTableProjection(OutputFactory.UnwrapType(p.ParameterType)));
                if (tableTypeParameters.Any())
                {
                    if (methodParameters.Length > 1 || tableTypeParameters.Count() > 1)
                    {
                        throw new InvalidOperationException("Only one table representation parameter is supported.");
                    }
                }

                var insertSpec = new InsertSpec();
                if (!string.IsNullOrEmpty(insertAttribute.TableName))
                {
                    insertSpec.Table = this.databaseConfiguration.Tables.FindByName(insertAttribute.TableName);
                }
                else
                {
                    if (methodInfo.ReturnType != typeof(void))
                    {
                        insertSpec.Table = this.databaseResolver.DetectTable(methodInfo.ReturnType);
                    }
                    else if(this.databaseResolver.IsTableOrTableProjection(methodInfo.GetParameters().First().ParameterType))
                    {
                        insertSpec.Table =
                            this.databaseResolver.DetectTable(methodInfo.GetParameters().First().ParameterType);
                    } 
                    else
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to determine insert table for method {methodInfo.Name}. Method does not specify a table attribute, the return type does not map to a recognized table, and the parameters do not map to a recognized table.");
                    }
                }


                var parameterTableRelations = this.databaseResolver.BuildTableRelations(insertSpec.Table,
                    new TableArgument(insertSpec.Table,
                        methodInfo.GetParameters().AsArguments(this.databaseResolver)),
                    TableRelationsColumnSource.Parameters, new ConcurrentDictionary<string, ITableKeyDefinition>());

                
                insertSpec.InsertTableRelationsCollection = GetInsertTableRelationsOrderedByDependencies(parameterTableRelations).ToList();
                
                insertSpec.ReturnType = methodInfo.ReturnType;
                insertSpec.UnwrappedReturnType = OutputFactory.UnwrapType(methodInfo.ReturnType);
                insertSpec.RootMethodInfo = methodInfo;

                return insertSpec;
            }
            
            return null;
        }

        private InsertTableRelations ToInsertTableRelations(TableRelations parameterTableRelations)
        {
            var insertRelations = new InsertTableRelations();
            insertRelations.ColumnParameters = parameterTableRelations.ProjectedColumns.SelectMany(pc =>
                pc.Arguments.All.Select(arg =>
                    {
                        var parameterPath = arg.ToParameterPath();
                        parameterPath.SqlParameterName = parameterPath.GenerateSuggestedSqlIdentifierName();
                        return new InsertColumnParameter()
                        {
                            Column = pc,
                            ParameterPath = parameterPath
                        };
                    }
                )
            ).ToList();
            insertRelations.TableRelations = parameterTableRelations;

            return insertRelations;
        }

        private IEnumerable<InsertTableRelations> GetInsertTableRelationsOrderedByDependencies(TableRelations root)
        {
            var tableRelationsList = new List<TableRelations>();
            root.Traverse(t => tableRelationsList.Add(t));
            // loop over each and find: 
            // - each table that has no foreign key dependency after
            //    excluding foreign tables that are already in the list

            var orderedTableRelations = new List<TableRelations>();
            while (orderedTableRelations.Count != tableRelationsList.Count)
            {
                var remainingTableRelations = tableRelationsList.Except(orderedTableRelations).ToList();
                remainingTableRelations.ForEach(t =>
                {
                    var hasPendingDependency =
                        HasPendingForeignKey(t.TargetTable, t.ForeignKeyToParent, orderedTableRelations) ||
                        t.NavigationTables.Any(n =>
                            HasPendingForeignKey(t.TargetTable, t.ForeignKeyToParent, orderedTableRelations));
                    if (!hasPendingDependency)
                    {
                        orderedTableRelations.Add(t);
                    }
                });
            }

            return orderedTableRelations.Select(tr => ToInsertTableRelations(tr)).ToList();
        }

        private bool HasPendingForeignKey(ITableDefinition table, IForeignKeyDefinition foreignKey, List<TableRelations> currentList)
        {
            if (foreignKey == null)
            {
                return false;
            }

            var foreignTableKeys = foreignKey.KeyPairs.Where(kp =>
                TableEqualityComparer.Default.Equals(table, kp.ForeignTableColumn.Table)).ToList();
            // check and see if any of these keys have not yet had their dependencies resolved
            if(foreignTableKeys.Any(kp => !currentList.Any(tr => TableEqualityComparer.Default.Equals(kp.PrimaryTableColumn.Table, tr.TargetTable))))
            {
                return true;
            }

            return false;
        }

        private class InsertTableRelations
        {
            public IList<InsertColumnParameter> ColumnParameters { get; set; }
            public TableRelations TableRelations { get; set; }
            public string LookupTableName => TableRelations.Alias.Replace("<", "$").Replace(">", "$") + "Lookup";
            public string InsertedTableName => "inserted" + TableRelations.Alias.Replace("<", "$").Replace(">", "$");
        }

        private class InsertSpec
        {
            public InsertSpec()
            {
                this.InsertTableRelationsCollection = new List<InsertTableRelations>();
            }

            public ITableDefinition Table { get; set; }
            public Type ReturnType { get; set; }
            public Type UnwrappedReturnType { get; set; }
            public MethodInfo RootMethodInfo { get; set; }

            public List<InsertTableRelations> InsertTableRelationsCollection { get; set; }
            public InsertTableRelations Next { get; set; }
        }
        

        private class InsertColumnParameter
        {
            public IColumnDefinition Column { get; set; }
            public ParameterPath ParameterPath { get; set; }
        }
    }
}
