using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using SigQL.Exceptions;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Sql;
using SigQL.Types;
using SigQL.Types.Attributes;

namespace SigQL
{
    internal partial class DatabaseResolver
    {
        private readonly IDatabaseConfiguration databaseConfiguration;

        public DatabaseResolver(IDatabaseConfiguration databaseConfiguration)
        {
            this.databaseConfiguration = databaseConfiguration;
        }

        public IEnumerable<ColumnDefinitionWithPropertyPath> ResolveColumnsForSelectStatement(
            TableRelations tableRelations, 
            IEnumerable<ColumnAliasForeignKeyDefinition> allForeignKeys,
            ConcurrentDictionary<string, ITableKeyDefinition> tableKeyDefinitions)
        {
            ITableDefinition table = null;
            table = tableRelations.TargetTable;
            //if (tableRelations.ProjectionType != null)
            //{
                //var classProperties = tableRelations.ProjectedColumns;

            var columns = tableRelations.ProjectedColumns.SelectMany(p =>
                {
                    //var currentPaths = propertyPath.ToList();
                    //currentPaths.Add(p.Name);
                    var targetColumn = table.Columns.FindByName(p.Name);
                    var argument = p.Arguments.GetArguments(TableRelationsColumnSource.ReturnType).First();
                    if (targetColumn == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for property {argument.FullyQualifiedName()}. Column {p.Name} does not exist in table {table.Name}.");
                    }

                    return new ColumnDefinitionWithPropertyPath()
                    {
                        ColumnDefinition = new ColumnAliasColumnDefinition(targetColumn, tableRelations),
                        PropertyPath = new PropertyPath() { PropertyPaths = argument.FindPropertiesFromRoot().Select(arg => arg.Name).ToList() }
                    }.AsEnumerable();
                }).ToList();

                columns.AddRange(tableRelations.NavigationTables.SelectMany(p =>
                {
                    return ResolveColumnsForSelectStatement(p, allForeignKeys, tableKeyDefinitions);
                }));

            //var columns = classProperties.SelectMany(p =>
            //    {
            //        var currentPaths = propertyPath.ToList();
            //        currentPaths.Add(p.Name);
            //        var propertyType = p.Type;
            //        if (!IsClrOnly(p))
            //        {
            //            if (IsColumnType(propertyType))
            //            {
            //                var targetColumn = table.Columns.FindByName(p.Name);
            //                if (targetColumn == null)
            //                {
            //                    throw new InvalidIdentifierException(
            //                        $"Unable to identify matching database column for property {p.FullyQualifiedName()}. Column {p.Name} does not exist in table {table.Name}.");
            //                }
            //                return new ColumnDefinitionWithPropertyPath()
            //                {
            //                    ColumnDefinition = new ColumnAliasColumnDefinition(targetColumn, tableRelations),
            //                    PropertyPath = new PropertyPath() { PropertyPaths = currentPaths }
            //                }.AsEnumerable();
            //            }
            //            else
            //            if (IsTableOrTableProjection(propertyType))
            //            {

            //                var navigationTableRelations = tableRelations.NavigationTables.SingleOrDefault(t => t.Argument == p);
            //                if (navigationTableRelations != null)
            //                {
            //                    return ResolveColumnsForSelectStatement(navigationTableRelations, currentPaths, allForeignKeys, tableKeyDefinitions);
            //                }
            //            }
            //            else
            //            {
            //                throw new InvalidIdentifierException($"Unable to identify matching database table for property {p.FullyQualifiedName()} of type {propertyType}. Table {GetExpectedTableNameForType(UnwrapCollectionTargetType(propertyType))} does not exist.");
            //            }
            //        }

            //        return new ColumnDefinitionWithPropertyPath[0];
            //    }).Where(c => c != null).ToList();

            var currentPaths = tableRelations.Argument.FindPropertiesFromRoot().Select(p => p.Name).ToList();
            

                if ((table.PrimaryKey?.Columns.Any()).GetValueOrDefault(false))
                {
                    var primaryColumns = table.PrimaryKey.Columns
                        .Select(c =>
                        {
                            return new ColumnDefinitionWithPropertyPath()
                            {
                                ColumnDefinition = new ColumnAliasColumnDefinition(c, tableRelations),
                                PropertyPath = new PropertyPath() {PropertyPaths = currentPaths.AppendOne(c.Name).ToList()}
                            };
                        }).ToList();

                    foreach (var primaryColumn in primaryColumns)
                    {
                        tableKeyDefinitions[string.Join(".", currentPaths.AppendOne(primaryColumn.ColumnDefinition.Name))] = table.PrimaryKey;
                    }
                

                    primaryColumns.AddRange(columns);
                    columns = primaryColumns.ToList();
                }
                
                columns = columns.GroupBy(c => c.Alias, c => c).Select(c => c.First()).ToList();

                return columns;
            //}
            //else
            //{
            //    return ResolveColumnsForSelectStatement(tableRelations.NavigationTables.Single(), propertyPath.ToList(), allForeignKeys, tableKeyDefinitions);
            //}
        }

