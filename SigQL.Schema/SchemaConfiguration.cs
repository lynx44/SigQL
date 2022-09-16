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
        public DatabaseConfiguration(ITableDefinitionCollection tables)
        {
            Tables = tables;
        }

        public ITableDefinitionCollection Tables { get; set; }
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
    }

    public class TableDefinitionCollection : ITableDefinitionCollection
    {
        private readonly IEnumerable<ITableDefinition> tableDefinitions;

        public TableDefinitionCollection(IEnumerable<ITableDefinition> tableDefinitions)
        {
            this.tableDefinitions = tableDefinitions;
        }

        public IEnumerator<ITableDefinition> GetEnumerator()
        {
            return tableDefinitions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) tableDefinitions).GetEnumerator();
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
        public TableDefinition(ISchemaDefinition schema, string name, IEnumerable<string> columnNames)
        {
            Schema = schema;
            Name = name;
            this.Columns = new TableColumnDefinitionCollection(this).AddColumns(columnNames.Select(c => new ColumnDefinition(c, this)));
            this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
        }

        public TableDefinition(ISchemaDefinition schema, string name, IEnumerable<ColumnDefinitionField> columns)
        {
            Schema = schema;
            Name = name;
            this.Columns = new TableColumnDefinitionCollection(this).AddColumns(columns.Select(c => new ColumnDefinition(c, this)));
            this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
        }

        public ISchemaDefinition Schema { get; set; }
        public string Name { get; set; }
        public ITableColumnDefinitionCollection Columns { get; set; }
        public IForeignKeyDefinitionCollection ForeignKeyCollection { get; set; }
        public ITableKeyDefinition PrimaryKey { get; set; }
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
        private readonly ITableDefinition table;
        private List<IColumnDefinition> columnDefinitions;

        public TableColumnDefinitionCollection(ITableDefinition table)
        {
            this.table = table;
            this.columnDefinitions = new List<IColumnDefinition>();
        }

        public TableColumnDefinitionCollection AddColumns(IEnumerable<IColumnDefinition> columns)
        {
            this.columnDefinitions.AddRange(columns);

            return this;
        }

        public IEnumerator<IColumnDefinition> GetEnumerator()
        {
            return columnDefinitions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) columnDefinitions).GetEnumerator();
        }
    }

    public interface IColumnDefinition
    {
        string Name { get; }
        string DataTypeDeclaration { get; }
        ITableDefinition Table { get; }
    }

    public class ColumnDefinition : IColumnDefinition
    {
        internal ColumnDefinition(string name)
        {
            Name = name;
        }

        public ColumnDefinition(string name, ITableDefinition table)
        {
            Name = name;
            Table = table;
        }

        public ColumnDefinition(ColumnDefinitionField field, ITableDefinition table)
        {
            this.Name = field.Name;
            this.DataTypeDeclaration = field.DataTypeDeclaration;
            this.Table = table;
        }

        public string Name { get; set; }
        public string DataTypeDeclaration { get; set; }
        public ITableDefinition Table { get; set; }
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
        private List<IForeignKeyDefinition> foreignKeyDefinitions;

        public ForeignKeyDefinitionCollection()
        {
            this.foreignKeyDefinitions = new List<IForeignKeyDefinition>();
        }

        public ForeignKeyDefinitionCollection AddForeignKeys(params IForeignKeyDefinition[] foreignKeys)
        {
            this.foreignKeyDefinitions.AddRange(foreignKeys);

            return this;
        }

        public IEnumerator<IForeignKeyDefinition> GetEnumerator()
        {
            return foreignKeyDefinitions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) foreignKeyDefinitions).GetEnumerator();
        }
    }

    public interface IForeignKeyDefinition
    {
        ITableDefinition PrimaryKeyTable { get; }
        IEnumerable<IForeignKeyPair> KeyPairs { get; }
    }

    public class ForeignKeyDefinition : IForeignKeyDefinition
    {
        public ForeignKeyDefinition(ITableDefinition primaryKeyTable, IEnumerable<IForeignKeyPair> keyPairs)
        {
            PrimaryKeyTable = primaryKeyTable;
            KeyPairs = keyPairs;
        }

        public ForeignKeyDefinition(ITableDefinition primaryKeyTable, params IForeignKeyPair[] keyPairs) : this(primaryKeyTable, (IEnumerable<IForeignKeyPair>) keyPairs)
        {
        }

        public ITableDefinition PrimaryKeyTable { get; set; }
        public IEnumerable<IForeignKeyPair> KeyPairs { get; set; }
    }

    public interface IForeignKeyPair
    {
        IColumnDefinition ForeignTableColumn { get; }
        IColumnDefinition PrimaryTableColumn { get; }
    }

    public class ForeignKeyPair : IForeignKeyPair
    {
        public ForeignKeyPair(IColumnDefinition foreignTableColumn, IColumnDefinition primaryTableColumn)
        {
            ForeignTableColumn = foreignTableColumn;
            PrimaryTableColumn = primaryTableColumn;
        }

        public IColumnDefinition ForeignTableColumn { get; set; }
        public IColumnDefinition PrimaryTableColumn { get; set; }
    }
    
    public interface ITableKeyDefinition
    {
        IEnumerable<IColumnDefinition> Columns { get; }
    }

    public class TableKeyDefinition : ITableKeyDefinition
    {
        public TableKeyDefinition(params IColumnDefinition[] columns)
        {
            this.Columns = columns.ToList();
        }
        public IEnumerable<IColumnDefinition> Columns { get; set; }
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
