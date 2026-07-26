using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SigQL.Exceptions;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Types.Attributes;

namespace SigQL
{
    public partial class MethodParser
    {
        private MethodSqlStatement BuildUpdateByKeyStatement(UpsertSpec insertSpec, List<ParameterPath> parameterPaths)
        {
            var targetTableType = insertSpec.UnwrappedReturnType;

            var statement = new List<AstNode>();
            var tablePrimaryKeyDefinitions = new ConcurrentDictionary<string, IEnumerable<string>>();

            var tokens = new List<TokenPath>();
            //if (insertSpec.IsSingular)
            //{
            //    var update = BuildUpdateSingleAst(insertSpec);
            //    statement.Add(update);
            //}
            //else
            {
                for (var index = 0; index < insertSpec.UpsertTableRelationsCollection.Count; index++)
                {
                    var upsertTableRelations = insertSpec.UpsertTableRelationsCollection[index];
                    var insertColumnParameter = upsertTableRelations.ColumnParameters.FirstOrDefault();

                    var tableColumns = BuildTableColumnsAst(upsertTableRelations);
                    var foreignColumns = BuildForeignColumnsAst(upsertTableRelations);

                    var lookupTableName = GetLookupTableName(upsertTableRelations.TableRelations);
                    var declareLookupParameterStatement = BuildDeclareLookupParameterStatement(lookupTableName,
                        upsertTableRelations);
                    statement.Add(declareLookupParameterStatement);

                    var lookupParameterTableInsertResult = BuildLookupParameterTableInsert(parameterPaths, lookupTableName, tableColumns, foreignColumns, upsertTableRelations, insertColumnParameter, insertSpec.Table.Name, insertSpec.RootMethodInfo.Name);
                    statement.Add(lookupParameterTableInsertResult.Item1);
                    if(lookupParameterTableInsertResult.Item2 != null)
                        tokens.Add(lookupParameterTableInsertResult.Item2);

                    var updateFromLookupStatement = BuildUpdateFromLookupStatement(upsertTableRelations, lookupTableName);
                    statement.Add(updateFromLookupStatement);
                }
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

        //private static Update BuildUpdateSingleAst(UpsertSpec insertSpec)
        //{
        //    var update = new Update();
        //    var tableRelations = insertSpec.UpsertTableRelationsCollection[0];
        //    update.SetClause = tableRelations.ColumnParameters
        //        .Where(cp =>
        //            !tableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Any(c =>
        //                ColumnEqualityComparer.Default.Equals(c, cp.Column)))
        //        .Select(cp =>
        //            new SetEqualOperator().SetArgs(
        //                new ColumnIdentifier().SetArgs(
        //                    new RelationalColumn()
        //                    {
        //                        Label = cp.Column.Name
        //                    }
        //                ),
        //                new NamedParameterIdentifier()
        //                {
        //                    Name = cp.ParameterPath.SqlParameterName
        //                }
        //            )
        //        ).ToList();
        //    update.WhereClause = new WhereClause();
        //    update.WhereClause.SetArgs(
        //        new AndOperator().SetArgs(
        //            tableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(c =>
        //                new EqualsOperator().SetArgs(
        //                    new ColumnIdentifier().SetArgs(
        //                        new RelationalColumn()
        //                        {
        //                            Label = c.Name
        //                        }
        //                    ),
        //                    new NamedParameterIdentifier()
        //                    {
        //                        Name = tableRelations.ColumnParameters
        //                            .Single(cp => ColumnEqualityComparer.Default.Equals(cp.Column, c)).ParameterPath
        //                            .SqlParameterName
        //                    }
        //                )
        //            )
        //        )
        //    );
        //    update.SetArgs(
        //        new TableIdentifier().SetArgs(
        //            new RelationalTable()
        //            {
        //                Label = tableRelations.TableRelations.TableName
        //            }
        //        )
        //    );
        //    return update;
        //}

        private Update BuildUpdateFromLookupStatement(UpsertTableRelations upsertTableRelations, string lookupTableName)
        {
            var targetTable = upsertTableRelations.TableRelations.TargetTable;
            var primaryKeyColumns = upsertTableRelations.KeyColumns ?? targetTable.PrimaryKey.Columns;
            var foreignValueLookupStatements = BuildForeignValueLookupStatements(upsertTableRelations, lookupTableName);
            var settableColumns = upsertTableRelations.ColumnParameters
                .Where(c =>
                {
                    return !c.Column.IsIdentity &&
                           !primaryKeyColumns.Any(pkc =>
                               ColumnEqualityComparer.Default.Equals(c.Column, pkc));
                }).ToList();

            // every value is a key value, so there is nothing left to assign. an update with an
            // empty set clause is not valid sql, and the caller almost certainly meant to include
            // at least one non-key column.
            if (!settableColumns.Any() && !foreignValueLookupStatements.Any())
            {
                throw new InvalidAttributeException(typeof(UpdateByKeyAttribute), Array.Empty<MemberInfo>(),
                    $"Unable to build an update for {targetTable.Name}: every supplied column ({string.Join(", ", upsertTableRelations.ColumnParameters.Select(c => c.Column.Name))}) is part of the key ({string.Join(", ", primaryKeyColumns.Select(c => c.Name))}), leaving nothing to update. Include at least one non-key column in the values class.");
            }

            var ast = new Update()
            {
                SetClause =
                    settableColumns
                        .Select(c => new SetEqualOperator()
                            .SetArgs(
                                new ColumnIdentifier().SetArgs(
                                    new RelationalColumn()
                                    {
                                        Label = c.Column.Name
                                    }),
                                BuildUpsertSetValueExpression(c, lookupTableName, targetTable.Name)
                            )).ToList()
                        .Concat(foreignValueLookupStatements.Select(c => 
                            new SetEqualOperator()
                                .SetArgs(new ColumnIdentifier()
                                    .SetArgs(new RelationalColumn()
                                        {
                                            Label = c.Item1.Name
                                        }),
                                c.Item2)
                            ).ToList()
                        ),
                FromClause = new FromClause().SetArgs(
                        new FromClauseNode().SetArgs(
                                new TableIdentifier().SetArgs(
                                    new RelationalTable() { Label = targetTable.Name }
                                ),
                                new InnerJoin()
                                {
                                    RightNode = new TableIdentifier()
                                        .SetArgs(
                                            new Alias()
                                            {
                                                Label = lookupTableName
                                            }.SetArgs(
                                                new NamedParameterIdentifier()
                                                {
                                                    Name = lookupTableName
                                                })
                                            )
                                }.SetArgs(
                                    new AndOperator().SetArgs(
                                        primaryKeyColumns.Select(pkc =>
                                            new EqualsOperator().SetArgs(
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = lookupTableName
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = pkc.Name
                                                    }),
                                                new ColumnIdentifier().SetArgs(
                                                    new RelationalTable()
                                                    {
                                                        Label = targetTable.Name
                                                    },
                                                    new RelationalColumn()
                                                    {
                                                        Label = pkc.Name
                                                    }))
                                        ).ToList()
                                    )
                                )
                         )
                 )
            }.SetArgs(new TableIdentifier().SetArgs(new RelationalTable() { Label = targetTable.Name }));

            return ast;
        }

        private AstNode BuildUpsertSetValueExpression(UpsertColumnParameter c, string lookupTableName, string targetTableName)
        {
            var lookupColumnRef = new ColumnIdentifier().SetArgs(
                new RelationalTable() { Label = lookupTableName },
                new RelationalColumn() { Label = c.Column.Name });

            if (c.IgnoreIfNullOrEmpty)
            {
                return new Function() { Name = "IsNull" }.SetArgs(
                    new Function() { Name = "NullIf" }.SetArgs(
                        lookupColumnRef,
                        new Literal() { Value = "''" }),
                    new ColumnIdentifier().SetArgs(
                        new RelationalTable() { Label = targetTableName },
                        new RelationalColumn() { Label = c.Column.Name }));
            }

            if (c.IgnoreIfNull)
            {
                return new Function() { Name = "IsNull" }.SetArgs(
                    lookupColumnRef,
                    new ColumnIdentifier().SetArgs(
                        new RelationalTable() { Label = targetTableName },
                        new RelationalColumn() { Label = c.Column.Name }));
            }

            return lookupColumnRef;
        }
    }
}
