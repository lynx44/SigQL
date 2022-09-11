using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Castle.Components.DictionaryAdapter;
using SigQL.Schema;

namespace SigQL.Sql
{
    internal class TableRelations : ITableHierarchyAlias
    {
        public TableRelations()
        {
            this.NavigationTables = new List<TableRelations>();
            this.ProjectedColumns = new List<TableRelationColumnDefinition>();
        }
        public IArgument Argument { get; set; }
        //public Type ProjectionType { get; set; }
        public ITableDefinition TargetTable { get; set; }
        public IEnumerable<TableRelations> NavigationTables { get; set; }
        public IEnumerable<TableRelationColumnDefinition> ProjectedColumns { get; set; }
        public IForeignKeyDefinition ForeignKeyToParent { get; set; }
        public string Alias => $"{TargetTable.Name}" + (RelationTreeHasAnyTableDefinedMultipleTimes() ? $"<{Argument.FullyQualifiedName()}>" : null);
        public string TableName => TargetTable.Name;
        public TableRelations Parent { get; set; }

        public TableRelations Filter(TableRelationsColumnSource source, TableRelationsFilter filter)
        {
            var matchingColumns = this.ProjectedColumns.Where(c => c.Source.HasFlag(source) && filter.IsMatch(c.Argument, false)).ToList();
            var filteredTableRelations = new TableRelations()
            {
                Argument = this.Argument,
                ForeignKeyToParent = this.ForeignKeyToParent,
                ProjectedColumns = matchingColumns,
                TargetTable = this.TargetTable
            };
            var matchingNavigationTables = this.NavigationTables.Select(t => Filter(source, filter)).Where(t => t != null).ToList();
            matchingNavigationTables.ForEach(t => t.Parent = filteredTableRelations);
            filteredTableRelations.NavigationTables = matchingNavigationTables;
            
            return filteredTableRelations;
        }

        public bool RelationTreeHasAnyTableDefinedMultipleTimes()
        {
            var root = Parent ?? this;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            List<ITableDefinition> tables = new List<ITableDefinition>();
            AppendTables(root, tables);
            
            return tables.GroupBy(t => t.Name).Any(g => g.Count() > 1);
        }

        private void AppendTables(TableRelations relations, List<ITableDefinition> tables)
        {
            tables.Add(relations.TargetTable);
            foreach (var navigationRelations in relations.NavigationTables)
            {
                AppendTables(navigationRelations, tables);
            }
        }

        public TableRelations Find(string tableName)
        {
            if (TargetTable.Name == tableName)
            {
                return this;
            }

            foreach (var navigationTable in this.NavigationTables)
            {
                var matchingRelation = navigationTable.Find(tableName);
                if (matchingRelation != null)
                {
                    return matchingRelation;
                }
            }

            return null;
        }

        public TableRelations FindViaRelations(IEnumerable<string> tableRelationPath)
        {
            if (tableRelationPath.FirstOrDefault() == this.TableName)
            {
                return FindViaRelations(tableRelationPath.Skip(1).ToList(), this);
            }

            return null;
        }

        private TableRelations FindViaRelations(IEnumerable<string> relatedTableNames, TableRelations tableRelation)
        {

            var nextTableName = relatedTableNames.FirstOrDefault();
            var matchingTableRelation = tableRelation.NavigationTables.FirstOrDefault(t => t.TableName == nextTableName);
            var remainingTableNames = relatedTableNames.Skip(1).ToList();

            if (!remainingTableNames.Any())
            {
                return matchingTableRelation;
            }

            return FindViaRelations(remainingTableNames, matchingTableRelation);
        }
    }

    internal class TableRelationColumnDefinition : IColumnDefinition
    {
        private readonly IColumnDefinition columnDefinition;
        
        public IArgument Argument { get; set; }

        public string Name => columnDefinition.Name;
        public string DataTypeDeclaration => columnDefinition.DataTypeDeclaration;

        public ITableDefinition Table => columnDefinition.Table;

        public TableRelationsColumnSource Source { get; set; }

        public TableRelationColumnDefinition(IColumnDefinition columnDefinition, IArgument argument, TableRelationsColumnSource source)
        {
            this.columnDefinition = columnDefinition;
            this.Argument = argument;
            this.Source = source;
        }
    }

    [Flags]
    internal enum TableRelationsColumnSource
    {
        ReturnType,
        Parameters
    }

    public interface ITableHierarchyAlias
    {
        string Alias { get; }
        string TableName { get; }
    }
}
