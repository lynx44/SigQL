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
        private readonly IPluralizationHelper pluralizationHelper;

        public DatabaseResolver(IDatabaseConfiguration databaseConfiguration, IPluralizationHelper pluralizationHelper)
        {
            this.databaseConfiguration = databaseConfiguration;
            this.pluralizationHelper = pluralizationHelper;
        }

        public IEnumerable<ColumnDefinitionWithPropertyPath> ResolveColumnsForSelectStatement(
            TableRelations tableRelations, 
            IEnumerable<ColumnAliasForeignKeyDefinition> allForeignKeys,
            ConcurrentDictionary<string, IEnumerable<string>> tableKeyDefinitions)
        {
            ITableDefinition table = null;
            table = tableRelations.TargetTable;

            var tableColumns = tableRelations.ProjectedColumns.SelectMany(p =>
            {
                //var targetColumn = table.Columns.FindByName(p.Name);
                var argument = p.Arguments.GetArguments(TableRelationsColumnSource.ReturnType).FirstOrDefault();
                //if (targetColumn == null)
                //{
                //    throw new InvalidIdentifierException(
                //        $"Unable to identify matching database column for property {argument.FullyQualifiedName()}. Column {p.Name} does not exist in table {table.Name}.");
                //}

                var propertyPaths = argument?.FindFromPropertyRoot().Select(arg => arg.Name).ToList();
                return new ColumnDefinitionWithPropertyPath()
                {
                    ColumnDefinition = new ColumnAliasColumnDefinition(p.Name, p.DataTypeDeclaration, p.Table, tableRelations, p.IsIdentity),
                    PropertyPath = new PropertyPath() { PropertyPaths = (propertyPaths?.Any()).GetValueOrDefault(false) ? propertyPaths :  new List<string>() { p.Name } }
                }.AsEnumerable();
            }).ToList();
            var columns = tableColumns.ToList();

            columns.AddRange(tableRelations.NavigationTables.SelectMany(p =>
            {
                return ResolveColumnsForSelectStatement(p, allForeignKeys, tableKeyDefinitions);
            }));
            
            var currentPaths = tableRelations.Argument.FindFromPropertyRoot().Select(p => p.Name).ToList();
            
            if ((tableRelations.PrimaryKey?.Any()).GetValueOrDefault(false))
            {
                var missingPkColumns = tableRelations.PrimaryKey.Where(pkc =>
                    !columns.Any(c => ColumnEqualityComparer.Default.Equals(pkc, c.ColumnDefinition))).ToList();
                if (missingPkColumns.Any())
                {
                    var primaryColumns = missingPkColumns
                        .Select(c =>
                        {
                            return new ColumnDefinitionWithPropertyPath()
                            {
                                ColumnDefinition = new ColumnAliasColumnDefinition(c.Name, c.DataTypeDeclaration, c.Table, tableRelations, c.IsIdentity),
                                PropertyPath = new PropertyPath() {PropertyPaths = currentPaths.AppendOne(c.Name).ToList()}
                            };
                        }).ToList();
                
                    primaryColumns.AddRange(tableColumns);
                    tableColumns = primaryColumns.ToList();
                }

                var selectedPrimaryColumns = tableColumns.Where(c =>
                    tableRelations.PrimaryKey.Any(pkc =>
                        ColumnEqualityComparer.Default.Equals(c.ColumnDefinition, pkc))).ToList();
                tableKeyDefinitions[string.Join(".", currentPaths)] = selectedPrimaryColumns.Select(c => c.Alias.Replace(string.Join(".", currentPaths) + ".", "")).ToList();
                selectedPrimaryColumns.AddRange(columns.Except(selectedPrimaryColumns));
                columns = selectedPrimaryColumns.ToList();
            }
            
            columns = columns.GroupBy(c => c.Alias, c => c).Select(c => c.First()).ToList();

            return columns;
        }

        private static bool IsColumnType(Type propertyType)
        {
            return propertyType.Namespace == "System" || propertyType.IsEnum;
        }
        
        public ITableDefinition DetectTable(Type t)
        {
            Type detectedType = null;
            var tableName = DetectTableName(t, ref detectedType);
            ITableDefinition table = null;
            if (tableName != null)
            {
                table = this.databaseConfiguration.Tables.FindByName(tableName);
            }
            
            return table ?? 
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

        public bool TryDetectTargetTable(Type columnOutputType, ref Type detectedType)
        {
            return DetectTableName(columnOutputType, ref detectedType) != null;
            //var type = UnwrapCollectionTargetType(columnOutputType);
            //var sqlIdentifier = type.GetCustomAttribute<SqlIdentifierAttribute>();
            //string tableName;
            //if (sqlIdentifier != null)
            //{
            //    tableName = this.databaseConfiguration.Tables.FindByName(sqlIdentifier.Name).Name;
            //}
            //else
            //{
            //    tableName = this.pluralizationHelper.AllCandidates(type.Name).FirstOrDefault(tableName =>
            //        this.databaseConfiguration.Tables.FindByName(tableName) != null);
            //}

            //if (tableName != null)
            //{
            //    detectedType = type;
            //    return true;
            //}

            //return type.DeclaringType != null && type.DeclaringType.IsClass && TryDetectTargetTable(type.DeclaringType, ref detectedType);
        }

        public string DetectTableName(Type columnOutputType, ref Type detectedType)
        {
            var type = UnwrapCollectionTargetType(columnOutputType);
            var sqlIdentifier = type.GetCustomAttribute<SqlIdentifierAttribute>();
            string tableName;
            if (sqlIdentifier != null)
            {
                tableName = this.databaseConfiguration.Tables.FindByName(sqlIdentifier.Name)?.Name;
            }
            else
            {
                tableName = this.pluralizationHelper.AllCandidates(type.Name).FirstOrDefault(tableName =>
                    this.databaseConfiguration.Tables.FindByName(tableName) != null);
            }
            
            if (tableName != null)
            {
                detectedType = type;
                return tableName;
            }

            return type.DeclaringType != null && type.DeclaringType.IsClass ? 
                DetectTableName(type.DeclaringType, ref detectedType) : 
                null;
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

        public string GetColumnName(IArgument argument)
        {
            var columnSpec = this.GetColumnSpec(argument);
            return columnSpec?.ColumnName ?? argument.Name;
        }

        private void BuildManyToManyTableRelations(IForeignKeyDefinition foreignKeyDefinition, TableRelations tableRelations, TableRelations navigationTableRelations)
        {
            var tableDefinition = foreignKeyDefinition.KeyPairs.First().ForeignTableColumn.Table;
            var navigationToManyToManyForeignKey = this.FindPrimaryForeignKeyMatchForTables(navigationTableRelations.TargetTable, tableDefinition);
            navigationTableRelations.ForeignKeyToParent = navigationToManyToManyForeignKey;

            var navigationTables = tableRelations.NavigationTables.ToList();
            navigationTables.Remove(navigationTableRelations);


            var manyToManyArgument = new TypeArgument(typeof(void), this);
            manyToManyArgument.Parent = tableRelations.Argument;

            var manyToManyTableRelations = new TableRelations()
            {
                Argument = manyToManyArgument,
                TargetTable = tableDefinition,
                NavigationTables = new []{ navigationTableRelations },
                ProjectedColumns = new List<TableRelationColumnDefinition>(),
                ForeignKeyToParent = foreignKeyDefinition,
                IsManyToMany = true
            };
            navigationTables.Add(manyToManyTableRelations);
            foreach (var navigationTable in manyToManyTableRelations.NavigationTables)
            {
                navigationTable.Parent = manyToManyTableRelations;
            }

            tableRelations.NavigationTables = navigationTables;
            
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

        internal IEnumerable<TableRelationColumnIdentifierDefinition> GetProjectedColumns(TableRelations tableRelations)
        {
            var columns = tableRelations.ProjectedColumns.ToList();
            var navigationColumns = tableRelations.NavigationTables.SelectMany(t =>
                GetProjectedColumns(t));

            return columns.Concat(navigationColumns).ToList();
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
