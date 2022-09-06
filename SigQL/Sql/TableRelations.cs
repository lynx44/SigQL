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
        public Type ProjectionType { get; set; }
        public ITableDefinition TargetTable { get; set; }
        public IEnumerable<TableRelations> NavigationTables { get; set; }
        public IEnumerable<ColumnDefinitionWithPath> ProjectedColumns { get; set; }
        public IForeignKeyDefinition ForeignKeyToParent { get; set; }
        public ColumnField ParentColumnField { get; set; }
        public TypeHierarchyNode HierarchyNode { get; set; }
        public string Alias => $"{TargetTable.Name}" + (RelationTreeHasAnyTableDefinedMultipleTimes() ? $"<{HierarchyNode.QualifiedPath}>" : null);
        public string TableName => TargetTable.Name;
        public TableRelations Parent { get; set; }

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

    internal class ColumnDefinitionWithPath : IColumnDefinition
    {
        private readonly IColumnDefinition columnDefinition;
        public ParameterInfo Parameter { get; set; }
        public PropertyInfo Property { get; }

        public string Name => columnDefinition.Name;
        public string DataTypeDeclaration => columnDefinition.DataTypeDeclaration;

        public ITableDefinition Table => columnDefinition.Table;

        public ColumnDefinitionWithPath(IColumnDefinition columnDefinition, PropertyInfo property)
        {
            this.columnDefinition = columnDefinition;
            this.Property = property;
        }

        public ColumnDefinitionWithPath(IColumnDefinition columnDefinition, ParameterInfo parameter)
        {
            this.columnDefinition = columnDefinition;
            this.Parameter = parameter;
        }
    }

    public interface ITableHierarchyAlias
    {
        string Alias { get; }
        string TableName { get; }
    }
}
