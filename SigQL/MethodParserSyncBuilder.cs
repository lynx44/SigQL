using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SigQL.Extensions;
using SigQL.Schema;

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
                for (var i = 0; i < upsertSpec.UpsertTableRelationsCollection.Count; i++)
                {
                    var upsertRelation = upsertSpec.UpsertTableRelationsCollection[i];
                    var tableRelations = upsertRelation.TableRelations;
                    var oneToManyNavigationTables = tableRelations.NavigationTables.Where(nt =>
                        TableEqualityComparer.Default.Equals(nt.ForeignKeyToParent.PrimaryKeyTable,
                            tableRelations.TargetTable)).ToList();
                    var deleteStatements = oneToManyNavigationTables.Select(nt =>
                    {
                        AstNode deleteStatement;
                        if (nt.Argument.Type != typeof(void))
                        {
                            deleteStatement = new Delete()
                            {
                                FromClause = new FromClause().SetArgs(new TableIdentifier().SetArgs(new RelationalTable()
                                {
                                    Label = nt.TableName
                                })),
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
                                                    nt.ForeignKeyToParent.KeyPairs.Select(fk =>
                                                        new EqualsOperator().SetArgs(
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = GetLookupTableName(tableRelations)
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = fk.PrimaryTableColumn.Name
                                                                }),
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = nt.TableName
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
                                            FromClause = new FromClause().SetArgs(
                                                new FromClauseNode().SetArgs(
                                                    new TableIdentifier().SetArgs(
                                                        new Alias()
                                                        {
                                                            Label = GetLookupTableName(nt)
                                                        }.SetArgs(
                                                            new NamedParameterIdentifier()
                                                            {
                                                                Name = GetLookupTableName(nt)
                                                            }
                                                        )
                                                    )
                                                )),
                                            WhereClause = new WhereClause().SetArgs(
                                                new AndOperator().SetArgs(
                                                    nt.TargetTable.PrimaryKey.Columns.Select(pk =>
                                                        new EqualsOperator().SetArgs(
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = GetLookupTableName(nt)
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = pk.Name
                                                                }),
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = nt.TableName
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
                            };
                        }
                        else
                        {
                            // if this is a many-to-many table, use a CTE so we can delete
                            // the correct number of rows when duplicated foreign key sets exist

                            var cteName = $"SigQL__Delete{nt.TableName}";
                            // ; with SigQL__Delete{Table} as (
                            var sigqlOccurrenceAlias = "SigQL__Occurrence";
                           deleteStatement = new CommonTableExpression()
                            {
                                Name = cteName,
                                Definition = new Select()
                                {
                                    // select {Table}.*
                                    SelectClause = new SelectClause().SetArgs(new ColumnIdentifier().SetArgs(
                                        new RelationalTable()
                                        {
                                            Label = nt.TableName
                                        }, new Literal()
                                        {
                                            Value = "*"
                                        })),
                                    FromClause = new FromClause().SetArgs(new FromClauseNode().SetArgs(
                                        new SubqueryAlias()
                                        {
                                            Alias = nt.TableName
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
                                                                nt.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
                                                                    .Select(c =>
                                                                        new ColumnIdentifier().SetArgs(
                                                                            new RelationalColumn()
                                                                            {
                                                                                Label = c.Name
                                                                            })
                                                                    ).ToList()
                                                            ),
                                                            new OrderByClause().SetArgs(
                                                                nt.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
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
                                                        { Label = nt.TableName })))
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
                                                    nt.ForeignKeyToParent.KeyPairs.Select(fk =>
                                                        new EqualsOperator().SetArgs(
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = GetLookupTableName(tableRelations)
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = fk.PrimaryTableColumn.Name
                                                                }),
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = nt.TableName
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
                                            Alias = GetLookupTableName(nt)
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
                                                                nt.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
                                                                    .Select(c =>
                                                                        new ColumnIdentifier().SetArgs(
                                                                            new RelationalColumn()
                                                                            {
                                                                                Label = c.Name
                                                                            })
                                                                    ).ToList()
                                                            ),
                                                            new OrderByClause().SetArgs(
                                                                nt.TargetTable.ForeignKeyCollection.GetAllForeignColumns()
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
                                                            Name = GetLookupTableName(nt)
                                                        }))
                                            }
                                        )
                                    )),
                                            WhereClause = new WhereClause().SetArgs(
                                                new AndOperator().SetArgs(
                                                    nt.TargetTable.ForeignKeyCollection.GetAllForeignColumns().Select(c =>
                                                        
                                                            new EqualsOperator().SetArgs(
                                                                new ColumnIdentifier().SetArgs(
                                                                    new RelationalTable()
                                                                    {
                                                                        Label = GetLookupTableName(nt)
                                                                    },
                                                                    new RelationalColumn()
                                                                    {
                                                                        Label = c.Name
                                                                    }),
                                                                new ColumnIdentifier().SetArgs(
                                                                    new RelationalTable()
                                                                    {
                                                                        Label = nt.TableName
                                                                    },
                                                                    new RelationalColumn()
                                                                    {
                                                                        Label = c.Name
                                                                    })
                                                            )
                                                        )
                                                        .AppendOne(
                                                            new EqualsOperator().SetArgs(
                                                                new ColumnIdentifier().SetArgs(new RelationalTable() { Label = nt.TableName }, new RelationalColumn() { Label = sigqlOccurrenceAlias }),
                                                                new ColumnIdentifier().SetArgs(new RelationalTable() { Label = GetLookupTableName(nt) }, new RelationalColumn() { Label = sigqlOccurrenceAlias })
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
                        }

                        return deleteStatement;
                    }).ToList();
                    builderAstCollection.Statements.AddRange(deleteStatements);
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
    }
}
