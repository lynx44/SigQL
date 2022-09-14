using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SigQL.Exceptions;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Sql;
using SigQL.Types.Attributes;

namespace SigQL
{
    internal partial class DatabaseResolver
    {
        public ArgumentContainer ToArgumentContainer(Type outputType)
        {
            return new ArgumentContainer()
            {
                TargetTable = this.DetectTable(outputType),
                Arguments = this.UnwrapCollectionTargetType(outputType).GetProperties()
                    .Select(p => new PropertyArgument(p, null, this)).ToList()
            };
        }

        public ArgumentContainer ToArgumentContainer(Type returnType, IEnumerable<IArgument> arguments)
        {
            return new ArgumentContainer()
            {
                TargetTable = this.DetectTable(returnType),
                Arguments = arguments
            };
        }

        public ArgumentContainer ToArgumentContainer(ITableDefinition table, IEnumerable<IArgument> arguments)
        {
            return new ArgumentContainer()
            {
                TargetTable = table,
                Arguments = arguments
            };
        }

        public TableRelations BuildTableRelations(ITableDefinition tableDefinition, IArgument argument, TableRelationsColumnSource source)
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
                    var navigationTableRelations = BuildTableRelations(this.DetectTable(p.Column.Argument.Type), p.Column.Argument, source);
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
                                $"Unable to identify matching database column for {d.Column.Argument.FullyQualifiedName()}. Column {d.Column.Name} does not exist in table {tableDefinition.Name}.");
                        else
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database table for {d.Column.Argument.FullyQualifiedName()} of type {d.Column.Argument.Type}. Table {GetExpectedTableNameForType(UnwrapCollectionTargetType(d.Column.Type))} does not exist.");

                    }
                    return new TableRelationColumnDefinition(tableColumn,
                                d.Column.Argument, source);
                }).ToList();

            var tableRelations = new TableRelations()
            {
                //ProjectionType = arguments.ProjectionType,
                //ParentColumnField = parentColumnField,
                Argument = argument,
                TargetTable = tableDefinition,
                NavigationTables = relations,
                ProjectedColumns = columns
            };


            foreach (var navigationTable in tableRelations.NavigationTables)
            {
                navigationTable.Parent = tableRelations;
            }

            IEnumerable<IForeignKeyDefinition> foreignKeys = null;
            foreach (var navigationTableRelations in tableRelations.NavigationTables)
            {
                if (!TableEqualityComparer.Default.Equals(navigationTableRelations.TargetTable,
                        tableRelations.TargetTable))
                {
                    var foreignKey = this.FindPrimaryForeignKeyMatchForTables(tableDefinition, navigationTableRelations.TargetTable);
                    if (foreignKey == null)
                    {
                        foreignKeys = FindManyToManyForeignKeyMatchesForTables(tableDefinition, navigationTableRelations.TargetTable);

                        if (!(foreignKeys?.Any()).GetValueOrDefault(false))
                        {
                            throw new InvalidIdentifierException(
                                $"Unable to identify matching database foreign key for property {navigationTableRelations.Argument.FullyQualifiedName()}. No foreign key between {tableDefinition.Name} and {navigationTableRelations.TargetTable.Name} could be found.");
                        }

                        var oneToManyFk = foreignKeys.First(fk => TableEqualityComparer.Default.Equals(fk.PrimaryKeyTable, navigationTableRelations.TargetTable));
                        var manyToOneFk = foreignKeys.Except(new[] { oneToManyFk }).First();
                        tableRelations.ForeignKeyToParent = oneToManyFk;
                        //tableRelations.ParentColumnField = null;

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

            //if (parentTable != null /*&& (node.Depth != 1 && !TableEqualityComparer.Default.Equals(parentTable, tableDefinition))*/)
            //{
            //    var foreignKey = this.FindPrimaryForeignKeyMatchForTables(parentTable, tableDefinition);
            //    if (foreignKey == null)
            //    {
            //        foreignKeys = FindManyToManyForeignKeyMatchesForTables(parentTable, tableDefinition);

            //        if (!(foreignKeys?.Any()).GetValueOrDefault(false))
            //        {
            //            throw new InvalidIdentifierException(
            //                $"Unable to identify matching database foreign key for property {GetParentTableClassQualifiedNameForType(projectionType)}.{parentColumnField.Property.Name}. No foreign key between {parentTable.Name} and {tableDefinition.Name} could be found.");
            //        }

            //        var oneToManyFk = foreignKeys.First(fk => TableEqualityComparer.Default.Equals(fk.PrimaryKeyTable, tableDefinition));
            //        var manyToOneFk = foreignKeys.Except(new[] { oneToManyFk }).First();
            //        tableRelations.ForeignKeyToParent = oneToManyFk;
            //        tableRelations.ParentColumnField = null;

            //        var hierarchyNode = new TypeHierarchyNode(typeof(void), node, null);
            //        node.Children.Add(hierarchyNode);
            //        return this.BuildTableRelations(manyToOneFk, parentColumnField, new[] { tableRelations }, hierarchyNode);
            //    }
            //    else
            //    {
            //        tableRelations.ForeignKeyToParent = foreignKey;
            //    }
            //}

            var viaTableRelations = viaRelationColumns.Select(c =>
            {
                IArgument argument = c.Column.Argument;
                return BuildTableRelationsFromViaParameter(argument,
                    argument.GetCustomAttribute<ViaRelationAttribute>().Path);
            }).ToList();

            var result = MergeTableRelations(viaTableRelations.AppendOne(tableRelations).ToArray());
            return result;
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
}
