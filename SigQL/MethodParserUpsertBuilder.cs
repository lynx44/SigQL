using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SigQL.Extensions;

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
                var mergeSelectStatement = builderAstCollection.GetMergeSelectReference(targetTable);
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
                                            new SelectClause().SetArgs(new Literal() { Value = "1" }),
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
    }
}
