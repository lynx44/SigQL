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
        public ITableDefinition TargetTable { get; set; }
        public IEnumerable<TableRelations> NavigationTables { get; set; }
        public IEnumerable<TableRelationColumnDefinition> ProjectedColumns { get; set; }
        public IForeignKeyDefinition ForeignKeyToParent { get; set; }
        public string Alias => $"{TargetTable.Name}" + (RelationTreeHasAnyTableDefinedMultipleTimes() ? $"<{(Argument.Type != typeof(void) ? Argument.FullyQualifiedName() : Argument.Parent.FullyQualifiedName())}>" : null);
        public string TableName => TargetTable.Name;
        public TableRelations Parent { get; set; }
        
        public TableRelations Filter(TableRelationsColumnSource source, TableRelationsFilter filter)
        {
            var matchingColumns = this.ProjectedColumns.Where(c => c.Source == source && c.Arguments.All.Any(arg => filter.IsMatch(arg, false))).ToList();
            var filteredTableRelations = new TableRelations()
            {
                Argument = this.Argument,
                ForeignKeyToParent = this.ForeignKeyToParent,
                ProjectedColumns = matchingColumns,
                TargetTable = this.TargetTable
            };
            var matchingNavigationTables = this.NavigationTables.Select(t => t.Filter(source, filter)).ToList();
            matchingNavigationTables.ForEach(t => t.Parent = filteredTableRelations);
            filteredTableRelations.NavigationTables = matchingNavigationTables;

            filteredTableRelations = PruneBranchesWithoutColumns(filteredTableRelations);
            
            return filteredTableRelations;
        }

        private static TableRelations PruneBranchesWithoutColumns(TableRelations tableRelations)
        {
            foreach (var navigationTable in tableRelations.NavigationTables)
            {
                PruneBranchesWithoutColumns(navigationTable);
            }

            tableRelations.NavigationTables = tableRelations.NavigationTables.Where(nt => 
                
                nt.ProjectedColumns.Any() || nt.NavigationTables.Any()).ToList();

            return tableRelations;
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
        
        public ArgumentCollection Arguments { get; set; }

        public string Name => columnDefinition.Name;
        public string DataTypeDeclaration => columnDefinition.DataTypeDeclaration;

        public ITableDefinition Table => columnDefinition.Table;

        public TableRelationsColumnSource Source { get; }

        public TableRelationColumnDefinition(IColumnDefinition columnDefinition, IArgument argument, TableRelationsColumnSource source)
        {
            this.columnDefinition = columnDefinition;
            this.Arguments = new ArgumentCollection();
            this.Arguments.AddArgument(argument, source);
            this.Source = source;
        }
    }

    internal class ArgumentCollection
    {
        private class ArgumentWithSource
        {
            public ArgumentWithSource(IArgument argument, TableRelationsColumnSource source)
            {
                Argument = argument;
                Source = source;
            }

            public IArgument Argument { get; set; }
            public TableRelationsColumnSource Source { get; set; }
        }

        private List<ArgumentWithSource> Arguments { get; set; }

        public ArgumentCollection()
        {
            this.Arguments = new List<ArgumentWithSource>();
        }

        public void AddArgument(IArgument argument, TableRelationsColumnSource source)
        {
            Arguments.Add(new ArgumentWithSource(argument, source));
        }

        public IEnumerable<IArgument> GetArguments(TableRelationsColumnSource source)
        {
            return Arguments.Where(a => a.Source == source)
                .Select(a => a.Argument)
                .ToList();
        }

        public IEnumerable<IArgument> All => Arguments.Select(a => a.Argument).ToList();
    }
    
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
