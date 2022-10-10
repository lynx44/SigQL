using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
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

            var valuesListClause = new ValuesListClause();
            var statement = new List<AstNode>();
            var tablePrimaryKeyDefinitions = new ConcurrentDictionary<string, ITableKeyDefinition>();

            var tokens = new List<TokenPath>();

            for (var index = 0; index < insertSpec.InsertTableRelationsCollection.Count; index++)
            {
                var insertTableRelations = insertSpec.InsertTableRelationsCollection[index];
                var insertColumnList = insertTableRelations.ColumnParameters.Select(cp =>
                    new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = cp.Column.Name})).ToList();

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
                    var lookupParameterTableName = insertTableRelations.LookupTableName;
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
                        fk.ForeignKey.KeyPairs.Select(kp =>
                            new ColumnIdentifier()
                                .SetArgs(new RelationalColumn()
                                {
                                    Label = kp.ForeignTableColumn.Name
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
                                        ))),
                                    FromClause =
                                        new FromClause().SetArgs(
                                            new FromClauseNode().SetArgs(
                                                new TableIdentifier().SetArgs(
                                                    new NamedParameterIdentifier() {Name = lookupParameterTableName})))
                                },
                            As = new TableAliasDefinition() {Alias = mergeTableAlias}
                                .SetArgs(
                                    insertTableRelations.ColumnParameters.Select(cp =>
                                        new ColumnDeclaration().SetArgs(
                                            new RelationalColumn() {Label = cp.Column.Name}
                                        )
                                    ).AppendOne(
                                        new ColumnDeclaration().SetArgs(
                                            new RelationalColumn() {Label = mergeIndexColumnName})
                                    )
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

                    valuesListClause.SetArgs(
                        new ValuesList().SetArgs(
                            insertTableRelations.ColumnParameters.Select(cp =>
                                new ColumnIdentifier().SetArgs(
                                    new RelationalTable() {Label = mergeTableAlias},
                                    new RelationalColumn() {Label = cp.Column.Name}
                                )
                            )
                        )
                    );

                    var tokenPath = new TokenPath(insertColumnParameter.ParameterPath.Argument.FindParameter())
                    {
                        SqlParameterName = insertColumnParameter.ParameterPath.SqlParameterName,
                        UpdateNodeFunc = (parameterValue, tokenPath) =>
                        {
                            var orderedParameterValues = OrderParameterValues(parameterValue,
                                insertColumnParameter.ParameterPath.Argument.FindParameter());

                            var orderedParametersForInsert = FindOrderedParameters(orderedParameterValues, insertTableRelations.TableRelations.Argument);
                            var parentIndexMappings = orderedParametersForInsert.Select(op =>
                            {
                                var parentArguments = insertTableRelations.ForeignTableColumns.Select(fc =>
                                {
                                    var primaryTableKeyIndex = FindArgumentIndex(op, insertTableRelations.TableRelations.Argument, fc.PrimaryTableRelations.Argument, fc.Direction);
                                    var foreignColumns = fc.ForeignKey.KeyPairs.Select(kp => kp.ForeignTableColumn).ToList();
                                    return new
                                    {
                                        PrimaryTableIndex = primaryTableKeyIndex, ForeignColumns = foreignColumns
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
                                            sqlParameters[sqlParameterName] = parameterValue;
                                            return new NamedParameterIdentifier()
                                            {
                                                Name = sqlParameterName
                                            };
                                        }).Cast<AstNode>().AppendOne(new Literal() {Value = i.ToString()})
                                            .Concat(
                                                parentIndexMappings
                                                    .SelectMany(pl => pl)
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
                        var outputParameterTableName = insertTableRelations.InsertedTableName;
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

        private IEnumerable<OrderedParameterValue> OrderParameterValues(object parameterValue, IArgument parameter, OrderedParameterValue parent = null)
        {
            var distinctValues = MethodSqlStatement.GetFlattenedValuesForCollectionParameterPath(parameterValue, parameter.Type,
                parameter.ToParameterPath().Properties).Distinct();
            return distinctValues.Select((v, i) =>
            {
                var orderedParameterValue = new OrderedParameterValue()
                {
                    Index = i,
                    Argument = parameter,
                    Value = v,
                    Parent = parent
                };
                orderedParameterValue.Children = parameter.ClassProperties.Select(c => OrderParameterValues(parameterValue, c, orderedParameterValue)).ToList();
                return orderedParameterValue;
            }).ToList();
        }

        private IEnumerable<OrderedParameterValue> FindOrderedParameters(IEnumerable<OrderedParameterValue> orderedParameterValues, IArgument argument) 
        {
            if (orderedParameterValues.Any() && HasMatchingArgument(orderedParameterValues.First().Argument, argument))
            {
                return orderedParameterValues;
            }


            foreach (var childrenParameterValues in orderedParameterValues.SelectMany(v => v.Children))
            {
                var findOrderedParameters = FindOrderedParameters(childrenParameterValues, argument);
                if (findOrderedParameters.Any())
                {
                    return findOrderedParameters;
                }
            }

            return new OrderedParameterValue[0];
            //return orderedParameterValues.Select(p => 
            //    p.Children.SelectMany(c => 
            //        FindOrderedParameters(c, argument)));
        }

        private static bool HasMatchingArgument(IArgument orderedParameterArgument, IArgument argument)
        {
            return (orderedParameterArgument.EquivalentTo(argument) || (argument is TableArgument && argument.ClassProperties.First().EquivalentTo(orderedParameterArgument)));
        }

        private int? FindArgumentIndex(OrderedParameterValue orderedParameterValue,
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

            return orderedParameterValue.Children.SelectMany(c => c).Select(c =>
                FindArgumentIndex(c, foreignTableArgument, primaryTableArgument, direction)).FirstOrDefault(c => c.HasValue);
        }

        private class OrderedParameterValue
        {
            public IArgument Argument { get; set; }
            public object Value { get; set; }
            public int Index { get; set; }
            public IEnumerable<IEnumerable<OrderedParameterValue>> Children { get; set; }
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
            public string LookupTableName => TableRelations.Alias.Replace("<", "$").Replace(">", "$") + "Lookup";
            public string InsertedTableName => "inserted" + TableRelations.Alias.Replace("<", "$").Replace(">", "$");
            public IEnumerable<ForeignTableColumn> ForeignTableColumns { get; set; }

            public int RowIndexFor(IArgument thisTableColumn)
            {
                throw new NotImplementedException();
            }
            public int ParentRowIndexFor(IArgument thisTableColumn, TableRelations parent)
            {
                throw new NotImplementedException();
            }
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
