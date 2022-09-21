﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
        public TableRelations BuildTableRelations(
            ITableDefinition tableDefinition, 
            IArgument argument, 
            TableRelationsColumnSource source,
            ConcurrentDictionary<string, ITableKeyDefinition> tableKeyDefinitions)
        {
            var columnFields = argument.ClassProperties.Where(p => !ColumnAttributes.IsDecoratedNonColumn(p)).Select(p => new ColumnField()
            {
                Name = this.GetColumnName(p),
                Type = p.Type,
                Argument = p
            }).ToList();
            var columnsWithTables =
                columnFields.Select(p => new { Column = p, IsTable = this.IsTableOrTableProjection(p.Type) });
            var viaRelationColumns = columnsWithTables.Where(c => ColumnFilters.ViaRelation.IsMatch(c.Column.Argument, c.IsTable)).ToList();

            columnsWithTables = columnsWithTables.Except(viaRelationColumns).ToList();
            var columnDescriptions = columnsWithTables.ToList();
            var unprocessedNavigationTables = columnDescriptions.Where(t => t.IsTable && !t.Column.Argument.IsDescendentOf(UnwrapCollectionTargetType(t.Column.Type))).ToList();
            
            var relations = unprocessedNavigationTables
                .Select(p =>
                {
                    var navigationTableRelations = BuildTableRelations(this.DetectTable(p.Column.Argument.Type), p.Column.Argument, source, tableKeyDefinitions);
                    return navigationTableRelations;
                }).ToList();
            var columns = columnDescriptions.Where(t => !t.IsTable)
                .Select(d =>
                {
                    var columnName = GetColumnName(d.Column.Argument);
                    var tableColumn = tableDefinition.Columns.FindByName(columnName);
                    if (tableColumn == null)
                    {
                        if (IsColumnType(d.Column.Type))
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database column for {d.Column.Argument.GetCallsiteTypeName()} {d.Column.Argument.FullyQualifiedTypeName()}. Column {d.Column.Name} does not exist in table {tableDefinition.Name}.");
                        else
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database table for {d.Column.Argument.GetCallsiteTypeName()} {d.Column.Argument.FullyQualifiedTypeName()} of type {d.Column.Argument.Type}. Table {GetExpectedTableNameForType(UnwrapCollectionTargetType(d.Column.Type))} does not exist.");

                    }
                    return new TableRelationColumnDefinition(tableColumn,
                                d.Column.Argument, source);
                }).ToList();

            var tableRelations = new TableRelations()
            {
                Argument = argument,
                TargetTable = tableDefinition,
                NavigationTables = relations,
                ProjectedColumns = columns
            };

            IEnumerable<IForeignKeyDefinition> foreignKeys = null;
            foreach (var navigationTableRelations in tableRelations.NavigationTables)
            {
                if (!TableEqualityComparer.Default.Equals(navigationTableRelations.TargetTable,
                        tableRelations.TargetTable))
                {
                    var joinRelationAttribute = navigationTableRelations.Argument.GetCustomAttribute<JoinRelationAttribute>();
                    IForeignKeyDefinition foreignKey;
                    if (joinRelationAttribute != null)
                    {
                        var joinTableRelations = BuildTableRelationsFromViaParameter(navigationTableRelations.Argument, joinRelationAttribute.Path,
                            null, TableRelationsColumnSource.ReturnType, tableKeyDefinitions);
                        foreignKey = joinTableRelations.NavigationTables.Single().ForeignKeyToParent;
                    }
                    else
                    {
                        foreignKey = this.FindPrimaryForeignKeyMatchForTables(tableDefinition, navigationTableRelations.TargetTable);
                    }
                    if (foreignKey == null)
                    {
                        foreignKeys = FindManyToManyForeignKeyMatchesForTables(tableDefinition, navigationTableRelations.TargetTable);

                        if (!(foreignKeys?.Any()).GetValueOrDefault(false))
                        {
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database foreign key for property {navigationTableRelations.Argument.FullyQualifiedTypeName()}. No foreign key between {tableDefinition.Name} and {navigationTableRelations.TargetTable.Name} could be found.");
                        }

                        var oneToManyFk = foreignKeys.First(fk => TableEqualityComparer.Default.Equals(fk.PrimaryKeyTable, navigationTableRelations.TargetTable));
                        var manyToOneFk = foreignKeys.Except(new[] { oneToManyFk }).First();
                        tableRelations.ForeignKeyToParent = oneToManyFk;

                        this.BuildManyToManyTableRelations(manyToOneFk, tableRelations, navigationTableRelations);
                    }
                    else
                    {
                        navigationTableRelations.ForeignKeyToParent = foreignKey;
                    }
                }
                else
                {
                    ;
                    tableRelations.NavigationTables = tableRelations.NavigationTables.Except(new [] { navigationTableRelations}).ToList();
                    tableRelations = MergeTableRelations(tableRelations, navigationTableRelations);
                }
            }
            
            foreach (var navigationTable in tableRelations.NavigationTables)
            {
                navigationTable.Parent = tableRelations;
                foreach (var projectedColumn in navigationTable.ProjectedColumns)
                {
                    projectedColumn.TableRelations = navigationTable;
                }
            }

            columns.ForEach(c => c.TableRelations = tableRelations);

            var viaTableRelations = viaRelationColumns.Select(c =>
            {
                IArgument argument = c.Column.Argument;
                var viaRelationAttribute = argument.GetCustomAttribute<ViaRelationAttribute>();
                return BuildTableRelationsFromViaParameter(argument,
                    viaRelationAttribute.Path, viaRelationAttribute.Column, source, tableKeyDefinitions);
            }).ToList();

            var result = MergeTableRelations(viaTableRelations.AppendOne(tableRelations).ToArray());
            return result;
        }

        private class ViaRelationColumns
        {
            public ViaRelationColumns()
            {
                this.RelationColumns = new List<IColumnDefinition>();
            }
            public TableRelations TableRelations { get; set; }
            public List<IColumnDefinition> RelationColumns { get; set; }
        }

        internal TableRelations BuildTableRelationsFromViaParameter(IArgument argument,
            string viaRelationPath, string endpointColumnName, TableRelationsColumnSource source, ConcurrentDictionary<string, ITableKeyDefinition> tableKeyDefinitions)
        {
            var relations =
                viaRelationPath.
                    Split(new[] { "->" }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

            TableRelations previousRelation = null;
            var relationsReversed = relations.ToList();
            relationsReversed.Reverse();
            var allTableRelations = new List<TableRelations>();
            var relationColumnsList = new List<ViaRelationColumns>();
            foreach (var relation in relationsReversed)
            {
                var tableName = relation.Split('.').First();
                var relationColumnName = relation.Split('.').Skip(1).FirstOrDefault();

                var targetTable = this.databaseConfiguration.Tables.FindByName(tableName);
                if (targetTable == null)
                {
                    throw new InvalidIdentifierException(
                        $"Unable to identify matching database table for parameter {argument.Name} with [ViaRelation(\"{viaRelationPath}\", \"{endpointColumnName}\")]. Table {tableName} does not exist.");
                }
                
                var tableRelations = new TableRelations()
                {
                    Argument = argument,
                    TargetTable = targetTable,
                    ProjectedColumns = new List<TableRelationColumnDefinition>(),
                    NavigationTables = new List<TableRelations>()
                };
                var relationColumns = new ViaRelationColumns()
                {
                    TableRelations = tableRelations
                };
                relationColumnsList.Add(relationColumns);

                if (!string.IsNullOrEmpty(relationColumnName))
                {
                    var column = targetTable.Columns.FindByName(relationColumnName);
                    if (column == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for parameter {argument.Name} with [ViaRelation(\"{viaRelationPath}\", \"{endpointColumnName}\")]. Column {endpointColumnName} does not exist in table {targetTable.Name}.");
                    }

                    relationColumns.RelationColumns.Add(column);
                }

                if (endpointColumnName != null && relationsReversed.IndexOf(relation) == 0)
                {
                    var column = targetTable.Columns.FindByName(endpointColumnName);
                    if (column == null)
                    {
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database column for parameter {argument.Name} with [ViaRelation(\"{viaRelationPath}\", \"{endpointColumnName}\")]. Column {endpointColumnName} does not exist in table {targetTable.Name}.");
                    }
                    
                    tableRelations.ProjectedColumns = new List<TableRelationColumnDefinition>()
                    {
                        new TableRelationColumnDefinition(column, argument, source)
                        {
                            TableRelations = tableRelations
                        }
                    };
                }

                if (previousRelation != null)
                {
                    tableRelations.Parent = previousRelation;
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
                    var relationColumns = relationColumnsList.Single(c => c.TableRelations == tableRelation);
                    var previousRelationColumns = relationColumnsList.Single(c => c.TableRelations == previousRelation);
                    // use the specified columns to join if specified
                    if (relationColumns.RelationColumns.Any() && previousRelationColumns.RelationColumns.Any())
                    {
                        tableRelation.ForeignKeyToParent = new ForeignKeyDefinition(relationColumns.TableRelations.TargetTable,
                            new ForeignKeyPair(previousRelationColumns.RelationColumns.Single(),
                                relationColumns.RelationColumns.Single()));
                    }
                    // otherwise, autodetect the relationship
                    else
                    {
                        tableRelation.ForeignKeyToParent =
                            FindPrimaryForeignKeyMatchForTables(tableRelation.TargetTable, previousRelation.TargetTable);
                    }
                    
                    if (tableRelation.ForeignKeyToParent == null)
                        throw new InvalidIdentifierException(
                            $"Unable to identify matching database foreign key for parameter {argument.Name} with [ViaRelation(\"{viaRelationPath}\", \"{endpointColumnName}\")]. No foreign key between {previousRelation.TargetTable.Name} and {tableRelation.TargetTable.Name} could be found.");
                    
                    if (!tableKeyDefinitions.ContainsKey(tableRelation.Argument.Name))
                    {
                        tableKeyDefinitions[tableRelation.Argument.Name] =
                            new TableKeyDefinition(tableRelation.ForeignKeyToParent.KeyPairs
                                .Select(kp => kp.PrimaryTableColumn).ToArray());
                    }
                }
                previousRelation = tableRelation;
            }

            return allTableRelations.First();
        }
        
        internal TableRelations MergeTableRelations(params TableRelations[] tableRelationsCollection)
        {
            return new TableRelations()
            {
                Argument = tableRelationsCollection.Where(t => t.Argument != null).Select(t => t.Argument).First(),
                TargetTable = tableRelationsCollection.First().TargetTable,
                NavigationTables = tableRelationsCollection.SelectMany(t => t.NavigationTables).GroupBy(nt => nt.TargetTable, nt => nt, TableEqualityComparer.Default).Select(nt => MergeTableRelations(nt.ToArray())).ToList(),
                ProjectedColumns = tableRelationsCollection.SelectMany(t => t.ProjectedColumns).ToList(),
                ForeignKeyToParent = tableRelationsCollection.Where(t => t.ForeignKeyToParent != null).Select(t => t.ForeignKeyToParent).Distinct(ForeignKeyDefinitionEqualityComparer.Default).FirstOrDefault(),
                Parent = tableRelationsCollection.Select(p => p.Parent).FirstOrDefault(parent => parent != null),
            };
        }

    }

    internal class TableRelationsFilter
    {
        private readonly Func<IArgument, bool, bool> condition;

        public TableRelationsFilter(Func<IArgument, bool, bool> condition)
        {
            this.condition = condition;
        }

        public bool IsMatch(IArgument argument, bool isTable)
        {
            return condition(argument, isTable);
        }
    }


    internal class ColumnFilters
    {
        public static TableRelationsFilter WhereClause =
            new TableRelationsFilter((column, isTable) =>
                !ColumnAttributes.IsDecoratedNonColumn(column) && !ColumnAttributes.IsOrderBy(column));

        public static TableRelationsFilter SelectClause =
            new TableRelationsFilter((column, isTable) =>
                !ColumnAttributes.IsDecoratedNonColumn(column));

        public static TableRelationsFilter FromClause =
            new TableRelationsFilter(
            (column, isTable) =>
            !ColumnAttributes.IsDecoratedNonColumn(column));

        public static TableRelationsFilter OrderBy =
            new TableRelationsFilter(
            (column, isTable) =>
            isTable ||
            ColumnAttributes.IsOrderBy(column));

        public static TableRelationsFilter ViaRelation =
            new TableRelationsFilter((column, isTable) =>
                ColumnAttributes.IsViaRelation(column));
    }

    internal class ColumnAttributes
    {

        public static bool IsDecoratedNonColumn(IArgument argument)
        {
            return
                argument.GetCustomAttribute<OffsetAttribute>() != null ||
                argument.GetCustomAttribute<FetchAttribute>() != null ||
                argument.GetCustomAttribute<ClrOnlyAttribute>() != null ||
                argument.GetCustomAttribute<ParameterAttribute>() != null ||
                IsDynamicOrderBy(argument);
        }

        public static bool IsViaRelation(IArgument property)
        {
            return property?.GetCustomAttribute<ViaRelationAttribute>() != null;
        }

        public static bool IsOrderBy(IArgument property)
        {
            return
                property?.Type == typeof(OrderByDirection);
        }

        public static bool IsDynamicOrderBy(IArgument property)
        {
            return
                (((property?.Type)?.IsAssignableFrom(typeof(OrderBy))).GetValueOrDefault(false) ||
                  ((property?.Type)?.IsAssignableFrom(
                      typeof(IEnumerable<OrderBy>))).GetValueOrDefault(false));
        }
    }
}