        private static bool IsColumnType(Type propertyType)
        {
            return propertyType.Namespace == "System" || propertyType.IsEnum;
        }

        public ITableDefinition GetTable(Type t)
        {
            return this.databaseConfiguration.Tables.FindByName(t.Name);
        }

        public ITableDefinition DetectTable(Type t)
        {
            Type detectedType = null;
            if (this.TryDetectTargetTable(t, ref detectedType))
            {
                return this.databaseConfiguration.Tables.FindByName(detectedType.Name);
            }
            
            throw new InvalidIdentifierException($"Unable to identify matching database table for type {GetParentTableClassQualifiedNameForType(t)}. Table {GetExpectedTableNameForType(t)} does not exist.");
        }

        private static string GetExpectedTableNameForType(Type t)
        {
            return ((t.DeclaringType?.IsClass).GetValueOrDefault(false) ? t.DeclaringType.Name : t.Name);
        }

        private static string GetParentTableClassQualifiedNameForType(Type t)
        {
            return ((t.DeclaringType?.IsClass).GetValueOrDefault(false) ? $"{t.DeclaringType.Name}.{t.Name}" : t.Name);
        }

        public bool TryDetectTargetFromOuterClass(Type columnOutputType, ref Type detectedType)
        {
            if ((columnOutputType.DeclaringType?.IsClass).GetValueOrDefault(false) &&
                this.databaseConfiguration.Tables.FindByName(columnOutputType.DeclaringType.Name) != null)
            {
                detectedType = columnOutputType.DeclaringType;
                return true;
            }

            return false;
        }

        public bool TryDetectTargetTable(Type columnOutputType, ref Type detectedType)
        {
            columnOutputType = UnwrapCollectionTargetType(columnOutputType);
            if (this.databaseConfiguration.Tables.FindByName(columnOutputType.Name) != null)
            {
                detectedType = columnOutputType;
                return true;
            }

            return columnOutputType.DeclaringType != null && columnOutputType.DeclaringType.IsClass && TryDetectTargetTable(columnOutputType.DeclaringType, ref detectedType);
        }

        public bool IsTableOrTableProjection(Type columnOutputType)
        {
            Type detectedType = null;
            return TryDetectTargetTable(columnOutputType, ref detectedType);
        }

        private Type UnwrapCollectionTargetType(Type columnOutputType)
        {
            return OutputFactory.UnwrapType(columnOutputType);
        }

        public IForeignKeyDefinition FindPrimaryForeignKeyMatchForTables(ITableDefinition tableDefinition, ITableDefinition navigationTable)
        {
            return tableDefinition.ForeignKeyCollection.FindPrimaryMatchForTable(navigationTable.Name) ?? navigationTable.ForeignKeyCollection.FindPrimaryMatchForTable(tableDefinition.Name);
        }

        public IEnumerable<IForeignKeyDefinition> FindManyToManyForeignKeyMatchesForTables(ITableDefinition primaryTable, ITableDefinition navigationTable)
        {
            var tablesReferencingPrimary = this.databaseConfiguration.Tables.Where(t => t.ForeignKeyCollection.FindForTable(primaryTable.Name).Any());
            var tablesReferencingNavigation = this.databaseConfiguration.Tables.Where(t => t.ForeignKeyCollection.FindForTable(navigationTable.Name).Any());

            var tablesReferencingBoth = tablesReferencingPrimary.Where(t => tablesReferencingNavigation.Any(n => TableEqualityComparer.Default.Equals(t, n)));

            // try to find a table that has exactly one reference to each of the target tables
            // with the fewest number of columns. this is likely the many-to-many table
            var manyToManyTable = 
                tablesReferencingBoth
                    .OrderBy(t => t.Columns.Count())
                    .FirstOrDefault(t => t.ForeignKeyCollection.FindForTable(primaryTable.Name).Count() == 1 &&
                                t.ForeignKeyCollection.FindForTable(navigationTable.Name).Count() == 1);

            var foreignKeys = new[]
            {
                manyToManyTable?.ForeignKeyCollection.FindForTable(primaryTable.Name).FirstOrDefault(),
                manyToManyTable?.ForeignKeyCollection.FindForTable(navigationTable.Name).FirstOrDefault()
            };

            return manyToManyTable != null ? foreignKeys : null;
        }

