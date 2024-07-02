using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Castle.Components.DictionaryAdapter;
using SigQL.Extensions;
using SigQL.Schema;

namespace SigQL.Sql
{
    internal class TableRelations : ITableHierarchyAlias
    {
        public TableRelations()
        {
            this.NavigationTables = new List<TableRelations>();
            this.ProjectedColumns = new List<TableRelationColumnDefinition>();
            this.FunctionParameters = new List<ParameterPath>();
        }
        public IArgument Argument { get; set; }
        public ITableDefinition TargetTable { get; set; }
        public IEnumerable<TableRelations> NavigationTables { get; set; }
        public IEnumerable<TableRelationColumnIdentifierDefinition> ProjectedColumns { get; set; }
        public IEnumerable<ParameterPath> FunctionParameters { get; set; }
        public IForeignKeyDefinition ForeignKeyToParent { get; set; }
        public string Alias => $"{TargetTable.Name}" + (((this.MasterRelations ?? this).RelationTreeHasAnyTableDefinedMultipleTimes()) ? $"<{(Argument.GetCallsiteTypeName() == "table" || Argument.Type != typeof(void) ? Argument.FullyQualifiedName() : Argument.Parent.FullyQualifiedName())}>" : null);
        public string TableName => TargetTable.Name;
        public TableRelations Parent { get; set; }
        public IEnumerable<TableRelationColumnIdentifierDefinition> PrimaryKey { get; set; }
        internal TableRelations MasterRelations { get; set; }
        public bool IsManyToMany { get; set; }

