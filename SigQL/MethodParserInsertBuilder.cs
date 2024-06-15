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

        private MethodSqlStatement BuildInsertStatement(UpsertSpec insertSpec, List<ParameterPath> parameterPaths)
        {
            var targetTableType = insertSpec.UnwrappedReturnType;
            var builderAstCollection = BuildInsertAstCollection(insertSpec, parameterPaths);

            var sqlStatement = new MethodSqlStatement()
            {
                CommandAst = builderAstCollection.Statements,
                SqlBuilder = this.builder,
                ReturnType = insertSpec.ReturnType,
                UnwrappedReturnType = targetTableType,
                Parameters = parameterPaths,
                Tokens = builderAstCollection.Tokens,
                TargetTablePrimaryKey = insertSpec.Table.PrimaryKey,
                TablePrimaryKeyDefinitions = builderAstCollection.TablePrimaryKeyDefinitions
            };

            return sqlStatement;
        }

        private InsertBuilderAstCollection BuildInsertAstCollection(UpsertSpec insertSpec,
            List<ParameterPath> parameterPaths)
        {
            var builderAstCollection = new InsertBuilderAstCollection();
            var statement = builderAstCollection.Statements;
            var tablePrimaryKeyDefinitions = builderAstCollection.TablePrimaryKeyDefinitions;

            var tokens = builderAstCollection.Tokens;

            
            for (var index = 0; index < insertSpec.UpsertTableRelationsCollection.Count; index++)
            {
                var upsertTableRelations = insertSpec.UpsertTableRelationsCollection[index];
                
                //if (insertSpec.IsSingular)
                //{
                //    var insert = BuildInsertSingleAst(insertSpec);
                //    statement.AddRange(insert);
                //}
                //else
                {
                    var insertColumnList = GenerateInsertColumnListAst(upsertTableRelations);
                    var valuesListClause = new ValuesListClause();
                    var mergeTableAlias = "i";

                    var insertColumnParameter = upsertTableRelations.ColumnParameters.FirstOrDefault();

                    var lookupParameterTableName = GetLookupTableName(upsertTableRelations.TableRelations);
                    var declareLookupParameterStatement = BuildDeclareLookupParameterStatement(lookupParameterTableName, upsertTableRelations);
                    statement.Add(declareLookupParameterStatement);

                    var tableColumns = BuildTableColumnsAst(upsertTableRelations);

                    var foreignColumns = BuildForeignColumnsAst(upsertTableRelations);

                    var lookupParameterTableInsertResult = BuildLookupParameterTableInsert(parameterPaths, lookupParameterTableName, tableColumns, foreignColumns, upsertTableRelations, insertColumnParameter, insertSpec.Table.Name, insertSpec.RootMethodInfo.Name);
                    var lookupParameterTableInsert = lookupParameterTableInsertResult.Item1;
                    var tokenPath = lookupParameterTableInsertResult.Item2;
                    statement.Add(lookupParameterTableInsert);
                    if (tokenPath != null)
                        tokens.Add(tokenPath);

                    var mergeSelectStatement = new Select()
                    {
                        SelectClause = new SelectClause().SetArgs(
                            upsertTableRelations.ColumnParameters.Select(c =>
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalColumn() { Label = c.Column.Name }
                                    )
                                ).AppendOne(new ColumnIdentifier().SetArgs(
                                    new RelationalColumn() { Label = MergeIndexColumnName }
                                ))
                                .Concat(foreignColumns)),
                        FromClause =
                            new FromClause().SetArgs(
                                new FromClauseNode().SetArgs(
                                    new TableIdentifier().SetArgs(
                                        new Alias()
                                        {
                                            Label = lookupParameterTableName
                                        }.SetArgs(
                                            new NamedParameterIdentifier() { Name = lookupParameterTableName }
                                        )
                                        )))
                    };
                    builderAstCollection.RegisterReference(upsertTableRelations.TableRelations.TargetTable, InsertBuilderAstCollection.AstReferenceSource.MergeSelectClause, mergeSelectStatement);
                    var merge = new Merge()
                    {
                        Table = new TableIdentifier().SetArgs(new RelationalTable()
                        { Label = upsertTableRelations.TableRelations.TableName }),
                        Using = new MergeUsing()
                        {
                            Values =
                                mergeSelectStatement,
                            As = new TableAliasDefinition() { Alias = mergeTableAlias }
                                .SetArgs(
                                    upsertTableRelations.ColumnParameters.Select(cp =>
                                        (AstNode)new ColumnDeclaration().SetArgs(
                                            new RelationalColumn() { Label = cp.Column.Name }
                                        )
                                    ).AppendOne(
                                        new ColumnDeclaration().SetArgs(
                                            new RelationalColumn() { Label = MergeIndexColumnName })
                                    ).Concat(foreignColumns)
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

                    var foreignSelectValueStatements =
                        BuildForeignValueLookupStatements(upsertTableRelations, mergeTableAlias);

                    valuesListClause.SetArgs(
                        new ValuesList().SetArgs(
                            upsertTableRelations.ColumnParameters.Where(cp => !cp.Column.IsIdentity).Select(cp =>
                                (AstNode)new ColumnIdentifier().SetArgs(
                                    new RelationalTable() { Label = mergeTableAlias },
                                    new RelationalColumn() { Label = cp.Column.Name }
                                )
                            ).Concat(foreignSelectValueStatements.Select(c => c.Item2).ToList())
                        )
                    );

                    builderAstCollection.RegisterReference(upsertTableRelations.TableRelations.TargetTable, InsertBuilderAstCollection.AstReferenceSource.Merge, merge);
                    statement.Add(merge);

                    if (upsertTableRelations.TableRelations.TargetTable.PrimaryKey != null && 
                        (FindRootArgument(upsertTableRelations.TableRelations.Argument).Type != typeof(void)))
                    {
                        var updateLookupTablePKsStatement = BuildUpdateLookupStatement(upsertTableRelations.TableRelations);
                        if (updateLookupTablePKsStatement != null)
                        {
                            statement.Add(updateLookupTablePKsStatement);
                            builderAstCollection.RegisterReference(upsertTableRelations.TableRelations.TargetTable, InsertBuilderAstCollection.AstReferenceSource.UpdateLookupIds, updateLookupTablePKsStatement);
                        }
                            
                        var outputParameterTableName = GetInsertedTableName(upsertTableRelations.TableRelations);
                        var declareOutputParameterStatement = BuildDeclareInsertedTableParameterStatement(outputParameterTableName, upsertTableRelations);
                        statement.Insert(0, declareOutputParameterStatement);

                        var insertedTableName = "inserted";
                        merge.WhenNotMatched.Insert.Output = new OutputClause()
                        {
                            Into = new IntoClause()
                            { Object = new NamedParameterIdentifier() { Name = outputParameterTableName } }.SetArgs(
                                upsertTableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(c =>
                                        new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = c.Name }))
                                    .AppendOne(
                                        new ColumnIdentifier().SetArgs(new RelationalColumn()
                                        { Label = MergeIndexColumnName })))
                        }.SetArgs(
                            upsertTableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(c =>
                                    new ColumnIdentifier().SetArgs(new RelationalTable() { Label = insertedTableName },
                                        new RelationalColumn() { Label = c.Name }))
                                .AppendOne(
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalTable() { Label = mergeTableAlias },
                                        new RelationalColumn() { Label = MergeIndexColumnName }
                                    ))
                        );

                    }
                }
            }
            
            if (insertSpec.ReturnType != typeof(void))
            {
                var selectStatement = BuildUpsertOutputSelectStatement(insertSpec, tablePrimaryKeyDefinitions);

                statement.Add(selectStatement);
            }

            return builderAstCollection;
        }

        private Select BuildUpsertOutputSelectStatement(UpsertSpec insertSpec, ConcurrentDictionary<string, IEnumerable<string>> tablePrimaryKeyDefinitions)
        {
            var tableRelations = this.databaseResolver.BuildTableRelations(insertSpec.Table,
                new TypeArgument(insertSpec.ReturnType, this.databaseResolver), TableRelationsColumnSource.ReturnType,
                tablePrimaryKeyDefinitions);
            var matchingInsertTableRelations = insertSpec.UpsertTableRelationsCollection
                .Where(tr =>
                    TableEqualityComparer.Default.Equals(tr.TableRelations.TargetTable, tableRelations.TargetTable))
                .OrderBy(tr => tr.TableRelations.CalculateDepth())
                .First();
            
            var selectClauseBuilder = new SelectClauseBuilder(this.databaseResolver);
            var resolvedSelectClause = selectClauseBuilder.Build(tableRelations, tablePrimaryKeyDefinitions);
            var fromClauseRelations = resolvedSelectClause.FromClauseRelations;
            var selectClause = resolvedSelectClause.Ast;

            var fromClauseNode = BuildFromClause(fromClauseRelations);

            var primaryTable = fromClauseRelations.TargetTable;

            OrderByClause orderByClause = null;
            WhereClause whereClause = null;
            //if (insertSpec.IsSingular)
            //{
            //    whereClause = new WhereClause().SetArgs(
            //        primaryTable.PrimaryKey.Columns.Select(c =>
            //            new AndOperator().SetArgs(
            //            new EqualsOperator().SetArgs(
            //                //(c.IsIdentity ? (AstNode)
            //                //    new Function()
            //                //    {
            //                //        Name = "SCOPE_IDENTITY"
            //                //    } :
            //                    new NamedParameterIdentifier()
            //                    {
            //                        Name = matchingInsertTableRelations.ColumnParameters.SingleOrDefault(cp => ColumnEqualityComparer.Default.Equals(cp.Column, c))?.ParameterPath.SqlParameterName ?? c.Name
            //                    }
            //                //)
            //                ,
            //                new ColumnIdentifier().SetArgs(
            //                    new RelationalTable()
            //                    {
            //                        Label = tableRelations.Alias
            //                    },
            //                    new RelationalColumn()
            //                    {
            //                        Label = c.Name
            //                    }
            //                )
            //            )
            //            )
            //        )
            //    );
            //}
            //else
            {
                var outputParameterTableName = GetLookupTableName(matchingInsertTableRelations.TableRelations);
                var outputParameterTableSelectAlias = outputParameterTableName;
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
                
                orderByClause = new OrderByClause().SetArgs(
                    primaryTable.PrimaryKey.Columns.Select(pks =>
                        new OrderByIdentifier().SetArgs(
                            new ColumnIdentifier().SetArgs(
                                new RelationalTable() { Label = outputParameterTableSelectAlias },
                                new RelationalColumn() { Label = MergeIndexColumnName })
                        )
                    )
                );
            }
            
            var fromClause = new FromClause().SetArgs(fromClauseNode);
            
            var selectStatement = new Select()
            {
                SelectClause = selectClause,
                FromClause = fromClause,
                WhereClause = whereClause,
                OrderByClause = orderByClause
            };
            return selectStatement;
        }

        //private static List<AstNode> BuildInsertSingleAst(UpsertSpec insertSpec)
        //{
        //    var statements = new List<AstNode>();
        //    var tableRelations = insertSpec.UpsertTableRelationsCollection[0];
        //    var insertColumnList = GenerateInsertColumnListAst(tableRelations);
        //    var valuesListClause = new ValuesListClause();
        //    valuesListClause.SetArgs(
        //        new ValuesList().SetArgs(
        //            tableRelations.ColumnParameters.Where(c => !c.Column.IsIdentity).Select(cp =>
        //                new NamedParameterIdentifier()
        //                {
        //                    Name = cp.ParameterPath.SqlParameterName
        //                })
        //        )
        //    );
        //    var insert = new Insert()
        //    {
        //        Object = new TableIdentifier().SetArgs(new RelationalTable() {Label = insertSpec.Table.Name}),
        //        ColumnList =
        //            insertColumnList,
        //        ValuesList = valuesListClause
        //    };
        //    statements.Add(insert);
        //    if (tableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Any(c => c.IsIdentity))
        //    {
        //        var declareStatements = tableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(pkc =>
        //            new DeclareStatement()
        //            {
        //                Parameter = new NamedParameterIdentifier()
        //                {
        //                    Name = tableRelations.ColumnParameters
        //                        .SingleOrDefault(cp => ColumnEqualityComparer.Default.Equals(cp.Column, pkc))
        //                        ?.ParameterPath.SqlParameterName ?? pkc.Name
        //                },
        //                DataType = new DataType() { Type = new Literal() { Value = pkc.DataTypeDeclaration } }
        //            }
        //        ).ToList();
        //        var setParameterStatements = tableRelations.TableRelations.TargetTable.PrimaryKey.Columns.Select(pkc =>
        //            new SetParameter()
        //            {
        //                Parameter = new NamedParameterIdentifier()
        //                {
        //                    Name = tableRelations.ColumnParameters
        //                        .SingleOrDefault(cp => ColumnEqualityComparer.Default.Equals(cp.Column, pkc))
        //                        ?.ParameterPath.SqlParameterName ?? pkc.Name
        //                },
        //                Value = new Function()
        //                {
        //                    Name = "SCOPE_IDENTITY"
        //                }
        //            }
        //        ).ToList();
        //        statements.AddRange(declareStatements);
        //        statements.AddRange(setParameterStatements);
        //    }
        //    return statements;
        //}

        private static List<ColumnIdentifier> GenerateInsertColumnListAst(UpsertTableRelations upsertTableRelations)
        {
            return upsertTableRelations.ColumnParameters.Where(cp => !cp.Column.IsIdentity).Select(cp =>
                    new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = cp.Column.Name }))
                .Concat(upsertTableRelations.ForeignTableColumns.SelectMany(fk =>
                    fk.ForeignKey.GetForeignColumns().Select(fc =>
                        new ColumnIdentifier().SetArgs(
                            new RelationalColumn() { Label = fc.Name }))
                ))
                .ToList();
        }

        private static List<Tuple<IColumnDefinition, AstNode>> BuildForeignValueLookupStatements(UpsertTableRelations upsertTableRelations, string sourceTableAlias)
        {
            var list = upsertTableRelations.ForeignTableColumns.SelectMany(fk =>
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
                                    new RelationalTable() {Label = sourceTableAlias},
                                    new RelationalColumn() {Label = GetForeignColumnIndexName(kp.ForeignTableColumn.Name) })
                            )
                        );
                    return new Tuple<IColumnDefinition, AstNode>(kp.ForeignTableColumn, new LogicalGrouping().SetArgs(selectStatement));
                })).ToList();
            return list;
        }

        private static IEnumerable<ColumnIdentifier> BuildTableColumnsAst(UpsertTableRelations upsertTableRelations)
        {
            var tableColumns = upsertTableRelations.ColumnParameters.Select(c =>
                new ColumnIdentifier().SetArgs(
                    new RelationalColumn() { Label = c.Column.Name }
                )).ToList();
            return tableColumns.AppendOne(
                        new ColumnIdentifier().SetArgs(
                            new RelationalColumn() { Label = MergeIndexColumnName }
                        )
                    );
        }

        private static List<ColumnIdentifier> BuildForeignColumnsAst(UpsertTableRelations upsertTableRelations)
        {
            return upsertTableRelations.ForeignTableColumns.SelectMany(fk =>
                fk.ForeignKey.GetForeignColumns().Select(fc =>
                    new ColumnIdentifier()
                        .SetArgs(new RelationalColumn()
                        {
                            Label = GetForeignColumnIndexName(fc.Name)
                        })
                )).ToList();
        }

        private Tuple<AstNode, TokenPath> BuildLookupParameterTableInsert(List<ParameterPath> parameterPaths, string lookupParameterTableName,
            IEnumerable<ColumnIdentifier> tableColumns, List<ColumnIdentifier> foreignColumns,
            UpsertTableRelations insertTableRelations, UpsertColumnParameter insertColumnParameter, string targetTableName, string rootMethodName)
        {
            var mergeValuesParametersList = new ValuesListClause();
            var lookupParameterTableInsert = new Insert()
            {
                Object = new TableIdentifier().SetArgs(new NamedParameterIdentifier()
                    {Name = lookupParameterTableName}),
                ColumnList = tableColumns.Concat(foreignColumns)
                    .ToList(),
                ValuesList = mergeValuesParametersList
            };

            if (IsMethodParamInsert(insertTableRelations))
            {
                mergeValuesParametersList.SetArgs(new ValuesList().SetArgs(
                    insertTableRelations.ColumnParameters.Select(cp => (AstNode)
                        new NamedParameterIdentifier()
                        {
                            Name = cp.ParameterPath.SqlParameterName
                        }).AppendOne(new Literal() { Value = "0" }))
                );
                return new Tuple<AstNode, TokenPath>(lookupParameterTableInsert, null);
            }
            else
            {
                var tokenPath = new TokenPath(FindRootArgument(insertTableRelations.TableRelations.Argument).FindParameter())
                {
                    SqlParameterName = insertColumnParameter?.ParameterPath.SqlParameterName,
                    UpdateNodeFunc = (parameterValue, tokenPath, allParameterArgs) =>
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
                            parentIndexMappings = BuildManyToManyParentIndexMappings(insertTableRelations, orderedParameterLookup);

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
                                            cp.ParameterPath.Properties.LastOrDefault()?.AsEnumerable() ?? new List<PropertyInfo>());
                                        return new NamedParameterIdentifier()
                                        {
                                            Name = sqlParameterName
                                        };
                                    }).Cast<AstNode>().AppendOne(new Literal() { Value = param.Index.ToString() })
                                        .Concat(
                                            OrderIndexReferences(insertTableRelations, parentIndexMappings)
                                                .Where(p => p.InsertedIndex == param.Index)
                                                .Select(p =>
                                                    new Literal() { Value = p.PrimaryTableIndex.ToString() }).ToList()));
                            }));
                        }
                        else
                        {
                            throw new ArgumentException(
                                $"Unable to insert items for {targetTableName} (via method {rootMethodName}) from null or empty list.");
                        }

                        return sqlParameters;
                    }
                };
                return new Tuple<AstNode, TokenPath>(lookupParameterTableInsert, tokenPath);
            }
        }

        private static bool IsMethodParamInsert(UpsertTableRelations insertTableRelations)
        {
            return (insertTableRelations.TableRelations.Argument.Parent == null || insertTableRelations.TableRelations.Argument is TableArgument) &&
                   insertTableRelations.TableRelations.Argument.ClassProperties.Any() && insertTableRelations.TableRelations.Argument.ClassProperties.All(c =>
                c is ParameterArgument pa && !pa.ClassProperties.Any());
        }

        private static IEnumerable<TableIndexReference> BuildManyToManyParentIndexMappings(UpsertTableRelations insertTableRelations,
            OrderedParameterValueLookup orderedParameterLookup)
        {
            IEnumerable<TableIndexReference> parentIndexMappings;
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
            return parentIndexMappings;
        }

        private static DeclareStatement BuildDeclareInsertedTableParameterStatement(string outputParameterTableName,
            UpsertTableRelations insertTableRelations)
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
            UpsertTableRelations insertTableRelations)
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

        private static IEnumerable<TableIndexReference> OrderIndexReferences(UpsertTableRelations insertTableRelations, IEnumerable<TableIndexReference> parentIndexMappings)
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

        private static IEnumerable<TableIndexReference> GetTableIndexReferences(IEnumerable<OrderedParameterValue> orderedParametersForInsert, UpsertTableRelations insertTableRelations, OrderedParameterValueLookup orderedParameterLookup)
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

        private UpsertSpec GetUpsertSpec(MethodInfo methodInfo)
        {
            var upsertAttribute = 
                methodInfo.GetCustomAttributes(typeof(InsertAttribute), false).Cast<IUpsertAttribute>().FirstOrDefault() 
                ?? methodInfo.GetCustomAttributes(typeof(UpdateByKeyAttribute), false).Cast<IUpsertAttribute>().FirstOrDefault()
                ?? methodInfo.GetCustomAttributes(typeof(UpsertAttribute), false).Cast<IUpsertAttribute>().FirstOrDefault()
                ?? methodInfo.GetCustomAttributes(typeof(SyncAttribute), false).Cast<IUpsertAttribute>().FirstOrDefault();
            if (upsertAttribute != null)
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

                var insertSpec = new UpsertSpec();
                if (!string.IsNullOrEmpty(upsertAttribute.TableName))
                {
                    insertSpec.Table = this.databaseConfiguration.Tables.FindByName(upsertAttribute.TableName);
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
                    TableRelationsColumnSource.Parameters, new ConcurrentDictionary<string, IEnumerable<string>>());


                var dependencyOrderedTableRelations = GetInsertTableRelationsOrderedByDependencies(parameterTableRelations).ToList();
                insertSpec.UpsertTableRelationsCollection = dependencyOrderedTableRelations.Select(tr => ToInsertTableRelations(tr, dependencyOrderedTableRelations)).ToList();
                
                insertSpec.ReturnType = methodInfo.ReturnType;
                insertSpec.UnwrappedReturnType = OutputFactory.UnwrapType(methodInfo.ReturnType);
                insertSpec.RootMethodInfo = methodInfo;

                var multipleInsertParameters = insertSpec.UpsertTableRelationsCollection[0].ColumnParameters.Where(c =>
                {
                    return this.databaseResolver.IsTableOrTableProjection(c.ParameterPath.Parameter.ParameterType) &&
                           TableEqualityComparer.Default.Equals(
                               this.databaseResolver.DetectTable(c.ParameterPath.Parameter.ParameterType),
                               insertSpec.Table);
                }).ToList();


                if (insertSpec.UpsertTableRelationsCollection[0].ColumnParameters.Any() &&
                    multipleInsertParameters.Any(p =>
                        p.ParameterPath.Parameter !=
                        insertSpec.UpsertTableRelationsCollection[0].ColumnParameters.First().ParameterPath.Parameter))
                {
                    throw new InvalidOperationException(
                        $"Only one parameter can represent multiple inserts for target table {insertSpec.Table.Name}");
                }

                insertSpec.IsSingular =
                    !multipleInsertParameters.Any(p => p.ParameterPath.Parameter.ParameterType.IsCollectionType());

                return insertSpec;
            }
            
            return null;
        }

        private UpsertTableRelations ToInsertTableRelations(TableRelations parameterTableRelations, List<TableRelations> dependencyList)
        {
            var insertRelations = new UpsertTableRelations();
            insertRelations.ColumnParameters = parameterTableRelations.ProjectedColumns.SelectMany(pc =>
                pc.Arguments.All.Select(arg =>
                    {
                        var parameterPath = arg.ToParameterPath();
                        parameterPath.SqlParameterName = parameterPath.GenerateSuggestedSqlIdentifierName();
                        return new UpsertColumnParameter()
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

        private class UpsertTableRelations
        {
            public IList<UpsertColumnParameter> ColumnParameters { get; set; }
            public TableRelations TableRelations { get; set; }
            
            public IEnumerable<ForeignTableColumn> ForeignTableColumns { get; set; }
            
        }

        private static string GetLookupTableName(TableRelations tableRelations)
        {
            return tableRelations.Alias.Replace("<", "$").Replace(">", "$").Replace(".", "$") + "Lookup";
        }

        private static string GetInsertedTableName(TableRelations tableRelations)
        {
            return "inserted" + tableRelations.Alias.Replace("<", "$").Replace(">", "$").Replace(".", "$");
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

        private class UpsertSpec
        {
            public UpsertSpec()
            {
                this.UpsertTableRelationsCollection = new List<UpsertTableRelations>();
            }

            public ITableDefinition Table { get; set; }
            public Type ReturnType { get; set; }
            public Type UnwrappedReturnType { get; set; }
            public MethodInfo RootMethodInfo { get; set; }
            public bool IsSingular { get; set; }
            
            public List<UpsertTableRelations> UpsertTableRelationsCollection { get; set; }
        }
        

        private class UpsertColumnParameter
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

        private class InsertBuilderAstCollection
        {
            internal List<AstNode> Statements { get; }
            internal ConcurrentDictionary<string, IEnumerable<string>> TablePrimaryKeyDefinitions { get; }
            internal List<TokenPath> Tokens { get; }

            private IDictionary<ITableDefinition, IDictionary<AstReferenceSource, AstNode>> astReferences;

            public InsertBuilderAstCollection()
            {
                this.Statements = new List<AstNode>();
                this.TablePrimaryKeyDefinitions = new ConcurrentDictionary<string, IEnumerable<string>>();
                this.Tokens = new List<TokenPath>();
                this.astReferences = new Dictionary<ITableDefinition, IDictionary<AstReferenceSource, AstNode>>(TableEqualityComparer.Default);
            }

            internal void RegisterReference(ITableDefinition tableDefinition, AstReferenceSource source, AstNode ast)
            {
                if (!astReferences.ContainsKey(tableDefinition))
                {
                    astReferences[tableDefinition] = new ConcurrentDictionary<AstReferenceSource, AstNode>();
                }
                
                astReferences[tableDefinition][source] = ast;
            }

            internal T GetReference<T>(ITableDefinition tableDefinition, AstReferenceSource source)
                where T: AstNode
            {
                if (astReferences[tableDefinition].ContainsKey(source))
                {
                    return (T)astReferences[tableDefinition][source];
                }

                return null;
            }

            internal enum AstReferenceSource
            {
                MergeSelectClause,
                UpdateLookupIds,
                Merge
            }
        }
    }
}
