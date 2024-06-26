﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using SigQL.Exceptions;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Sql;
using SigQL.Types;
using SigQL.Types.Attributes;

namespace SigQL
{
    public partial class MethodParser
    {
        private readonly SqlStatementBuilder builder;
        private readonly IDatabaseConfiguration databaseConfiguration;
        private DatabaseResolver databaseResolver;

        public MethodParser(SqlStatementBuilder builder, IDatabaseConfiguration databaseConfiguration, IPluralizationHelper pluralizationHelper)
        {
            this.builder = builder;
            this.databaseConfiguration = databaseConfiguration;
            this.databaseResolver = new DatabaseResolver(this.databaseConfiguration, pluralizationHelper);
        }

        public MethodSqlStatement SqlFor(MethodInfo methodInfo)
        {
            var statementType = DetectStatementType(methodInfo);
            if (statementType == StatementType.Insert)
            {
                var spec = GetUpsertSpec(methodInfo);
                return BuildInsertStatement(spec, Enumerable.Select<UpsertColumnParameter, ParameterPath>(spec.UpsertTableRelationsCollection.First().ColumnParameters, cp => cp.ParameterPath).ToList());
            }
            if (statementType == StatementType.UpdateByKey)
            {
                var spec = GetUpsertSpec(methodInfo);
                return BuildUpdateByKeyStatement(spec, Enumerable.Select<UpsertColumnParameter, ParameterPath>(spec.UpsertTableRelationsCollection.First().ColumnParameters, cp => cp.ParameterPath).ToList());
            }
            if (statementType == StatementType.Upsert)
            {
                var spec = GetUpsertSpec(methodInfo);
                return BuildUpsertStatement(spec, Enumerable.Select<UpsertColumnParameter, ParameterPath>(spec.UpsertTableRelationsCollection.First().ColumnParameters, cp => cp.ParameterPath).ToList());
            }
            if (statementType == StatementType.Sync)
            {
                var spec = GetUpsertSpec(methodInfo);
                return BuildSyncStatement(spec, Enumerable.Select<UpsertColumnParameter, ParameterPath>(spec.UpsertTableRelationsCollection.First().ColumnParameters, cp => cp.ParameterPath).ToList());
            }
            if (statementType == StatementType.Delete)
            {
                var deleteSpec = GetDeleteSpec(methodInfo);
                return BuildDeleteStatement(deleteSpec);
            }
            if (statementType == StatementType.Update)
            {
                var updateSpec = GetUpdateSpec(methodInfo);
                return BuildUpdateStatement(updateSpec);
            }
            
            return BuildSelectStatement(methodInfo);
        }

        private MethodSqlStatement BuildSelectStatement(MethodInfo methodInfo)
        {
            Type projectionType;
            var returnType = methodInfo.ReturnType;
            var isCountResult = returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ICountResult<>);
            if (isCountResult)
            {
                returnType = methodInfo.ReturnType.GetGenericArguments().First();
            }
            
            projectionType = OutputFactory.UnwrapType(returnType);


            var methodParameters = methodInfo.GetParameters();
            var arguments = methodParameters.AsArguments(this.databaseResolver);

            var tableDefinition = this.databaseResolver.DetectTable(projectionType);
            var tablePrimaryKeyDefinitions = new ConcurrentDictionary<string, IEnumerable<string>>();

            TableRelations allTableRelations;
            
            TableRelations projectionTableRelations;
            List<TableRelations> parameterRelations;
            {
                projectionTableRelations = this.databaseResolver.BuildTableRelations(tableDefinition, new TypeArgument(projectionType, this.databaseResolver),
                    TableRelationsColumnSource.ReturnType, tablePrimaryKeyDefinitions);
                //var parametersTableRelations = this.databaseResolver.BuildTableRelations(tableDefinition, new TableArgument(tableDefinition, arguments),
                //    TableRelationsColumnSource.Parameters, new ConcurrentDictionary<string, IEnumerable<string>>());
                
                parameterRelations = arguments.Select(arg => this.databaseResolver.BuildTableRelations(tableDefinition, arg,
                    TableRelationsColumnSource.Parameters, new ConcurrentDictionary<string, IEnumerable<string>>())).ToList();
                
                var allTrs = new List<TableRelations>();
                allTrs.Add(projectionTableRelations);
                allTrs.AddRange(parameterRelations);
                allTableRelations = this.databaseResolver.MergeTableRelations(
                    allTrs.ToArray());
                
            }

