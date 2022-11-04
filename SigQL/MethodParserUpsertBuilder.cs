using System;
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
            var builderAstCollection = BuildInsertAstCollection(upsertSpec, parameterPaths);

            for (var index = 0; index < upsertSpec.UpsertTableRelationsCollection.Count; index++)
            {
                var upsertTableRelations = upsertSpec.UpsertTableRelationsCollection[index];
                var targetTable = upsertTableRelations.TableRelations.TargetTable;
                ModifyMergeSelectStatement(builderAstCollection, targetTable, upsertTableRelations);
                
                var updateLookupIdsAst = builderAstCollection.GetReference<AstNode>(targetTable,
                    InsertBuilderAstCollection.AstReferenceSource.UpdateLookupIds);
                var updateLookupIdsAstIndex = builderAstCollection.Statements.IndexOf(updateLookupIdsAst);
                var updateFromLookupStatement = BuildUpdateFromLookupStatement(upsertTableRelations, GetLookupTableName(upsertTableRelations.TableRelations));
                AppendWhereClauseToUpdateStatement(updateFromLookupStatement, targetTable, upsertTableRelations);
                builderAstCollection.Statements.Insert(updateLookupIdsAstIndex + 1, updateFromLookupStatement);
            }

            var sqlStatement = new MethodSqlStatement()
            {
                CommandAst = builderAstCollection.Statements,
                SqlBuilder = this.builder,
                ReturnType = upsertSpec.ReturnType,
                UnwrappedReturnType = targetTableType,
                Parameters = parameterPaths,
                Tokens = builderAstCollection.Tokens,
                TargetTablePrimaryKey = upsertSpec.Table.PrimaryKey,
                TablePrimaryKeyDefinitions = builderAstCollection.TablePrimaryKeyDefinitions
            };

            return sqlStatement;
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