        //public TableRelations BuildTableRelationsFromType(Type projectionType, TableRelationsFilter filter, TypeHierarchyNode node = null, ITableDefinition parentTable = null, ColumnField parentColumnField = null, Type parentType = null)
        //{
        //    node ??= new TypeHierarchyNode(UnwrapCollectionTargetType(projectionType));
        //    return BuildTableRelations(new TableFromType() {
        //            Type = projectionType,
        //            ColumnFields = this.ToArgumentContainer(projectionType).Arguments.Where(p => !IsClrOnly(p)).Select(p => new ColumnField()
        //            {
        //                Name = this.GetColumnName(p),
        //                Type = p.Type,
        //                Argument = p
        //            }).ToList()
        //        }, node, parentTable, parentColumnField, parentType, columnFilter);
        //}

        internal bool IsClrOnly(IArgument argument)
        {
            return argument.GetCustomAttribute<ClrOnlyAttribute>() != null;
        }
        internal bool IsClrOnly(ParameterInfo parameter)
        {
            return parameter.GetCustomAttribute<ClrOnlyAttribute>() != null;
        }

        //public TableRelations BuildTableRelationsWithMethodParams(ITableDefinition tableDefinition,
        //    IEnumerable<IArgument> parameters, TableRelationsFilter filter,
        //    TypeHierarchyNode node,
        //    ITableDefinition parentTable = null, ColumnField parentColumnField = null, Type parentType = null)
        //{
        //    return BuildTableRelations(tableDefinition, parameters.Select(p => new ColumnField()
        //    {
        //        Name = this.GetColumnName(p),
        //        Type = p.Type,
        //        Argument = p
        //    }).ToList(), null, parentTable, parentColumnField, parentType, node, filter);
        //}

        //public TableRelations BuildTableRelationsWithMethodParams(Type projectionType,
        //    IEnumerable<ParameterInfo> parameters, Func<ColumnField, bool, bool> columnSelector)
        //{
        //    var tableDefinition = this.DetectTable(projectionType);
        //    var node = new TypeHierarchyNode(this.UnwrapCollectionTargetType(projectionType));

        //    BuildTableRelations(ToArgumentContainer(projectionType))
        //    return BuildTableRelations(tableDefinition, parameters.Select(p => new ColumnField()
        //    {
        //        Name = this.GetColumnName(p),
        //        Type = p.ParameterType,
        //        Parameter = p
        //    }).ToList(), projectionType, null, null, null, node, columnSelector);
        //}

        public string GetColumnName(IArgument argument)
        {
            var columnSpec = this.GetColumnSpec(argument);
            return columnSpec?.ColumnName ?? argument.Name;
        }

        //private TableRelations BuildTableRelations(TableFromType tableType, TypeHierarchyNode node, ITableDefinition parentTable, ColumnField parentColumnField, Type parentType, TableRelationsFilter filter)
        //{
        //    var tableDefinition = this.DetectTable(tableType.Type);
        //    return BuildTableRelations(tableDefinition, tableType.ColumnFields, tableType.Type, parentTable,
        //        parentColumnField, parentType, node, filter);
        //}
        
        private void BuildManyToManyTableRelations(IForeignKeyDefinition foreignKeyDefinition, TableRelations tableRelations, TableRelations navigationTableRelations)
        {
            var tableDefinition = foreignKeyDefinition.KeyPairs.First().ForeignTableColumn.Table;
            var navigationToManyToManyForeignKey = this.FindPrimaryForeignKeyMatchForTables(navigationTableRelations.TargetTable, tableDefinition);
            navigationTableRelations.ForeignKeyToParent = navigationToManyToManyForeignKey;

            var navigationTables = tableRelations.NavigationTables.ToList();
            navigationTables.Remove(navigationTableRelations);
            

            var manyToManyTableRelations = new TableRelations()
            {
                //ProjectionType = null,
                //ParentColumnField = parentColumnField,
                Argument = new TypeArgument(typeof(void), this),
                TargetTable = tableDefinition,
                NavigationTables = new []{ navigationTableRelations },
                ProjectedColumns = new List<TableRelationColumnDefinition>(),
                ForeignKeyToParent = foreignKeyDefinition
            };
            navigationTables.Add(manyToManyTableRelations);

            tableRelations.NavigationTables = navigationTables;
        }

