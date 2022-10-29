using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Castle.Components.DictionaryAdapter;
using SigQL.Exceptions;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Sql;
using SigQL.Types.Attributes;

namespace SigQL
{
    public partial class MethodParser
    {
        private const string MergeIndexColumnName = "_index";

        public class TableIndexReference
        {
            public int? PrimaryTableIndex { get; }
            public List<IColumnDefinition> ForeignColumns { get; }
            public int InsertedIndex { get; set; }

            public TableIndexReference(int? primaryTableIndex, List<IColumnDefinition> foreignColumns, int insertedIndex)
            {
                PrimaryTableIndex = primaryTableIndex;
                ForeignColumns = foreignColumns;
                InsertedIndex = insertedIndex;
            }
        }

        private MethodSqlStatement BuildInsertStatement(InsertSpec insertSpec, List<ParameterPath> parameterPaths)
        {
            var targetTableType = insertSpec.UnwrappedReturnType;
            
            var statement = new List<AstNode>();
            var tablePrimaryKeyDefinitions = new ConcurrentDictionary<string, ITableKeyDefinition>();

            var tokens = new List<TokenPath>();

            var multipleInsertParameters = insertSpec.InsertTableRelationsCollection[0].ColumnParameters.Where(c =>
            {
                return this.databaseResolver.IsTableOrTableProjection(c.ParameterPath.Parameter.ParameterType) &&
                       TableEqualityComparer.Default.Equals(
                           this.databaseResolver.DetectTable(c.ParameterPath.Parameter.ParameterType),
                           insertSpec.Table);
            }).ToList();
            

            for (var index = 0; index < insertSpec.InsertTableRelationsCollection.Count; index++)
            {
                var valuesListClause = new ValuesListClause();
                var insertTableRelations = insertSpec.InsertTableRelationsCollection[index];
                var insertColumnList = insertTableRelations.ColumnParameters.Select(cp =>
                    new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = cp.Column.Name}))
                    .Concat(insertTableRelations.ForeignTableColumns.SelectMany(fk =>
                        fk.ForeignKey.GetForeignColumns().Select(fc =>
                            new ColumnIdentifier().SetArgs(
                                new RelationalColumn() { Label = fc.Name }))
                    ))
                    .ToList();

                
                if (insertTableRelations.ColumnParameters.Any() &&
                    multipleInsertParameters.Any(p =>
                        p.ParameterPath.Parameter !=
                        insertTableRelations.ColumnParameters.First().ParameterPath.Parameter))
                {
                    throw new InvalidOperationException(
                        $"Only one parameter can represent multiple inserts for target table {insertSpec.Table.Name}");
                }

