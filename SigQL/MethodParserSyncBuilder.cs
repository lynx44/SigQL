using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Sql;

namespace SigQL
{
    public partial class MethodParser
    {
        private MethodSqlStatement BuildSyncStatement(UpsertSpec upsertSpec, List<ParameterPath> parameterPaths)
        {
            var targetTableType = upsertSpec.UnwrappedReturnType;

            List<AstNode> statements;
            List<TokenPath> tokenPaths;
            ConcurrentDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions;
            {
                var builderAstCollection = BuildUpsertAstCollection(upsertSpec, parameterPaths);
                var deleteOrderedRelations = upsertSpec.UpsertTableRelationsCollection.ToList();
                deleteOrderedRelations.Reverse();
                for (var i = 0; i < deleteOrderedRelations.Count(); i++)
                {
                    var upsertRelation = deleteOrderedRelations[i];
                    var tableRelations = upsertRelation.TableRelations;
                    var parentRelations = tableRelations;
                    if (tableRelations.ForeignKeyToParent != null)
                    {
                        parentRelations = tableRelations.Parent;
                        if (tableRelations.Argument.Type != typeof(void))
                        {
                            var dependentTables = tableRelations.NavigationTables.Where(nt =>
                                TableEqualityComparer.Default.Equals(nt.ForeignKeyToParent.PrimaryKeyTable,
                                    tableRelations.TargetTable)).ToList();
                            var dependentDeletes = dependentTables.Select(dt =>
                                BuildOneToManyDeleteStatement(tableRelations, parentRelations, dt)).ToList();
                            builderAstCollection.Statements.AddRange(dependentDeletes);
                            var deleteStatement = BuildOneToManyDeleteStatement(tableRelations, parentRelations);
                            builderAstCollection.Statements.Add(deleteStatement);
                        }
                        else
                        {
                            // if this is a many-to-many table, use a CTE so we can delete
                            // the correct number of rows when duplicated foreign key sets exist

                            var cteName = $"SigQL__Delete{tableRelations.TableName}";
                            // ; with SigQL__Delete{Table} as (
                            var sigqlOccurrenceAlias = "SigQL__Occurrence";
                           var deleteStatement = new CommonTableExpression()
                            {
                                Name = cteName,
                                Definition = new Select()
                                {
                                    // select {Table}.*
                                    SelectClause = new SelectClause().SetArgs(new ColumnIdentifier().SetArgs(
                                        new RelationalTable()
                                        {
                                            Label = tableRelations.TableName
                                        }, new Literal()
                                        {
                                            Value = "*"
                                        })),
                                    FromClause = new FromClause().SetArgs(new FromClauseNode().SetArgs(
                                        new SubqueryAlias()
                                        {
                                            Alias = tableRelations.TableName
                                        }.SetArgs(
                                            new Select()
                                            {
                                            // select *, (ROW_NUMBER() over(partition by addressesid, employeesid order by employeesid, addressesid)) SigQL__Occurrence
                                            SelectClause = new SelectClause().SetArgs(
                                                    new Literal() { Value = "*" },
                                                    new Alias()
                                                    {
                                                        Label = sigqlOccurrenceAlias
                                                    }.SetArgs(
                                                        new OverClause()
                                                        {
                                                            Function = new Function()
                                                            {
                                                                Name = "ROW_NUMBER"
                                                            }
                                                        }.SetArgs(
                                                            new PartitionByClause().SetArgs(
                                                                tableRelations.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
                                                                    .Select(c =>
                                                                        new ColumnIdentifier().SetArgs(
                                                                            new RelationalColumn()
                                                                            {
                                                                                Label = c.Name
                                                                            })
                                                                    ).ToList()
                                                            ),
                                                            new OrderByClause().SetArgs(
                                                                tableRelations.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
                                                                    .Select(c =>
                                                                        new ColumnIdentifier().SetArgs(
                                                                            new RelationalColumn()
                                                                            {
                                                                                Label = c.Name
                                                                            })
                                                                    ).ToList()))
                                                    )
                                                ),
                                                FromClause = new FromClause().SetArgs(
                                                    new FromClauseNode().SetArgs(
                                                        new TableIdentifier().SetArgs(new RelationalTable()
                                                        { Label = tableRelations.TableName })))
                                            }
                                        )
                                    )),
                                    WhereClause =
                                        new WhereClause().SetArgs(
                                new AndOperator().SetArgs(
                                    new Exists().SetArgs(
                                        new Select()
                                        {
                                            SelectClause = new SelectClause().SetArgs(
                                                new Literal() { Value = "1" }),
                                            FromClause = new FromClause().SetArgs(
                                                new FromClauseNode().SetArgs(
                                                    new TableIdentifier().SetArgs(
                                                        new Alias()
                                                        {
                                                            Label = GetLookupTableName(parentRelations)
                                                        }.SetArgs(
                                                            new NamedParameterIdentifier()
                                                            {
                                                                Name = GetLookupTableName(parentRelations)
                                                            }
                                                        )
                                                    )
                                                )),
                                            WhereClause = new WhereClause().SetArgs(
                                                new AndOperator().SetArgs(
                                                    tableRelations.ForeignKeyToParent.KeyPairs.Select(fk =>
                                                        new EqualsOperator().SetArgs(
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = GetLookupTableName(parentRelations)
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = fk.PrimaryTableColumn.Name
                                                                }),
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = tableRelations.TableName
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = fk.ForeignTableColumn.Name
                                                                })
                                                        )
                                                    )
                                                )
                                            )
                                        }
                                    ),
                                    new NotExists().SetArgs(
                                        new Select()
                                        {
                                            SelectClause = new SelectClause().SetArgs(
                                                new Literal() { Value = "1" }),
                                            FromClause =

                                                new FromClause().SetArgs(new FromClauseNode().SetArgs(
                                        new SubqueryAlias()
                                        {
                                            Alias = GetLookupTableName(tableRelations)
                                        }.SetArgs(
                                            new Select()
                                            {
                                                // select *, (ROW_NUMBER() over(partition by addressesid, employeesid order by employeesid, addressesid)) SigQL__Occurrence
                                                SelectClause = new SelectClause().SetArgs(
                                                    new Literal() { Value = "*" },
                                                    new Alias()
                                                    {
                                                        Label = sigqlOccurrenceAlias
                                                    }.SetArgs(
                                                        new OverClause()
                                                        {
                                                            Function = new Function()
                                                            {
                                                                Name = "ROW_NUMBER"
                                                            }
                                                        }.SetArgs(
                                                            new PartitionByClause().SetArgs(
                                                                tableRelations.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
                                                                    .Select(c =>
                                                                        new ColumnIdentifier().SetArgs(
                                                                            new RelationalColumn()
                                                                            {
                                                                                Label = c.Name
                                                                            })
                                                                    ).ToList()
                                                            ),
                                                            new OrderByClause().SetArgs(
                                                                tableRelations.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
                                                                    .Select(c =>
                                                                        new ColumnIdentifier().SetArgs(
                                                                            new RelationalColumn()
                                                                            {
                                                                                Label = c.Name
                                                                            })
                                                                    ).ToList()))
                                                    )
                                                ),
                                                FromClause = new FromClause().SetArgs(
                                                    new FromClauseNode().SetArgs(
                                                        new NamedParameterIdentifier()
                                                        {
                                                            Name = GetLookupTableName(tableRelations)
                                                        }))
                                            }
                                        )
                                    )),
                                            WhereClause = new WhereClause().SetArgs(
                                                new AndOperator().SetArgs(
                                                    tableRelations.TargetTable.ForeignKeyCollection.GetAllForeignColumns().Select(c =>
                                                        
                                                            new EqualsOperator().SetArgs(
                                                                new ColumnIdentifier().SetArgs(
                                                                    new RelationalTable()
                                                                    {
                                                                        Label = GetLookupTableName(tableRelations)
                                                                    },
                                                                    new RelationalColumn()
                                                                    {
                                                                        Label = c.Name
                                                                    }),
                                                                new ColumnIdentifier().SetArgs(
                                                                    new RelationalTable()
                                                                    {
                                                                        Label = tableRelations.TableName
                                                                    },
                                                                    new RelationalColumn()
                                                                    {
                                                                        Label = c.Name
                                                                    })
                                                            )
                                                        )
                                                        .AppendOne(
                                                            new EqualsOperator().SetArgs(
                                                                new ColumnIdentifier().SetArgs(new RelationalTable() { Label = tableRelations.TableName }, new RelationalColumn() { Label = sigqlOccurrenceAlias }),
                                                                new ColumnIdentifier().SetArgs(new RelationalTable() { Label = GetLookupTableName(tableRelations) }, new RelationalColumn() { Label = sigqlOccurrenceAlias })
                                                            )
                                                        )
                                                    )
                                            )
                                        })
                                )
                            )
                                }
                            }.SetArgs(new Delete()
                            {
                                FromClause = new FromClause().SetArgs(
                                    new FromClauseNode().SetArgs(new TableIdentifier().SetArgs(new RelationalTable()
                                    { Label = cteName })))
                            });
                           builderAstCollection.Statements.Add(deleteStatement);
                        }
                        
                    }
                }

                statements = builderAstCollection.Statements;
                tokenPaths = builderAstCollection.Tokens;
                tablePrimaryKeyDefinitions = builderAstCollection.TablePrimaryKeyDefinitions;
            }


            var sqlStatement = new MethodSqlStatement()
            {
                CommandAst = statements,
                SqlBuilder = this.builder,
                ReturnType = upsertSpec.ReturnType,
                UnwrappedReturnType = targetTableType,
                Parameters = parameterPaths,
                Tokens = tokenPaths,
                TargetTablePrimaryKey = upsertSpec.Table.PrimaryKey,
                TablePrimaryKeyDefinitions = tablePrimaryKeyDefinitions
            };

            return sqlStatement;
        }

        private static Delete BuildOneToManyDeleteStatement(TableRelations tableRelations, TableRelations parentRelations,
            TableRelations joinSubject = null)
        {
            AstNode innerJoin = null;
            if (joinSubject != null)
            {
                innerJoin = new InnerJoin()
                {
                    RightNode = new TableIdentifier().SetArgs(new RelationalTable() { Label = joinSubject.TableName })
                }.SetArgs(
                    new AndOperator().SetArgs(
                        new EqualsOperator().SetArgs(
                            joinSubject.ForeignKeyToParent.KeyPairs.SelectMany(kp =>
                                new ColumnIdentifier().SetArgs(
                                        new RelationalTable() { Label = kp.PrimaryTableColumn.Table.Name },
                                        new RelationalColumn() { Label = kp.PrimaryTableColumn.Name }).AsEnumerable()
                                    .AppendOne(
                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable() { Label = kp.ForeignTableColumn.Table.Name },
                                            new RelationalColumn() { Label = kp.ForeignTableColumn.Name }))
                            )
                        )
                    ));
            }
            
            return new Delete()
            {
                FromClause = new FromClause().SetArgs(
                    new FromClauseNode().SetArgs(
                        new TableIdentifier().SetArgs(new RelationalTable()
                        {
                            Label = tableRelations.TableName
                        }),
                        innerJoin
                    )
                ),
                WhereClause = new WhereClause().SetArgs(
                    new AndOperator().SetArgs(
                        new Exists().SetArgs(
                            new Select()
                            {
                                SelectClause = new SelectClause().SetArgs(
                                    new Literal() { Value = "1" }),
                                FromClause = new FromClause().SetArgs(
                                    new FromClauseNode().SetArgs(
                                        new TableIdentifier().SetArgs(
                                            new Alias()
                                            {
                                                Label = GetLookupTableName(parentRelations)
                                            }.SetArgs(
                                                new NamedParameterIdentifier()
                                                {
                                                    Name = GetLookupTableName(parentRelations)
                                                }
                                            )
                                        )
                                    )),
                                WhereClause = new WhereClause().SetArgs(
                                    new AndOperator().SetArgs(
                                        tableRelations.ForeignKeyToParent.KeyPairs.Select(fk =>
                                            new EqualsOperator().SetArgs(
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = GetLookupTableName(parentRelations)
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = fk.GetColumnForTable(parentRelations.TargetTable).Name 
                                                    }),
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = tableRelations.TableName
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = fk.GetColumnForTable(tableRelations.TargetTable).Name
                                                    })
                                            )
                                        )
                                    )
                                )
                            }
                        ),
                        new NotExists().SetArgs(
                            new Select()
                            {
                                SelectClause = new SelectClause().SetArgs(
                                    new Literal() { Value = "1" }),
                                FromClause = new FromClause().SetArgs(
                                    new FromClauseNode().SetArgs(
                                        new TableIdentifier().SetArgs(
                                            new Alias()
                                            {
                                                Label = GetLookupTableName(tableRelations)
                                            }.SetArgs(
                                                new NamedParameterIdentifier()
                                                {
                                                    Name = GetLookupTableName(tableRelations)
                                                }
                                            )
                                        )
                                    )),
                                WhereClause = new WhereClause().SetArgs(
                                    new AndOperator().SetArgs(
                                        tableRelations.TargetTable.PrimaryKey.Columns.Select(pk =>
                                            new EqualsOperator().SetArgs(
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = GetLookupTableName(tableRelations)
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = pk.Name
                                                    }),
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = tableRelations.TableName
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = pk.Name
                                                    })
                                            )
                                        )
                                    )
                                )
                            })
                    )
                )
            }.SetArgs(new []
            {
                new Alias()
                {
                    Label = joinSubject?.TableName ?? tableRelations.TableName
                }
            });
        }
    }
}