        //public TableRelations BuildTableRelations(Type projectionType, IEnumerable<IArgument> methodArguments, TableRelationsFilter filter)
        //{
        //    var node = new TypeHierarchyNode(this.UnwrapCollectionTargetType(projectionType));
        //    var filteredMethodParameters = methodArguments.ToList();
        //    // method parameters can use a filter that represents the projection table. We don't want
        //    // to include the output table twice, so set that aside and merge it at the end instead
        //    var projectionTypeTableFilterParameters = methodArguments.Where(p =>
        //        IsTableOrTableProjection(p.Type) && TableEqualityComparer.Default.Equals(DetectTable(p.Type), DetectTable(projectionType))).ToList();
        //    if (projectionTypeTableFilterParameters.Any())
        //    {
        //        filteredMethodParameters.RemoveAll(p => projectionTypeTableFilterParameters.Any(p2 => p == p2));
        //    }
        //    var tableType = new TableFromType()
        //    {
        //        Type = projectionType,
        //        ColumnFields = filteredMethodParameters.Select(p => new ColumnField()
        //        {
        //            Name = this.GetColumnName(p),
        //            Type = p.Type,
        //            Argument = p
        //        }).ToList()
        //    };
        //    var tableTypeWithProjections = new TableFromType()
        //    {
        //        Type = projectionType,
        //        ColumnFields = projectionTypeTableFilterParameters.Select(p => new ColumnField()
        //        {
        //            Name = this.GetColumnName(p),
        //            Type = p.Type,
        //            Argument = p
        //        }).ToList()
        //    };
            


        //    var projectionTypeFilterTableRelations = tableTypeWithProjections.ColumnFields.Select(p => this.BuildTableRelations(p.Type, columnFilter, node, parentColumnField: p)).ToList();
        //    projectionTypeFilterTableRelations.Add(BuildTableRelations(tableType, node, null, null, null, filter));
        //    return MergeTableRelations(projectionTypeFilterTableRelations.ToArray());
        //}

        private IEnumerable<DetectedParameter> BuildViaRelationParameters(ITableDefinition primaryTableDefinition,
            IEnumerable<IArgument> arguments)
        {
            var viaRelationArgs = FindMatchingArguments(arguments, a => a.GetCustomAttribute<ViaRelationAttribute>() != null);
            var detectedParameters = viaRelationArgs.Select(a =>
                new DetectedParameter(a.Name, a.Type, a.PathToRoot().Reverse().First().GetParameterInfo())
                {
                    TableRelations = BuildTableRelationsFromViaParameter(a, a.GetCustomAttribute<ViaRelationAttribute>().Path)
                }).ToList();

            return detectedParameters;
        }
        
        public IEnumerable<IArgument> FindMatchingArguments(IEnumerable<IArgument> arguments,
            Func<IArgument, bool> matchCondition)
        {
            var matches = new List<IArgument>();
            var matchingArguments = arguments.Where(a => matchCondition(a)).ToList();
            matches.AddRange(matchingArguments);
            matches.AddRange(arguments.SelectMany(a => FindMatchingArguments(a.ClassProperties, matchCondition)).ToList());

            return matches;
        }

        //public IEnumerable<DetectedParameter> BuildDetectedParameters(Type projectionType, IEnumerable<IArgument> parameters)
        //{
        //    var columnFilter = ColumnFilters.WhereClause;
        //    var allDetectedParameters = new List<DetectedParameter>();
        //    var primaryTableDefinition = this.GetTable(projectionType);
        //    var viaRelationParameters = this.BuildViaRelationParameters(primaryTableDefinition, parameters);
        //    foreach (var parameter in parameters.Where(IsWhereClauseParameter))
        //    {
        //        var hasViaRelationAttribute =
        //            parameter.GetCustomAttribute<ViaRelationAttribute>() != null;
        //        if (hasViaRelationAttribute)
        //        {
        //        }
        //        else if (parameter.GetCustomAttribute<ParameterAttribute>() != null)
        //        {
        //            // skip