                if (!multipleInsertParameters.Any(p => p.ParameterPath.Parameter.ParameterType.IsCollectionType()) &&
                    insertSpec.RootMethodInfo.ReturnType == typeof(void))
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
                        Object = new TableIdentifier().SetArgs(new RelationalTable() {Label = insertSpec.Table.Name}),
                        ColumnList =
                            insertColumnList,
                        ValuesList = valuesListClause
                    });
                }
                else
                {
                    var mergeTableAlias = "i";

                    var insertColumnParameter = insertTableRelations.ColumnParameters.FirstOrDefault();
                    
                    var lookupParameterTableName = GetLookupTableName(insertTableRelations.TableRelations);
                    var declareLookupParameterStatement = BuildDeclareLookupParameterStatement(lookupParameterTableName, insertTableRelations);
                    statement.Add(declareLookupParameterStatement);

                    var mergeValuesParametersList = new ValuesListClause();

                    var tableColumns = insertTableRelations.ColumnParameters.Select(c =>
                        new ColumnIdentifier().SetArgs(
                            new RelationalColumn() { Label = c.Column.Name }
                        )).AppendOne(new ColumnIdentifier().SetArgs(
                        new RelationalColumn() { Label = MergeIndexColumnName }
                    ));

                    var foreignColumns = insertTableRelations.ForeignTableColumns.SelectMany(fk =>
                        fk.ForeignKey.GetForeignColumns().Select(fc =>
                            new ColumnIdentifier()
                                .SetArgs(new RelationalColumn()
                                {
                                    Label = GetForeignColumnIndexName(fc.Name)
                                })
                        )).ToList();

                    var lookupParameterTableInsertResult = BuildLookupParameterTableInsert(insertSpec, parameterPaths, lookupParameterTableName, tableColumns, foreignColumns, mergeValuesParametersList, insertTableRelations, insertColumnParameter);
                    var lookupParameterTableInsert = lookupParameterTableInsertResult.Item1;
                    var tokenPath = lookupParameterTableInsertResult.Item2;
                    statement.Add(lookupParameterTableInsert);
                    tokens.Add(tokenPath);

                    var merge = new Merge()
                    {
                        Table = new TableIdentifier().SetArgs(new RelationalTable()
                            {Label = insertTableRelations.TableRelations.TableName}),
                        Using = new MergeUsing()
                        {
                            Values =
                                new Select()
                                {
                                    SelectClause = new SelectClause().SetArgs(
                                        insertTableRelations.ColumnParameters.Select(c =>
                                            new ColumnIdentifier().SetArgs(
                                                new RelationalColumn() {Label = c.Column.Name}
                                            )
                                        ).AppendOne(new ColumnIdentifier().SetArgs(
                                            new RelationalColumn() {Label = MergeIndexColumnName}
                                        ))
                                        .Concat(foreignColumns)),
                                    FromClause =
                                        new FromClause().SetArgs(
                                            new FromClauseNode().SetArgs(
                                                new TableIdentifier().SetArgs(
                                                    new NamedParameterIdentifier() {Name = lookupParameterTableName})))
                                },
                            As = new TableAliasDefinition() {Alias = mergeTableAlias}
                                .SetArgs(
                                    insertTableRelations.ColumnParameters.Select(cp =>
                                        (AstNode) new ColumnDeclaration().SetArgs(
                                            new RelationalColumn() {Label = cp.Column.Name}
                                        )
                                    ).AppendOne(
                                        new ColumnDeclaration().SetArgs(
                                            new RelationalColumn() {Label = MergeIndexColumnName})
                                    ).Concat(foreignColumns)
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

                    var foreignSelectValueStatements = insertTableRelations.ForeignTableColumns.SelectMany(fk =>
                        fk.ForeignKey.KeyPairs.Select(kp =>
                        {
                            var selectStatement = new Select();
                            selectStatement.SelectClause =
                                new SelectClause().SetArgs(
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalColumn() {Label = kp.PrimaryTableColumn.Name}));
                            selectStatement.FromClause =
                                new FromClause().SetArgs(
                                    new Alias() {Label = GetLookupTableName(fk.PrimaryTableRelations)}
                                        .SetArgs(new NamedParameterIdentifier()
                                            {Name = GetLookupTableName(fk.PrimaryTableRelations)}));
                            selectStatement.WhereClause =
                                new WhereClause().SetArgs(
                                    new EqualsOperator().SetArgs(

                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable()
                                                {Label = GetLookupTableName(fk.PrimaryTableRelations)},
                                            new RelationalColumn() {Label = MergeIndexColumnName}),
                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable() {Label = mergeTableAlias},
                                            new RelationalColumn() {Label = GetForeignColumnIndexName(kp.ForeignTableColumn.Name) })
                                    )
                                );
                            return new LogicalGrouping().SetArgs(selectStatement);
                        })).ToList();

                    valuesListClause.SetArgs(
                        new ValuesList().SetArgs(
                            insertTableRelations.ColumnParameters.Select(cp =>
                                (AstNode) new ColumnIdentifier().SetArgs(
                                    new RelationalTable() {Label = mergeTableAlias},
                                    new RelationalColumn() {Label = cp.Column.Name}
                                )
                            ).Concat(foreignSelectValueStatements)
                        )
                    );

                    
                    statement.Add(merge);
                    var updateLookupTablePKsStatement = BuildUpdateLookupStatement(insertTableRelations.TableRelations);
                    if(updateLookupTablePKsStatement != null)
                        statement.Add(updateLookupTablePKsStatement);

                    if (insertTableRelations.TableRelations.TargetTable.PrimaryKey != null && (insertSpec.ReturnType != typeof(void) || insertSpec.InsertTableRelationsCollection.Count > 1))
                    {
                        var outputParameterTableName = GetInsertedTableName(insertTableRelations.TableRelations);
                        var declareOutputParameterStatement = BuildDeclareInsertedTableParameterStatement(outputParameterTableName, insertTableRelations);
                        statement.Insert(0, declareOutputParameterStatement);

                        var insertedTableName = "inserted";
                        merge.WhenNotMatched.Insert.Output = new OutputClause()
                        {
                            Into = new IntoClause()
                                {Object = new NamedParameterIdentifier() {Name = outputParameterTableName}}.SetArgs(
                                insertTableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(c =>
                                        new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = c.Name}))
                                    .AppendOne(
                                        new ColumnIdentifier().SetArgs(new RelationalColumn()
                                            {Label = MergeIndexColumnName})))
                        }.SetArgs(
                            insertTableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(c =>
                                    new ColumnIdentifier().SetArgs(new RelationalTable() {Label = insertedTableName},
                                        new RelationalColumn() {Label = c.Name}))
                                .AppendOne(
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalTable() {Label = mergeTableAlias},
                                        new RelationalColumn() {Label = MergeIndexColumnName}
                                    ))
                        );
                        
                    }

                    //var manyTables = insertTableRelations.TableRelations.NavigationTables.Where(nt =>
                    //    TableEqualityComparer.Default.Equals(nt.ForeignKeyToParent.PrimaryKeyTable, insertSpec.Table)).ToList();

                    //var manyTableInsertSpecs = manyTables.Select(t => new InsertSpec()
                    //{
                    //    Table = t.TargetTable,
                    //    InsertTableRelationsCollection = new List<InsertTableRelations>()
                    //    {
                    //        new InsertTableRelations() {
                    //            TableRelations = t,
                    //            ColumnParameters = t.ForeignKeyToParent.KeyPairs.Select(c => new InsertColumnParameter()
                    //            {
                    //                Column = c.ForeignTableColumn,
                    //                ParameterPath = new ParameterPath(t.Argument)
                    //                {
                    //                    SqlParameterName = $"{t.TargetTable.Name}"
                    //                }
                    //            }).ToList(),
                    //        }
                    //    },
                    //    ReturnType = typeof(void),
                    //    UnwrappedReturnType = typeof(void),
                    //    RootMethodInfo = insertSpec.RootMethodInfo
                    //}).ToList();

                    //// parameterPaths.AddRange(manyTableInsertSpecs.SelectMany(m => m.ColumnParameters.Select(c => c.ParameterPath)).ToList());
                    //var methodSqlStatements = manyTableInsertSpecs.Select(tis =>
                    //    BuildInsertStatement(tis, parameterPaths)).ToList();

                    //statement.AddRange(methodSqlStatements.SelectMany(mst => mst.CommandAst));
                    //tokens.AddRange(methodSqlStatements.SelectMany(mst => mst.Tokens));
                }
                
            }


            if (insertSpec.ReturnType != typeof(void))
            {
                
                var tableRelations = this.databaseResolver.BuildTableRelations(insertSpec.Table, new TypeArgument(insertSpec.ReturnType, this.databaseResolver), TableRelationsColumnSource.ReturnType, tablePrimaryKeyDefinitions);
                var matchingInsertTableRelations = insertSpec.InsertTableRelationsCollection
                    .Where(tr =>
                        TableEqualityComparer.Default.Equals(tr.TableRelations.TargetTable, tableRelations.TargetTable))
                    .OrderBy(tr => tr.TableRelations.CalculateDepth())
                    .First();
                var outputParameterTableName = GetInsertedTableName(matchingInsertTableRelations.TableRelations);
                var selectClauseBuilder = new SelectClauseBuilder(this.databaseResolver);
                var resolvedSelectClause = selectClauseBuilder.Build(tableRelations, tablePrimaryKeyDefinitions);
                var fromClauseRelations = resolvedSelectClause.FromClauseRelations;
                var selectClause = resolvedSelectClause.Ast;

                var fromClauseNode = BuildFromClause(fromClauseRelations);

                var primaryTable = fromClauseRelations.TargetTable;
                var outputParameterTableSelectAlias = "i";
                fromClauseNode.SetArgs(fromClauseNode.Args.AppendOne(new InnerJoin()
                {
                    RightNode =
                        new TableIdentifier().SetArgs(
                            new Alias() { Label = outputParameterTableSelectAlias }.SetArgs(
                                new NamedParameterIdentifier() { Name = outputParameterTableName }))
                }.SetArgs(
                    primaryTable.PrimaryKey.Columns.Select(pks =>
                        new AndOperator().SetArgs(
                            new EqualsOperator().SetArgs(
                                new ColumnIdentifier().SetArgs(
                                    new RelationalTable() { Label = fromClauseRelations.Alias },
                                    new RelationalColumn() { Label = pks.Name }),
                                new ColumnIdentifier().SetArgs(
                                    new RelationalTable() { Label = outputParameterTableSelectAlias },
                                    new RelationalColumn() { Label = pks.Name })
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
                                new ColumnIdentifier().SetArgs(
                                    new RelationalTable() { Label = outputParameterTableSelectAlias },
                                    new RelationalColumn() { Label = MergeIndexColumnName })
                            )
                        )
                    )
                };

                statement.Add(selectStatement);
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

        private Tuple<AstNode, TokenPath> BuildLookupParameterTableInsert(InsertSpec insertSpec, List<ParameterPath> parameterPaths, string lookupParameterTableName,
            IEnumerable<ColumnIdentifier> tableColumns, List<ColumnIdentifier> foreignColumns, ValuesListClause mergeValuesParametersList,
            InsertTableRelations insertTableRelations, InsertColumnParameter insertColumnParameter)
        {
            var lookupParameterTableInsert = new Insert()
            {
                Object = new TableIdentifier().SetArgs(new NamedParameterIdentifier()
                    {Name = lookupParameterTableName}),
                ColumnList = tableColumns.Concat(foreignColumns)
                    .ToList(),
                ValuesList = mergeValuesParametersList
            };

            var tokenPath = new TokenPath(FindRootArgument(insertTableRelations.TableRelations.Argument).FindParameter())
            {
                SqlParameterName = insertColumnParameter?.ParameterPath.SqlParameterName,
                UpdateNodeFunc = (parameterValue, tokenPath) =>
                {
                    var orderedParameterLookup = new OrderedParameterValueLookup();
                    OrderParameterValues(orderedParameterLookup, parameterValue,
                        FindRootArgument(insertTableRelations.TableRelations.Argument).FindParameter(), null);

                    IEnumerable<TableIndexReference> parentIndexMappings;
                    var orderedParametersForInsert =
                        orderedParameterLookup.FindOrderedParameters(
                            FindRootArgument(insertTableRelations.TableRelations.Argument));
                    // one-to-many or many-to-one
                    if (FindRootArgument(insertTableRelations.TableRelations.Argument).Type != typeof(void))
                    {
                        parentIndexMappings = GetTableIndexReferences(orderedParametersForInsert, insertTableRelations,
                            orderedParameterLookup);
                    }
                    // many-to-many
                    else
                    {
                        var parentRelations = insertTableRelations.TableRelations.Parent;
                        var navigationTableRelations =
                            insertTableRelations.TableRelations.NavigationTables.Single();
                        // get all the parameter values for the parent and the navigation tables, so we can find the indicies of all
                        // and send them as the parameters of this table
                        var parentOrderedParametersForInsert =
                            orderedParameterLookup.FindOrderedParameters(FindRootArgument(parentRelations.Argument));
                        var navigationOrderedParametersForInsert =
                            orderedParameterLookup.FindOrderedParameters(FindRootArgument(navigationTableRelations.Argument));

                        var parentForeignColumns =
                            insertTableRelations.ForeignTableColumns.SelectMany(ftc =>
                                    ftc.ForeignKey.KeyPairs.Where(kp =>
                                            TableEqualityComparer.Default.Equals(kp.PrimaryTableColumn.Table,
                                                parentRelations.TargetTable))
                                        .Select(kp => kp.ForeignTableColumn))
                                .ToList();
                        var navigationForeignColumns =
                            insertTableRelations.ForeignTableColumns.SelectMany(ftc =>
                                    ftc.ForeignKey.KeyPairs.Where(kp =>
                                            TableEqualityComparer.Default.Equals(kp.PrimaryTableColumn.Table,
                                                navigationTableRelations.TargetTable))
                                        .Select(kp => kp.ForeignTableColumn))
                                .ToList();

                        // walk through both sides of the relations and collect the indecies in relation to each other
                        var parentToNavigationIndecies = parentOrderedParametersForInsert.Select((op, i) =>
                        {
                            var primaryTableKeyIndex = orderedParameterLookup.FindArgumentIndex(op,
                                FindRootArgument(parentRelations.Argument), FindRootArgument(navigationTableRelations.Argument),
                                ForeignTablePropertyDirection.Navigation);
                            return new TableIndexReference(primaryTableKeyIndex, parentForeignColumns, op.Index);
                        }).ToList();
                        var navigationToParentIndecies = navigationOrderedParametersForInsert.Select((op, i) =>
                        {
                            var primaryTableKeyIndex = orderedParameterLookup.FindArgumentIndex(op,
                                FindRootArgument(navigationTableRelations.Argument), FindRootArgument(parentRelations.Argument),
                                ForeignTablePropertyDirection.Parent);
                            return new TableIndexReference(primaryTableKeyIndex, navigationForeignColumns, op.Index);
                        }).ToList();

                        var parentToNavigationValues = parentToNavigationIndecies.Join(navigationToParentIndecies,
                            p => p.PrimaryTableIndex,
                            n => n.InsertedIndex, (p, n) => new {Parent = p, Navigation = n}).ToList();

                        var navigationToParentValues = navigationToParentIndecies.Join(parentToNavigationIndecies,
                            p => p.PrimaryTableIndex,
                            n => n.InsertedIndex, (n, p) => new {Parent = p, Navigation = n}).ToList();

                        // filter down to distinct values, since either side can hold an uneven number of values
                        var distinctValues = parentToNavigationValues.Concat(navigationToParentValues).Distinct().ToList();

                        // decouple the distinct column pairs into individual rows, and order 
                        // them for the many-to-many lookup table variable
                        parentIndexMappings = distinctValues.Select((v, i) =>
                        {
                            return new[]
                            {
                                new TableIndexReference(v.Parent.InsertedIndex, v.Parent.ForeignColumns, i),
                                new TableIndexReference(v.Navigation.InsertedIndex, v.Navigation.ForeignColumns, i)
                            };
                        }).SelectMany(v => v).ToList();

                        // fake the data for this table by padding it with the same number of indexed rows from above.
                        // since there is no row data, only the Foreign Columns will be populated
                        orderedParametersForInsert = parentIndexMappings.Select(p => p.InsertedIndex).Distinct().Select(p =>
                            new OrderedParameterValue()
                            {
                                Index = p
                            }).ToList();
                    }


                    //var enumerable = tokenPath.Argument.Type.IsCollectionType()
                    //    ? parameterValue as IEnumerable
                    //    : parameterValue.AsEnumerable();
                    var sqlParameters = new Dictionary<string, object>();
                    //var allParameters = enumerable?.Cast<object>();
                    if (orderedParametersForInsert.Any() || parentIndexMappings.Any())
                    {
                        mergeValuesParametersList.SetArgs(orderedParametersForInsert.Select((param, i) =>
                        {
                            return new ValuesList().SetArgs(
                                insertTableRelations.ColumnParameters.Select(cp =>
                                    {
                                        if (i == 0)
                                            parameterPaths.RemoveAll(p =>
                                                p.SqlParameterName == cp.ParameterPath.SqlParameterName);
                                        var sqlParameterName = $"{cp.ParameterPath.SqlParameterName}{param.Index}";
                                        var parameterValue = param.Value;
                                        sqlParameters[sqlParameterName] =
                                            MethodSqlStatement.GetValueForParameterPath(parameterValue,
                                                cp.ParameterPath.Properties.Last().AsEnumerable());
                                        return new NamedParameterIdentifier()
                                        {
                                            Name = sqlParameterName
                                        };
                                    }).Cast<AstNode>().AppendOne(new Literal() {Value = param.Index.ToString()})
                                    .Concat(
                                        OrderIndexReferences(insertTableRelations, parentIndexMappings)
                                            .Where(p => p.InsertedIndex == param.Index)
                                            .Select(p =>
                                                new Literal() {Value = p.PrimaryTableIndex.ToString()}).ToList()));
                        }));
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unable to insert items for {insertSpec.Table.Name} (via method {insertSpec.RootMethodInfo.Name}) from null or empty list.");
                    }

                    return sqlParameters;
                }
            };
            return new Tuple<AstNode, TokenPath>(lookupParameterTableInsert, tokenPath);
        }

        private static DeclareStatement BuildDeclareInsertedTableParameterStatement(string outputParameterTableName,
            InsertTableRelations insertTableRelations)
        {
            var declareOutputParameterStatement = new DeclareStatement()
            {
                Parameter = new NamedParameterIdentifier() {Name = outputParameterTableName},
                DataType = new DataType() {Type = new Literal() {Value = "table"}}
                    .SetArgs(
                        insertTableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(c =>
                            new ColumnDeclaration().SetArgs(
                                new RelationalColumn() {Label = c.Name},
                                new DataType() {Type = new Literal() {Value = c.DataTypeDeclaration}}
                            )
                        ).Concat(new ColumnDeclaration().SetArgs(
                            new RelationalColumn() {Label = MergeIndexColumnName},
                            new DataType() {Type = new Literal() {Value = "int"}}
                        ).AsEnumerable())
                    )
            };
            return declareOutputParameterStatement;
        }

        private static DeclareStatement BuildDeclareLookupParameterStatement(string lookupParameterTableName,
            InsertTableRelations insertTableRelations)
        {
            List<AstNode> primaryKeyColumns = new List<AstNode>();
            if ((insertTableRelations.TableRelations.TargetTable.PrimaryKey?.Columns.Any()).GetValueOrDefault(false))
            {
                var unselectedPrimaryKeys = insertTableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Where(c =>
                    !insertTableRelations.ColumnParameters.Any(
                        ic => ColumnEqualityComparer.Default.Equals(ic.Column, c)));
                primaryKeyColumns = unselectedPrimaryKeys.Select(c =>
                    new ColumnDeclaration().SetArgs(
                        new RelationalColumn() {Label = c.Name},
                        new DataType() {Type = new Literal() {Value = c.DataTypeDeclaration}}
                    )
                ).ToList<AstNode>();
            }

            var declareLookupParameterStatement = new DeclareStatement()
            {
                Parameter = new NamedParameterIdentifier() {Name = lookupParameterTableName},
                DataType = new DataType() {Type = new Literal() {Value = "table"}}
                    .SetArgs(
                        primaryKeyColumns.Concat(
                        insertTableRelations.ColumnParameters.Select(c =>
                                new ColumnDeclaration().SetArgs(
                                    new RelationalColumn() {Label = c.Column.Name},
                                    new DataType() {Type = new Literal() {Value = c.Column.DataTypeDeclaration}}
                                )
                            ).Concat(new ColumnDeclaration().SetArgs(
                                new RelationalColumn() {Label = MergeIndexColumnName},
                                new DataType() {Type = new Literal() {Value = "int"}}
                            ).AsEnumerable()))
                            .Concat(insertTableRelations.ForeignTableColumns.SelectMany(fk =>
                                fk.ForeignKey.GetForeignColumns().Select(fc =>
                                    new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() {Label = GetForeignColumnIndexName(fc.Name)},
                                        new DataType() {Type = new Literal() {Value = "int"}}))
                            )).ToList()
                    )
            };
            return declareLookupParameterStatement;
        }

        private AstNode BuildUpdateLookupStatement(TableRelations tableRelations)
        {
            if ((tableRelations.TargetTable.PrimaryKey?.Columns.Any()).GetValueOrDefault(false))
            {
                var lookupTableName = GetLookupTableName(tableRelations);
                var insertedTableName = GetInsertedTableName(tableRelations);
                var ast = new Update()
                {
                    SetClause =
                        tableRelations.TargetTable.PrimaryKey.Columns.Select(pk =>
                            new SetEqualOperator()
                                .SetArgs(
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalColumn()
                                        {
                                            Label = pk.Name
                                        }),
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalTable()
                                        {
                                            Label = insertedTableName
                                        },
                                        new RelationalColumn()
                                        {
                                            Label = pk.Name
                                        })
                                    )).ToList(),
                    FromClause = new FromClause().SetArgs(
                            new FromClauseNode().SetArgs(
                                    new Alias()
                                    {
                                        Label = lookupTableName
                                    }.SetArgs(
                                        new NamedParameterIdentifier()
                                        {
                                            Name = lookupTableName
                                        }),
                                    new InnerJoin()
                                    {
                                        RightNode = new Alias()
                                        {
                                            Label = insertedTableName
                                        }.SetArgs(
                                            new NamedParameterIdentifier()
                                            {
                                                Name = insertedTableName
                                            })
                                    }.SetArgs(new EqualsOperator().SetArgs(
                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable()
                                            {
                                                Label = lookupTableName
                                            },
                                            new RelationalColumn()
                                            {
                                                Label = MergeIndexColumnName
                                            }),
                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable()
                                            {
                                                Label = insertedTableName
                                            },
                                            new RelationalColumn()
                                            {
                                                Label = MergeIndexColumnName
                                            })))
                             )
                     )
                }.SetArgs(new Alias()
                {
                    Label = lookupTableName
                });

                return ast;
            }
            
            return null;
        }

        private static IEnumerable<TableIndexReference> OrderIndexReferences(InsertTableRelations insertTableRelations, IEnumerable<TableIndexReference> parentIndexMappings)
        {
            var columnCollectionComparer = new FuncEqualityComparer<IEnumerable<IColumnDefinition>>((l1, l2) => Enumerable.SequenceEqual(l1, l2, ColumnEqualityComparer.Default));
            var result = insertTableRelations.ForeignTableColumns
                .SelectMany(ftc => parentIndexMappings.Where(pi =>
                    columnCollectionComparer.Equals(pi.ForeignColumns, ftc.ForeignKey.GetForeignColumns())))
                .ToList();
                //.Join(parentIndexMappings, 
                //    fc => fc.ForeignKey.GetForeignColumns(),
                //    pi => pi.ForeignColumns,
                //    (ftc, pi) =>
                //    {
                //        return pi;
                //    },
                //    columnCollectionComparer).ToList();
            return result;
        }

        private static IEnumerable<TableIndexReference> GetTableIndexReferences(IEnumerable<OrderedParameterValue> orderedParametersForInsert, InsertTableRelations insertTableRelations, OrderedParameterValueLookup orderedParameterLookup)
        {
            return orderedParametersForInsert.SelectMany((op, i) =>
            {
                var parentArguments = insertTableRelations.ForeignTableColumns.Select(fc =>
                {
                    var primaryTableKeyIndex = orderedParameterLookup.FindArgumentIndex(op, FindRootArgument(insertTableRelations.TableRelations.Argument), FindRootArgument(fc.PrimaryTableRelations.Argument), fc.Direction);
                    var foreignColumns = fc.ForeignKey.KeyPairs.Select(kp => kp.ForeignTableColumn).ToList();
                    return new TableIndexReference(primaryTableKeyIndex, foreignColumns, op.Index);
                }).ToList();

                return parentArguments;
            });
        }

        private static string GetForeignColumnIndexName(string columnName)
        {
            return $"{columnName}_index";
        }

        private static IArgument FindRootArgument(IArgument argument)
        {
            return argument is TableArgument ? argument.ClassProperties.First() : argument;
        }

        private class OrderedParameterValueLookup
        {
            private List<OrderedParameterValue> orderedParameterValues;

            public OrderedParameterValueLookup()
            {
                this.orderedParameterValues = new List<OrderedParameterValue>();
            }

            public void AddValue(IArgument argument, object value, object parentValue)
            {
                var currentIndex = this.orderedParameterValues.Where(v => HasMatchingArgument(v.Argument, argument)).Count();
                var item = new OrderedParameterValue()
                {
                    Argument = argument,
                    Index = currentIndex,
                    Value = value
                };
                this.orderedParameterValues.Add(item);
                var parent = this.orderedParameterValues.FirstOrDefault(v => v.Value == parentValue);
                if (parent != null)
                {
                    var existingChildrenList = parent.Children.ContainsKey(argument) ? parent.Children[argument] : null;
                    if (existingChildrenList == null)
                    {
                        existingChildrenList = new List<OrderedParameterValue>();
                        parent.Children[argument] = existingChildrenList;
                    }
                    existingChildrenList.Add(item);
                    item.Parent = parent;
                }
                
            }
            
            public IEnumerable<OrderedParameterValue> FindOrderedParameters(IArgument argument)
            {
                return this.orderedParameterValues.Where(v => HasMatchingArgument(argument, v.Argument)).ToList();
            }
            
            private static bool HasMatchingArgument(IArgument orderedParameterArgument, IArgument argument)
            {
                return (orderedParameterArgument.EquivalentTo(argument) || (argument is TableArgument && argument.ClassProperties.First().EquivalentTo(orderedParameterArgument)));
            }

            public int? FindArgumentIndex(OrderedParameterValue orderedParameterValue,
                IArgument foreignTableArgument, IArgument primaryTableArgument, ForeignTablePropertyDirection direction)
            {
                if (HasMatchingArgument(orderedParameterValue.Argument, primaryTableArgument))
                {
                    return orderedParameterValue.Index;
                }

                if (direction == ForeignTablePropertyDirection.Parent)
                {
                    if (orderedParameterValue.Parent != null)
                    {
                        return FindArgumentIndex(orderedParameterValue.Parent, foreignTableArgument, primaryTableArgument,
                            direction);
                    }

                    return null;
                }

                return orderedParameterValue.Children.SelectMany(c => c.Value).Select(c =>
                    FindArgumentIndex(c, foreignTableArgument, primaryTableArgument, direction)).FirstOrDefault(c => c.HasValue);
            }
        }

        private void OrderParameterValues(OrderedParameterValueLookup indexLookup, object value, IArgument argument, object parentValue)
        {
            var distinctValues = value.AsEnumerable().ToList();
            //var currentValuesList = new List<OrderedParameterValue>();
            for (var index = 0; index < distinctValues.Count; index++)
            {
                indexLookup.AddValue(argument, distinctValues[index], parentValue);
            }
            
            argument.ClassProperties.ToList().ForEach(c =>
                distinctValues.ForEach(v =>
                    OrderParameterValues(indexLookup, MethodSqlStatement.GetValueForParameterPath(v, c.GetPropertyInfo().AsEnumerable()), c, v)
                )
            );
        }

        private class OrderedParameterValue
        {
            public OrderedParameterValue()
            {
                Children = new ConcurrentDictionary<IArgument, List<OrderedParameterValue>>();
            }

            public IArgument Argument { get; set; }
            public object Value { get; set; }
            public int Index { get; set; }
            public IDictionary<IArgument, List<OrderedParameterValue>> Children { get; set; }
            public OrderedParameterValue Parent { get; set; }
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


                var dependencyOrderedTableRelations = GetInsertTableRelationsOrderedByDependencies(parameterTableRelations).ToList();
                insertSpec.InsertTableRelationsCollection = dependencyOrderedTableRelations.Select(tr => ToInsertTableRelations(tr, dependencyOrderedTableRelations)).ToList();
                
                insertSpec.ReturnType = methodInfo.ReturnType;
                insertSpec.UnwrappedReturnType = OutputFactory.UnwrapType(methodInfo.ReturnType);
                insertSpec.RootMethodInfo = methodInfo;

                return insertSpec;
            }
            
            return null;
        }

        private InsertTableRelations ToInsertTableRelations(TableRelations parameterTableRelations, List<TableRelations> dependencyList)
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
            insertRelations.ForeignTableColumns = GetRequiredForeignKeys(parameterTableRelations, dependencyList);
            insertRelations.TableRelations = parameterTableRelations;

            return insertRelations;
        }

        private IEnumerable<TableRelations> GetInsertTableRelationsOrderedByDependencies(TableRelations root)
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
                        HasPendingForeignKey(t, remainingTableRelations);
                    if (!hasPendingDependency)
                    {
                        orderedTableRelations.Add(t);
                    }
                });
            }

            return orderedTableRelations.ToList();
        }

        private bool HasPendingForeignKey(TableRelations tableRelations, List<TableRelations> remainingTableRelations)
        {
            var allForeignKeys = 
                remainingTableRelations.Select(c => c.ForeignKeyToParent)
                    .Where(fk => fk != null && fk.KeyPairs.Any(kp => remainingTableRelations.Any(tr => TableEqualityComparer.Default.Equals(kp.PrimaryTableColumn.Table, tr.TargetTable)) )).ToList();
            return allForeignKeys.Any(fk => fk.KeyPairs.Any(kp =>
                TableEqualityComparer.Default.Equals(tableRelations.TargetTable, kp.ForeignTableColumn.Table)));
        }

        private IEnumerable<ForeignTableColumn> GetRequiredForeignKeys(TableRelations tableRelations, List<TableRelations> dependencyList)
        {
            var foreignTableKey = GetForeignKey(tableRelations.ForeignKeyToParent, tableRelations.TargetTable);
            ForeignTableColumn foreignTableColumn = null;
            if (foreignTableKey != null)
            {
                foreignTableColumn = new ForeignTableColumn()
                {
                    ForeignKey = foreignTableKey,
                    PrimaryTableRelations = tableRelations.Parent,
                    Direction = ForeignTablePropertyDirection.Parent
                };
            }
            
            var allForeignKeys = tableRelations.NavigationTables
                .Select(nt =>
                {
                    var foreignKeyDefinition = GetForeignKey(nt.ForeignKeyToParent, tableRelations.TargetTable);
                    return foreignKeyDefinition != null ? new ForeignTableColumn()
                    {
                        ForeignKey = foreignKeyDefinition,
                        PrimaryTableRelations = nt,
                        Direction = ForeignTablePropertyDirection.Navigation
                    } : null;
                })
                .AppendOne(foreignTableColumn)
                .Where(fk => fk != null && dependencyList.Any(dt => TableEqualityComparer.Default.Equals(dt.TargetTable, fk.ForeignKey.PrimaryKeyTable))).ToList();

            return allForeignKeys;
        }

        private IForeignKeyDefinition GetForeignKey(IForeignKeyDefinition foreignKey, ITableDefinition tableDefinition)
        {
            if ((foreignKey?.KeyPairs.Any(kp =>
                    TableEqualityComparer.Default.Equals(tableDefinition, kp.ForeignTableColumn.Table))).GetValueOrDefault(false))
            {
                return foreignKey;
            }

            return null;
        }

        private class InsertTableRelations
        {
            public IList<InsertColumnParameter> ColumnParameters { get; set; }
            public TableRelations TableRelations { get; set; }
            
            public IEnumerable<ForeignTableColumn> ForeignTableColumns { get; set; }
            
        }

        private static string GetLookupTableName(TableRelations tableRelations)
        {
            return tableRelations.Alias.Replace("<", "$").Replace(">", "$") + "Lookup";
        }

        private static string GetInsertedTableName(TableRelations tableRelations)
        {
            return "inserted" + tableRelations.Alias.Replace("<", "$").Replace(">", "$");
        }

        private class ForeignTableColumn
        {
            public IForeignKeyDefinition ForeignKey { get; set; }
            public TableRelations PrimaryTableRelations { get; set; }
            public ForeignTablePropertyDirection Direction { get; set; }

        }

        private enum ForeignTablePropertyDirection
        {
            Parent,
            Navigation
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
        }
        

        private class InsertColumnParameter
        {
            public IColumnDefinition Column { get; set; }
            public ParameterPath ParameterPath { get; set; }
        }

        private class FuncEqualityComparer<T> : IEqualityComparer<T>
        {
            private readonly Func<T, T, bool> comparisonFunc;

            public FuncEqualityComparer(Func<T, T, bool> comparisonFunc)
            {
                this.comparisonFunc = comparisonFunc;
            }
            public bool Equals(T x, T y)
            {
                return comparisonFunc(x, y);
            }

            public int GetHashCode(T obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
