using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SigQL.Schema
{
    public interface IDatabaseConfiguration
    {
        ITableDefinitionCollection Tables { get; }
    }

    public class DatabaseConfiguration : IDatabaseConfiguration
    {
        public DatabaseConfiguration(List<TableDefinition> tables)
        {
            Tables = new TableDefinitionCollection(tables);
        }

        ITableDefinitionCollection IDatabaseConfiguration.Tables => this.Tables;
        public TableDefinitionCollection Tables { get; }
    }

    public interface ITableDefinitionCollection : IEnumerable<ITableDefinition>
    {
    }

    public static class ITableDefinitionCollectionExtensions
    {
        public static ITableDefinition FindByName(this ITableDefinitionCollection tables, string tableName)
        {
            return tables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.InvariantCultureIgnoreCase));
        }
        public static TableDefinition FindByName(this List<TableDefinition> tables, string tableName)
        {
            return tables.FirstOrDefault(t => t.Name.Equals(tableName, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class TableDefinitionCollection : ITableDefinitionCollection
    {
        private List<ITableDefinition> TableDefinitions { get; }

        public void Add(params TableDefinition[] tableDefinitions)
        {
            TableDefinitions.AddRange(tableDefinitions);
        }

        public void Remove(params TableDefinition[] tableDefinitions)
        {
            foreach (var tableDefinition in tableDefinitions)
            {
                TableDefinitions.Remove(tableDefinition);
            }
        }

        public TableDefinitionCollection(IEnumerable<ITableDefinition> tableDefinitions)
        {
            this.TableDefinitions = tableDefinitions.ToList();
        }

        public IEnumerator<ITableDefinition> GetEnumerator()
        {
            return TableDefinitions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) TableDefinitions).GetEnumerator();
        }
    }

    public interface ISchemaDefinition
    {
        string Name { get; }
    }

    public class SchemaDefinition : ISchemaDefinition
    {
        public SchemaDefinition(string name)
        {
            Name = name;
        }

        public string Name { get; set; }
    }

    public interface ITableDefinition
    {
        ISchemaDefinition Schema { get; }
        string Name { get; }
        ITableColumnDefinitionCollection Columns { get; }
        IForeignKeyDefinitionCollection ForeignKeyCollection { get; }
        ITableKeyDefinition PrimaryKey { get; }
        DatabaseObjectType ObjectType { get; }
    }

    public enum DatabaseObjectType
    {
        Table,
        View,
        Function
    }

    public class TableDefinition : ITableDefinition
    {
        public TableDefinition(SchemaDefinition schema, string name, IEnumerable<string> columnNames)
            : this(schema, name, columnNames.Select(c => new ColumnDefinitionField() { Name = c }))
        {
            //Schema = schema;
            //Name = name;
            //this.Columns = new TableColumnDefinitionCollection(this).AddColumns(columnNames.Select(c => new ColumnDefinition(c, this)));
            //this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
        }

        public TableDefinition(SchemaDefinition schema, string name, IEnumerable<ColumnDefinitionField> columns)
        {
            Schema = schema;
            Name = name;
            this.Columns = new TableColumnDefinitionCollection(this).AddColumns(columns.Select(c => new ColumnDefinition(c, this)).ToList());
            this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
        }

        ISchemaDefinition ITableDefinition.Schema => this.Schema;
        public SchemaDefinition Schema { get; set; }
        public string Name { get; set; }
        ITableColumnDefinitionCollection ITableDefinition.Columns => this.Columns;
        public TableColumnDefinitionCollection Columns { get; }
        IForeignKeyDefinitionCollection ITableDefinition.ForeignKeyCollection => this.ForeignKeyCollection;
        public ForeignKeyDefinitionCollection ForeignKeyCollection { get; }
        ITableKeyDefinition ITableDefinition.PrimaryKey => this.PrimaryKey;
        public TableKeyDefinition PrimaryKey { get; set; }
        public DatabaseObjectType ObjectType { get; set; }
    }

    public interface ITableColumnDefinitionCollection : IEnumerable<IColumnDefinition>
    {
    }

    public static class ITableColumnDefinitionCollectionExtensions
    {
        public static IColumnDefinition FindByName(this ITableColumnDefinitionCollection columns, string columnName)
        {
            return columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class TableColumnDefinitionCollection : ITableColumnDefinitionCollection
    {
        private readonly TableDefinition table;
        private List<ColumnDefinition> ColumnDefinitions { get; }

        public void Add(params ColumnDefinition[] columns)
        {
            foreach (var column in columns)
            {
                column.Table = table;
            }
            
            this.ColumnDefinitions.AddRange(columns);
        }

        public void Remove(params ColumnDefinition[] columns)
        {
            foreach (var column in columns)
            {
                this.ColumnDefinitions.Remove(column);
            }
        }

        public TableColumnDefinitionCollection(TableDefinition table)
        {
            this.table = table;
            this.ColumnDefinitions = new List<ColumnDefinition>();
        }

        public TableColumnDefinitionCollection AddColumns(IEnumerable<ColumnDefinition> columns)
        {
            this.ColumnDefinitions.AddRange(columns);

            return this;
        }

        public IEnumerator<IColumnDefinition> GetEnumerator()
        {
            return ColumnDefinitions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) ColumnDefinitions).GetEnumerator();
        }
    }

    public interface IColumnDefinition
    {
        string Name { get; }
        string DataTypeDeclaration { get; }
        bool IsIdentity { get; }
        ITableDefinition Table { get; }
    }

    public class ColumnDefinition : IColumnDefinition
    {
        internal ColumnDefinition(string name)
        {
            Name = name;
        }

        public ColumnDefinition(string name, TableDefinition table)
        {
            Name = name;
            Table = table;
        }

        public ColumnDefinition(ColumnDefinitionField field, TableDefinition table)
        {
            this.Name = field.Name;
            this.DataTypeDeclaration = field.DataTypeDeclaration;
            this.Table = table;
        }

        public string Name { get; set; }
        public string DataTypeDeclaration { get; set; }
        public bool IsIdentity { get; set; }
        ITableDefinition IColumnDefinition.Table => this.Table;
        public TableDefinition Table { get; set; }
    }

    public class ColumnDefinitionField
    {
        public string Name { get; set; }
        public string DataTypeDeclaration { get; set; }
    }

    public interface IForeignKeyDefinitionCollection : IEnumerable<IForeignKeyDefinition>
    {
    }

    public static class IForeignKeyDefinitionCollectionExtensions
    {
        public static IEnumerable<IForeignKeyDefinition> FindForTable(this IForeignKeyDefinitionCollection foreignKeys, string tableName)
        {
            return foreignKeys.Where(fk => fk.PrimaryKeyTable.Name.Equals(tableName, StringComparison.InvariantCultureIgnoreCase));
        }

        public static IForeignKeyDefinition FindPrimaryMatchForTable(this IForeignKeyDefinitionCollection foreignKeys, string tableName)
        {
            return foreignKeys.OrderBy(fk => fk.KeyPairs.Count()).FirstOrDefault(fk => fk.PrimaryKeyTable.Name.Equals(tableName, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    public class ForeignKeyDefinitionCollection : IForeignKeyDefinitionCollection
    {
        private List<ForeignKeyDefinition> ForeignKeyDefinitions { get; }
        
        public ForeignKeyDefinitionCollection Add(params ForeignKeyDefinition[] foreignKeys)
        {
            this.ForeignKeyDefinitions.AddRange(foreignKeys);

            return this;
        }

        public void Remove(params ForeignKeyDefinition[] foreignKeyDefinitions)
        {
            foreach (var foreignKeyDefinition in foreignKeyDefinitions)
            {
                this.ForeignKeyDefinitions.Remove(foreignKeyDefinition);
            }
        }

        public ForeignKeyDefinitionCollection()
        {
            this.ForeignKeyDefinitions = new List<ForeignKeyDefinition>();
        }

        public IEnumerator<IForeignKeyDefinition> GetEnumerator()
        {
            return ForeignKeyDefinitions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) ForeignKeyDefinitions).GetEnumerator();
        }
    }

    public interface IForeignKeyDefinition
    {
        ITableDefinition PrimaryKeyTable { get; }
        IEnumerable<IForeignKeyPair> KeyPairs { get; }
    }

    public class ForeignKeyDefinition : IForeignKeyDefinition
    {
        public ForeignKeyDefinition(TableDefinition primaryKeyTable, IEnumerable<ForeignKeyPair> keyPairs)
        {
            PrimaryKeyTable = primaryKeyTable;
            KeyPairs = keyPairs.ToList();
        }

        public ForeignKeyDefinition(TableDefinition primaryKeyTable, params ForeignKeyPair[] keyPairs) : this(primaryKeyTable, (IEnumerable<ForeignKeyPair>) keyPairs)
        {
        }

        ITableDefinition IForeignKeyDefinition.PrimaryKeyTable => this.PrimaryKeyTable;
        public TableDefinition PrimaryKeyTable { get; set; }
        IEnumerable<IForeignKeyPair> IForeignKeyDefinition.KeyPairs => this.KeyPairs;
        public List<ForeignKeyPair> KeyPairs { get; }
    }

    public static class ForeignKeyDefinitionExtensions
    {
        public static IEnumerable<IColumnDefinition> GetForeignColumns(this IForeignKeyDefinition foreignKey)
        {
            return foreignKey.KeyPairs.Select(kp => kp.ForeignTableColumn).ToList();
        }
    }

    public interface IForeignKeyPair
    {
        IColumnDefinition ForeignTableColumn { get; }
        IColumnDefinition PrimaryTableColumn { get; }
    }

    public class ForeignKeyPair : IForeignKeyPair
    {
        public ForeignKeyPair(ColumnDefinition foreignTableColumn, ColumnDefinition primaryTableColumn)
        {
            ForeignTableColumn = foreignTableColumn;
            PrimaryTableColumn = primaryTableColumn;
        }

        IColumnDefinition IForeignKeyPair.ForeignTableColumn => this.ForeignTableColumn;
        public ColumnDefinition ForeignTableColumn { get; set; }
        IColumnDefinition IForeignKeyPair.PrimaryTableColumn => this.PrimaryTableColumn;
        public ColumnDefinition PrimaryTableColumn { get; set; }
    }
    
    public interface ITableKeyDefinition
    {
        IEnumerable<IColumnDefinition> Columns { get; }
    }

    public class TableKeyDefinition : ITableKeyDefinition
    {
        public TableKeyDefinition(params ColumnDefinition[] columns)
        {
            this.Columns = columns.ToList();
        }

        IEnumerable<IColumnDefinition> ITableKeyDefinition.Columns => this.Columns;
        public List<ColumnDefinition> Columns { get; }
    }

    public class TableEqualityComparer : IEqualityComparer<ITableDefinition>
    {
        private readonly static TableEqualityComparer _tableEqualityComparer = new TableEqualityComparer();

        public static TableEqualityComparer Default
        {
            get
            {
                return _tableEqualityComparer;
            }
        }

        public bool Equals(ITableDefinition x, ITableDefinition y)
        {
            return x.Name == y.Name && SchemaEqualityComparer.Default.Equals(x.Schema, y.Schema);
        }

        public int GetHashCode(ITableDefinition obj)
        {
            return new Tuple<string, string>(obj.Schema.Name, obj.Name).GetHashCode();
        }
    }

    public class SchemaEqualityComparer : IEqualityComparer<ISchemaDefinition>
    {
        private static readonly SchemaEqualityComparer _schemaEqualityComparer = new SchemaEqualityComparer();

        public static SchemaEqualityComparer Default
        {
            get { return _schemaEqualityComparer; }
        }

        public bool Equals(ISchemaDefinition x, ISchemaDefinition y)
        {
            return x.Name == y.Name;
        }

        public int GetHashCode(ISchemaDefinition obj)
        {
            return obj.Name.GetHashCode();
        }
    }

    public class ColumnEqualityComparer : IEqualityComparer<IColumnDefinition>
    {
        private readonly static ColumnEqualityComparer _columnEqualityComparer = new ColumnEqualityComparer();

        public static ColumnEqualityComparer Default
        {
            get
            {
                return _columnEqualityComparer;
            }
        }

        public bool Equals(IColumnDefinition x, IColumnDefinition y)
        {
            return x.Name == y.Name && TableEqualityComparer.Default.Equals(x.Table, y.Table);
        }

        public int GetHashCode(IColumnDefinition obj)
        {
            return new Tuple<string, string, string>(obj.Table.Schema.Name, obj.Table.Name, obj.Name).GetHashCode();
        }
    }

    public class ForeignKeyDefinitionEqualityComparer : IEqualityComparer<IForeignKeyDefinition>
    {
        private readonly static ForeignKeyDefinitionEqualityComparer _foreignKeyEqualityComparer = new ForeignKeyDefinitionEqualityComparer();
        
        public static ForeignKeyDefinitionEqualityComparer Default
        {
            get
            {
                return _foreignKeyEqualityComparer;
            }
        }

        public bool Equals(IForeignKeyDefinition x, IForeignKeyDefinition y)
        {
            return TableEqualityComparer.Default.Equals(x.PrimaryKeyTable, y.PrimaryKeyTable) && 
                   x.KeyPairs.Count() == y.KeyPairs.Count() &&
                   x.KeyPairs.All(xkp => 
                       y.KeyPairs.Any(ykp => 
                           ColumnEqualityComparer.Default.Equals(ykp.PrimaryTableColumn, xkp.PrimaryTableColumn) 
                           && ColumnEqualityComparer.Default.Equals(ykp.ForeignTableColumn, xkp.ForeignTableColumn)));
        }

        public int GetHashCode(IForeignKeyDefinition obj)
        {
            return new Tuple<string, IEnumerable<Tuple<string, string, string>>>(obj.PrimaryKeyTable.Name, obj.KeyPairs.Select(c => new Tuple<string, string, string>(c.PrimaryTableColumn.Name, c.ForeignTableColumn.Name, c.ForeignTableColumn.Table.Name))).GetHashCode();
        }
    }
}
