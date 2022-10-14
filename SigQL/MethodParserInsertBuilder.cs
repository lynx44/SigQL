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
        private MethodSqlStatement BuildInsertStatement(InsertSpec insertSpec, List<ParameterPath> parameterPaths)
        {
            var targetTableType = insertSpec.UnwrappedReturnType;

            
            var statement = new List<AstNode>();
            var tablePrimaryKeyDefinitions = new ConcurrentDictionary<string, ITableKeyDefinition>();

            var tokens = new List<TokenPath>();

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

                var multipleInsertParameters = insertTableRelations.ColumnParameters.Where(c =>
                {
                    return this.databaseResolver.IsTableOrTableProjection(c.ParameterPath.Parameter.ParameterType) &&
                           TableEqualityComparer.Default.Equals(
                               this.databaseResolver.DetectTable(c.ParameterPath.Parameter.ParameterType),
                               insertSpec.Table);
                }).ToList();
                if (multipleInsertParameters.Any(p =>
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
                    var mergeIndexColumnName = "_index";
                    var mergeTableAlias = "i";

                    var insertColumnParameter = multipleInsertParameters.FirstOrDefault();


                    //var lookupParameterTableName = $"{insertSpec.RelationalPrefix}{insertSpec.Table.Name}Lookup";
                    var lookupParameterTableName = GetLookupTableName(insertTableRelations.TableRelations);
                    var declareLookupParameterStatement = new DeclareStatement()
                    {
                        Parameter = new NamedParameterIdentifier() {Name = lookupParameterTableName},
                        DataType = new DataType() {Type = new Literal() {Value = "table"}}
                            .SetArgs(
                                insertTableRelations.ColumnParameters.Select(c =>
                                    new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() {Label = c.Column.Name},
                                        new DataType() {Type = new Literal() {Value = c.Column.DataTypeDeclaration}}
                                    )
                                ).Concat(new ColumnDeclaration().SetArgs(
                                    new RelationalColumn() {Label = mergeIndexColumnName},
                                    new DataType() {Type = new Literal() {Value = "int"}}
                                ).AsEnumerable())
                                .Concat(insertTableRelations.ForeignTableColumns.SelectMany(fk => 
                                        fk.ForeignKey.GetForeignColumns().Select(fc =>
                                            new ColumnDeclaration().SetArgs(
                                                new RelationalColumn() { Label = GetForeignColumnIndexName(fc.Name) },
                                                new DataType() { Type = new Literal() { Value = "int" } }))
                                )).ToList()
                            )
                    };
                    statement.Add(declareLookupParameterStatement);

                    var mergeValuesParametersList = new ValuesListClause();

                    var tableColumns = insertTableRelations.ColumnParameters.Select(c =>
                        new ColumnIdentifier().SetArgs(
                            new RelationalColumn() { Label = c.Column.Name }
                        )).AppendOne(new ColumnIdentifier().SetArgs(
                        new RelationalColumn() { Label = mergeIndexColumnName }
                    ));

                    var foreignColumns = insertTableRelations.ForeignTableColumns.SelectMany(fk =>
                        fk.ForeignKey.GetForeignColumns().Select(fc =>
                            new ColumnIdentifier()
                                .SetArgs(new RelationalColumn()
                                {
                                    Label = GetForeignColumnIndexName(fc.Name)
                                })
                        ));
                    var lookupParameterTableInsert = new Insert()
                    {
                        Object = new TableIdentifier().SetArgs(new NamedParameterIdentifier()
                            {Name = lookupParameterTableName}),
                        ColumnList = tableColumns.Concat(foreignColumns)
                        .ToList(),
                        ValuesList = mergeValuesParametersList
                    };

                    statement.Add(lookupParameterTableInsert);

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
                                            new RelationalColumn() {Label = mergeIndexColumnName}
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
                                            new RelationalColumn() {Label = mergeIndexColumnName})
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
                                    new Alias() {Label = GetInsertedTableName(fk.PrimaryTableRelations)}
                                        .SetArgs(new NamedParameterIdentifier()
                                            {Name = GetInsertedTableName(fk.PrimaryTableRelations)}));
                            selectStatement.WhereClause =
                                new WhereClause().SetArgs(
                                    new EqualsOperator().SetArgs(

                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable()
                                                {Label = GetInsertedTableName(fk.PrimaryTableRelations)},
                                            new RelationalColumn() {Label = mergeIndexColumnName}),
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

                    var tokenPath = new TokenPath(FindRootArgument(insertTableRelations.TableRelations.Argument).FindParameter())
                    {
                        SqlParameterName = insertColumnParameter.ParameterPath.SqlParameterName,
                        UpdateNodeFunc = (parameterValue, tokenPath) =>
                        {
                            var orderedParameterLookup = new OrderedParameterValueLookup();
                            OrderParameterValues(orderedParameterLookup, parameterValue,
                                FindRootArgument(insertTableRelations.TableRelations.Argument).FindParameter(), null);

                            var orderedParametersForInsert = orderedParameterLookup.FindOrderedParameters(FindRootArgument(insertTableRelations.TableRelations.Argument));
                            var parentIndexMappings = orderedParametersForInsert.SelectMany((op, i) =>
                            {
                                var parentArguments = insertTableRelations.ForeignTableColumns.Select(fc =>
                                {
                                    var primaryTableKeyIndex = orderedParameterLookup.FindArgumentIndex(op, FindRootArgument(insertTableRelations.TableRelations.Argument), FindRootArgument(fc.PrimaryTableRelations.Argument), fc.Direction);
                                    var foreignColumns = fc.ForeignKey.KeyPairs.Select(kp => kp.ForeignTableColumn).ToList();
                                    return new
                                    {
                                        PrimaryTableIndex = primaryTableKeyIndex, ForeignColumns = foreignColumns, InsertedIndex = i
                                    };
                                }).ToList();

                                return parentArguments;
                            });


                            //var enumerable = tokenPath.Argument.Type.IsCollectionType()
                            //    ? parameterValue as IEnumerable
                            //    : parameterValue.AsEnumerable();
                            var sqlParameters = new Dictionary<string, object>();
                            //var allParameters = enumerable?.Cast<object>();
                            if (orderedParametersForInsert.Any())
                            {
                                mergeValuesParametersList.SetArgs(orderedParametersForInsert.Select((param, i) =>
                                {
                                    return new ValuesList().SetArgs(
                                        insertTableRelations.ColumnParameters.Select(cp =>
                                        {
                                            if (i == 0)
                                                parameterPaths.RemoveAll(p =>
                                                    p.SqlParameterName == cp.ParameterPath.SqlParameterName);
                                            var sqlParameterName = $"{cp.ParameterPath.SqlParameterName}{i}";
                                            var parameterValue = param.Value;
                                            sqlParameters[sqlParameterName] = MethodSqlStatement.GetValueForParameterPath(parameterValue, cp.ParameterPath.Properties.Last().AsEnumerable());
                                            return new NamedParameterIdentifier()
                                            {
                                                Name = sqlParameterName
                                            };
                                        }).Cast<AstNode>().AppendOne(new Literal() {Value = i.ToString()})
                                            .Concat(
                                                parentIndexMappings
                                                    .Where(p => p.InsertedIndex == i)
                                                    .Select(p => 
                                                        new Literal() { Value = p.PrimaryTableIndex.ToString() }).ToList()));
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

                    tokens.Add(tokenPath);
                    statement.Add(merge);

                    if (insertSpec.ReturnType != typeof(void) || index != insertSpec.InsertTableRelationsCollection.Count - 1)
                    {
                        var outputParameterTableName = GetInsertedTableName(insertTableRelations.TableRelations);
                        var declareOutputParameterStatement = new DeclareStatement()
                        {
                            Parameter = new NamedParameterIdentifier() {Name = outputParameterTableName},
                            DataType = new DataType() {Type = new Literal() {Value = "table"}}
                                .SetArgs(
                                    insertSpec.Table.PrimaryKey.Columns.Select(c =>
                                        new ColumnDeclaration().SetArgs(
                                            new RelationalColumn() {Label = c.Name},
                                            new DataType() {Type = new Literal() {Value = c.DataTypeDeclaration}}
                                        )
                                    ).Concat(new ColumnDeclaration().SetArgs(
                                        new RelationalColumn() {Label = mergeIndexColumnName},
                                        new DataType() {Type = new Literal() {Value = "int"}}
                                    ).AsEnumerable())
                                )
                        };
                        statement.Insert(0, declareOutputParameterStatement);

                        var insertedTableName = "inserted";
                        merge.WhenNotMatched.Insert.Output = new OutputClause()
                        {
                            Into = new IntoClause()
                                {Object = new NamedParameterIdentifier() {Name = outputParameterTableName}}.SetArgs(
                                insertSpec.Table.PrimaryKey.Columns.Select(c =>
                                        new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = c.Name}))
                                    .AppendOne(
                                        new ColumnIdentifier().SetArgs(new RelationalColumn()
                                            {Label = mergeIndexColumnName})))
                        }.SetArgs(
                            insertSpec.Table.PrimaryKey.Columns.Select(c =>
                                    new ColumnIdentifier().SetArgs(new RelationalTable() {Label = insertedTableName},
                                        new RelationalColumn() {Label = c.Name}))
                                .AppendOne(
                                    new ColumnIdentifier().SetArgs(
                                        new RelationalTable() {Label = mergeTableAlias},
                                        new RelationalColumn() {Label = mergeIndexColumnName}
                                    ))
                        );

                        if (insertSpec.ReturnType != typeof(void))
                        {
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
                                                new RelationalColumn() { Label = mergeIndexColumnName })
                                        )
                                    )
                                )
                            };

                            statement.Add(selectStatement);
                        }
                        
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
                var currentIndex = this.orderedParameterValues.FindIndex(v => HasMatchingArgument(v.Argument, argument));
                var item = new OrderedParameterValue()
                {
                    Argument = argument,
                    Index = currentIndex + 1,
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

            //foreach (var childrenPropertyValue in childrenPropertyValues)
            //{
            //    var childrenByParent = childrenPropertyValue.GroupBy(g => g.Parent);
            //    OrderParameterValues(dictionary, childrenByParent.Select(a => a.).ToList(), childrenPropertyValue.Select(c => c.Argument).First(), childrenByParent.Select(c => c.Key).First());
            //}


            //return distinctValues.Select((v, i) =>
            //{
            //    var orderedParameterValue = new OrderedParameterValue()
            //    {
            //        Index = i,
            //        Argument = parameter,
            //        Value = v,
            //        Parent = parent
            //    };
            //    orderedParameterValue.Children = parameter.ClassProperties.Select(c => OrderParameterValues(parameterValue, c, orderedParameterValue)).ToList();
            //    return orderedParameterValue;
            //}).ToList();
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
                        HasPendingForeignKey(t.TargetTable, t.ForeignKeyToParent, orderedTableRelations) ||
                        t.NavigationTables.Any(n =>
                            HasPendingForeignKey(t.TargetTable, t.ForeignKeyToParent, orderedTableRelations));
                    if (!hasPendingDependency)
                    {
                        orderedTableRelations.Add(t);
                    }
                });
            }

            return orderedTableRelations.ToList();
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
                        PrimaryTableRelations = tableRelations,
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
            public InsertTableRelations Next { get; set; }
        }
        

        private class InsertColumnParameter
        {
            public IColumnDefinition Column { get; set; }
            public ParameterPath ParameterPath { get; set; }
        }
    }
}