        //            //var detectedParameter = new DetectedParameter(parameter.Name, parameter.ParameterType, parameter)
        //            //{
        //            //    TableRelations = null
        //            //};
        //            //allDetectedParameters.Add(
        //            //    detectedParameter);
        //        }
        //        else
        //        {
        //            var node = new TypeHierarchyNode(UnwrapCollectionTargetType(parameter.Type));
        //            var detectedParameter = new DetectedParameter(GetColumnName(parameter), parameter.Type, parameter.RootToPath().First().GetParameterInfo())
        //            {
        //                TableRelations =
        //                    IsTableOrTableProjection(parameter.Type) && !TableEqualityComparer.Default.Equals(DetectTable(parameter.Type), primaryTableDefinition)
        //                        ? BuildTableRelations(this.ToArgumentContainer(projectionType), TableRelationsColumnSource.Parameters) 
        //                        : IsTableOrTableProjection(parameter.Type) ? BuildTableRelations(this.ToArgumentContainer(parameter.Type), TableRelationsColumnSource.Parameters)
        //                            : null
        //            };

        //            if (detectedParameter.TableRelations == null && primaryTableDefinition.Columns.FindByName(detectedParameter.Name) == null)
        //            {
        //                throw new InvalidIdentifierException(
        //                    $"Unable to identify matching database column for parameter {parameter.Name}. Column {detectedParameter.Name} does not exist in table {primaryTableDefinition.Name}.");
        //            }

        //            allDetectedParameters.Add(
        //                detectedParameter);
        //        }
        //    }

        //    allDetectedParameters.AddRange(viaRelationParameters);
           
        //    return allDetectedParameters;
        //}
        
        public static bool IsWhereClauseParameter(IArgument p)
        {
            return
                ((p.GetCustomAttribute<OffsetAttribute>() == null) &&
                 (p.GetCustomAttribute<FetchAttribute>() == null) &&
                 (p.Type != typeof(OrderByDirection)) &&
                 (!p.Type.IsAssignableFrom(typeof(IEnumerable<IOrderBy>)))) &&
                 (!p.Type.IsAssignableFrom(typeof(IOrderBy))) ||
                (p.Type.Namespace == typeof(Like).Namespace && p.Type.GetInterfaces().Any(i => i == typeof(IWhereClauseFilterParameter)));
        }

        private static bool HasLikeAttribute(ParameterInfo p)
        {
            return p.GetCustomAttribute<StartsWithAttribute>() == null ||
                   p.GetCustomAttribute<ContainsAttribute>() == null ||
                   p.GetCustomAttribute<EndsWithAttribute>() == null;
        }

