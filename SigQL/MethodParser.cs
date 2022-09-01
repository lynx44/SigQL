﻿using System;
using System.Collections;
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

        public MethodParser(SqlStatementBuilder builder, IDatabaseConfiguration databaseConfiguration)
        {
            this.builder = builder;
            this.databaseConfiguration = databaseConfiguration;
            this.databaseResolver = new DatabaseResolver(this.databaseConfiguration);
        }

        public MethodSqlStatement SqlFor(MethodInfo methodInfo)
        {
            var statementType = DetectStatementType(methodInfo);
            if (statementType == StatementType.Insert)
            {
                var insertSpec = GetInsertSpec(methodInfo);
                return BuildInsertStatement(insertSpec, Enumerable.Select<InsertColumnParameter, ParameterPath>(insertSpec.ColumnParameters, cp => cp.ParameterPath).ToList());
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

            var selectClauseBuilder = new SelectClauseBuilder(this.databaseResolver);
            var resolvedSelectClause = selectClauseBuilder.Build(returnType);
            var fromClauseRelations = resolvedSelectClause.FromClauseRelations;
            
            var tablePrimaryKeyDefinitions = resolvedSelectClause.TableKeyDefinitions;
            
            var selectClause = resolvedSelectClause.Ast;

            var primaryTable = fromClauseRelations.TargetTable;
            var parameterPaths = new List<ParameterPath>();
            var tokens = new List<TokenPath>();

            var methodParameters = methodInfo.GetParameters();
            var detectedParameters =
                this.databaseResolver.BuildDetectedParameters(primaryTable, methodInfo.GetParameters());
            var allJoinRelations = fromClauseRelations;

            var functionParameters = methodParameters.Where(p => IsFunctionParameter(p)).ToList();
            parameterPaths.AddRange(functionParameters.Select(p => new ParameterPath()
            {
                Parameter = p,
                SqlParameterName = p.Name
            }));
            var fromClauseNode = BuildFromClause(allJoinRelations, functionParameters);
            var fromClause = new FromClause().SetArgs(fromClauseNode);

            var statement = new Select()
            {
                SelectClause = selectClause,
                FromClause = fromClause
            };

            var orderByParameters = FindOrderByParameters(projectionType, methodParameters);
            IEnumerable<OrderBySpec> orderBySpecs = ConvertToOrderBySpecs(orderByParameters, primaryTable, fromClauseRelations);
            if (orderBySpecs.Any())
            {
                statement.OrderByClause = BuildOrderByClause(null, orderBySpecs, tokens);
            }

            // offset functionality
            var offsetParameter = GetParameterPathWithAttribute<OffsetAttribute>(methodParameters);
            var fetchParameter = GetParameterPathWithAttribute<FetchAttribute>(methodParameters);
            if ((offsetParameter != null || fetchParameter != null))
            {
                if ((fromClauseRelations.NavigationTables != null && fromClauseRelations.NavigationTables.Any()))
                {
                    var primaryTableAlias = $"{fromClauseRelations.TargetTable.Name}0";
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
                    if (detectedParameters.Any())
                    {
                        offsetWhereClause = BuildWhereClauseFromTargetTablePerspective(new Alias() { Label = primaryTableAlias }, primaryTable, detectedParameters, parameterPaths, tokens);
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
                                            new RelationalTable() {Label = fromClauseRelations.Alias},
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
                    if (detectedParameters.Any())
                    {
                        statement.WhereClause = BuildWhereClauseFromTargetTablePerspective(
                            new RelationalTable() { Label = primaryTable.Name }, primaryTable, detectedParameters, parameterPaths,
                            tokens);
                    }
                }
            }
            else if (detectedParameters.Any())
            {
                statement.WhereClause = BuildWhereClauseFromTargetTablePerspective(
                    new RelationalTable() {Label = fromClauseRelations.Alias}, primaryTable, detectedParameters, parameterPaths,
                    tokens);
            }

            // remove IN parameters - these are converted to separate parameters
            parameterPaths.RemoveAll(p =>
                (p.Properties?.Any()).GetValueOrDefault(false)
                    ? p.Properties.Select(pp => pp.PropertyType).Last().IsCollectionType()
                    : p.Parameter.ParameterType.IsCollectionType());

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
                // ColumnAliasRelations = columnAliasForeignKeyDefinitions,
                TargetTablePrimaryKey = !isCountResult ? fromClauseRelations.TargetTable.PrimaryKey : new TableKeyDefinition(),
                TablePrimaryKeyDefinitions = !isCountResult ? tablePrimaryKeyDefinitions : new Dictionary<string, ITableKeyDefinition>()
            };
            return sqlStatement;
        }

        private TableRelations FindOrderByParameters(Type projectionType, ParameterInfo[] methodParameters)
        {
            var orderByTableRelations = this.databaseResolver.BuildTableRelations(projectionType, methodParameters, ColumnFilters.OrderBy);
            return orderByTableRelations;
            //return methodParameters.Where(p =>
            //    IsOrderByAttributeParameter(p) ||
            //    IsDynamicOrderByParameter(p) ||
            //    IsOrderByDirectionParameter(p)).Select(p => new ParameterPath()
            //    {
            //        Parameter = p
            //    }).ToList();
        }

        private static ParameterPath GetParameterPathWithAttribute<TAttribute>(ParameterInfo[] methodParameters)
            where TAttribute : Attribute
        {
            var parameterPathWithAttribute = methodParameters.SingleOrDefault(p => p.GetCustomAttribute<TAttribute>() != null);
            if (parameterPathWithAttribute != null)
            {
                return 
                    new ParameterPath()
                    {
                        Parameter = parameterPathWithAttribute
                    };
            }

            return FindClassFilterPropertyWithAttribute<TAttribute>(methodParameters);
        }

        private static ParameterPath FindClassFilterPropertyWithAttribute<TAttribute>(ParameterInfo[] methodParameters)
            where TAttribute : Attribute
        {
            var propertiesWithAttribute = methodParameters.Select(p =>
            {
                var isClass = p.ParameterType.IsClass || p.ParameterType.IsInterface;
                if (isClass)
                {
                    var propsWithAttr = p.ParameterType.GetProperties().Where(p => p.GetCustomAttribute<TAttribute>() != null);
                    if (propsWithAttr.Count() >= 2)
                    {
                        throw new InvalidAttributeException(typeof(TAttribute), propsWithAttr,
                            $"Attribute [{typeof(TAttribute).Name}] is specified more than once.");
                    }

                    if (propsWithAttr.Any())
                    {
                        return
                            new ParameterPath()
                            {
                                Parameter = p,
                                Properties = propsWithAttr.ToList()
                            };
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

        private bool IsFunctionParameter(ParameterInfo parameter)
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

        private bool IsDynamicOrderByParameter(ParameterInfo parameter)
        {
            return parameter.ParameterType.IsAssignableFrom(typeof(OrderBy)) || parameter.ParameterType.IsAssignableFrom(typeof(IEnumerable<OrderBy>));
        }

        //private static bool IsGenericOrderByParameter(ParameterInfo p)
        //{
        //    return p.ParameterType.IsGenericType &&
        //           p.ParameterType.GetGenericTypeDefinition().IsAssignableFrom(typeof(OrderBy<>));
        //}

        private static bool IsOrderByAttributeParameter(ParameterInfo p)
        {
            return p.GetCustomAttribute<OrderByAttribute>() != null;
        }

        private static bool IsOrderByDirectionParameter(ParameterPath p)
        {
            return p.GetEndpointType() == typeof(OrderByDirection);
        }

        private IEnumerable<OrderBySpec> ConvertToOrderBySpecs(TableRelations orderByParameters,
            ITableDefinition primaryTable, TableRelations primaryTableRelations)
        {
            var parameterPaths = this.databaseResolver.ProjectedColumnsToParameterPaths(orderByParameters, new List<PropertyInfo>());
            return parameterPaths.Select(p => ConvertToOrderBySpec(p, primaryTable, primaryTableRelations)).ToList();
        }

        private OrderBySpec ConvertToOrderBySpec(ParameterPath parameterPath, ITableDefinition primaryTable,
            TableRelations primaryTableRelations)
        {
            Func<string, string> resolveTableAlias = (tableName) => primaryTableRelations.Find(tableName).Alias;
            var orderByAttribute = parameterPath.GetEndpointAttribute<OrderByAttribute>();
            if (orderByAttribute != null)
            {
                var matchingRelation = primaryTableRelations.Find(orderByAttribute.Table);
                var table = matchingRelation.Alias;

                var column = orderByAttribute.Column;

                return new OrderBySpec(resolveTableAlias)
                {
                    TableName = table,
                    ColumnName = column,
                    ParameterPath = parameterPath,
                    IsDynamic = false,
                    IsCollection = false
                };
            }
            else if (IsOrderByDirectionParameter(parameterPath))
            {
                var column = primaryTable.Columns.FindByName(parameterPath.GetEndpointColumnName());

                if (column == null)
                {
                    throw new InvalidIdentifierException(
                        $"Unable to identify matching database column for order by parameter \"{parameterPath.GenerateClassQualifiedName()}\". Column \"{parameterPath.GetEndpointColumnName()}\" does not exist in table {primaryTable.Name}.");
                }

                return new OrderBySpec(resolveTableAlias)
                {
                    TableName = primaryTableRelations.Alias,
                    ColumnName = column.Name,
                    ParameterPath = parameterPath,
                    IsDynamic = false,
                    IsCollection = false
                };
            }
            else
            {
                return new OrderBySpec(resolveTableAlias)
                {
                    ParameterPath = parameterPath,
                    IsDynamic = true,
                    IsCollection = parameterPath.GetEndpointType().IsCollectionType()
                };

            }
            
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

            return StatementType.Select;
        }

        private WhereClause BuildWhereClauseFromTargetTablePerspective(AstNode primaryTableReference,
            ITableDefinition primaryTable, IEnumerable<DetectedParameter> parameters,
            List<ParameterPath> parameterPaths, List<TokenPath> tokens)
        {
            var andOperator = new AndOperator().SetArgs(parameters.SelectMany<DetectedParameter, AstNode>(
                    parameter =>
                    {
                        // if this is a primitive or built in .NET type, just set it equal
                        // to the expected value
                        //var isTableOrTableProjection = this.databaseResolver.IsTableOrTableProjection(parameter.Type);
                        if (!this.databaseResolver.IsClrOnly(parameter.ParameterInfo))
                        {

                            if (!this.databaseResolver.IsTableOrTableProjection(parameter.Type) &&
                                parameter.TableRelations == null)
                            {
                                var parameterName = parameter.ParameterInfo.Name;
                                var column = GetColumnForParameterName(primaryTable, parameter.Name);

                                var comparisonNode = BuildComparisonNode(new ColumnIdentifier()
                                    .SetArgs(primaryTableReference,
                                        new RelationalColumn()
                                        {
                                            Label = column.Name
                                        }), parameterName, parameter.Type, parameter.ParameterInfo, new List<PropertyInfo>(), parameterPaths, tokens);
                                return comparisonNode.AsEnumerable().ToList();
                            }
                            else
                            if (this.databaseResolver.IsTableOrTableProjection(parameter.Type) &&
                                TableEqualityComparer.Default.Equals(primaryTable, parameter.TableRelations.TargetTable))
                            {
                                var filterComparisons = parameter.Type.GetProperties().Where(p => !this.databaseResolver.IsTableOrTableProjection(p.PropertyType)).SelectMany(property =>
                                {
                                    if (!ColumnAttributes.IsDecoratedNonColumn(property))
                                    {
                                        var parameterName = property.Name;

                                        var column = GetColumnForParameterName(parameter.TableRelations, this.databaseResolver.GetColumnName(property));

                                        var comparisonNode = BuildComparisonNode(new ColumnIdentifier()
                                                .SetArgs(primaryTableReference,
                                                    new RelationalColumn()
                                                    {
                                                        Label = column.Name
                                                    }), parameterName, property.PropertyType, parameter.ParameterInfo,
                                            new List<PropertyInfo>() { property }, parameterPaths, tokens);
                                        return comparisonNode.AsEnumerable().ToList();
                                    }
                                    else
                                    {
                                        return new List<AstNode>();
                                    }
                                    
                                }).ToList();

                                var navComparisons = parameter.TableRelations.NavigationTables.Select(nt =>
                                {
                                    return BuildWhereClauseForPerspective(primaryTableReference,
                                        nt, "0", parameterPaths, parameter.ParameterInfo,
                                        new List<PropertyInfo>() { nt.ParentColumnField.Property }, tokens);
                                });

                                return navComparisons.Concat(filterComparisons).ToList();
                            }
                            else
                                // this is referencing a foreign table, reference the table
                            {
                                return parameter.TableRelations.NavigationTables.Select(nt =>
                                {
                                    return BuildWhereClauseForPerspective(primaryTableReference,
                                        nt, "0", parameterPaths, parameter.ParameterInfo,
                                        nt.ParentColumnField?.Property.AsEnumerable().ToList() ?? new List<PropertyInfo>(), tokens);
                                });
                            }
                        }
                        else
                        {
                            return new List<AstNode>();
                        }

                    }).ToList()
            );
            var whereClause = new WhereClause().SetArgs(andOperator);
            return andOperator.Args.Any() ? whereClause : null;
        }

        private AstNode BuildComparisonNode(AstNode columnNode,
            string parameterName, Type methodParameterType, ParameterInfo rootParameter,
            List<PropertyInfo> propertyPath, List<ParameterPath> parameterPaths, List<TokenPath> tokens)
        {
            AstNode operatorNode = null;
            var comparisonSpec = this.databaseResolver.GetColumnSpec(methodParameterType, propertyPath.Any() ? propertyPath.Last().CustomAttributes : rootParameter.CustomAttributes);
            var parameterType = comparisonSpec.ComparisonType;

            var placeholder = new Placeholder();
            if (typeof(Like).IsAssignableFrom(parameterType) || 
                comparisonSpec.IsAnyLike)
            {
                operatorNode = new LikeOperator();
                var token = new TokenPath()
                {
                    Parameter = rootParameter,
                    Properties = propertyPath,
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
                var token = new TokenPath()
                {
                    Parameter = rootParameter,
                    Properties = propertyPath,
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
                                //   where colName in (select 1 where 0=1)
                                //
                                // this will cause no results to return
                                var emptySelect = new Select();
                                emptySelect.SelectClause = new SelectClause().SetArgs(new Literal() { Value = "1" });
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

                var token = new TokenPath()
                {
                    Parameter = rootParameter,
                    Properties = propertyPath,
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

            parameterPaths.Add(new ParameterPath() { Parameter = rootParameter, Properties = propertyPath, SqlParameterName = parameterName });
            
            var logicalOperatorNode = operatorNode.SetArgs(
                columnNode, 
                new NamedParameterIdentifier()
                {
                    Name = parameterName
                });

            placeholder.SetArgs(logicalOperatorNode);

            if (comparisonSpec.IgnoreIfNull || comparisonSpec.IgnoreIfNullOrEmpty)
            {
                var token = new TokenPath()
                {
                    Parameter = rootParameter,
                    Properties = propertyPath,
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

        private AstNode BuildWhereClauseForPerspective(
            AstNode primaryTableReference, TableRelations navigationTable, string navigationTableAliasPostfix,
            List<ParameterPath> parameterPaths, ParameterInfo rootParameter,
            List<PropertyInfo> currentPropertyPaths, List<TokenPath> tokens)
        {
            var navigationTableAlias = $"{navigationTable.TargetTable.Name}{navigationTableAliasPostfix}";

            var selectStatement = new Select();
            selectStatement.SelectClause = 
                new SelectClause().SetArgs(
                    new Literal() { Value = "1" });
            selectStatement.FromClause = new FromClause();
            selectStatement.FromClause.SetArgs(
                new Alias() { Label = navigationTableAlias }.SetArgs(
                    new TableIdentifier().SetArgs(
                        new RelationalTable() { Label = navigationTable.TargetTable.Name })));

            selectStatement.WhereClause = new WhereClause().SetArgs(
                new AndOperator().SetArgs(
                    
                        // match foreign keys to parent
                        navigationTable.ForeignKeyToParent.KeyPairs.Select(kp =>
                            {
                                IColumnDefinition primaryTableColumn = null;
                                IColumnDefinition navigationTableColumn = null;
                                if (TableEqualityComparer.Default.Equals(kp.ForeignTableColumn.Table,
                                    navigationTable.TargetTable))
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
                                    new TableIdentifier().SetArgs(new Alias() {Label = navigationTableAlias}, new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = navigationTableColumn.Name })),
                                    new TableIdentifier().SetArgs(primaryTableReference, new ColumnIdentifier().SetArgs(new RelationalColumn() { Label = primaryTableColumn.Name })));
                            }).ToList()
                        .Concat(
                            navigationTable.ProjectedColumns.Select(c =>
                            {
                                var sqlParameterName = $"{navigationTableAlias}{c.Name}";
                                var currentPropertyPath = currentPropertyPaths.ToList();
                                if(c.Property != null)
                                    currentPropertyPath.Add(c.Property);
                                return BuildComparisonNode(
                                    new ColumnIdentifier().SetArgs(new Alias() {Label = navigationTableAlias},
                                        new ColumnIdentifier().SetArgs(new RelationalColumn() {Label = c.Name})),
                                    sqlParameterName, c.Property?.PropertyType ?? rootParameter.ParameterType, rootParameter, currentPropertyPath, parameterPaths, tokens);
                            }).ToList()
                        )
                        .Concat(
                            navigationTable.NavigationTables.Select((nt , i) =>
                            {
                                if(nt.ParentColumnField != null)
                                    currentPropertyPaths.Add(nt.ParentColumnField.Property);
                                return BuildWhereClauseForPerspective(new Alias() { Label = navigationTableAlias }, nt,
                                        $"{navigationTableAliasPostfix}{i}", parameterPaths, rootParameter, currentPropertyPaths, tokens);
                            }).ToList()
                        )       
                    ));

            return new AndOperator().SetArgs(
                new Exists().SetArgs(selectStatement));
        }

        private WhereClause  BuildWhereClauseForJoinRelations(List<DetectedParameter> parameters, TableRelations fromClauseRelations, List<ParameterPath> parameterPaths)
        {
            return new WhereClause().SetArgs(new AndOperator().SetArgs(parameters.SelectMany(
                parameter =>
                {
                    // if this is a primitive or built in .NET type, just set it equal
                    // to the expected value
                    if (!this.databaseResolver.IsTableOrTableProjection(parameter.Type))
                    {
                        var parameterName = parameter.Name;
                        var column = GetColumnForParameterName(fromClauseRelations, parameterName);
                        parameterPaths.Add(new ParameterPath()
                        {
                            Parameter = parameter.ParameterInfo,
                            SqlParameterName = parameterName
                        });

                        return new EqualsOperator().SetArgs(
                            new ColumnIdentifier()
                            {
                                Args = new AstNode[]
                                {
                                    new RelationalTable()
                                    {
                                        Label = column.Table.Name
                                    },
                                    new RelationalColumn()
                                    {
                                        Label = column.Name
                                    }
                                }
                            }, new NamedParameterIdentifier()
                            {
                                Name = parameterName
                            }).AsEnumerable();
                    }
                    else
                        // this is referencing a foreign table, reference the table
                    {
                        return BuildWhereOperationsForJoinTables(this.databaseResolver.BuildTableRelationsFromType(parameter.Type, ColumnFilters.WhereClause), parameterPaths, parameter.ParameterInfo, new List<PropertyInfo>());
                    }
                            
                })
            ));
        }

        private class OrderBySpec
        {
            private readonly Func<string, string> resolveTableNameFunc;

            public OrderBySpec(Func<string, string> resolveTableNameFunc)
            {
                this.resolveTableNameFunc = resolveTableNameFunc;
            }

            public string TableName { get; set; }
            public string ColumnName { get; set; }
            public bool IsDynamic { get; set; }
            public bool IsCollection { get; set; }
            public ParameterPath ParameterPath { get; set; }

            public string ResolveTableAlias(string tableName)
            {
                return resolveTableNameFunc(tableName);
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
                        tokens.Add(new TokenPath()
                        {
                            Parameter = p.ParameterPath.Parameter,
                            Properties = p.ParameterPath.Properties,
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

        private IEnumerable<OrderByIdentifier> BuildDynamicOrderByIdentifier(OrderByClause orderByClause,
             List<TokenPath> tokens, OrderBySpec p)
        {
            var tokenName = $"{p.ParameterPath.GenerateSuggestedSqlIdentifierName()}_OrderBy";

            List<OrderByIdentifier> orderByClauses = new List<OrderByIdentifier>();
            tokens.Add(new TokenPath()
            {
                Parameter = p.ParameterPath.Parameter,
                Properties = p.ParameterPath.Properties,
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
                        var tableIdentifier = this.databaseConfiguration.Tables.FindByName(orderBy.Table);
                        if (tableIdentifier == null)
                        {
                            throw new InvalidIdentifierException($"Unable to identify matching database table for order by parameter {p.ParameterPath.GenerateClassQualifiedName()} with specified table name \"{orderBy.Table}\". Table {orderBy.Table} could not be found.");
                        }

                        if (tableIdentifier.Columns.FindByName(orderBy.Column) == null)
                        {
                            throw new InvalidIdentifierException($"Unable to identify matching database column for order by parameter {p.ParameterPath.GenerateClassQualifiedName()} with specified column name \"{orderBy.Column}\". Column {orderBy.Column} does not exist in table {tableIdentifier.Name}.");
                        }

                        var orderByNode = new OrderByIdentifier()
                        {
                            Direction = $"{{{tokenName}}}"
                        };
                        var directionString = orderBy.Direction == OrderByDirection.Ascending ? "asc" : "desc";
                        orderByNode.Direction = directionString;

                        return orderByNode.SetArgs(
                            new ColumnIdentifier().SetArgs(
                                (AstNode) new RelationalTable() {Label = p.ResolveTableAlias(orderBy.Table) },
                                new RelationalColumn() {Label = orderBy.Column}
                            ));
                    }).ToList();

                    orderByClause.SetArgs(orderByIdentifiers);

                    return new Dictionary<string, object>();
                }
            });
            return new List<OrderByIdentifier>();
        }

        private FromClauseNode BuildFromClause(TableRelations tableRelations, IEnumerable<ParameterInfo> functionParameters)
        {
            var fromClauseNode = new FromClauseNode();
            var targetTable = tableRelations.TargetTable;
            var tableIdentifier = new TableIdentifier().SetArgs(
                (tableRelations.Alias == targetTable.Name ? (AstNode) new Placeholder() : new Alias() { Label = tableRelations.Alias }).SetArgs(
                (targetTable.ObjectType == DatabaseObjectType.Table || targetTable.ObjectType == DatabaseObjectType.View)
                ? (AstNode) new RelationalTable()
                {
                    Label = targetTable.Name
                }
                : new Function()
                {
                    Name = targetTable.Name
                }.SetArgs(functionParameters.Select(p => new NamedParameterIdentifier() { Name = p.Name }))));

            var leftOuterJoins = BuildJoins(tableRelations);

            return fromClauseNode.SetArgs(tableIdentifier.AsEnumerable<AstNode>().Concat(leftOuterJoins));
        }

        private IEnumerable<LeftOuterJoin> BuildJoins(TableRelations tableRelations)
        {
            var references = tableRelations.NavigationTables;
            var leftOuterJoins = references.SelectMany(navigationTableRelations =>
            {
                var foreignKey = navigationTableRelations.ForeignKeyToParent;
                return BuildLeftOuterJoin(foreignKey, navigationTableRelations.TargetTable, navigationTableRelations, tableRelations, navigationTableRelations).AsEnumerable();
                
            });
            return leftOuterJoins;
        }

        private LeftOuterJoin BuildLeftOuterJoin(IForeignKeyDefinition foreignKey,
            ITableDefinition navigationTable, ITableHierarchyAlias primaryTableAlias, ITableHierarchyAlias foreignTableAlias, TableRelations navigationTableRelations = null)
        {
            var tableHierarchyAliases = new List<ITableHierarchyAlias>();
            tableHierarchyAliases.Add(primaryTableAlias);
            tableHierarchyAliases.Add(foreignTableAlias);

            var leftOuterJoin = new LeftOuterJoin().SetArgs(
                new AndOperator().SetArgs(
                foreignKey.KeyPairs.Select(kp =>
                            new EqualsOperator().SetArgs(
                                new ColumnIdentifier().SetArgs(new RelationalTable() {Label = tableHierarchyAliases.Single(s => s.TableName == kp.ForeignTableColumn.Table.Name).Alias },
                                    new RelationalColumn() {Label = kp.ForeignTableColumn.Name}),
                                new ColumnIdentifier().SetArgs(new RelationalTable() {Label = tableHierarchyAliases.Single(s => s.TableName == kp.PrimaryTableColumn.Table.Name).Alias },
                                    new RelationalColumn() {Label = kp.PrimaryTableColumn.Name})))).AsEnumerable().Cast<AstNode>()
                    .Concat(navigationTableRelations != null ? BuildJoins(navigationTableRelations) : new LeftOuterJoin[0]));
            var rightTableAlias = tableHierarchyAliases.Single(s => s.TableName == navigationTable.Name).Alias;
            leftOuterJoin.RightNode = (navigationTable.Name == rightTableAlias ? (AstNode) new Placeholder() : new Alias() { Label = rightTableAlias }).SetArgs(new TableIdentifier().SetArgs(new RelationalTable() { Label = navigationTable.Name }));
            return leftOuterJoin;
        }

        private IEnumerable<AstNode> BuildWhereOperationsForJoinTables(TableRelations navigationTableRelations,
            List<ParameterPath> parameterPaths, ParameterInfo rootParameter,
            List<PropertyInfo> currentPropertyPaths)
        {
            var targetTable = navigationTableRelations.TargetTable;
            var operations = navigationTableRelations.ProjectedColumns.Select(column =>
                {
                    var sqlParameterName = $"{targetTable.Name}{column.Name}";
                    var currentPropertyPath = currentPropertyPaths.ToList();
                    currentPropertyPath.Add(column.Property);
                    parameterPaths.Add(new ParameterPath() { Parameter = rootParameter, Properties = currentPropertyPath, SqlParameterName = sqlParameterName });
                    return new AndOperator().SetArgs(
                        new EqualsOperator().SetArgs(
                            new ColumnIdentifier().SetArgs(new RelationalTable() {Label = targetTable.Name},
                                new RelationalColumn() {Label = column.Name}),
                            new ColumnIdentifier().SetArgs(new NamedParameterIdentifier()
                                {Name = sqlParameterName})));
                }).Cast<AstNode>()
                    .Concat(navigationTableRelations.NavigationTables != null ? navigationTableRelations.NavigationTables.SelectMany(navigationTableRelations1 =>
                    {
                        currentPropertyPaths.Add(navigationTableRelations1.ParentColumnField.Property);
                        return BuildWhereOperationsForJoinTables(navigationTableRelations1, parameterPaths, rootParameter,
                                currentPropertyPaths);
                    }) : new LeftOuterJoin[0]);
            return operations;
        }

        private IColumnDefinition GetColumnForParameterName(TableRelations tableRelations, string parameterName)
        {
            var matchingTargetTableColumn = tableRelations.TargetTable.Columns.FirstOrDefault(c => c.Name.Equals(parameterName, StringComparison.InvariantCultureIgnoreCase));
            if (matchingTargetTableColumn == null)
            {
                return tableRelations.NavigationTables.Select(t => GetColumnForParameterName(t, parameterName)).FirstOrDefault();
            }
            return matchingTargetTableColumn;
        }

        private IColumnDefinition GetColumnForParameterName(ITableDefinition table, string parameterName)
        {
            var matchingTargetTableColumn = table.Columns.FirstOrDefault(c => c.Name.Equals(parameterName, StringComparison.InvariantCultureIgnoreCase));
            
            return matchingTargetTableColumn;
        }

        private IEnumerable<DetectedParameter> DetectParameters(IEnumerable<ParameterInfo> whereClauseParameters, MethodInfo methodInfo)
        {
            return whereClauseParameters.Select(p => new DetectedParameter(this.databaseResolver.GetColumnName(p), p.ParameterType, p));
        }

        private bool TryDetectTargetFromOuterClass(Type columnOutputType, ref Type detectedType)
        {
            return this.databaseResolver.TryDetectTargetFromOuterClass(columnOutputType, ref detectedType);
        }

        private bool TryDetectTargetFromIRepositoryInterfaceArg(MethodInfo methodInfo, Type columnOutputType, ref Type fromTableType)
        {
            var irepositoryType = methodInfo.DeclaringType.GetInterfaces().FirstOrDefault(iface =>
                iface.IsGenericType && iface.GetGenericTypeDefinition().IsAssignableFrom(typeof(IRepository<>)));
            if (irepositoryType != null)
            {
                fromTableType = irepositoryType.GetGenericArguments().First();
                return true;
            }

            return false;
        }

        public AstNode AstFor<T>(Expression<Func<T>> args)
        {
            throw new NotImplementedException();
        }

        private enum StatementType
        {
            Select,
            Insert,
            Update, // not yet supported
            Delete
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
        public string SqlParameterName { get; set; }
        public ParameterInfo Parameter { get; set; }
        public IEnumerable<PropertyInfo> Properties { get; set; }
        
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
            return $"{Parameter.Name}{(Properties != null ? "." + string.Join(".", Properties.Select(p => p.Name)) : null)}";
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
        public string SqlParameterName { get; set; }
        public ParameterInfo Parameter { get; set; }
        public IEnumerable<PropertyInfo> Properties { get; set; }
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
            this.ForeignTableColumnWithAlias = new ColumnAliasColumnDefinition(this.ForeignTableColumn, null);
            this.PrimaryTableColumnWithAlias = new ColumnAliasColumnDefinition(this.PrimaryTableColumn, null);
        }

        public ColumnAliasColumnDefinition ForeignTableColumnWithAlias { get; }
        public ColumnAliasColumnDefinition PrimaryTableColumnWithAlias { get; }
    }

    public class ColumnAliasColumnDefinition : IColumnDefinition
    {
        private readonly IColumnDefinition columnDefinition;
        public string Name => columnDefinition.Name;
        public string DataTypeDeclaration => columnDefinition.DataTypeDeclaration;

        public ITableDefinition Table => columnDefinition.Table;

        public ITableHierarchyAlias TableAlias { get; }

        public ColumnAliasColumnDefinition(IColumnDefinition columnDefinition, ITableHierarchyAlias tableAlias)
        {
            TableAlias = tableAlias;
            this.columnDefinition = columnDefinition;
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

    internal class DetectedParameter
    {
        public string Name { get; }
        public Type Type { get; }
        public ParameterInfo ParameterInfo { get; }
        public TableRelations TableRelations { get; set; }

        public DetectedParameter(string name, Type type, ParameterInfo parameterInfo)
        {
            Name = name;
            Type = type;
            ParameterInfo = parameterInfo;
        }
    }
}
