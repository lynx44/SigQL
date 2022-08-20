using System;
using System.Collections.Generic;
using System.Reflection;
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
        public string Alias => $"{TargetTable.Name}_{HierarchyNode.Depth}_{HierarchyNode.Ordinal}";
        public string TableName => TargetTable.Name;
    }

    internal class ColumnDefinitionWithPath : IColumnDefinition
    {
        private readonly IColumnDefinition columnDefinition;
        public ParameterInfo Parameter { get; }
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