            List<TableRelations> whereClauseRelations =
                parameterRelations.Select(tr => tr.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.WhereClause)).ToList();
            TableRelations orderbyTableRelations = allTableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.OrderBy);

            var selectClauseBuilder = new SelectClauseBuilder(this.databaseResolver);
            var selectTableRelations = allTableRelations.Mask(TableRelationsColumnSource.ReturnType, ColumnFilters.SelectClause);
            var resolvedSelectClause = selectClauseBuilder.Build(selectTableRelations, tablePrimaryKeyDefinitions);
            
            var selectClause = resolvedSelectClause.Ast;

            var primaryTable = allTableRelations.TargetTable;
            var parameterPaths = new List<ParameterPath>();
            var tokens = new List<TokenPath>();
            
            var allJoinRelations = orderbyTableRelations != null ? this.databaseResolver.MergeTableRelations(selectTableRelations, orderbyTableRelations) : selectTableRelations;
            allJoinRelations.MasterRelations = allTableRelations;

            allJoinRelations.Traverse(tr => parameterPaths.AddRange(tr.FunctionParameters));

            var fromClauseNode = BuildFromClause(allJoinRelations);
            var fromClause = new FromClause().SetArgs(fromClauseNode);

            var statement = new Select()
            {
                SelectClause = selectClause,
                FromClause = fromClause
            };

            var orderBySpecs = new List<OrderBySpec>();
            if (orderbyTableRelations != null)
            {
                //orderbyTableRelations.MasterRelations = null;
                //var tableRelationsWithOrderBy = this.databaseResolver.MergeTableRelations(allTableRelations, orderbyTableRelations);
                var orderByParameters = orderbyTableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.OrderBy);

                orderBySpecs = ConvertToOrderBySpecs(arguments, orderByParameters, orderbyTableRelations).ToList();
                var dynamicOrderByParameterPaths = this.FindDynamicOrderByParameterPaths(arguments);
                var dynamicOrderBySpecs = ConvertToOrderBySpecs(arguments, dynamicOrderByParameterPaths, primaryTable, allTableRelations);

                orderBySpecs.AddRange(dynamicOrderBySpecs);
                orderBySpecs = orderBySpecs.OrderBy(o => o.Ordinal).ToList();
                if (orderBySpecs.Any())
                {
                    statement.OrderByClause = BuildOrderByClause(allTableRelations, orderBySpecs, tokens, null);
                }
            }


            // offset functionality
            var offsetParameter = GetParameterPathWithAttribute<OffsetAttribute>(arguments);
            var fetchParameter = GetParameterPathWithAttribute<FetchAttribute>(arguments);
            var whereClauseTableRelations = allTableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.WhereClause);
            if ((offsetParameter != null || fetchParameter != null))
            {
                if ((allTableRelations.NavigationTables != null && allTableRelations.NavigationTables.Any()))
                {
                    var primaryTableAlias = allTableRelations.Alias;
                    var tableIdentifier = new TableIdentifier().SetArgs(
                        new RelationalTable() { Label = primaryTable.Name });
                    var offsetFromClause =
                        new FromClauseNode();

                    if (primaryTable.Name == primaryTableAlias)
                    {
                        offsetFromClause
                            .SetArgs(tableIdentifier);
                    } else 
                    {
                        offsetFromClause
                            .SetArgs(
                                new Alias() {Label = primaryTableAlias}.SetArgs(
                                    tableIdentifier));
                    }

                    var primaryKey = primaryTable.PrimaryKey;
                    var primaryKeyColumnNodes = primaryKey.Columns.Select(column =>
                        new ColumnIdentifier()
                            .SetArgs(
                                new Alias()
                                {
                                    Label = allTableRelations.Alias
                                },
                                new RelationalColumn()
                                {
                                    Label = column.Name
                                }
                            )
                    ).ToArray();

                    WhereClause offsetWhereClause = null;
                    if (whereClauseTableRelations.ProjectedColumns.Any() || whereClauseTableRelations.NavigationTables.Any())
                    {
                        offsetWhereClause = BuildWhereClauseFromTargetTablePerspective(new Alias() { Label = primaryTableAlias }, whereClauseRelations, parameterPaths, tokens);
                    }

                    TableRelations dynamicTableRelations = null;
                    var orderByResults = new List<OrderByResult>();

                    var offsetSubquery = new Select()
                    {
                        SelectClause = new SelectClause().SetArgs(primaryKeyColumnNodes),
                        FromClause = new FromClause().SetArgs(offsetFromClause),
                        WhereClause = offsetWhereClause
                    };
                    OrderByClause offsetOrderByClause = null;
                    offsetOrderByClause = BuildOrderByWithOffsetClause(statement, allTableRelations, orderBySpecs, tokens, offsetParameter, parameterPaths, fetchParameter,
                        orderByResult =>
                        {
                            var nonManyToOneRelations = orderByResult.TableRelations.NavigationTables
                                .SelectManyRecursive(t => t.NavigationTables)
                                .Where(nt => TableEqualityComparer.Default.Equals(nt.ForeignKeyToParent.PrimaryKeyTable,
                                    nt.Parent.TargetTable));
                            if (nonManyToOneRelations.Any())
                            {
                                throw new InvalidOrderByException(
                                    $"Unable to order by {orderByResult.TableRelations.GetSingularEndpoint().TableName}.{orderByResult.ColumnDefinition.Name} when using OFFSET/FETCH, since it is not a many-to-one or one-to-one relationship with {orderByResult.TableRelations.TableName}. Relationship to table{(nonManyToOneRelations.Count() > 1 ? "s" : "")} {string.Join(", ", nonManyToOneRelations.Select(t => t.TableName))} causes one-to-many or many-to-many cardinality.");
                            }
                            if (dynamicTableRelations == null)
                            {
                                dynamicTableRelations = orderByResult.TableRelations;
                            }
                            else
                            {
                                dynamicTableRelations = databaseResolver.MergeTableRelations(dynamicTableRelations,
                                    orderByResult.TableRelations);
                            }
                            orderByResults.Add(orderByResult);
                            var buildFromClause = BuildFromClause(dynamicTableRelations);
                            offsetFromClause.Args = buildFromClause.Args;
                            
                            var primaryKeyColumnNodes = primaryKey.Columns.Select(column =>
                                new ColumnIdentifier()
                                    .SetArgs(
                                        new Alias()
                                        {
                                            Label = dynamicTableRelations.Alias
                                        },
                                        new RelationalColumn()
                                        {
                                            Label = column.Name
                                        }
                                    )
                            ).ToArray();
                            offsetSubquery.SelectClause = new SelectClause().SetArgs(primaryKeyColumnNodes);

                            offsetSubquery.OrderByClause = new OrderByClause() { Offset = offsetOrderByClause.Offset }.SetArgs(
                                orderByResults.Select(r =>
                                    new OrderByIdentifier() { Direction = r.Direction == OrderByDirection.Ascending ? "asc" : "desc" }.SetArgs(
                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable()
                                            {
                                                Label = dynamicTableRelations.FindEquivalentBranch(r.TableRelations.GetSingularEndpoint()).Alias
                                            },
                                            new RelationalColumn() {Label = r.ColumnDefinition.Name}
                                        )
                                    )
                                )
                            );
                        });

                    offsetSubquery.OrderByClause = offsetOrderByClause;

                    statement.WhereClause = null;

                    var offsetTableAliasName = $"offset_{primaryTable.Name}";
                    var primaryTableArg = fromClauseNode.Args.First();
                    var otherArgs = fromClauseNode.Args.Skip(1).ToList();

                    var newFromClauseNode = new FromClauseNode().SetArgs(new FromClauseNode().SetArgs(new AstNode[]
                        {
                        new Alias() {Label = offsetTableAliasName}.SetArgs(new LogicalGrouping().SetArgs(offsetSubquery)),
                        new InnerJoin()
                        {
                            RightNode = primaryTableArg
                        }.SetArgs(
                            primaryKey.Columns.Select(c =>
                                new AndOperator().SetArgs(
                                    new EqualsOperator().SetArgs(
                                        new ColumnIdentifier().SetArgs(
                                            new Alias() {Label = offsetTableAliasName},
                                            new RelationalColumn() {Label = c.Name}),
                                        new ColumnIdentifier().SetArgs(
                                            new RelationalTable() {Label = allTableRelations.Alias},
                                            new RelationalColumn() {Label = c.Name})))
                            )
                        )
                        }.Concat(otherArgs)
                    ));

                    fromClause.SetArgs(newFromClauseNode);
                }
                else
                {
                    var offsetOrderByClause = BuildOrderByWithOffsetClause(statement, allTableRelations, orderBySpecs, tokens, offsetParameter, parameterPaths, fetchParameter,
                        null);
                    statement.OrderByClause = offsetOrderByClause;
                    if (whereClauseTableRelations.ProjectedColumns.Any() || whereClauseTableRelations.NavigationTables.Any())
                    {
                        statement.WhereClause = BuildWhereClauseFromTargetTablePerspective(
                            new RelationalTable() { Label = allTableRelations.Alias }, whereClauseRelations, parameterPaths,
                            tokens);
                    }
                }
            }
            else if (whereClauseTableRelations.ProjectedColumns.Any() || whereClauseTableRelations.NavigationTables.Any())
            {
                statement.WhereClause = BuildWhereClauseFromTargetTablePerspective(
                    new RelationalTable() {Label = allTableRelations.Alias}, whereClauseRelations, parameterPaths,
                    tokens);
            }
            
            if (isCountResult)
            {
                var countStatement = new Select();
                countStatement.SelectClause = new SelectClause();
                countStatement.SelectClause.SetArgs(
                    new Alias() {Label = "Count"}.SetArgs(new Count().SetArgs(new Literal() {Value = "1"})));
                countStatement.FromClause = new FromClause();
                countStatement.FromClause.SetArgs(new SubqueryAlias() {Alias = "Subquery"}.SetArgs(statement));

                statement = countStatement;
            }

            var sqlStatement = new MethodSqlStatement()
            {
                CommandAst = statement.AsEnumerable(),
                SqlBuilder = this.builder,
                ReturnType = methodInfo.ReturnType,
                UnwrappedReturnType = projectionType,
                Parameters = parameterPaths,
                Tokens = tokens,
                TargetTablePrimaryKey = !isCountResult ? new TableKeyDefinition(allTableRelations.PrimaryKey.ToArray()) : new TableKeyDefinition(),
                TablePrimaryKeyDefinitions = !isCountResult ? tablePrimaryKeyDefinitions : new ConcurrentDictionary<string, IEnumerable<string>>()
            };
            return sqlStatement;
        }

        private IEnumerable<IArgument> FindDynamicOrderByParameterPaths(IEnumerable<IArgument> arguments)
        {
            var orderByArguments = arguments.Filter(a => a.Type.IsAssignableFrom(typeof(OrderBy)) ||
                                                           a.Type.IsAssignableFrom(typeof(IEnumerable<OrderBy>)));
            var matchingArgs = orderByArguments.Select(a =>
            {
                return a;
            }).ToList();

            return matchingArgs;
        }
        
        private static ParameterPath GetParameterPathWithAttribute<TAttribute>(IEnumerable<IArgument> arguments)
            where TAttribute : Attribute
        {
            var parameterPathWithAttribute = arguments.SingleOrDefault(p => p.GetCustomAttribute<TAttribute>() != null);
            if (parameterPathWithAttribute != null)
            {
                return 
                    new ParameterPath(parameterPathWithAttribute);
            }

            return FindClassFilterPropertyWithAttribute<TAttribute>(arguments);
        }

        private static ParameterPath FindClassFilterPropertyWithAttribute<TAttribute>(IEnumerable<IArgument> arguments)
            where TAttribute : Attribute
        {
            var propertiesWithAttribute = arguments.Select(p =>
            {
                var isClass = p.Type.IsClass || p.Type.IsInterface;
                if (isClass)
                {
                    var propsWithAttr = p.ClassProperties.Where(p => p.GetCustomAttribute<TAttribute>() != null).ToList();
                    if (propsWithAttr.Count() >= 2)
                    {
                        throw new InvalidAttributeException(typeof(TAttribute), propsWithAttr.Select(a => a.GetPropertyInfo()),
                            $"Attribute [{typeof(TAttribute).Name}] is specified more than once.");
                    }

                    if (propsWithAttr.Any())
                    {
                        return
                            new ParameterPath(propsWithAttr.First());
                    }
                }

                return null;
            }).Where(p => p != null).ToList();

            if (propertiesWithAttribute.Count >= 2)
            {
                throw new InvalidAttributeException(typeof(TAttribute), propertiesWithAttribute.Select(p => p.Properties.Last()),
                    $"Attribute [{typeof(TAttribute).Name}] is specified more than once.");
            }

            return propertiesWithAttribute.SingleOrDefault();
        }

        private bool IsFunctionParameter(IArgument parameter)
        {
            return parameter.GetCustomAttribute<ParameterAttribute>() != null;
        }

        private OrderByClause BuildOrderByWithOffsetClause(Select statement, TableRelations tableRelations, IEnumerable<OrderBySpec> orderBySpecs,
            List<TokenPath> tokens, ParameterPath offsetParameter, List<ParameterPath> parameterPaths, ParameterPath fetchParameter, Action<OrderByResult> orderByResultAction)
        {
            OrderByClause offsetOrderByClause;
            if (statement.OrderByClause != null)
            {
                offsetOrderByClause = BuildOrderByClause(tableRelations, orderBySpecs, tokens, orderByResultAction);
            }
            else
            {
                var defaultSelect = new Select();
                defaultSelect.SelectClause = new SelectClause().SetArgs(new Literal() {Value = "1"});
                offsetOrderByClause =
                    new OrderByClause().SetArgs(
                        new OrderByIdentifier().SetArgs(new LogicalGrouping().SetArgs(defaultSelect)));
            }

            var offsetClause = new OffsetClause();
            if (offsetParameter != null)
            {
                var offsetSqlParameterName = offsetParameter.GenerateSuggestedSqlIdentifierName();
                offsetParameter.SqlParameterName = offsetSqlParameterName;
                parameterPaths.Add(offsetParameter);
                offsetClause.OffsetCount = new NamedParameterIdentifier() {Name = offsetSqlParameterName};
            }
            else
            {
                offsetClause.OffsetCount = new Literal() {Value = "0"};
            }

            if (fetchParameter != null)
            {
                var fetchSqlParameterName = fetchParameter.GenerateSuggestedSqlIdentifierName();
                fetchParameter.SqlParameterName = fetchSqlParameterName;
                parameterPaths.Add(fetchParameter);
                offsetClause.Fetch = new FetchClause()
                    {FetchCount = new NamedParameterIdentifier() {Name = fetchSqlParameterName}};
            }

            offsetOrderByClause.Offset = offsetClause;
            return offsetOrderByClause;
        }

        private static bool IsOrderByDirectionParameter(IArgument arg)
        {
            return arg.Type == typeof(OrderByDirection);
        }

        private IEnumerable<OrderBySpec> ConvertToOrderBySpecs(IEnumerable<IArgument> arguments, TableRelations orderByTableRelations, TableRelations primaryTableRelations)
        {
            var columns = this.databaseResolver.GetProjectedColumns(orderByTableRelations);
            return columns.SelectMany(c => ConvertToOrderBySpec(c, arguments, primaryTableRelations)).ToList();
        }
        
        private IEnumerable<OrderBySpec> ConvertToOrderBySpecs(IEnumerable<IArgument> arguments, IEnumerable<IArgument> dynamicOrderByParameterPaths, ITableDefinition primaryTable, TableRelations primaryTableRelations)
        {
            Func<string, string> resolveTableAlias = (tableName) => primaryTableRelations.Find(tableName).Alias;

            return dynamicOrderByParameterPaths.Select(argument =>
            {
                return new OrderBySpec(resolveTableAlias, primaryTableRelations.FindByTablePaths)
                {
                    ParameterPath = argument.ToParameterPath(),
                    IsDynamic = true,
                    IsCollection = argument.ToParameterPath().GetEndpointType().IsCollectionType(),
                    Ordinal = arguments.GetOrdinal(argument)
                };
            }).ToList();
        }

        private IEnumerable<OrderBySpec> ConvertToOrderBySpec(TableRelationColumnIdentifierDefinition columnRelation, IEnumerable<IArgument> arguments,
            TableRelations fromTableRelations)
        {
            Func<string, string> resolveTableAlias = (tableName) => fromTableRelations.Find(tableName).Alias;

            return columnRelation.Arguments.All.Select(arg =>
            {
                var ordinal = arguments.GetOrdinal(arg);
                if (IsOrderByDirectionParameter(arg))
                {
                    string columnName;
                    string tableName;
                    string tableAliasName;
                    columnName = this.databaseResolver.GetColumnName(arg);

                    var viaRelationAttribute = arg.GetCustomAttribute<ViaRelationAttribute>();
                    if (viaRelationAttribute != null)
                    {
                        var parameterArgument = new ParameterArgument(arg.FindParameter().GetParameterInfo(), this.databaseResolver);
                        var viaRelationPath = viaRelationAttribute.Path;
                        var viaRelationColumn = viaRelationAttribute.Column;
                        var result = ResolveTableAliasNameForViaRelationOrderBy(parameterArgument, viaRelationPath,
                            viaRelationColumn, fromTableRelations.FindByTablePaths);
                        var orderByTableRelations = result.GetSingularEndpoint();
                        columnName = orderByTableRelations.ProjectedColumns.Single().Name;
                        tableName = orderByTableRelations.TableName;
                        tableAliasName = orderByTableRelations.Alias;
                    }
                    else
                    {
                        tableName = columnRelation.Table.Name;
                        var columnTableRelations = fromTableRelations.FindEquivalentBranch(columnRelation.TableRelations);
                        tableAliasName = columnTableRelations.Alias;
                    }

                    var orderByTable = this.databaseConfiguration.Tables.FindByName(tableName);
                    var column = orderByTable.Columns.FindByName(columnName);

                    if (column == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for order by parameter \"{arg.FullyQualifiedTypeName()}\". Column \"{columnName}\" does not exist in table {tableName}.");
                    }

                    return new OrderBySpec(resolveTableAlias, fromTableRelations.FindByTablePaths)
                    {
                        TableName = tableAliasName ?? tableName ?? fromTableRelations.Alias,
                        ColumnName = column.Name,
                        ParameterPath = arg.ToParameterPath(),
                        IsDynamic = false,
                        IsCollection = false,
                        Ordinal = ordinal
                    };
                }
                else
                {
                    return new OrderBySpec(resolveTableAlias, fromTableRelations.FindByTablePaths)
                    {
                        ParameterPath = arg.ToParameterPath(),
                        IsDynamic = true,
                        IsCollection = arg.Type.IsCollectionType(),
                        Ordinal = ordinal
                    };

                }
            }).ToList();


        }

        private StatementType DetectStatementType(MethodInfo methodInfo)
        {
            if (IsInsertMethod(methodInfo))
            {
                return StatementType.Insert;
            }
            if (IsDeleteMethod(methodInfo))
            {
                return StatementType.Delete;
            }
            if (IsUpdateMethod(methodInfo))
            {
                return StatementType.Update;
            }
            if (IsUpdateByKeyMethod(methodInfo))
            {
                return StatementType.UpdateByKey;
            }
            if (IsUpsertMethod(methodInfo))
            {
                return StatementType.Upsert;
            }
            if (IsSyncMethod(methodInfo))
            {
                return StatementType.Sync;
            }

            return StatementType.Select;
        }
        private bool IsDeleteMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(DeleteAttribute), false)?.Any()).GetValueOrDefault(false);
        }

        private bool IsUpdateMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(UpdateAttribute), false)?.Any()).GetValueOrDefault(false);
        }

        private bool IsUpdateByKeyMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(UpdateByKeyAttribute), false)?.Any()).GetValueOrDefault(false);
        }
        private bool IsUpsertMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(UpsertAttribute), false)?.Any()).GetValueOrDefault(false);
        }
        private bool IsSyncMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(SyncAttribute), false)?.Any()).GetValueOrDefault(false);
        }

        private WhereClause BuildWhereClauseFromTargetTablePerspective(AstNode primaryTableReference, IEnumerable<TableRelations> whereClauseTableRelations, List<ParameterPath> parameterPaths, List<TokenPath> tokens)
        {
            var allTableRelationsGroups = 
                whereClauseTableRelations
                    .Where(tr => tr.ProjectedColumns.Any() || tr.NavigationTables.Any())
                    .GroupBy(g => g.Argument.GetCustomAttribute<OrGroupAttribute>()?.Group).ToList();

            AstNode whereClauseConditionals = new AndOperator();
            
            var conditionals = new List<AstNode>();
            whereClauseConditionals.Args = conditionals;
            foreach (var tableRelationsGroup in allTableRelationsGroups)
            {
                AstNode tableRelationsConditional = new AndOperator();
                if (!string.IsNullOrEmpty(tableRelationsGroup.Key))
                {
                    tableRelationsConditional = new OrOperator();
                }
                conditionals.Add(tableRelationsConditional);
                
                foreach (var tableRelations in tableRelationsGroup)
                {
                    var projectedColumns =
                        tableRelations.ProjectedColumns
                        .ToList();

                    var conditionalLookup = new Dictionary<string, AstNode>();
                    //private AstNode GenerateColumnAst(projectedColumns)...
                    //{
                    var columnGroups = projectedColumns
                        .GroupBy(c =>
                            c.Arguments.GetArguments(TableRelationsColumnSource.Parameters).Single()
                                .GetCustomAttribute<OrGroupAttribute>()?.Group)
                        .ToList();
                    foreach (var columnGroup in columnGroups)
                    {

                        var columnConditional = AppendGetConditionalOperand(conditionalLookup, columnGroup.Key, tableRelationsConditional);

                        var parentArgumentCount = new Dictionary<IArgument, int>();
                        // columns
                        columnConditional.SetArgs(
                            columnGroup.Select(columnIdentifier =>
                            {
                                var argument = columnIdentifier.Arguments.GetArguments(TableRelationsColumnSource.Parameters).Single()
                                    .GetEndpoint();
                                var parameterName = argument.Name;

                                if (!(argument.Parent?.Type.IsCollectionType()).GetValueOrDefault(false))
                                {
                                    var comparisonNode = BuildComparisonNode(new ColumnIdentifier()
                                        .SetArgs(primaryTableReference,
                                            new RelationalColumn()
                                            {
                                                Label = columnIdentifier.Name
                                            }), parameterName, argument, parameterPaths, tokens);
                                    return (AstNode)comparisonNode;
                                }
                                else
                                {
                                    if (argument.Parent.ClassProperties?.Count() > 1)
                                    {
                                        if (parentArgumentCount.ContainsKey(argument.Parent))
                                        {
                                            parentArgumentCount[argument.Parent] += 1;
                                            return null;
                                        }
                                        else
                                        {
                                            parentArgumentCount[argument.Parent] = 1;
                                        }
                                    }
                                    
                                    var placeholder = new Placeholder();
                                    var token = new TokenPath(argument.Parent)
                                    {
                                        UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                                        {
                                            var collection = parameterValue.AsEnumerable();

                                            var newParameters = new Dictionary<string, object>();
                                            placeholder.SetArgs(new OrOperator().SetArgs(
                                                collection.Select((c, i) =>
                                                {
                                                    return new AndOperator().SetArgs(
                                                        argument.Parent.ClassProperties.SelectMany(arg =>
                                                        {
                                                            var argument = arg.GetEndpoint();
                                                            var parameterName = argument.Name + i;
                                                            var dbColumn = GetColumnForParameterName(
                                                                columnIdentifier.Table,
                                                                argument.Name);

                                                            var dbColumnName = dbColumn.Name;
                                                            var propertyValue =
                                                                MethodSqlStatement.GetValueForParameterPath(c,
                                                                    arg.GetPropertyInfo().AsEnumerable());
                                                            newParameters[parameterName] = propertyValue;
                                                            var comparisonNode = BuildComparisonNode(
                                                                new ColumnIdentifier()
                                                                    .SetArgs(primaryTableReference,
                                                                        new RelationalColumn()
                                                                        {
                                                                            Label = dbColumnName
                                                                        }), parameterName, argument,
                                                                new List<ParameterPath>(), tokens);
                                                            return comparisonNode.AsEnumerable().ToList();
                                                        }));
                                                })
                                            ));

                                            return newParameters;
                                        }
                                    };
                                    tokens.Add(token);
                                    return ((AstNode)placeholder);
                                }
                            }).Where(n => n != null).ToList()
                        );
                    }

                    var navigationTablesGroup = tableRelations.NavigationTables
                        .GroupBy(tr => tr.Argument?.GetCustomAttribute<OrGroupAttribute>()?.Group).ToList();

                    foreach (var navigationTables in navigationTablesGroup)
                    {
                        var columnConditional = AppendGetConditionalOperand(conditionalLookup, navigationTables.Key, tableRelationsConditional);
                        columnConditional.AppendArgs(
                            navigationTables.Select(nt =>
                                BuildWhereClauseForPerspective(primaryTableReference, nt, "0", parameterPaths,
                                    tokens)).ToList()
                        );
                    }
                }


                

                //var tableNodes = tableRelationsGroup.SelectMany(nt =>
                //    nt.NavigationTables.Select(ntc => BuildWhereClauseForPerspective(primaryTableReference, ntc, "0", parameterPaths,
                //        tokens))).ToList();

                //tableRelationsConditional.AppendArgs(tableNodes);
                //}
                //}

            }

            var whereClause = new WhereClause().SetArgs(whereClauseConditionals);
            return whereClauseConditionals.Args.Any() ? whereClause : null;
        }
        
        private AstNode BuildWhereClauseForPerspective(
            AstNode primaryTableReference, TableRelations navigationTableRelations, string navigationTableAliasPostfix,
            List<ParameterPath> parameterPaths, List<TokenPath> tokens)
        {
            var navigationTableAlias = $"{navigationTableRelations.TargetTable.Name}{navigationTableAliasPostfix}";

            var selectStatement = new Select();
            selectStatement.SelectClause =
                new SelectClause().SetArgs(
                    new Literal() { Value = "1" });
            selectStatement.FromClause = new FromClause();
            selectStatement.FromClause.SetArgs(
                new Alias() { Label = navigationTableAlias }.SetArgs(
                    new TableIdentifier().SetArgs(
                        new RelationalTable() { Label = navigationTableRelations.TargetTable.Name })));

            
            var columnGroups = navigationTableRelations.ProjectedColumns
                .GroupBy(c =>
                    c.Arguments.GetArguments(TableRelationsColumnSource.Parameters).Single()
                        .GetCustomAttribute<OrGroupAttribute>()?.Group)
                .ToList();
            AstNode columnOperator = new AndOperator();

            var conditionalLookup = new Dictionary<string, AstNode>();
            var tableRelationsGroups = navigationTableRelations.NavigationTables
                .GroupBy(g => !g.IsManyToMany ? g.Argument.GetCustomAttribute<OrGroupAttribute>()?.Group : g.NavigationTables.First().Argument.GetCustomAttribute<OrGroupAttribute>()?.Group).ToList();
            foreach (var columnGroup in columnGroups)
            {
                var columnConditional = AppendGetConditionalOperand(conditionalLookup, columnGroup.Key, columnOperator);

                columnConditional.SetArgs(
                    columnGroup.SelectMany(c =>
                     {
                         var sqlParameterName = $"{navigationTableAlias}{c.Name}";
                         var parameterArguments = c.Arguments.GetArguments(TableRelationsColumnSource.Parameters).ToList();

                         var astNodes = parameterArguments.Select(arg =>
                             BuildComparisonNode(
                                 new ColumnIdentifier().SetArgs(new Alias() { Label = navigationTableAlias },
                                     new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = c.Name })),
                                 sqlParameterName, arg, parameterPaths, tokens)).ToList();

                         var parameterTokens = tokens.Where(t => parameterArguments.Contains(t.Argument)).ToList();
                         var notNullTokenCountForTable = parameterTokens.Count;
                         parameterTokens.ForEach(t =>
                         {
                             var existingFunc = t.UpdateNodeFunc;
                             var comparisonSpec = this.databaseResolver.GetColumnSpec(t.Argument);
                             if (comparisonSpec.IgnoreIfNull ||
                                 comparisonSpec.IgnoreIfNullOrEmpty)
                             {
                                 t.UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                                 {
                                     var argumentTree = new ArgumentTree(this.databaseResolver, allParameterArgs);
                                     var tableRelationParameterArguments = c.TableRelations.ProjectedColumns.SelectMany(c => c.Arguments.GetArguments(TableRelationsColumnSource.Parameters)).Concat(c.TableRelations.NavigationTables.SelectManyRecursive(t => t.NavigationTables).SelectMany(t => t.ProjectedColumns.SelectMany(c => c.Arguments.GetArguments(TableRelationsColumnSource.Parameters)))).ToList();
                                     var childTreeNodes = argumentTree.FindTreeNodes(tableRelationParameterArguments);
                                     if (((parameterValue == null && (comparisonSpec.IgnoreIfNull || comparisonSpec.IgnoreIfNullOrEmpty)) ||
                                         (parameterValue.IsEmpty() && comparisonSpec.IgnoreIfNullOrEmpty)) &&
                                         childTreeNodes.All(a => (a.Value == null && (a.Argument.GetCustomAttribute<IgnoreIfNullAttribute>() != null || a.Argument.GetCustomAttribute<IgnoreIfNullOrEmptyAttribute>() != null)) ||
                                                                 (a.Value.IsEmpty() && (a.Argument.GetCustomAttribute<IgnoreIfNullOrEmptyAttribute>() != null))))
                                     {
                                         notNullTokenCountForTable--;
                                         if (notNullTokenCountForTable == 0)
                                         {
                                             selectStatement.FromClause = null;
                                             selectStatement.WhereClause = null;
                                         }

                                         return new Dictionary<string, object>();
                                     }
                                     else
                                     {
                                         return existingFunc(parameterValue, parameterArg, allParameterArgs);
                                     }
                                 };
                             }

                         });
                         return
                             astNodes;

                     }).ToList()
                );

                var matchingTrGroup = tableRelationsGroups.FirstOrDefault(trg => trg.Key == columnGroup.Key);
                if (matchingTrGroup != null)
                {
                    columnConditional.AppendArgs(
                        matchingTrGroup.Select((nt, i) =>
                        {
                            return BuildWhereClauseForPerspective(new Alias() { Label = navigationTableAlias }, nt,
                                $"{navigationTableAliasPostfix}{i}", parameterPaths, tokens);
                        }).ToList());

                    tableRelationsGroups.Remove(matchingTrGroup);
                }
            }

            foreach (var tableRelationsGroup in tableRelationsGroups)
            {
                var columnConditional = AppendGetConditionalOperand(conditionalLookup, tableRelationsGroup.Key, columnOperator);
                
                if (tableRelationsGroup.Any())
                {
                    columnConditional.AppendArgs(
                        tableRelationsGroup.Select((nt, i) =>
                        {
                            return BuildWhereClauseForPerspective(new Alias() { Label = navigationTableAlias }, nt,
                                $"{navigationTableAliasPostfix}{i}", parameterPaths, tokens);
                        }).ToList());
                }
            }

            var whereClauseStatement = navigationTableRelations.ForeignKeyToParent.KeyPairs.Select(kp =>
                {
                    IColumnDefinition primaryTableColumn = null;
                    IColumnDefinition navigationTableColumn = null;
                    if (TableEqualityComparer.Default.Equals(kp.ForeignTableColumn.Table,
                            navigationTableRelations.TargetTable))
                    {
                        primaryTableColumn = kp.PrimaryTableColumn;
                        navigationTableColumn = kp.ForeignTableColumn;
                    }
                    else
                    {
                        primaryTableColumn = kp.ForeignTableColumn;
                        navigationTableColumn = kp.PrimaryTableColumn;
                    }
                    return new EqualsOperator().SetArgs(
                        new TableIdentifier().SetArgs(new Alias() { Label = navigationTableAlias }, new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = navigationTableColumn.Name })),
                        new TableIdentifier().SetArgs(primaryTableReference, new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = primaryTableColumn.Name })));
                }).Cast<AstNode>().ToList();
            if (columnOperator.Args != null)
            {
                whereClauseStatement = whereClauseStatement.AppendOne(columnOperator).ToList();
            }
            selectStatement.WhereClause = new WhereClause().SetArgs(
                new AndOperator().SetArgs(

                        // match foreign keys to parent
                        whereClauseStatement
                    ));


            return new AndOperator().SetArgs(
                new LogicalGrouping().SetArgs(new Exists().SetArgs(selectStatement)));
        }

        private AstNode AppendGetConditionalOperand(IDictionary<string, AstNode> conditionalLookup, string groupName, AstNode outerNode)
        {
            if (!conditionalLookup.TryGetValue(groupName ?? "", out var columnConditional))
            {
                columnConditional = new AndOperator();
                if (!string.IsNullOrEmpty(groupName))
                {
                    columnConditional = new OrOperator();
                }

                conditionalLookup[groupName ?? ""] = columnConditional;
                outerNode.AppendArgs(columnConditional);
            }

            return columnConditional;
        }
        
        private AstNode BuildComparisonNode(AstNode columnNode,
            string parameterName, IArgument argument, List<ParameterPath> parameterPaths, List<TokenPath> tokens)
        {
            AstNode operatorNode = null;
            var comparisonSpec = this.databaseResolver.GetColumnSpec(argument);
            var parameterType = comparisonSpec.ComparisonType;

            var placeholder = new Placeholder();
            if (typeof(Like).IsAssignableFrom(parameterType) || 
                comparisonSpec.IsAnyLike)
            {
                operatorNode = 
                   comparisonSpec.Not ? new NotLikePredicate() : 
                       new LikePredicate();
                var token = new TokenPath(argument)
                {
                    SqlParameterName = parameterName,
                    UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                    {

                        var additionalParameters = new Dictionary<string, object>();
                        if (parameterValue == null)
                        {
                            if (comparisonSpec.Not)
                            {
                                placeholder.SetArgs(new IsNotOperator().SetArgs(
                                    columnNode,
                                    new NullLiteral()));
                            }
                            else
                            {
                                placeholder.SetArgs(new IsOperator().SetArgs(
                                    columnNode,
                                    new NullLiteral()));
                            }
                        }
                        else if (comparisonSpec.IsAnyLike && parameterValue is string stringValue)
                        {
                            object likeValue = stringValue;
                            if (comparisonSpec.StartsWith)
                            {
                                likeValue = new StartsWith(stringValue).SqlValue;
                            }
                            else if (comparisonSpec.Contains)
                            {
                                likeValue = new Contains(stringValue).SqlValue;
                            }
                            else if (comparisonSpec.EndsWith)
                            {
                                likeValue = new EndsWith(stringValue).SqlValue;
                            }

                            additionalParameters[parameterArg.SqlParameterName] = likeValue;
                        }

                        return additionalParameters;
                    }
                };
                tokens.Add(token);
            }
            else if (comparisonSpec.GreaterThan)
            {
                operatorNode = new GreaterThanOperator();
            }
            else if (comparisonSpec.GreaterThanOrEqual)
            {
                operatorNode = new GreaterThanOrEqualToOperator();
            }
            else if (comparisonSpec.LessThan)
            {
                operatorNode = new LessThanOperator();
            }
            else if (comparisonSpec.LessThanOrEqual)
            {
                operatorNode = new LessThanOrEqualToOperator();
            }
            else if (parameterType.IsCollectionType())
            {

                var inPredicate =
                    (comparisonSpec.Not ? (InPredicate) new NotInPredicate() : new InPredicate());
                inPredicate.LeftComparison = columnNode;
                placeholder.SetArgs(inPredicate);
                var token = new TokenPath(argument)
                {
                    SqlParameterName = parameterName,
                    UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                    {
                        var enumerable = parameterValue as IEnumerable;
                        var sqlParameters = new Dictionary<string, object>();
                        var allItems = enumerable?.Cast<object>();
                        if (allItems != null && allItems.Any())
                        {
                            var itemsList = allItems.ToList();
                            var hasNullItem = allItems.Any(i => i == null);
                            if (hasNullItem)
                            {
                                itemsList.Remove(null);
                            }

                            if (itemsList.Any())
                            {
                                var parameterNames = itemsList.Select((pv, i) =>
                                {
                                    var pname = parameterName + i;
                                    sqlParameters[pname] = pv;

                                    return pname;
                                }).ToList();
                                inPredicate.SetArgs(parameterNames.Select(pn => new NamedParameterIdentifier() { Name = pn }));
                            }
                            
                            if (hasNullItem)
                            {
                                // if a null item is passed, also check if the column is null
                                AstNode isNullOperator = (comparisonSpec.Not ? 
                                    (AstNode) new IsNotOperator() :  
                                    new IsOperator()).SetArgs(
                                    columnNode,
                                    new NullLiteral());
                                // column in (@param1, @param2) or column is null
                                if (itemsList.Any())
                                {
                                    placeholder.SetArgs(
                                        (comparisonSpec.Not ? 
                                           (AstNode) new AndOperator() :
                                            new OrOperator()).SetArgs(
                                            inPredicate,
                                            isNullOperator));
                                }
                                // column is null
                                else
                                {
                                    placeholder.SetArgs(
                                            isNullOperator);
                                }
                            }
                        }
                        else
                        {
                            if ((allItems == null && (comparisonSpec.IgnoreIfNull || comparisonSpec.IgnoreIfNullOrEmpty)) ||
                                (!(allItems?.Any()).GetValueOrDefault(false) && comparisonSpec.IgnoreIfNullOrEmpty))
                            {
                                placeholder.SetArgs(
                                    new EqualsOperator().SetArgs(
                                        new Literal() {Value = "1"},
                                        new Literal() {Value = "1"}));
                            }
                            else
                            {
                                // set the in clause to an empty set:
                                //   where colName in (select null where 0=1)
                                //
                                // this will cause no results to return
                                var emptySelect = new Select();
                                emptySelect.SelectClause = new SelectClause().SetArgs(new Literal() { Value = "null" });
                                emptySelect.WhereClause = new WhereClause().SetArgs(
                                    new EqualsOperator().SetArgs(
                                        new Literal() { Value = "0"},
                                        new Literal() { Value = "1" }
                                    )
                                );
                                inPredicate.SetArgs(emptySelect);
                            }
                            
                        }

                        return sqlParameters;
                    }
                };
                tokens.Add(token);
                return placeholder;
            }
            else
            {
                if (comparisonSpec.Not)
                {
                    operatorNode = new NotEqualOperator();
                }
                else
                {
                    operatorNode = new EqualsOperator();
                }

                var token = new TokenPath(argument)
                {
                    SqlParameterName = parameterName,
                    UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                    {

                        var additionalParameters = new Dictionary<string, object>();
                        if (parameterValue == null)
                        {
                            if (comparisonSpec.Not)
                            {
                                placeholder.SetArgs(new IsNotOperator().SetArgs(
                                    columnNode,
                                    new NullLiteral()));
                            } else 
                            {
                                placeholder.SetArgs(new IsOperator().SetArgs(
                                    columnNode,
                                    new NullLiteral()));
                            }
                        }

                        return additionalParameters;
                    }
                };
                tokens.Add(token);
            }

            parameterPaths.Add(new ParameterPath(argument) { SqlParameterName = parameterName });
            
            var logicalOperatorNode = operatorNode.SetArgs(
                columnNode, 
                new NamedParameterIdentifier()
                {
                    Name = parameterName
                });

            placeholder.SetArgs(logicalOperatorNode);

            if (comparisonSpec.IgnoreIfNull || comparisonSpec.IgnoreIfNullOrEmpty)
            {
                var token = new TokenPath(argument)
                {
                    SqlParameterName = parameterName,
                    UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                    {
                        if ((parameterValue == null && (comparisonSpec.IgnoreIfNull || comparisonSpec.IgnoreIfNullOrEmpty)) ||
                            (parameterValue is string && ((string) parameterValue == "") && comparisonSpec.IgnoreIfNullOrEmpty))
                        {
                            placeholder.SetArgs(
                                new EqualsOperator().SetArgs(
                                    new Literal() {Value = "1"},
                                    new Literal() {Value = "1"}));
                        }

                        return new Dictionary<string, object>();
                    }
                };
                tokens.Add(token);
                return placeholder;
            }

            return placeholder;
        }

        private class OrderBySpec
        {
            private readonly Func<string, string> resolveTableNameFunc;
            private readonly Func<IEnumerable<string>, TableRelations> resolveTableViaRelationPathFunc;

            public OrderBySpec(Func<string, string> resolveTableNameFunc,
                Func<IEnumerable<string>, TableRelations> resolveTableViaRelationPathFunc)
            {
                this.resolveTableNameFunc = resolveTableNameFunc;
                this.resolveTableViaRelationPathFunc = resolveTableViaRelationPathFunc;
            }

            public string TableName { get; set; }
            public string ColumnName { get; set; }
            public bool IsDynamic { get; set; }
            public bool IsCollection { get; set; }
            public ParameterPath ParameterPath { get; set; }
            public int Ordinal { get; set; }

            public string ResolveTableAlias(string tableName)
            {
                return resolveTableNameFunc(tableName);
            }

            public TableRelations ResolveTableRelations(IEnumerable<string> tableRelationPath)
            {
                return resolveTableViaRelationPathFunc(tableRelationPath);
            }
        }

        private OrderByClause BuildOrderByClause(TableRelations tableRelations, IEnumerable<OrderBySpec> parameters, List<TokenPath> tokens, Action<OrderByResult> orderByResultAction)
        {
            var orderByClause = new OrderByClause();
            orderByClause.SetArgs(
                parameters.SelectMany(p =>
                {
                    if (!p.IsDynamic)
                    {
                        var tokenName = $"{p.ParameterPath.GenerateSuggestedSqlIdentifierName()}_OrderByDirection";

                        var orderByNode = new OrderByIdentifier() { Direction = $"{{{tokenName}}}" };
                        var orderByIdentifier = orderByNode.SetArgs(
                            new ColumnIdentifier().SetArgs(
                                (AstNode)new RelationalTable() { Label = p.TableName },
                                new RelationalColumn() { Label = p.ColumnName }
                            )
                        );
                        var pathToRelations = tableRelations.FindByAlias(p.TableName) ?? tableRelations.Find(p.TableName);
                        var orderByRelations = tableRelations.PickBranch(pathToRelations);
                        var orderByResult = new OrderByResult()
                        {
                            OrderByIdentifier = orderByIdentifier,
                            TableRelations = orderByRelations,
                            ColumnDefinition = pathToRelations.TargetTable.Columns.FindByName(p.ColumnName)
                        };

                        tokens.Add(new TokenPath(p.ParameterPath.Argument)
                        {
                            UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                            {
                                var directionString = "asc";
                                if (parameterValue is OrderByDirection direction)
                                {
                                    directionString = direction == OrderByDirection.Ascending ? "asc" : "desc";
                                    orderByResult.Direction = direction;
                                    orderByResultAction?.Invoke(orderByResult);
                                }

                                orderByNode.Direction = directionString;
                                
                                return new Dictionary<string, object>();
                            }
                        });
                        return orderByIdentifier.AsEnumerable();
                    }
                    else
                    {
                        return BuildDynamicOrderByIdentifier(tableRelations, orderByClause, tokens, p, orderByResultAction).AsEnumerable();
                    }
                    
                }).ToList()
            );
            return orderByClause;
        }

        private IEnumerable<OrderByIdentifier> BuildDynamicOrderByIdentifier(TableRelations tableRelations,
            OrderByClause orderByClause,
            List<TokenPath> tokens, OrderBySpec p, Action<OrderByResult> orderByResultAction)
        {
            var tokenName = $"{p.ParameterPath.GenerateSuggestedSqlIdentifierName()}_OrderBy";

            List<OrderByIdentifier> orderByClauses = new List<OrderByIdentifier>();
            tokens.Add(new TokenPath(p.ParameterPath.Argument)
            {
                UpdateNodeFunc = (parameterValue, parameterArg, allParameterArgs) =>
                {
                    var orderByResultCollection = ResolveOrderBySpec(tableRelations, orderByClause, p, parameterValue, tokenName);
                    foreach (var orderByResult in orderByResultCollection.OrderByResults)
                    {
                        orderByResultAction?.Invoke(orderByResult);
                    }
                    return orderByResultCollection.AdditionalParameters;
                }
            });
            return new List<OrderByIdentifier>();
        }

        private OrderByResultCollection ResolveOrderBySpec(TableRelations primaryTableRelations, OrderByClause orderByClause,
            OrderBySpec p,
            object parameterValue, string tokenName)
        {
            var orderBys = p.IsCollection
                ? parameterValue as IEnumerable<IOrderBy>
                : (parameterValue as IOrderBy).AsEnumerable();

            if (orderBys == null || !orderBys.Any())
            {
                orderByClause.Args = null;
                orderBys = new List<IOrderBy>();
            }

            var orderByIdentifiers = orderBys.Select(orderBy =>
            {
                string tableName;
                var orderByResult = new OrderByResult();
                var orderByRelation = orderBy as OrderByRelation;
                if (orderByRelation != null)
                {
                    var viaRelationPath = orderByRelation.ViaRelationPath;
                    var viaRelationColumn = orderByRelation.ViaRelationColumnName;
                    var parameterArgument = new ParameterArgument(p.ParameterPath.Parameter, this.databaseResolver);

                    var tableRelations = ResolveTableAliasNameForViaRelationOrderBy(parameterArgument, viaRelationPath,
                        viaRelationColumn, p.ResolveTableRelations);

                    var endpointTableRelations = tableRelations.GetSingularEndpoint();
                    var mergedEndpointTableRelations = primaryTableRelations.FindEquivalentBranch(endpointTableRelations);
                    orderByRelation.Table = mergedEndpointTableRelations.Alias;
                    var orderByColumn = endpointTableRelations.ProjectedColumns.Single();
                    orderByRelation.Column = orderByColumn.Name;
                    tableName = mergedEndpointTableRelations.Alias;

                    orderByResult.TableRelations = tableRelations;
                    orderByResult.ColumnDefinition = orderByColumn;
                }
                else
                {
                    var tableIdentifier = this.databaseConfiguration.Tables.FindByName(orderBy.Table);
                    if (tableIdentifier == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database table for order by parameter {p.ParameterPath.GenerateClassQualifiedName()} with specified table name \"{orderBy.Table}\". Table {orderBy.Table} could not be found.");
                    }

                    if (tableIdentifier.Columns.FindByName(orderBy.Column) == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for order by parameter {p.ParameterPath.GenerateClassQualifiedName()} with specified column name \"{orderBy.Column}\". Column {orderBy.Column} does not exist in table {tableIdentifier.Name}.");
                    }

                    var tableRelations = primaryTableRelations.Find(orderBy.Table);
                    
                    var relations = primaryTableRelations.PickBranch(tableRelations);
                    tableName = p.ResolveTableAlias(tableIdentifier.Name);
                    orderByResult.TableRelations = relations;
                    orderByResult.ColumnDefinition = tableRelations.TargetTable.Columns.FindByName(orderBy.Column);
                }


                var orderByNode = new OrderByIdentifier()
                {
                    Direction = $"{{{tokenName}}}"
                };
                var directionString = orderBy.Direction == OrderByDirection.Ascending ? "asc" : "desc";
                orderByNode.Direction = directionString;

                var orderByIdentifier = orderByNode.SetArgs(
                    new ColumnIdentifier().SetArgs(
                        (AstNode) new RelationalTable() {Label = tableName},
                        new RelationalColumn() {Label = orderBy.Column}
                    ));
                orderByResult.OrderByIdentifier = orderByIdentifier;
                orderByResult.Direction = orderBy.Direction;
                return orderByResult;
            }).ToList();

            orderByClause.Args = (orderByClause.Args ?? new List<AstNode>()).Concat(orderByIdentifiers.Select(i => i.OrderByIdentifier)).ToList();

            return new OrderByResultCollection()
            {
                OrderByResults = orderByIdentifiers,
                AdditionalParameters = new Dictionary<string, object>()
            };
        }

        internal class OrderByResultCollection
        {
            public IEnumerable<OrderByResult> OrderByResults { get; set; }
            public IDictionary<string, object> AdditionalParameters { get; set; }
        }

        internal class OrderByResult
        {
            public OrderByIdentifier OrderByIdentifier { get; set; }
            public TableRelations TableRelations { get; set; }
            public IColumnDefinition ColumnDefinition { get; set; }
            public OrderByDirection Direction { get; set; }
            //public string TableAliasName { get; set; }
        }

        internal TableRelations ResolveTableAliasNameForViaRelationOrderBy(
            IArgument parameterArgument, 
            string viaRelationPath,
            string viaRelationColumnName,
            Func<IEnumerable<string>, TableRelations> resolveTableRelationsAliasFunc)
        {
            var tableRelations = this.databaseResolver.BuildTableRelationsFromRelationalPath(
                parameterArgument,
                viaRelationPath,
                viaRelationColumnName,
                TableRelationsColumnSource.Parameters, new ConcurrentDictionary<string, IEnumerable<string>>());

            var tableRelationPaths = new List<string>();
            var currTableRelations = tableRelations;
            string columnName = null;
            do
            {
                tableRelationPaths.Add(currTableRelations.TableName);
                if (!currTableRelations.NavigationTables.Any())
                {
                    columnName = currTableRelations.ProjectedColumns.Single().Name;
                }
                currTableRelations = currTableRelations.NavigationTables.SingleOrDefault();

            } while (currTableRelations != null);

            //var aliasRelation = resolveTableRelationsAliasFunc(tableRelationPaths);

            return tableRelations;
        }

        private FromClauseNode BuildFromClause(TableRelations tableRelations)
        {
            var fromClauseNode = new FromClauseNode();
            var tableIdentifier = new TableIdentifier().SetArgs(
                BuildFromClauseTable(tableRelations));

            var leftOuterJoins = BuildJoins(tableRelations);

            return fromClauseNode.SetArgs(tableIdentifier.AsEnumerable<AstNode>().Concat(leftOuterJoins));
        }

        private static AstNode BuildFromClauseTable(TableRelations tableRelations)
        {
            if (HasRowNumberPrimaryKey(tableRelations))
            {
                return BuildRowNumberProjectionTableReference(tableRelations);
            }
            
            var tableNode = new RelationalTable()
            {
                Label = tableRelations.TargetTable.Name
            };

            if (tableRelations.Alias != tableRelations.TableName)
            {
                return new Alias() { Label = tableRelations.Alias }.SetArgs(
                    tableNode);
            }

            return tableNode;
        }

        private static AstNode BuildRowNumberProjectionTableReference(TableRelations tableRelations)
        {
            var selectStatement = new Select();
            var selectClause = new SelectClause()
                .SetArgs(
                    tableRelations.PrimaryKey.Where(c => c is TableRelationColumnRowNumberFunctionDefinition).Select(column =>
                        BuildFromClauseSelectColumn(column, tableRelations.ProjectedColumns)
                    ).Concat(tableRelations.TargetTable.Columns.Select(c => BuildFromClauseSelectColumn(c, tableRelations.TargetTable.Columns)).ToList()).ToList()
                );

            AstNode tableIdentifier = new TableIdentifier().SetArgs(new RelationalTable()
                {Label = tableRelations.TableName});
            if (tableRelations.TargetTable.ObjectType == DatabaseObjectType.Function)
            {
                tableIdentifier = new Function()
                {
                    Name = tableRelations.TableName
                }.SetArgs(tableRelations.FunctionParameters.Select(p => new NamedParameterIdentifier()
                    {Name = p.SqlParameterName}));
            }
            var fromClause = new FromClause().SetArgs(new FromClauseNode().SetArgs(tableIdentifier));
            
            selectStatement.SelectClause = selectClause;
            selectStatement.FromClause = fromClause;

            return new Alias()
            {
                Label = tableRelations.Alias
            }.SetArgs(new LogicalGrouping().SetArgs(selectStatement));
        }

        private static AstNode BuildFromClauseSelectColumn(IColumnDefinition column, IEnumerable<IColumnDefinition> allColumns)
        {
            // this is a ROW_NUMBER column
            if (column is TableRelationColumnRowNumberFunctionDefinition)
            {
                var firstTableColumn = allColumns.First(c => !(c is TableRelationColumnRowNumberFunctionDefinition));
                return new Alias()
                {
                    Label = column.Name
                }.SetArgs(
                        new OverClause()
                        {
                            Function = new Function()
                            {
                                Name = "ROW_NUMBER"
                            }
                        }.SetArgs(
                            new OrderByClause().SetArgs(
                                new ColumnIdentifier().SetArgs(
                                    new RelationalColumn()
                                    {
                                        Label = firstTableColumn.Name
                                    })))
                );
            }

            // this is a normal column
            return new ColumnIdentifier()
                .SetArgs(
                    new RelationalColumn()
                {
                    Label = column.Name
                });
        }

        private static bool HasRowNumberPrimaryKey(TableRelations tableRelations)
        {
            return tableRelations.PrimaryKey.Any(p => p is TableRelationColumnRowNumberFunctionDefinition);
        }

        private IEnumerable<LeftOuterJoin> BuildJoins(TableRelations tableRelations)
        {
            var references = tableRelations.NavigationTables;
            var leftOuterJoins = references.SelectMany(navigationTableRelations =>
            {
                return BuildLeftOuterJoin(tableRelations, navigationTableRelations).AsEnumerable();
                
            });
            return leftOuterJoins;
        }

        private LeftOuterJoin BuildLeftOuterJoin(
             TableRelations tableRelations, TableRelations navigationTableRelations)
        {
            var tableHierarchyAliases = new List<ITableHierarchyAlias>();
            tableHierarchyAliases.Add(tableRelations);
            tableHierarchyAliases.Add(navigationTableRelations);
            
            var leftOuterJoin = new LeftOuterJoin().SetArgs(
                new AndOperator().SetArgs(
                navigationTableRelations.ForeignKeyToParent.KeyPairs.Select(kp =>
                            new EqualsOperator().SetArgs(
                                new ColumnIdentifier().SetArgs(new RelationalTable() { Label = tableHierarchyAliases.Single(s => s.TableName == kp.ForeignTableColumn.Table.Name).Alias },
                                    new RelationalColumn() { Label = kp.ForeignTableColumn.Name }),
                                new ColumnIdentifier().SetArgs(new RelationalTable() { Label = tableHierarchyAliases.Single(s => s.TableName == kp.PrimaryTableColumn.Table.Name).Alias },
                                    new RelationalColumn() {Label = kp.PrimaryTableColumn.Name})))).AsEnumerable().Cast<AstNode>()
                    .Concat(navigationTableRelations != null ? BuildJoins(navigationTableRelations) : new LeftOuterJoin[0]));
            //var rightTableAlias = tableHierarchyAliases.Single(s => s.TableName == navigationTableRelations.TableName).Alias;
            leftOuterJoin.RightNode = BuildFromClauseTable(navigationTableRelations); //(navigationTableRelations.TableName == rightTableAlias ? (AstNode) new Placeholder() : new Alias() { Label = rightTableAlias }).SetArgs(new TableIdentifier().SetArgs(new RelationalTable() { Label = navigationTableRelations.TableName }));
            return leftOuterJoin;
        }

        private IColumnDefinition GetColumnForParameterName(ITableDefinition table, string parameterName)
        {
            var matchingTargetTableColumn = table.Columns.FirstOrDefault(c => c.Name.Equals(parameterName, StringComparison.InvariantCultureIgnoreCase));
            
            return matchingTargetTableColumn;
        }
        
        private enum StatementType
        {
            Select,
            Insert,
            Update,
            Delete,
            UpdateByKey,
            Upsert,
            Sync
        }
    }

    public class PreparedSqlStatement
    {
        public PreparedSqlStatement()
        {
        }

        public PreparedSqlStatement(string commandText, IDictionary<string, object> parameters, PrimaryKeyQuerySpecifierCollection primaryKeyColumns)
        {
            this.CommandText = commandText;
            this.Parameters = parameters;
            this.PrimaryKeyColumns = primaryKeyColumns;
        }

        public PreparedSqlStatement(string commandText, object parameters) : this(commandText, parameters?.ToDictionary(), null)
        {
        }

        public string CommandText { get; set; }
        public IDictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// A list of qualified column names in the select list that are primary keys. This will deduplicate
        /// the query results for their respective Collection Properties
        /// </summary>
        /// <example>
        /// For SQL Query:
        ///  select Address.Id Id, Location.Id [Locations.Id], Location.Name [Locations.Name]
        ///  from Address
        ///  inner join Location on Location.Id = Address.Id
        ///
        /// then setting:
        /// 
        /// PrimaryKeyColumns = ["Id", "Location.Id"]
        ///
        /// will deduplicate the "Locations" property by the column key "Locations.Id". Note that the
        /// name of the alias is important. The first part must match the property name and the second part
        /// must match the property of the class</example>
        public PrimaryKeyQuerySpecifierCollection PrimaryKeyColumns { get; set; }
    }

    public class PrimaryKeyQuerySpecifier
    {
        internal string Path { get; }
        internal string Name { get; }

        public PrimaryKeyQuerySpecifier(string path, string name)
        {
            Path = path;
            Name = name;
        }

        public static implicit operator PrimaryKeyQuerySpecifier(string qualifiedPath)
        {
            var lastDotIndex = qualifiedPath.LastIndexOf(".");
            if (lastDotIndex == -1)
            {
                return new PrimaryKeyQuerySpecifier(string.Empty, qualifiedPath);
            }

            var path = qualifiedPath.Substring(0, lastDotIndex);
            var name = qualifiedPath.Substring(lastDotIndex + 1, qualifiedPath.Length - (lastDotIndex + 1));

            return new PrimaryKeyQuerySpecifier(path, name);
        }


        public override string ToString()
        {
            return !string.IsNullOrEmpty(this.Path) ? $"{this.Path}.{this.Name}" : this.Name;
        }
    }

    public class PrimaryKeyQuerySpecifierCollection
    {
        private readonly List<PrimaryKeyQuerySpecifier> items;

        public PrimaryKeyQuerySpecifierCollection(IEnumerable<PrimaryKeyQuerySpecifier> items)
        {
            this.items = items.ToList();
        }
        //internal List<PrimaryKeyQuerySpecifier> Get()
        //{
        //    return items;
        //}

        internal IDictionary<string, IEnumerable<string>> ToGroup()
        {
            return 
                items.GroupBy(i => i.Path)
                    .ToDictionary(g => g.Key, g => g.Select(p => p.Name));
        }

        public static implicit operator PrimaryKeyQuerySpecifierCollection(string[] paths)
        {
            return new PrimaryKeyQuerySpecifierCollection(paths.Select(p => (PrimaryKeyQuerySpecifier) p));
        }

        public static implicit operator PrimaryKeyQuerySpecifierCollection(List<string> paths)
        {
            return new PrimaryKeyQuerySpecifierCollection(paths.Select(p => (PrimaryKeyQuerySpecifier)p));
        }
    }

    public class SqlMethodInvocation
    {
        public MethodSqlStatement SqlStatement { get; set; }
    }

    public class ParameterPath
    {
        internal IArgument Argument { get; }
        public string SqlParameterName { get; set; }
        public ParameterInfo Parameter => Argument.FindParameter().GetParameterInfo();

        public IEnumerable<PropertyInfo> Properties =>
            this.Argument.FindPropertiesFromRoot().Select(a => a.GetPropertyInfo()).ToList();


        internal ParameterPath(IArgument argument)
        {
            Argument = argument;
        }
        
        public Type GetEndpointType()
        {
            if (Properties != null && Properties.Any()) return Properties.Last().PropertyType;
            return Parameter.ParameterType;
        }

        public T GetEndpointAttribute<T>()
            where T: Attribute
        {
            if (Properties != null && Properties.Any()) return Properties.Last().GetCustomAttribute<T>();
            return Parameter.GetCustomAttribute<T>();
        }

        public string GenerateSuggestedSqlIdentifierName()
        {
            return $"{Parameter.Name}{(Properties != null ? string.Join("_", Properties.Select(p => p.Name)) : null)}";
        }

        public string GenerateClassQualifiedName()
        {
            return $"{Parameter.Name}{(Properties != null && Properties.Any() ? "." + string.Join(".", Properties.Select(p => p.Name)) : null)}";
        }

        public string GetEndpointColumnName()
        {
            var columnAttribute = this.GetEndpointAttribute<ColumnAttribute>();
            if (columnAttribute != null)
            {
                return columnAttribute.ColumnName;
            }
            if (Properties != null && Properties.Any()) return Properties.Last().Name;
            return Parameter.Name;
        }
    }

    public class TokenPath
    {
        internal TokenPath(IArgument argument)
        {
            Argument = argument;
        }

        public ParameterInfo Parameter => Argument.FindParameter().GetParameterInfo();

        public IEnumerable<PropertyInfo> Properties =>
            this.Argument.FindPropertiesFromRoot().Select(a => a.GetPropertyInfo()).ToList();
        public string SqlParameterName { get; set; }
        internal IArgument Argument { get; set; }
        public Func<object, TokenPath, IEnumerable<ParameterArg>, IDictionary<string, object>> UpdateNodeFunc { get; set; }
    }

    public class ParameterArg
    {
        public ParameterInfo Parameter { get; set; }
        public object Value { get; set; }
    }

    public class ColumnAliasForeignKeyDefinition : IForeignKeyDefinition
    {
        private readonly IForeignKeyDefinition foreignKeyDefinition;
        public ITableDefinition PrimaryKeyTable => foreignKeyDefinition.PrimaryKeyTable;

        public IEnumerable<IForeignKeyPair> KeyPairs => foreignKeyDefinition.KeyPairs;

        public ColumnAliasForeignKeyDefinition(IForeignKeyDefinition foreignKeyDefinition)
        {
            this.foreignKeyDefinition = foreignKeyDefinition;
            this.ColumnAliasForeignKeyPairs =
                foreignKeyDefinition.KeyPairs.Select(kp => new ColumnAliasForeignKeyPair(kp)).ToList();
        }

        public IEnumerable<ColumnAliasForeignKeyPair> ColumnAliasForeignKeyPairs { get; }
    }

    public class ColumnAliasForeignKeyPair : IForeignKeyPair
    {
        private readonly IForeignKeyPair foreignKeyPair;
        public IColumnDefinition ForeignTableColumn => foreignKeyPair.ForeignTableColumn;

        public IColumnDefinition PrimaryTableColumn => foreignKeyPair.PrimaryTableColumn;

        public ColumnAliasForeignKeyPair(IForeignKeyPair foreignKeyPair)
        {
            this.foreignKeyPair = foreignKeyPair;
            this.ForeignTableColumnWithAlias = new ColumnAliasColumnDefinition(this.ForeignTableColumn.Name, this.ForeignTableColumn.DataTypeDeclaration, this.ForeignTableColumn.Table, null, this.ForeignTableColumn.IsIdentity);
            this.PrimaryTableColumnWithAlias = new ColumnAliasColumnDefinition(this.PrimaryTableColumn.Name, this.PrimaryTableColumn.DataTypeDeclaration, this.PrimaryTableColumn.Table, null, this.ForeignTableColumn.IsIdentity);
        }

        public ColumnAliasColumnDefinition ForeignTableColumnWithAlias { get; }
        public ColumnAliasColumnDefinition PrimaryTableColumnWithAlias { get; }
    }

    public class ColumnAliasColumnDefinition : IColumnDefinition
    {
        private readonly IColumnDefinition columnDefinition;
        public string Name { get; }
        public string DataTypeDeclaration { get; }
        public bool IsIdentity { get; }

        public ITableDefinition Table { get; }

        public string TableAlias { get; }

        public ColumnAliasColumnDefinition(string name, string dataTypeDeclaration, ITableDefinition table, ITableHierarchyAlias tableAlias, bool isIdentity)
        {
            Name = name;
            DataTypeDeclaration = dataTypeDeclaration;
            Table = table;
            TableAlias = tableAlias?.Alias;
        }
    }

    public static class ForeignKeyDefinitionExtensions
    {
        public static IEnumerable<ColumnAliasForeignKeyDefinition> ToColumnAliasForeignKeyDefinitions(
            this IEnumerable<IForeignKeyDefinition> definitions)
        {
            return definitions.Select(d => new ColumnAliasForeignKeyDefinition(d)).ToList();
        }
    }

    internal struct TableRelationResult
    {
        public string TableName { get; }
        public string ColumnName { get; }
        public string TableAliasName { get; }

        public TableRelationResult(string tableName, string tableAliasName, string columnName)
        {
            this.TableName = tableName;
            this.ColumnName = columnName;
            this.TableAliasName = tableAliasName;
        }
    }
}
