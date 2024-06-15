using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
                        new Delete()
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
                                                new Literal() { Value = "1"}),
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
                                                new Literal() { Value = "1"}),
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
                        }
                    ).ToList();
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