        public TableRelations Mask(TableRelationsColumnSource source, TableRelationsFilter filter, TableRelations masterRelations = null)
        {
            var matchingColumns = this.ProjectedColumns.Where(c => c.Source == source && (!c.Arguments.All.Any() || c.Arguments.All.Any(arg => filter.IsMatch(arg, false)))).ToList();
            var filteredTableRelations = new TableRelations()
            {
                Argument = this.Argument,
                ForeignKeyToParent = this.ForeignKeyToParent,
                ProjectedColumns = matchingColumns,
                TargetTable = this.TargetTable,
                PrimaryKey = this.PrimaryKey,
                FunctionParameters = this.FunctionParameters,
                MasterRelations = masterRelations ?? MasterRelations ?? this,
                IsManyToMany = this.IsManyToMany
            };
            var matchingNavigationTables = this.NavigationTables.Select(t => t.Mask(source, filter, filteredTableRelations.MasterRelations)).ToList();
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

        public TableRelations FindEquivalentBranch(TableRelations endpointRelations)
        {
            var path = new List<TableRelations>();
            var currentRelations = endpointRelations;

            do
            {
                path.Add(currentRelations);
                currentRelations = currentRelations.Parent;
            } while (currentRelations != null);

            path.Reverse();

            if (!TableEqualityComparer.Default.Equals(this.TargetTable, path.First().TargetTable))
            {
                throw new InvalidOperationException(
                    $"TableRelations do not match. Expected table {this.TargetTable.Name}, received {path.First().TargetTable.Name}");
            }

            TableRelations thisEndpoint = this;
            path.RemoveAt(0);
            while (path.Any())
            {
                var relation = path.First();
                thisEndpoint = this.NavigationTables.Single(t =>
                    TableEqualityComparer.Default.Equals(relation.TargetTable, t.TargetTable));

                path.RemoveAt(0);
            }

            return thisEndpoint;
        }

        public IEnumerable<TableRelations> CollectPath(TableRelations navigationTable)
        {
            var path = new List<TableRelations>();
            this.Traverse(tr =>
            {
                if (tr.Alias == navigationTable.Alias)
                {
                    while (tr != null)
                    {
                        path.Add(tr);
                        tr = tr.Parent;
                    }
                }
            });
            
            path.Reverse();
            return path;
        }

        public string GetQualifiedTablePath()
        {
            return GetQualifiedTablePathInternal(this, new List<string>());
        }

        private string GetQualifiedTablePathInternal(TableRelations tableRelations, List<string> path)
        {
            path.Add(tableRelations.TableName);
            if (tableRelations.Parent != null)
            {
                this.GetQualifiedTablePathInternal(tableRelations.Parent, path);
            }

            path.Reverse();
            return string.Join("->", path);
        }

        public bool RelationTreeHasAnyTableDefinedMultipleTimes()
        {
            var root = Parent ?? this;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            root = root.MasterRelations ?? root;

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

        public TableRelations FindByAlias(string tableAlias)
        {
            if (Alias == tableAlias)
            {
                return this;
            }

            foreach (var navigationTable in this.NavigationTables)
            {
                if (navigationTable.Alias == tableAlias)
                {
                    return navigationTable;
                }
            }

            return null;
        }

        public TableRelations GetSingularEndpoint()
        {
            if (!this.NavigationTables.Any())
            {
                return this;
            }

            return this.NavigationTables.Single().GetSingularEndpoint();
        }
        
        public TableRelations FindByTablePaths(IEnumerable<string> tableRelationPath)
        {
            if (tableRelationPath.FirstOrDefault() == this.TableName && tableRelationPath.Count() == 1)
            {
                return this;
            }

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

        public void Traverse(Action<TableRelations> action)
        {
            action(this);
            foreach (var navigationTable in this.NavigationTables)
            {
                navigationTable.Traverse(action);
            }
        }

        public int CalculateDepth()
        {
            var tableRelations = this;
            var depth = 0;
            while (tableRelations.Parent != null)
            {
                depth++;
                tableRelations = tableRelations.Parent;
            }

            return depth;
        }

        //public TableRelations Segment(TableRelations tableRelations)
        //{
        //    var filteredTableRelations = new TableRelations()
        //    {
        //        Argument = this.Argument,
        //        ForeignKeyToParent = this.ForeignKeyToParent,
        //        ProjectedColumns = this.ProjectedColumns.ToList(),
        //        TargetTable = this.TargetTable,
        //        PrimaryKey = this.PrimaryKey,
        //        FunctionParameters = this.FunctionParameters,
        //        MasterRelations = this
        //    };
        //    var matchingNavigationTables = this.NavigationTables.Where(t => t.Alias == tableRelations.Alias || t.NavigationTables.SelectManyRecursive(t => t.NavigationTables).Any(t => t.Alias == tableRelations.Alias)).ToList();
        //    filteredTableRelations.NavigationTables = matchingNavigationTables.ToList();

        //    return filteredTableRelations;
        //}

        public TableRelations PickBranch(TableRelations tableRelations)
        {
            var relations = tableRelations.Copy(tableRelations.Parent);
            relations.NavigationTables = new List<TableRelations>();
            return PickBranchRecursive(relations);
            //return relations;

            //var path = tableRelations.SelectRecursive(t => t.Parent).ToList();

            //var copy = this.Copy();

            //var foreignKeys = path.Select(p => p.ForeignKeyToParent).Skip(1).ToList();

            //foreach (var foreignKey in foreignKeys)
            //{
            //    copy.NavigationTables = copy.NavigationTables.Where(nt =>
            //        ForeignKeyDefinitionEqualityComparer.Default.Equals(nt.ForeignKeyToParent, foreignKey)).ToList();
            //}

            //return copy;
        }

        private TableRelations PickBranchRecursive(TableRelations relations)
        {
            if (relations.Parent != null)
            {
                relations.Parent = relations.Parent.Copy(relations.Parent?.Parent);
                relations.Parent.NavigationTables = new List<TableRelations>() { relations };
                return PickBranchRecursive(relations.Parent);
            }

            return relations;
        }

        public IEnumerable<TableRelations> SeparateByColumn()
        {
            var list = new List<TableRelations>();
            this.Traverse(tr =>
            {
                var separatedTableRelations = tr.ProjectedColumns.Select(c =>
                {
                    var tableRelations = this.PickBranch(tr).GetSingularEndpoint();
                    var parent = tableRelations.Parent;
                    while (parent != null)
                    {
                        parent.ProjectedColumns = new List<TableRelationColumnIdentifierDefinition>();
                        parent = parent.Parent;
                    }
                    tableRelations.ProjectedColumns = tableRelations.ProjectedColumns.Where(p => p.Name == c.Name).ToList();
                    foreach (var column in tableRelations.ProjectedColumns)
                    {
                        column.TableRelations = tableRelations;
                    }
                    return tableRelations;
                }).ToList();
                list.AddRange(separatedTableRelations);
            });

            return list;
        }

        public TableRelations Copy(TableRelations parent = null)
        {
            var copy = new TableRelations()
            {
                Argument = this.Argument,
                ForeignKeyToParent = this.ForeignKeyToParent,
                FunctionParameters = this.FunctionParameters.ToList(),
                MasterRelations = this,
                Parent = parent,
                ProjectedColumns = this.ProjectedColumns.ToList(),
                PrimaryKey = this.PrimaryKey?.ToList() ?? new List<TableRelationColumnIdentifierDefinition>(),
                TargetTable = this.TargetTable
            };
            copy.NavigationTables = this.NavigationTables.Select(t => t.Copy(copy)).ToList();
            return copy;
        }

        public TableRelations GetRootParent()
        {
            var final = this;
            while (final.Parent != null)
            {
                final = final.Parent;
            }

            return final;
        }
    }

    internal class TableRelationColumnDefinition : TableRelationColumnIdentifierDefinition
    {
        public TableRelationColumnDefinition(string name, string dataTypeDeclaration, ITableDefinition table, IArgument argument, TableRelationsColumnSource source, bool isIdentity)
            : base(name, table, argument, source, isIdentity)
        {
            this.DataTypeDeclaration = dataTypeDeclaration;
        }
    }

    internal class TableRelationColumnIdentifierDefinition : IColumnDefinition
    {
        private readonly string name;
        private readonly ITableDefinition table;

        public ArgumentCollection Arguments { get; set; }

        public string Name => this.name;
        public string DataTypeDeclaration { get; protected set; }
        public bool IsIdentity { get; }

        public ITableDefinition Table => this.table;

        public TableRelationsColumnSource Source { get; }
        public TableRelations TableRelations { get; set; }

        public TableRelationColumnIdentifierDefinition(string name, ITableDefinition table, IArgument argument, TableRelationsColumnSource source, bool isIdentity)
        {
            this.name = name;
            this.table = table;
            this.Arguments = new ArgumentCollection();
            if(argument != null)
                this.Arguments.AddArgument(argument, source);
            this.Source = source;
            this.IsIdentity = isIdentity;
        }
    }

    class TableRelationColumnRowNumberFunctionDefinition : TableRelationColumnIdentifierDefinition
    {
        public TableRelationColumnRowNumberFunctionDefinition(string name, ITableDefinition table, TableRelationsColumnSource source, bool isIdentity) 
            : base(name, table, null, source, isIdentity)
        {
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