        internal TableRelations BuildTableRelationsFromViaParameter(IArgument argument,
            string viaRelationPath)
        {
            var relations =
                viaRelationPath.
                    Split(new [] { "->" }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

            TableRelations previousRelation = null;
            var relationsReversed = relations.ToList();
            relationsReversed.Reverse();
            var allTableRelations = new List<TableRelations>();
            foreach (var relation in relationsReversed)
            {
                var tableName = relation.Split('.').First();
                var columnName = relation.Split('.').Skip(1).FirstOrDefault();

                var targetTable = this.databaseConfiguration.Tables.FindByName(tableName);
                if (targetTable == null)
                {
                    throw new InvalidIdentifierException(
                        $"Unable to identify matching database table for parameter {argument.Name} with ViaRelation[\"{viaRelationPath}\"]. Table {tableName} does not exist.");
                }
                //if(!allTableRelations.Any() && !TableEqualityComparer.Default.Equals(targetTable, primaryTable)
                //{

                //})
                var tableRelations = new TableRelations()
                {
                    TargetTable = targetTable,
                    ProjectedColumns = new List<TableRelationColumnDefinition>(),
                    NavigationTables = new List<TableRelations>()
                };

                if (!string.IsNullOrEmpty(columnName))
                {
                    var column = targetTable.Columns.FindByName(columnName);
                    if (column == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for parameter {argument.Name} with ViaRelation[\"{viaRelationPath}\"]. Column {columnName} does not exist in table {targetTable.Name}.");
                    }
                    tableRelations.ProjectedColumns = new List<TableRelationColumnDefinition>()
                    {
                        new TableRelationColumnDefinition(column, argument, TableRelationsColumnSource.Parameters)
                    };
                }

                if (previousRelation != null)
                {
                    tableRelations.NavigationTables = new List<TableRelations>()
                    {
                        previousRelation
                    };
                }

                previousRelation = tableRelations;

                allTableRelations.Add(tableRelations);
            }

            allTableRelations.Reverse();
            previousRelation = null;
            foreach (var tableRelation in allTableRelations)
            {
                if (previousRelation != null)
                {
                    tableRelation.ForeignKeyToParent =
                        FindPrimaryForeignKeyMatchForTables(tableRelation.TargetTable, previousRelation.TargetTable);
                    if(tableRelation.ForeignKeyToParent == null)
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database foreign key for parameter {argument.Name} with ViaRelation[\"{viaRelationPath}\"]. No foreign key between {previousRelation.TargetTable.Name} and {tableRelation.TargetTable.Name} could be found.");
                }
                previousRelation = tableRelation;
            }

            return allTableRelations.First();
        }

        private class TableFromType
        {
            public Type Type { get; set; }
            public IEnumerable<ColumnField> ColumnFields { get; set; }
        }

        public TableRelations MergeTableRelations(params TableRelations[] tableRelationsCollection)
        {
            return new TableRelations()
            {
                Argument = tableRelationsCollection.Where(t => t.Argument != null).Select(t => t.Argument).First(),
                //ProjectionType = tableRelationsCollection.Where(p => p.ProjectionType != null).Select(p => p.ProjectionType).FirstOrDefault(),
                TargetTable = tableRelationsCollection.First().TargetTable,
                NavigationTables = tableRelationsCollection.SelectMany(t => t.NavigationTables).GroupBy(nt => nt.TargetTable, nt => nt, TableEqualityComparer.Default).Select(nt => MergeTableRelations(nt.ToArray())).ToList(),
                ProjectedColumns = tableRelationsCollection.SelectMany(t => t.ProjectedColumns)
                    //.Join(
                    //    tableRelationsCollection.SelectMany(t => t.ProjectedColumns),
                    //    t => t.Name,
                    //    t => t.Name,
                    //    (cd1, cd2) =>
                    //    {
                    //        if (ColumnEqualityComparer.Default.Equals(cd1, cd2))
                    //        {
                    //            cd1.Source |= cd2.Source;
                    //        }
                    //        return cd1;
                    //    })
                    //.Distinct(ColumnEqualityComparer.Default)
                    //.Cast<TableRelationColumnDefinition>()
                    .ToList(),
                ForeignKeyToParent = tableRelationsCollection.Where(t => t.ForeignKeyToParent != null).Select(t => t.ForeignKeyToParent).Distinct(ForeignKeyDefinitionEqualityComparer.Default).FirstOrDefault(),
                //ParentColumnField = tableRelationsCollection.Select(p => p.ParentColumnField).FirstOrDefault(c => c != null),
                Parent = tableRelationsCollection.Select(p => p.Parent).FirstOrDefault(parent => parent != null),
            };
        }

        //public void InsertNavigationTable(TableRelations primaryTableRelations,
        //    TableRelations foreignTableRelations)
        //{
        //    var existingForeignTable = primaryTableRelations.NavigationTables.FirstOrDefault(t =>
        //        TableEqualityComparer.Default.Equals(t.TargetTable, foreignTableRelations.TargetTable));
        //    if (existingForeignTable != null)
        //    {
        //        primaryTableRelations.NavigationTables = primaryTableRelations.NavigationTables
        //            .Where(t => !TableEqualityComparer.Default.Equals(t.TargetTable, existingForeignTable.TargetTable))
        //            .Concat(new [] { this.MergeTableRelations(existingForeignTable, foreignTableRelations) }).ToList();
        //    }
        //    else
        //    {
        //        primaryTableRelations.NavigationTables = primaryTableRelations.NavigationTables                                                                    
        //            .Concat(new [] { foreignTableRelations }).ToList();
        //    }
        //}

        public class PropertyPath
        {
            public PropertyPath()
            {
                this.PropertyPaths = new List<string>();
            }

            public List<string> PropertyPaths { get; set; }
        }

        public class ColumnDefinitionWithPropertyPath
        {
            public ColumnAliasColumnDefinition ColumnDefinition { get; set; }
            public PropertyPath PropertyPath { get; set; }
            
            public string Alias => string.Join(".", PropertyPath.PropertyPaths.Select(p => p));
        }

        public IEnumerable<IForeignKeyDefinition> FindAllForeignKeys(TableRelations tableRelations)
        {
            return (new [] { tableRelations.ForeignKeyToParent }.Where(fk => fk != null).ToList()).Concat(
                tableRelations.NavigationTables.SelectMany(FindAllForeignKeys));
        }

        public ColumnSpec GetColumnSpec(IArgument argument)
        {
            var specInfo = new ColumnSpec()
            {
                ComparisonType = argument.Type
            };
            
            specInfo.IgnoreIfNull = argument.GetCustomAttribute<IgnoreIfNullAttribute>() != null;
            specInfo.IgnoreIfNullOrEmpty = argument.GetCustomAttribute<IgnoreIfNullOrEmptyAttribute>() != null;
            // pull this from ViaRelation as well, if it has a . near the end OR decorate with both
            specInfo.ColumnName = argument.GetCustomAttribute<ColumnAttribute>()?.ColumnName ??
                                  argument.GetCustomAttribute<ViaRelationAttribute>()?.Path.Split('.').Last();
            specInfo.Not = argument.GetCustomAttribute<NotAttribute>() != null;
            specInfo.GreaterThan = argument.GetCustomAttribute<GreaterThanAttribute>() != null;
            specInfo.GreaterThanOrEqual = argument.GetCustomAttribute<GreaterThanOrEqualAttribute>() != null;
            specInfo.LessThan = argument.GetCustomAttribute<LessThanAttribute>() != null;
            specInfo.LessThanOrEqual = argument.GetCustomAttribute<LessThanOrEqualAttribute>() != null;
            specInfo.StartsWith = argument.GetCustomAttribute<StartsWithAttribute>() != null;
            specInfo.Contains = argument.GetCustomAttribute<ContainsAttribute>() != null;
            specInfo.EndsWith = argument.GetCustomAttribute<EndsWithAttribute>() != null;

            return specInfo;
        }

        internal IEnumerable<IArgument> ProjectedColumnsToArguments(TableRelations tableRelations)
        {
            var tableParameterPaths = tableRelations.ProjectedColumns
                .SelectMany(p => 
                    p.Arguments.GetArguments(TableRelationsColumnSource.Parameters)).ToList();
            var navigationParameterPaths = tableRelations.NavigationTables.SelectMany(t =>
                ProjectedColumnsToArguments(t));

            return tableParameterPaths.Concat(navigationParameterPaths).ToList();
        }
        
        
    }
    
