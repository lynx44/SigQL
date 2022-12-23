using System;
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
            TableRelations orderbyTableRelations;
            {
                var projectionTableRelations = this.databaseResolver.BuildTableRelations(tableDefinition, new TypeArgument(projectionType, this.databaseResolver),
                    TableRelationsColumnSource.ReturnType, tablePrimaryKeyDefinitions);
                var parametersTableRelations = this.databaseResolver.BuildTableRelations(tableDefinition, new TableArgument(tableDefinition, arguments),
                    TableRelationsColumnSource.Parameters, new ConcurrentDictionary<string, IEnumerable<string>>());
                
                orderbyTableRelations = parametersTableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.OrderBy);
                allTableRelations = this.databaseResolver.MergeTableRelations(
                    projectionTableRelations,
                    parametersTableRelations);
            }

            var selectClauseBuilder = new SelectClauseBuilder(this.databaseResolver);
            var selectTableRelations = allTableRelations.Mask(TableRelationsColumnSource.ReturnType, ColumnFilters.SelectClause);
            var resolvedSelectClause = selectClauseBuilder.Build(selectTableRelations, tablePrimaryKeyDefinitions);
            
            var selectClause = resolvedSelectClause.Ast;

            var primaryTable = allTableRelations.TargetTable;
            var parameterPaths = new List<ParameterPath>();
            var tokens = new List<TokenPath>();
            
            var allJoinRelations = this.databaseResolver.MergeTableRelations(selectTableRelations, orderbyTableRelations);
            allJoinRelations.MasterRelations = allTableRelations;

            allJoinRelations.Traverse(tr => parameterPaths.AddRange(tr.FunctionParameters));

            var fromClauseNode = BuildFromClause(allJoinRelations);
            var fromClause = new FromClause().SetArgs(fromClauseNode);

            var statement = new Select()
            {
                SelectClause = selectClause,
                FromClause = fromClause
            };

            var orderByParameters = allTableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.OrderBy);
            
            var orderBySpecs = ConvertToOrderBySpecs(arguments, orderByParameters, allTableRelations).ToList();
            var dynamicOrderByParameterPaths = this.FindDynamicOrderByParameterPaths(arguments);
            var dynamicOrderBySpecs = ConvertToOrderBySpecs(arguments, dynamicOrderByParameterPaths, primaryTable, allTableRelations);

            orderBySpecs.AddRange(dynamicOrderBySpecs);
            orderBySpecs = orderBySpecs.OrderBy(o => o.Ordinal).ToList();
            if (orderBySpecs.Any())
            {
                statement.OrderByClause = BuildOrderByClause(null, orderBySpecs, tokens);
            }

            // offset functionality
            var offsetParameter = GetParameterPathWithAttribute<OffsetAttribute>(arguments);
            var fetchParameter = GetParameterPathWithAttribute<FetchAttribute>(arguments);
            var whereClauseTableRelations = allTableRelations.Mask(TableRelationsColumnSource.Parameters, ColumnFilters.WhereClause);
            if ((offsetParameter != null || fetchParameter != null))
            {
                if ((allTableRelations.NavigationTables != null && allTableRelations.NavigationTables.Any()))
                {
                    var primaryTableAlias = $"{allTableRelations.TargetTable.Name}0";
                    var offsetFromClause =
                        new FromClauseNode().SetArgs(
                            new Alias() { Label = primaryTableAlias }.SetArgs(
                                new TableIdentifier().SetArgs(
                                    new RelationalTable() { Label = primaryTable.Name })));

                    var primaryKey = primaryTable.PrimaryKey;
                    var primaryKeyColumnNodes = primaryKey.Columns.Select(column =>
                        new ColumnIdentifier()
                            .SetArgs(
                                new Alias()
                                {
                                    Label = primaryTableAlias
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
                        offsetWhereClause = BuildWhereClauseFromTargetTablePerspective(new Alias() { Label = primaryTableAlias }, whereClauseTableRelations, parameterPaths, tokens);
                    }

                    OrderByClause offsetOrderByClause = BuildOrderByWithOffsetClause(statement, primaryTableAlias, orderBySpecs, tokens, offsetParameter, parameterPaths, fetchParameter);
                    var offsetSubquery = new Select()
                    {
                        SelectClause = new SelectClause().SetArgs(primaryKeyColumnNodes),
                        FromClause = new FromClause().SetArgs(offsetFromClause),
                        WhereClause = offsetWhereClause,
                        OrderByClause = offsetOrderByClause
                    };

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
                    var offsetOrderByClause = BuildOrderByWithOffsetClause(statement, null, orderBySpecs, tokens, offsetParameter, parameterPaths, fetchParameter);
                    statement.OrderByClause = offsetOrderByClause;
                    if (whereClauseTableRelations.ProjectedColumns.Any() || whereClauseTableRelations.NavigationTables.Any())
                    {
                        statement.WhereClause = BuildWhereClauseFromTargetTablePerspective(
                            new RelationalTable() { Label = allTableRelations.Alias }, whereClauseTableRelations, parameterPaths,
                            tokens);
                    }
                }
            }
            else if (whereClauseTableRelations.ProjectedColumns.Any() || whereClauseTableRelations.NavigationTables.Any())
            {
                statement.WhereClause = BuildWhereClauseFromTargetTablePerspective(
                    new RelationalTable() {Label = allTableRelations.Alias}, whereClauseTableRelations, parameterPaths,
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

        private OrderByClause BuildOrderByWithOffsetClause(Select statement, string primaryTableAlias, IEnumerable<OrderBySpec> orderBySpecs,
            List<TokenPath> tokens, ParameterPath offsetParameter, List<ParameterPath> parameterPaths, ParameterPath fetchParameter)
        {
            OrderByClause offsetOrderByClause;
            if (statement.OrderByClause != null)
            {
                offsetOrderByClause = BuildOrderByClause(primaryTableAlias, orderBySpecs, tokens);
                statement.OrderByClause = null;
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
                        columnName = result.ColumnName;
                        tableName = result.TableName;
                        tableAliasName = result.TableAliasName;
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

        private WhereClause BuildWhereClauseFromTargetTablePerspective(AstNode primaryTableReference, TableRelations whereClauseTableRelations, List<ParameterPath> parameterPaths, List<TokenPath> tokens)
        {
            var andOperator = new AndOperator().SetArgs(whereClauseTableRelations.ProjectedColumns.SelectMany(column =>
                {
                    return column.Arguments.GetArguments(TableRelationsColumnSource.Parameters).SelectMany(arg =>
                    {
                        var argument = arg.GetEndpoint();
                        var parameterName = argument.Name;
                        var dbColumn = GetColumnForParameterName(whereClauseTableRelations.TargetTable, parameterName);


                        var comparisonNode = BuildComparisonNode(new ColumnIdentifier()
                            .SetArgs(primaryTableReference,
                                new RelationalColumn()
                                {
                                    Label = column.Name
                                }), parameterName, argument, parameterPaths, tokens);
                        return comparisonNode.AsEnumerable().ToList();
                    });

                }
            ));

            andOperator.Args = andOperator.Args.Concat(
            whereClauseTableRelations.NavigationTables.Select(nt =>
                BuildWhereClauseForPerspective(primaryTableReference, nt, "0", parameterPaths, tokens))).ToList();
            
            var whereClause = new WhereClause().SetArgs(andOperator);
            return andOperator.Args.Any() ? whereClause : null;
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

            selectStatement.WhereClause = new WhereClause().SetArgs(
                new AndOperator().SetArgs(

                        // match foreign keys to parent
                        navigationTableRelations.ForeignKeyToParent.KeyPairs.Select(kp =>
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
                        }).ToList()
                        .Concat(
                            navigationTableRelations.ProjectedColumns.SelectMany(c =>
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
                                        t.UpdateNodeFunc = (parameterValue, parameterArg) =>
                                        {
                                            if ((parameterValue == null && (comparisonSpec.IgnoreIfNull || comparisonSpec.IgnoreIfNullOrEmpty)) ||
                                                (parameterValue.IsEmpty() && comparisonSpec.IgnoreIfNullOrEmpty))
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
                                                return existingFunc(parameterValue, parameterArg);
                                            }
                                        };
                                    }
                                    
                                });
                                return
                                    astNodes;

                            }).ToList()
                        )
                        .Concat(
                            navigationTableRelations.NavigationTables.Select((nt, i) =>
                            {
                                return BuildWhereClauseForPerspective(new Alias() { Label = navigationTableAlias }, nt,
                                        $"{navigationTableAliasPostfix}{i}", parameterPaths, tokens);
                            }).ToList()
                        )
                    ));


            return new AndOperator().SetArgs(
                new Exists().SetArgs(selectStatement));
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
                    UpdateNodeFunc = (parameterValue, parameterArg) =>
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
                    UpdateNodeFunc = (parameterValue, parameterArg) =>
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
                    UpdateNodeFunc = (parameterValue, parameterArg) =>
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
                    UpdateNodeFunc = (parameterValue, parameterArg) =>
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

        private OrderByClause BuildOrderByClause(string tableAliasName, IEnumerable<OrderBySpec> parameters, List<TokenPath> tokens)
        {
            var orderByClause = new OrderByClause();
            orderByClause.SetArgs(
                parameters.SelectMany(p =>
                {
                    if (!p.IsDynamic)
                    {
                        var tokenName = $"{p.ParameterPath.GenerateSuggestedSqlIdentifierName()}_OrderByDirection";

                        var orderByNode = new OrderByIdentifier() { Direction = $"{{{tokenName}}}" };
                        tokens.Add(new TokenPath(p.ParameterPath.Argument)
                        {
                            UpdateNodeFunc = (parameterValue, parameterArg) =>
                            {
                                var directionString = "asc";
                                if (parameterValue is OrderByDirection direction)
                                {
                                    directionString = direction == OrderByDirection.Ascending ? "asc" : "desc";
                                }
                               
                                orderByNode.Direction = directionString;

                                return new Dictionary<string, object>();
                            }
                        });
                        return orderByNode.SetArgs(
                            new ColumnIdentifier().SetArgs(
                                (AstNode)new RelationalTable() { Label = tableAliasName ?? p.TableName },
                                new RelationalColumn() { Label = p.ColumnName }
                            )
                        ).AsEnumerable();
                    }
                    else
                    {
                        return BuildDynamicOrderByIdentifier(orderByClause, tokens, p).AsEnumerable();
                    }
                    
                }).ToList()
            );
            return orderByClause;
        }

        private IEnumerable<OrderByIdentifier> BuildDynamicOrderByIdentifier(
            OrderByClause orderByClause,
             List<TokenPath> tokens, OrderBySpec p)
        {
            var tokenName = $"{p.ParameterPath.GenerateSuggestedSqlIdentifierName()}_OrderBy";

            List<OrderByIdentifier> orderByClauses = new List<OrderByIdentifier>();
            tokens.Add(new TokenPath(p.ParameterPath.Argument)
            {
                UpdateNodeFunc = (parameterValue, parameterArg) =>
                {
                    var orderBys = p.IsCollection ? parameterValue as IEnumerable<IOrderBy> : (parameterValue as IOrderBy).AsEnumerable();

                    if (orderBys == null || !orderBys.Any())
                    {
                        orderByClause.Args = null;
                        orderBys = new List<IOrderBy>();
                    }

                    var orderByIdentifiers = orderBys.Select(orderBy =>
                    {
                        string tableName;
                        var orderByRelation = orderBy as OrderByRelation;
                        if (orderByRelation != null)
                        {
                            var viaRelationPath = orderByRelation.ViaRelationPath;
                            var viaRelationColumn = orderByRelation.ViaRelationColumnName;
                            var parameterArgument = new ParameterArgument(p.ParameterPath.Parameter, this.databaseResolver);
                            
                            var result = ResolveTableAliasNameForViaRelationOrderBy(parameterArgument, viaRelationPath,
                                viaRelationColumn, p.ResolveTableRelations);

                            orderByRelation.Table = result.TableAliasName;
                            orderByRelation.Column = result.ColumnName;
                            tableName = result.TableAliasName;
                        }
                        else
                        {
                            var tableIdentifier = this.databaseConfiguration.Tables.FindByName(orderBy.Table);
                            if (tableIdentifier == null)
                            {
                                throw new InvalidIdentifierException($"Unable to identify matching database table for order by parameter {p.ParameterPath.GenerateClassQualifiedName()} with specified table name \"{orderBy.Table}\". Table {orderBy.Table} could not be found.");
                            }

                            if (tableIdentifier.Columns.FindByName(orderBy.Column) == null)
                            {
                                throw new InvalidIdentifierException($"Unable to identify matching database column for order by parameter {p.ParameterPath.GenerateClassQualifiedName()} with specified column name \"{orderBy.Column}\". Column {orderBy.Column} does not exist in table {tableIdentifier.Name}.");
                            }

                            tableName = p.ResolveTableAlias(tableIdentifier.Name);
                        }
                        

                        var orderByNode = new OrderByIdentifier()
                        {
                            Direction = $"{{{tokenName}}}"
                        };
                        var directionString = orderBy.Direction == OrderByDirection.Ascending ? "asc" : "desc";
                        orderByNode.Direction = directionString;

                        return orderByNode.SetArgs(
                            new ColumnIdentifier().SetArgs(
                                (AstNode) new RelationalTable() {Label = tableName },
                                new RelationalColumn() {Label = orderBy.Column}
                            ));
                    }).ToList();

                    orderByClause.Args = (orderByClause.Args ?? new List<AstNode>()).Concat(orderByIdentifiers).ToList();

                    return new Dictionary<string, object>();
                }
            });
            return new List<OrderByIdentifier>();
        }

        private TableAliasResult ResolveTableAliasNameForViaRelationOrderBy(
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

            var aliasRelation = resolveTableRelationsAliasFunc(tableRelationPaths);

            return new TableAliasResult(aliasRelation.TableName, aliasRelation.Alias, columnName);
        }

        private FromClauseNode BuildFromClause(TableRelations tableRelations)
        {
            var fromClauseNode = new FromClauseNode();
            var targetTable = tableRelations.TargetTable;
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
            Upsert
        }
    }

    public class PreparedSqlStatement
    {
        public PreparedSqlStatement()
        {
        }

        public PreparedSqlStatement(string commandText, object parameters)
        {
            this.CommandText = commandText;
            this.Parameters = ((object) parameters).ToDictionary();
        }

        public string CommandText { get; set; }
        public IDictionary<string, object> Parameters { get; set; }
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
        public Func<object, TokenPath, IDictionary<string, object>> UpdateNodeFunc { get; set; }
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

    internal struct TableAliasResult
    {
        public string TableName { get; }
        public string ColumnName { get; }
        public string TableAliasName { get; }

        public TableAliasResult(string tableName, string tableAliasName, string columnName)
        {
            this.TableName = tableName;
            this.ColumnName = columnName;
            this.TableAliasName = tableAliasName;
        }
    }
}
