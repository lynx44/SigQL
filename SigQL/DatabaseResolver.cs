﻿using System;
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
    internal class DatabaseResolver
    {
        private readonly IDatabaseConfiguration databaseConfiguration;

        public DatabaseResolver(IDatabaseConfiguration databaseConfiguration)
        {
            this.databaseConfiguration = databaseConfiguration;
        }

        public IEnumerable<ColumnDefinitionWithPropertyPath> ResolveColumnsForSelectStatement(
            TableRelations tableRelations, IEnumerable<string> propertyPath,
            IEnumerable<ColumnAliasForeignKeyDefinition> allForeignKeys,
            ConcurrentDictionary<string, ITableKeyDefinition> tableKeyDefinitions)
        {
            ITableDefinition table = null;
            table = tableRelations.TargetTable;
            if (tableRelations.ProjectionType != null)
            {
            var classProperties = UnwrapCollectionTargetType(tableRelations.ProjectionType).GetProperties();
            var columns = classProperties.SelectMany(p =>
            {
                var currentPaths = propertyPath.ToList();
                currentPaths.Add(p.Name);
                var propertyType = p.PropertyType;
                if (!IsClrOnly(p))
                {
                    if (IsColumnType(propertyType))
                    {
                        var targetColumn = table.Columns.FindByName(p.Name);
                        if (targetColumn == null)
                        {
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database column for property {GetParentTableClassQualifiedNameForType(tableRelations.ProjectionType)}.{string.Join(".", currentPaths)}. Column {p.Name} does not exist in table {table.Name}.");
                        }
                        return new ColumnDefinitionWithPropertyPath()
                        {
                            ColumnDefinition = new ColumnAliasColumnDefinition(targetColumn, tableRelations),
                            PropertyPath = new PropertyPath() { PropertyPaths = currentPaths }
                        }.AsEnumerable();
                    }
                    else
                    if (IsTableOrTableProjection(propertyType))
                    {
                        var navigationTableRelations = tableRelations.NavigationTables.SingleOrDefault(t => t.ParentColumnField.Name == p.Name && t.ParentColumnField.Type == propertyType);
                        if (navigationTableRelations != null)
                        {
                            return ResolveColumnsForSelectStatement(navigationTableRelations, currentPaths, allForeignKeys, tableKeyDefinitions);
                        }
                    }
                    else
                    {
                        throw new InvalidIdentifierException($"Unable to identify matching database table for property {GetParentTableClassQualifiedNameForType(tableRelations.ProjectionType)}.{string.Join(".", currentPaths)} of type {propertyType}. Table {GetExpectedTableNameForType(UnwrapCollectionTargetType(propertyType))} does not exist.");
                    }
                }

                return new ColumnDefinitionWithPropertyPath[0];
            }).Where(c => c != null).ToList();

            tableKeyDefinitions[string.Join(".", propertyPath.ToList())] = table.PrimaryKey;

            if (table.PrimaryKey.Columns.Any())
            {
                var primaryColumns = table.PrimaryKey.Columns
                    .Select(c =>
                    {
                        var currentPaths = propertyPath.ToList();
                        currentPaths.Add(c.Name);
                        return new ColumnDefinitionWithPropertyPath()
                        {
                            ColumnDefinition = new ColumnAliasColumnDefinition(c, tableRelations),
                            PropertyPath = new PropertyPath() {PropertyPaths = currentPaths.ToList()}
                        };
                    }).ToList();
            
                primaryColumns.AddRange(columns);
                columns = primaryColumns.ToList();
            }
            
            columns = columns.GroupBy(c => c.Alias, c => c).Select(c => c.First()).ToList();

            return columns;
            }
            else
            {
                return ResolveColumnsForSelectStatement(tableRelations.NavigationTables.Single(), propertyPath.ToList(), allForeignKeys, tableKeyDefinitions);
            }
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

        public TableRelations BuildTableRelationsFromType(Type projectionType, TypeHierarchyNode node = null, ITableDefinition parentTable = null, ColumnField parentColumnField = null, Type parentType = null)
        {
            node ??= new TypeHierarchyNode(UnwrapCollectionTargetType(projectionType));
            return BuildTableRelations(new TableFromType() {
                    Type = projectionType,
                    ColumnFields = UnwrapCollectionTargetType(projectionType).GetProperties().Where(p => !IsClrOnly(p)).Select(p => new ColumnField()
                    {
                        Name = this.GetColumnName(p),
                        Type = p.PropertyType,
                        Property = p
                    }).ToList()
                }, node, parentTable, parentColumnField, parentType);
        }

        internal bool IsClrOnly(PropertyInfo propertyInfo)
        {
            return propertyInfo.GetCustomAttribute<ClrOnlyAttribute>() != null;
        }
        internal bool IsClrOnly(ParameterInfo parameterInfo)
        {
            return parameterInfo.GetCustomAttribute<ClrOnlyAttribute>() != null;
        }

        public TableRelations BuildTableRelationsWithMethodParams(ITableDefinition tableDefinition, TypeHierarchyNode node, IEnumerable<ParameterInfo> parameters, ITableDefinition parentTable = null, ColumnField parentColumnField = null, Type parentType = null)
        {
            return BuildTableRelations(tableDefinition, parameters.Select(p => new ColumnField()
            {
                Name = this.GetColumnName(p),
                Type = p.ParameterType,
                Parameter = p
            }).ToList(), null, parentTable, parentColumnField, parentType, node);
        }

        public string GetColumnName(PropertyInfo propertyInfo)
        {
            var columnSpec = this.GetColumnSpec(propertyInfo.PropertyType, propertyInfo.CustomAttributes);
            return columnSpec?.ColumnName ?? propertyInfo.Name;
        }

        public string GetColumnName(ParameterInfo parameterInfo)
        {
            var columnSpec = this.GetColumnSpec(parameterInfo.ParameterType, parameterInfo.CustomAttributes);
            return columnSpec?.ColumnName ?? parameterInfo.Name;
        }

        private TableRelations BuildTableRelations(TableFromType tableType, TypeHierarchyNode node, ITableDefinition parentTable, ColumnField parentColumnField, Type parentType)
        {
            var tableDefinition = this.DetectTable(tableType.Type);
            return BuildTableRelations(tableDefinition, tableType.ColumnFields, tableType.Type, parentTable,
                parentColumnField, parentType, node);
        }

        private TableRelations BuildTableRelations(ITableDefinition tableDefinition, IEnumerable<ColumnField> columnFields, Type tableType, ITableDefinition parentTable,
            ColumnField parentColumnField, Type parentType, TypeHierarchyNode node)
        {
             var columnDescriptions = columnFields
                .Select(p => new {Column = p, IsTable = this.IsTableOrTableProjection(p.Type)}).Where(p => !IsDecoratedNonColumn(p.Column.Property)).ToList();
             var unprocessedNavigationTables = columnDescriptions.Where(t => t.IsTable && !node.IsDescendentOf(UnwrapCollectionTargetType(t.Column.Type))).ToList();
             var typeHierarchyNodes = unprocessedNavigationTables.Select(p => 
                 new
                 {
                     ColumnDescription = p,
                     Node = new TypeHierarchyNode(UnwrapCollectionTargetType(p.Column.Type), node, p.Column.Property)
                 }).ToList();
             node.Children.AddRange(typeHierarchyNodes.Select(p => p.Node).ToList());
             var relations = unprocessedNavigationTables
                .Select(p => BuildTableRelationsFromType(p.Column.Type, typeHierarchyNodes.First(n => n.ColumnDescription == p).Node, tableDefinition, p.Column, tableType)).ToList();
            var columns = columnDescriptions.Where(t => !t.IsTable)
                .Select(d =>
                {
                    var tableColumn = tableDefinition.Columns.FindByName(d.Column.Name);
                    if (tableColumn == null)
                    {
                        if(IsColumnType(d.Column.Property.PropertyType))
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database column for property {GetParentTableClassQualifiedNameForType(tableType)}.{d.Column.Property.Name}. Column {d.Column.Name} does not exist in table {tableDefinition.Name}.");
                        else
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database table for property {GetParentTableClassQualifiedNameForType(tableType)}.{d.Column.Property.Name} of type {d.Column.Property.PropertyType}. Table {GetExpectedTableNameForType(UnwrapCollectionTargetType(d.Column.Property.PropertyType))} does not exist.");
                    }
                    return d.Column.Parameter != null
                            ? new ColumnDefinitionWithPath(tableColumn,
                                d.Column.Parameter)
                            : new ColumnDefinitionWithPath(tableColumn,
                                d.Column.Property);
                }).ToList();
            
            var tableRelations = new TableRelations()
            {
                ProjectionType = tableType,
                ParentColumnField = parentColumnField,
                TargetTable = tableDefinition,
                NavigationTables = relations,
                ProjectedColumns = columns,
                HierarchyNode = node
            };

            foreach (var navigationTable in tableRelations.NavigationTables)
            {
                navigationTable.Parent = tableRelations;
            }

            IEnumerable<IForeignKeyDefinition> foreignKeys = null;
            if (parentTable != null)
            {
                var foreignKey = this.FindPrimaryForeignKeyMatchForTables(parentTable, tableDefinition);
                if (foreignKey == null)
                {
                    foreignKeys = FindManyToManyForeignKeyMatchesForTables(parentTable, tableDefinition);

                    if (!(foreignKeys?.Any()).GetValueOrDefault(false))
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database foreign key for property {GetParentTableClassQualifiedNameForType(parentType)}.{parentColumnField.Property.Name}. No foreign key between {parentTable.Name} and {tableDefinition.Name} could be found.");
                    }

                    var oneToManyFk = foreignKeys.First(fk => TableEqualityComparer.Default.Equals(fk.PrimaryKeyTable, tableDefinition));
                    var manyToOneFk = foreignKeys.Except(new [] { oneToManyFk }).First();
                    tableRelations.ForeignKeyToParent = oneToManyFk;
                    tableRelations.ParentColumnField = null;
                    
                    var hierarchyNode = new TypeHierarchyNode(typeof(void), node, null);
                    node.Children.Add(hierarchyNode);
                    return this.BuildTableRelations(manyToOneFk, parentColumnField, new[] { tableRelations }, hierarchyNode);
                }
                else
                {
                    tableRelations.ForeignKeyToParent = foreignKey;
                }
            }
            return tableRelations;
        }

        public bool IsDecoratedNonColumn(PropertyInfo property)
        {
            return
                property.GetCustomAttribute<OffsetAttribute>() != null ||
                property.GetCustomAttribute<FetchAttribute>() != null ||
                property.GetCustomAttribute<ClrOnlyAttribute>() != null;
        }

        private TableRelations BuildTableRelations(IForeignKeyDefinition foreignKeyDefinition, ColumnField parentColumnField, IEnumerable<TableRelations> relations, TypeHierarchyNode node)
        {
            var tableDefinition = foreignKeyDefinition.KeyPairs.First().ForeignTableColumn.Table;
            
            return new TableRelations()
            {
                ProjectionType = null,
                ParentColumnField = parentColumnField,
                TargetTable = tableDefinition,
                NavigationTables = relations,
                ProjectedColumns = new List<ColumnDefinitionWithPath>(),
                ForeignKeyToParent = foreignKeyDefinition, 
                HierarchyNode = node
            };
        }

        public TableRelations BuildTableRelations(Type projectionType, TypeHierarchyNode node, IEnumerable<ParameterInfo> methodParameters)
        {
            var filteredMethodParameters = methodParameters.ToList();
            // method parameters can use a filter that represents the projection table. We don't want
            // to include the output table twice, so set that aside and merge it at the end instead
            var projectionTypeTableFilterParameters = methodParameters.Where(p =>
                IsTableOrTableProjection(p.ParameterType) && TableEqualityComparer.Default.Equals(DetectTable(p.ParameterType), DetectTable(projectionType))).ToList();
            if (projectionTypeTableFilterParameters.Any())
            {
                filteredMethodParameters.RemoveAll(p => projectionTypeTableFilterParameters.Any(p2 => p == p2));
            }
            var tableType = new TableFromType()
            {
                Type = projectionType,
                ColumnFields = filteredMethodParameters.Select(p => new ColumnField()
                {
                    Name = this.GetColumnName(p),
                    Type = p.ParameterType,
                    Parameter = p
                }).ToList()
            };


            var projectionTypeFilterTableRelations = projectionTypeTableFilterParameters.Select(p => this.BuildTableRelationsFromType(p.ParameterType, node)).ToList();
            projectionTypeFilterTableRelations.Add(BuildTableRelations(tableType, node, null, null, null));
            return MergeTableRelations(projectionTypeFilterTableRelations.ToArray());
        }

        public IEnumerable<DetectedParameter> BuildDetectedParameters(ITableDefinition primaryTableDefinition, IEnumerable<ParameterInfo> parameters)
        {
            var allDetectedParameters = new List<DetectedParameter>();
            foreach (var parameter in parameters.Where(IsWhereClauseParameter))
            {
                var hasViaRelationAttribute =
                    parameter.CustomAttributes.Any(a => a.AttributeType == typeof(ViaRelationAttribute));
                if (hasViaRelationAttribute)
                {
                    var viaRelationAttribute = parameter.GetCustomAttributes(typeof(ViaRelationAttribute)).Cast<ViaRelationAttribute>().First();
                    allDetectedParameters.Add(
                        new DetectedParameter(GetColumnName(parameter), parameter.ParameterType, parameter)
                        {
                            TableRelations =
                                BuildTableRelationsFromViaParameter(parameter, viaRelationAttribute)
                        });
                }
                else if (parameter.GetCustomAttribute<ParameterAttribute>() != null)
                {
                    // skip

                    //var detectedParameter = new DetectedParameter(parameter.Name, parameter.ParameterType, parameter)
                    //{
                    //    TableRelations = null
                    //};
                    //allDetectedParameters.Add(
                    //    detectedParameter);
                }
                else
                {
                    var node = new TypeHierarchyNode(UnwrapCollectionTargetType(parameter.ParameterType));
                    var detectedParameter = new DetectedParameter(GetColumnName(parameter), parameter.ParameterType, parameter)
                    {
                        TableRelations =
                            IsTableOrTableProjection(parameter.ParameterType) && !TableEqualityComparer.Default.Equals(DetectTable(parameter.ParameterType), primaryTableDefinition)
                                ? BuildTableRelationsWithMethodParams(primaryTableDefinition, node, parameter.AsEnumerable()) 
                                : IsTableOrTableProjection(parameter.ParameterType) ? BuildTableRelationsFromType(parameter.ParameterType, node)
                                    : null
                    };

                    if (detectedParameter.TableRelations == null && primaryTableDefinition.Columns.FindByName(detectedParameter.Name) == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for parameter {parameter.Name}. Column {detectedParameter.Name} does not exist in table {primaryTableDefinition.Name}.");
                    }

                    allDetectedParameters.Add(
                        detectedParameter);
                }
            }
           
            return allDetectedParameters;
        }
        
        public static bool IsWhereClauseParameter(ParameterInfo p)
        {
            return
                ((p.GetCustomAttribute<OffsetAttribute>() == null) &&
                 (p.GetCustomAttribute<FetchAttribute>() == null) &&
                 (p.GetCustomAttribute<OrderByAttribute>() == null) &&
                 (p.ParameterType != typeof(OrderByDirection)) &&
                 (!p.ParameterType.IsAssignableFrom(typeof(IEnumerable<IOrderBy>)))) &&
                 (!p.ParameterType.IsAssignableFrom(typeof(IOrderBy))) ||
                (p.ParameterType.Namespace == typeof(Like).Namespace && p.ParameterType.GetInterfaces().Any(i => i == typeof(IWhereClauseFilterParameter)));
        }

        private static bool HasLikeAttribute(ParameterInfo p)
        {
            return p.GetCustomAttribute<StartsWithAttribute>() == null ||
                   p.GetCustomAttribute<ContainsAttribute>() == null ||
                   p.GetCustomAttribute<EndsWithAttribute>() == null;
        }

        private TableRelations BuildTableRelationsFromViaParameter(ParameterInfo parameter,
            ViaRelationAttribute attribute)
        {
            var relations = 
                attribute.Path.
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
                        $"Unable to identify matching database table for parameter {parameter.Name} with ViaRelation[\"{attribute.Path}\"]. Table {tableName} does not exist.");
                }
                //if(!allTableRelations.Any() && !TableEqualityComparer.Default.Equals(targetTable, primaryTable)
                //{

                //})
                var tableRelations = new TableRelations()
                {
                    TargetTable = targetTable,
                    ProjectedColumns = new List<ColumnDefinitionWithPath>(),
                    NavigationTables = new List<TableRelations>()
                };

                if (!string.IsNullOrEmpty(columnName))
                {
                    var column = targetTable.Columns.FindByName(columnName);
                    if (column == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for parameter {parameter.Name} with ViaRelation[\"{attribute.Path}\"]. Column {columnName} does not exist in table {targetTable.Name}.");
                    }
                    tableRelations.ProjectedColumns = new List<ColumnDefinitionWithPath>()
                    {
                        new ColumnDefinitionWithPath(column, parameter)
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
                            $"Unable to identify matching database foreign key for parameter {parameter.Name} with ViaRelation[\"{attribute.Path}\"]. No foreign key between {previousRelation.TargetTable.Name} and {tableRelation.TargetTable.Name} could be found.");
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
                TargetTable = tableRelationsCollection.First().TargetTable,
                NavigationTables = tableRelationsCollection.SelectMany(t => t.NavigationTables).GroupBy(nt => nt.TargetTable, nt => nt, TableEqualityComparer.Default).Select(nt => MergeTableRelations(nt.ToArray())).ToList(),
                ProjectedColumns = tableRelationsCollection.SelectMany(t => t.ProjectedColumns).Distinct(ColumnEqualityComparer.Default).Cast<ColumnDefinitionWithPath>().ToList(),
                ForeignKeyToParent = tableRelationsCollection.Where(t => t.ForeignKeyToParent != null).Select(t => t.ForeignKeyToParent).Distinct(ForeignKeyDefinitionEqualityComparer.Default).FirstOrDefault(),
                ParentColumnField = tableRelationsCollection.Select(p => p.ParentColumnField).FirstOrDefault(c => c != null),
                Parent = tableRelationsCollection.Select(p => p.Parent).FirstOrDefault(parent => parent != null)
            };
        }

        public void InsertNavigationTable(TableRelations primaryTableRelations,
            TableRelations foreignTableRelations)
        {
            var existingForeignTable = primaryTableRelations.NavigationTables.FirstOrDefault(t =>
                TableEqualityComparer.Default.Equals(t.TargetTable, foreignTableRelations.TargetTable));
            if (existingForeignTable != null)
            {
                primaryTableRelations.NavigationTables = primaryTableRelations.NavigationTables
                    .Where(t => !TableEqualityComparer.Default.Equals(t.TargetTable, existingForeignTable.TargetTable))
                    .Concat(new [] { this.MergeTableRelations(existingForeignTable, foreignTableRelations) }).ToList();
            }
            else
            {
                primaryTableRelations.NavigationTables = primaryTableRelations.NavigationTables                                                                    
                    .Concat(new [] { foreignTableRelations }).ToList();
            }
        }

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

        public ColumnSpec GetColumnSpec(Type methodParameterType,
            IEnumerable<CustomAttributeData> customAttributes)
        {
            var specInfo = new ColumnSpec()
            {
                ComparisonType = methodParameterType
            };

            if (customAttributes != null && customAttributes.Any())
            {
                specInfo.IgnoreIfNull = customAttributes.Any(c => c.AttributeType == typeof(IgnoreIfNullAttribute));
                specInfo.IgnoreIfNullOrEmpty = customAttributes.Any(c => c.AttributeType == typeof(IgnoreIfNullOrEmptyAttribute));
                // pull this from ViaRelation as well, if it has a . near the end OR decorate with both
                specInfo.ColumnName = customAttributes.Where(c => c.AttributeType == typeof(ColumnAttribute)).Select(c => c.ConstructorArguments.First().Value as string).FirstOrDefault();
                specInfo.Not = customAttributes.Any(c => c.AttributeType == typeof(NotAttribute));
                specInfo.GreaterThan = customAttributes.Any(c => c.AttributeType == typeof(GreaterThanAttribute));
                specInfo.GreaterThanOrEqual = customAttributes.Any(c => c.AttributeType == typeof(GreaterThanOrEqualAttribute));
                specInfo.LessThan = customAttributes.Any(c => c.AttributeType == typeof(LessThanAttribute));
                specInfo.LessThanOrEqual = customAttributes.Any(c => c.AttributeType == typeof(LessThanOrEqualAttribute));
                specInfo.StartsWith = customAttributes.Any(c => c.AttributeType == typeof(StartsWithAttribute));
                specInfo.Contains = customAttributes.Any(c => c.AttributeType == typeof(ContainsAttribute));
                specInfo.EndsWith = customAttributes.Any(c => c.AttributeType == typeof(EndsWithAttribute));
            }

            return specInfo;
        }
    }
    
    public class TypeHierarchyNode
    {
        private readonly PropertyInfo propertyInfo;

        public TypeHierarchyNode(Type type) : this(type, null, null)
        {
        }

        public TypeHierarchyNode(Type type, TypeHierarchyNode parent, PropertyInfo propertyInfo)
        {
            this.propertyInfo = propertyInfo;
            Type = type;
            this.Children = new List<TypeHierarchyNode>();
            this.Parent = parent;
        }

        public TypeHierarchyNode Parent { get; set; }
        private Type Type { get; set; }
        public List<TypeHierarchyNode> Children { get; set; }
        public int Depth => this.Parent == null ? 0 : this.Parent.Depth + 1;
        public int Ordinal => this.Parent == null ? 0 : this.Parent.Children.IndexOf(this);

        public string Path => propertyInfo?.Name;

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