    internal class TypeHierarchyNode
    {
        private readonly IArgument argument;

        public TypeHierarchyNode(Type type) : this(type, null, null)
        {
        }

        public TypeHierarchyNode(Type type, TypeHierarchyNode parent, IArgument argument)
        {
            this.argument = argument;
            Type = type;
            this.Children = new List<TypeHierarchyNode>();
            this.Parent = parent;
        }

        public TypeHierarchyNode Parent { get; set; }
        private Type Type { get; set; }
        public List<TypeHierarchyNode> Children { get; set; }
        public int Depth => this.Parent == null ? 0 : this.Parent.Depth + 1;
        public int Ordinal => this.Parent == null ? 0 : this.Parent.Children.IndexOf(this);

        public string Path => argument?.Name;

        public string QualifiedPath => Parent == null ? Type.Name : string.Join(".", new [] { Parent.QualifiedPath, Path }.Where(p => !string.IsNullOrEmpty(p)));

        public string Position => Parent == null ? $"{Depth}_{Ordinal}" : $"{Parent.Position}_{Depth}_{Ordinal}";
        
        public bool IsDescendentOf(Type parentType)
        {
            var node = this;
            while (parentType != typeof(void) && node.Parent != null)
            {
                if (node.Parent.Type == parentType)
                {
                    return true;
                }

                node = node.Parent;
            }

            return false;
        }
    }

    public class ColumnSpec
    {
        public bool IgnoreIfNull { get; set; }
        public bool IgnoreIfNullOrEmpty { get; set; }
        public Type ComparisonType { get; set; }
        public string ColumnName { get; set; }
        public bool Not { get; set; }
        public bool GreaterThan { get; set; }
        public bool GreaterThanOrEqual { get; set; }
        public bool LessThan { get; set; }
        public bool LessThanOrEqual { get; set; }
        public bool StartsWith { get; set; }
        public bool Contains { get; set; }
        public bool EndsWith { get; set; }
        public bool IsAnyLike => StartsWith || Contains || EndsWith;
    }
}
