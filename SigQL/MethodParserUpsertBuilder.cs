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
        private MethodSqlStatement BuildUpsertStatement(UpsertSpec upsertSpec, List<ParameterPath> parameterPaths)
        {
            var targetTableType = upsertSpec.UnwrappedReturnType;

            List<AstNode> statements;
            List<TokenPath> tokenPaths;
            ConcurrentDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions;
            {
                var builderAstCollection = BuildUpsertAstCollection(upsertSpec, parameterPaths);

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

        private InsertBuilderAstCollection BuildUpsertAstCollection(UpsertSpec upsertSpec, List<ParameterPath> parameterPaths)
        {
            var builderAstCollection = BuildInsertAstCollection(upsertSpec, parameterPaths);
            for (var index = 0; index < upsertSpec.UpsertTableRelationsCollection.Count; index++)
            {
                var upsertTableRelations = upsertSpec.UpsertTableRelationsCollection[index];
                var targetTable = upsertTableRelations.TableRelations.TargetTable;
                ModifyMergeSelectStatement(builderAstCollection, targetTable, upsertTableRelations);

                var updateLookupIdsAst = builderAstCollection.GetReference<AstNode>(targetTable,
                    InsertBuilderAstCollection.AstReferenceSource.UpdateLookupIds);
                if (updateLookupIdsAst != null)
                {
                    var updateLookupIdsAstIndex = builderAstCollection.Statements.IndexOf(updateLookupIdsAst);
                    if (upsertTableRelations.TableRelations.Argument is TableArgument ||
                        upsertTableRelations.TableRelations.Argument.Type != typeof(void))
                    {
                        var updateFromLookupStatement = BuildUpdateFromLookupStatement(upsertTableRelations,
                            GetLookupTableName(upsertTableRelations.TableRelations));
                        AppendWhereClauseToUpdateStatement(updateFromLookupStatement, targetTable, upsertTableRelations);
                        builderAstCollection.Statements.Insert(updateLookupIdsAstIndex + 1, updateFromLookupStatement);
                    }
                    else
                    // many to many - no need to do an update
                    {
                        
                        //builderAstCollection.Statements.RemoveAt(updateLookupIdsAstIndex);
                    }
                }
            }

            return builderAstCollection;
        }

        private static void AppendWhereClauseToUpdateStatement(Update updateFromLookupStatement, ITableDefinition targetTable,
            UpsertTableRelations upsertTableRelations)
        {
            if ((targetTable.PrimaryKey?.Columns?.Any()).GetValueOrDefault(false))
            {
                updateFromLookupStatement.WhereClause ??= new WhereClause();
                updateFromLookupStatement.WhereClause.Args ??= new List<AstNode>();
                // where not exists
                // (select 1 from "Employee"
                // inner join @insertedEmployee "insertedEmployee"
                // on "Employee"."Id" = "insertedEmployee"."Id"
                // where "EmployeeLookup"."Id" = "insertedEmployee"."Id")
                updateFromLookupStatement.WhereClause.Args =
                    updateFromLookupStatement.WhereClause.Args.AppendOne(
                        new NotExists().SetArgs(
                            new Select()
                            {
                                SelectClause = new SelectClause().SetArgs(new Literal() { Value = "1" }),
                                FromClause =
                                    new FromClause().SetArgs(
                                        new FromClauseNode().SetArgs(
                                            new TableIdentifier().SetArgs(new RelationalTable() { Label = targetTable.Name }),
                                            new InnerJoin()
                                            {
                                                RightNode =
                                                    new Alias()
                                                    {
                                                        Label = GetInsertedTableName(upsertTableRelations.TableRelations)
                                                    }.SetArgs(
                                                        new NamedParameterIdentifier()
                                                        {
                                                            Name = GetInsertedTableName(upsertTableRelations.TableRelations)
                                                        }
                                                    )
                                            }.SetArgs(
                                                new AndOperator().SetArgs(
                                                    targetTable.PrimaryKey.Columns.Select(c =>
                                                        new EqualsOperator().SetArgs(
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = targetTable.Name
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = c.Name
                                                                }
                                                            ),
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = GetInsertedTableName(
                                                                        upsertTableRelations.TableRelations)
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = c.Name
                                                                }
                                                            )
                                                        )
                                                    )
                                                )
                                            )
                                        )
                                    ),
                                WhereClause = new WhereClause().SetArgs(
                                    new AndOperator().SetArgs(
                                        targetTable.PrimaryKey.Columns.Select(c =>
                                            new EqualsOperator().SetArgs(
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = GetLookupTableName(upsertTableRelations.TableRelations)
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = c.Name
                                                    }
                                                ),
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = GetInsertedTableName(upsertTableRelations.TableRelations)
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = c.Name
                                                    }
                                                )
                                            )
                                        ).ToList()
                                    )
                                )
                            }
                        )
                    );
            }
        }

        private static void ModifyMergeSelectStatement(InsertBuilderAstCollection builderAstCollection,
            ITableDefinition targetTable, UpsertTableRelations upsertTableRelations)
        {
            var mergeSelectStatement = builderAstCollection.GetReference<Select>(targetTable, InsertBuilderAstCollection.AstReferenceSource.MergeSelectClause);
            if (upsertTableRelations.TableRelations.Argument is TableArgument || upsertTableRelations.TableRelations.Argument.Type != typeof(void))
            {
                ModifyOneToManyMergeSelectStatement(targetTable, upsertTableRelations, mergeSelectStatement);
            }
            else
            {
                ModifyManyToManyMergeSelectStatement(targetTable, upsertTableRelations, mergeSelectStatement);
            }
            
        }

        private static void ModifyManyToManyMergeSelectStatement(ITableDefinition targetTable,
            UpsertTableRelations upsertTableRelations, Select mergeSelectStatement)
        {
            // this query replicates the same number of many-to-many relationships
            // without removing any items
            //
            // for example, if these key pairs are specified in the upsert:
            // AddressId |  EmployeeId
            // 1            1
            // 1            2
            // 1            2
            //
            // this query will ensure that (1,2) will always be represented by
            // two rows in the m2m relationship
            //
            // note that this will not delete any rows from the m2m table

            //select "_index", "AddressesId_index", "EmployeesId_index" from(
            //    select * from
            //        (select row_number() over(partition by AddressLookup.Id, EmployeeLookup.Id order by AddressLookup.Id, EmployeeLookup.Id) RowNumber,
            //        AddressLookup.Id AddressesId,
            //        EmployeeLookup.Id EmployeesId,
            //        EmployeeAddressLookup."_index",
            //        EmployeeAddressLookup."AddressesId_index",
            //        EmployeeAddressLookup."EmployeesId_index"

            //        from @EFAddressEFEmployeeLookup "EmployeeAddressLookup"

            //        inner join @AddressLookup AddressLookup on AddressLookup._index = EmployeeAddressLookup.AddressesId_index

            //        inner join @EmployeeLookup EmployeeLookup on EmployeeLookup._index = EmployeeAddressLookup.EmployeesId_index
            //        ) SigQLM2MRowNumberLookupQuery

            //    where not exists(
            //        select * from(
            //            select row_number() over(partition by AddressesId, EmployeesId order by AddressesId, EmployeesId) RowNumber,
            //            *
            //            from EFAddressEFEmployee) EFAddressEFEmployee2M2MRowNumberLookup

            //            where SigQLM2MRowNumberLookupQuery.RowNumber = EFAddressEFEmployee2M2MRowNumberLookup.RowNumber and

            //            SigQLM2MRowNumberLookupQuery.AddressesId = EFAddressEFEmployee2M2MRowNumberLookup.AddressesId and

            //            SigQLM2MRowNumberLookupQuery.EmployeesId = EFAddressEFEmployee2M2MRowNumberLookup.EmployeesId)
            //        ) SigQLM2MMultiplicityQuery

            var rowNumberAlias = "SigQLRowNumber";
            var lookupRowNumberQueryAlias = "SigQLM2MRowNumberLookupQuery";
            var tableRowNumberQueryAlias = $"{upsertTableRelations.TableRelations.TargetTable.Name}M2MRowNumberLookup";
            mergeSelectStatement.FromClause = 
                new FromClause().SetArgs(
                    new SubqueryAlias()
                    {
                        Alias = "SigQLM2MMultiplicityQuery"
                    }.SetArgs(
                        new Select()
                        {
                            SelectClause = new SelectClause().SetArgs(
                                    new Literal()
                                    {
                                        Value = "*"
                                    }
                                ),
                            FromClause = 
                                new FromClause().SetArgs(
                                    new SubqueryAlias()
                                    {
                                        Alias = lookupRowNumberQueryAlias
                                    }.SetArgs(
                                        new Select()
                                        {
                                            SelectClause = new SelectClause().SetArgs(
                                                upsertTableRelations.ForeignTableColumns.SelectMany(c =>
                                                    c.ForeignKey.KeyPairs.Select(kp => (AstNode)
                                                        new Alias() //AddressLookup.Id AddressesId, 
                                                        {
                                                            Label = $"{GetLookupTableName(c.PrimaryTableRelations)}{kp.ForeignTableColumn.Name}"
                                                        }.SetArgs(
                                                            new ColumnIdentifier().SetArgs(
                                                                new RelationalTable()
                                                                {
                                                                    Label = GetLookupTableName(c.PrimaryTableRelations)
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = kp.PrimaryTableColumn.Name
                                                                }
                                                            )
                                                        )
                                                    )
                                                )
                                                .Concat(
                                                    upsertTableRelations.ForeignTableColumns.SelectMany(c =>
                                                        c.ForeignKey.KeyPairs.Select(kp => 
                                                            new ColumnIdentifier().SetArgs(
                                                                // EmployeeAddressLookup."AddressesId_index",
                                                                new RelationalTable()
                                                                {
                                                                    Label = GetLookupTableName(upsertTableRelations.TableRelations)
                                                                },
                                                                new RelationalColumn()
                                                                {
                                                                    Label = GetForeignColumnIndexName(kp.ForeignTableColumn.Name)
                                                                }
                                                            )
                                                        )
                                                    )
                                                )
                                                .AppendOne(
                                                    new ColumnIdentifier().SetArgs(
                                                        // EmployeeAddressLookup."_index",
                                                        new RelationalTable()
                                                        {
                                                            Label = GetLookupTableName(upsertTableRelations.TableRelations)
                                                        },
                                                        new RelationalColumn()
                                                        {
                                                            Label = MergeIndexColumnName
                                                        }
                                                    )
                                                )
                                                .AppendOne(
                                                    new Alias()
                                                    {
                                                        Label = rowNumberAlias
                                                    }.SetArgs(
                                                    new OverClause()
                                                    {
                                                        Function = new Function()
                                                        {
                                                            Name = "ROW_NUMBER"
                                                        }
                                                    }.SetArgs(
                                                        new PartitionByClause().SetArgs(
                                                            upsertTableRelations.ForeignTableColumns.SelectMany(c =>
                                                                c.ForeignKey.KeyPairs.Select(kp =>
                                                                    new ColumnIdentifier().SetArgs(
                                                                        new RelationalTable()
                                                                        {
                                                                            Label = GetLookupTableName(c.PrimaryTableRelations)
                                                                        },
                                                                        new RelationalColumn()
                                                                        {
                                                                            Label = kp.PrimaryTableColumn.Name
                                                                        }
                                                                    )
                                                                )
                                                            )
                                                        ),
                                                        new OrderByClause().SetArgs(
                                                            upsertTableRelations.ForeignTableColumns.SelectMany(c =>
                                                                c.ForeignKey.KeyPairs.Select(kp =>
                                                                    new ColumnIdentifier().SetArgs(
                                                                        new RelationalTable()
                                                                        {
                                                                            Label = GetLookupTableName(c.PrimaryTableRelations)
                                                                        },
                                                                        new RelationalColumn()
                                                                        {
                                                                            Label = kp.PrimaryTableColumn.Name
                                                                        }
                                                                    )
                                                                )
                                                            )
                                                        )
                                                    )
                                                )
                                                )
                                            ),
                                            FromClause = new FromClause().SetArgs(new FromClauseNode().SetArgs(
                                                new Alias()
                                                {
                                                    Label = GetLookupTableName(upsertTableRelations.TableRelations)
                                                }.SetArgs(
                                                    new NamedParameterIdentifier()
                                                    {
                                                        Name = GetLookupTableName(upsertTableRelations.TableRelations)
                                                    }
                                                ).AsEnumerable<AstNode>().Concat(
                                                upsertTableRelations.ForeignTableColumns.SelectMany(c => 
                                                    c.ForeignKey.KeyPairs.Select(kp =>
                                                        new InnerJoin()
                                                        {
                                                            RightNode = new Alias()
                                                            {
                                                                Label = GetLookupTableName(c.PrimaryTableRelations)
                                                            }.SetArgs(
                                                                new NamedParameterIdentifier()
                                                                {
                                                                    Name = GetLookupTableName(c.PrimaryTableRelations)
                                                                }
                                                            )
                                                        }.SetArgs(
                                                            new EqualsOperator().SetArgs(
                                                                new ColumnIdentifier().SetArgs(
                                                                    new RelationalTable()
                                                                    {
                                                                        Label = GetLookupTableName(c.PrimaryTableRelations)
                                                                    },
                                                                    new RelationalColumn()
                                                                    {
                                                                        Label = MergeIndexColumnName
                                                                    }
                                                                ),
                                                                new ColumnIdentifier().SetArgs(
                                                                    new RelationalTable()
                                                                    {
                                                                        Label = GetLookupTableName(upsertTableRelations.TableRelations)
                                                                    },
                                                                    new RelationalColumn()
                                                                    {
                                                                        Label = GetForeignColumnIndexName(kp.ForeignTableColumn.Name)
                                                                    }
                                                                )
                                                            )
                                                        )
                                                    )
                                                ))
                                                
                                            ))
                                        }
                                    )
                                ),
                            WhereClause = new WhereClause().SetArgs(
                                                new NotExists().SetArgs(
                                                    new Select()
                                                    {
                                                        SelectClause = new SelectClause().SetArgs(
                                                            new Literal() { Value = "*" }
                                                        ),
                                                        FromClause = new FromClause().SetArgs(
                                                            new SubqueryAlias()
                                                            {
                                                                Alias = tableRowNumberQueryAlias
                                                            }.SetArgs(
                                                                new Select()
                                                                {
                                                                    SelectClause = new SelectClause().SetArgs(
                                                                        new Alias()
                                                                        {
                                                                            Label = rowNumberAlias
                                                                        }.SetArgs(
                                                                            new OverClause()
                                                                            {
                                                                                Function = new Function()
                                                                                {
                                                                                    Name = "ROW_NUMBER"
                                                                                }
                                                                            }.SetArgs(
                                                                                new PartitionByClause().SetArgs(
                                                                                    upsertTableRelations.TableRelations.TargetTable.Columns.Select(c =>
                                                                                        new ColumnIdentifier().SetArgs(
                                                                                            new RelationalColumn()
                                                                                            {
                                                                                                Label = c.Name
                                                                                            }
                                                                                        )
                                                                                    )
                                                                                ),
                                                                                new OrderByClause().SetArgs(
                                                                                    upsertTableRelations.TableRelations.TargetTable.Columns.Select(c =>
                                                                                        new ColumnIdentifier().SetArgs(
                                                                                            new RelationalColumn()
                                                                                            {
                                                                                                Label = c.Name
                                                                                            }
                                                                                        )
                                                                                    )
                                                                                )
                                                                            )
                                                                        ).AsEnumerable<AstNode>().Concat(
                                                                            upsertTableRelations.TableRelations.TargetTable.Columns.Select(c =>
                                                                                new ColumnIdentifier().SetArgs(
                                                                                    new RelationalColumn()
                                                                                    {
                                                                                        Label = c.Name
                                                                                    }
                                                                                )
                                                                            )
                                                                        )
                                                                    ),
                                                                    FromClause = new FromClause().SetArgs(
                                                                        new TableIdentifier().SetArgs(
                                                                            new RelationalTable()
                                                                            {
                                                                                Label = upsertTableRelations.TableRelations.TableName
                                                                            }
                                                                        )
                                                                    )
                                                                }
                                                            )),
                                                        WhereClause = new WhereClause().SetArgs(
                                                            new AndOperator().SetArgs(
                                                                new EqualsOperator().SetArgs(
                                                                    new ColumnIdentifier().SetArgs(
                                                                        new RelationalTable()
                                                                        {
                                                                            Label = lookupRowNumberQueryAlias
                                                                        },
                                                                        new RelationalColumn()
                                                                        {
                                                                            Label = rowNumberAlias
                                                                        }
                                                                    ),
                                                                    new ColumnIdentifier().SetArgs(
                                                                        new RelationalTable()
                                                                        {
                                                                            Label = tableRowNumberQueryAlias
                                                                        },
                                                                        new RelationalColumn()
                                                                        {
                                                                            Label = rowNumberAlias
                                                                        }
                                                                    )
                                                                ).AsEnumerable().Concat(
                                                                    upsertTableRelations.ForeignTableColumns.SelectMany(c =>
                                                                        c.ForeignKey.KeyPairs.Select(kp =>
                                                                            new EqualsOperator().SetArgs(
                                                                                new ColumnIdentifier().SetArgs(
                                                                                    new RelationalTable()
                                                                                    {
                                                                                        Label = lookupRowNumberQueryAlias
                                                                                    },
                                                                                    new RelationalColumn()
                                                                                    {
                                                                                        Label = $"{GetLookupTableName(c.PrimaryTableRelations)}{kp.ForeignTableColumn.Name}"
                                                                                    }
                                                                                ),
                                                                                new ColumnIdentifier().SetArgs(
                                                                                    new RelationalTable()
                                                                                    {
                                                                                        Label = tableRowNumberQueryAlias
                                                                                    },
                                                                                    new RelationalColumn()
                                                                                    {
                                                                                        Label = kp.ForeignTableColumn.Name
                                                                                    }
                                                                                )
                                                                            )
                                                                        )
                                                                    )
                                                                )
                                                            )
                                                        )
                                                    }
                                                )
                                            )
                        }
                    )
                );
        }

        private static void ModifyOneToManyMergeSelectStatement(ITableDefinition targetTable,
            UpsertTableRelations upsertTableRelations, Select mergeSelectStatement)
        {
            //where("Id" is null /* and "CompositeId2" is null */)
            //or not exists(
            //  select 1 from "Employee"
            //  where "Employee"."Id" = "EmployeeLookup"."Id" /*
            //  and "Employee"."CompositeId2" = "EmployeeLookup"."CompositeId2" */)
            if ((targetTable.PrimaryKey?.Columns?.Any()).GetValueOrDefault(false))
            {
                mergeSelectStatement.WhereClause ??= new WhereClause();
                mergeSelectStatement.WhereClause.Args ??= new List<AstNode>();
                mergeSelectStatement.WhereClause.Args =
                    mergeSelectStatement.WhereClause.Args.AppendOne(
                        new OrOperator().SetArgs(
                            targetTable.PrimaryKey.Columns.Select(c =>
                                (AstNode)
                                new AndOperator().SetArgs(
                                    new IsOperator().SetArgs(
                                        new ColumnIdentifier()
                                            .SetArgs(new RelationalColumn()
                                            {
                                                Label = c.Name
                                            }),
                                        new NullLiteral()
                                    )
                                )
                            ).AppendOne(
                                new NotExists().SetArgs(
                                    new Select()
                                    {
                                        SelectClause =
                                            new SelectClause().SetArgs(new Literal() {Value = "1"}),
                                        FromClause = new FromClause().SetArgs(
                                            new FromClauseNode().SetArgs(
                                                new TableIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = targetTable.Name
                                                    }
                                                )
                                            )
                                        ),
                                        WhereClause = new WhereClause().SetArgs(
                                            new AndOperator().SetArgs(
                                                targetTable.PrimaryKey.Columns.Select(c =>
                                                    new EqualsOperator().SetArgs(
                                                        new ColumnIdentifier().SetArgs(
                                                            new RelationalTable()
                                                            {
                                                                Label = targetTable.Name
                                                            },
                                                            new RelationalColumn()
                                                            {
                                                                Label = c.Name
                                                            }),
                                                        new ColumnIdentifier().SetArgs(
                                                            new RelationalTable()
                                                            {
                                                                Label = GetLookupTableName(upsertTableRelations.TableRelations)
                                                            },
                                                            new RelationalColumn()
                                                            {
                                                                Label = c.Name
                                                            })
                                                    )
                                                )
                                            )
                                        )
                                    }
                                ))
                        )
                    );
            }
        }
    }
}
