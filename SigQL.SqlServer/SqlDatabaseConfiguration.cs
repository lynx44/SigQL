using System;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using SigQL.Extensions;
using SigQL.Schema;

namespace SigQL.SqlServer
{
    public class SqlDatabaseConfiguration : IDatabaseConfiguration
    {
        private ITableDefinitionCollection tables;

        public SqlDatabaseConfiguration(string connectionString)
        {
            ReadDatabase(connectionString);
        }

        //public SqlDatabaseConfiguration(string serverName, string databaseName, string username = null,
        //    string password = null)
        //{
        //    ReadDatabase(serverName, databaseName, username, password);
        //}

        private void ReadDatabase(string connectionString)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                var tables = connection.GetSchema("Tables", new string[] {null, null, null});
                var views = connection.GetSchema("Views", new string[] {null, null, null});
                var functions = connection.GetSchema("Procedures", new string[] {null, null, null}).Rows.Cast<DataRow>()
                    .Where(r => ((string) r["ROUTINE_TYPE"]).Equals("function",
                        StringComparison.InvariantCultureIgnoreCase)).ToList();
                

                this.tables = new TableDefinitionCollection(
                    tables.Rows.Cast<DataRow>()
                        .Select(t => new SqlTableDefinition(t["TABLE_SCHEMA"].ToString(), t["TABLE_NAME"].ToString()))
                        .Concat<ITableDefinition>(views.Rows.Cast<DataRow>()
                            .Select(v =>
                                new SqlViewDefinition(v["TABLE_SCHEMA"].ToString(), v["TABLE_NAME"].ToString())))
                        .Concat(functions
                            .Select(f =>
                                new SqlFunctionDefinition(f["ROUTINE_SCHEMA"].ToString(),
                                    f["ROUTINE_NAME"].ToString())))
                        .ToList());

                
                var columns = connection.GetSchema("Columns", new string[] {null, null, null});
                var identityQuery = @"SELECT
                t.name AS TableName,
                    c.name AS ColumnName,
                    c.is_identity
                    FROM
                sys.tables AS t
                    INNER JOIN sys.columns AS c
                    ON t.object_id = c.object_id
                WHERE
                c.is_identity = 1
                ";
                var identityDataTable = new DataTable();
                using (var selectCommand = new SqlCommand(identityQuery, connection))
                {
                    var sqlDataAdapter = new SqlDataAdapter(selectCommand);
                    sqlDataAdapter.Fill(identityDataTable);
                }
                var pkQuery = @"SELECT
                    t.name AS table_name,
                    c.name AS column_name
                FROM
                    sys.tables AS t
                INNER JOIN
                    sys.indexes AS i
                ON
                    t.object_id = i.object_id
                INNER JOIN
                    sys.index_columns AS ic
                ON
                    i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN
                    sys.columns AS c
                ON
                    ic.object_id = c.object_id AND ic.column_id = c.column_id
                WHERE
                    i.is_primary_key = 1

                ";
                var pkTable = new DataTable();
                using (var selectCommand = new SqlCommand(pkQuery, connection))
                {
                    var sqlDataAdapter = new SqlDataAdapter(selectCommand);
                    sqlDataAdapter.Fill(pkTable);
                }
                foreach (DataRow row in columns.Rows)
                {
                    var tableName = row["TABLE_NAME"].ToString();
                    var table = (SqlTableDefinition) this.tables.FindByName(tableName);
                    ((TableColumnDefinitionCollection) table.Columns).AddColumns(
                        new SqlColumnDefinition(row, table, identityDataTable.Rows.Cast<DataRow>().Any(r => r["TableName"] == tableName && r["ColumnName"] == row["COLUMN_NAME"])).AsEnumerable());
                }

                var primaryKeysByTable = pkTable.Rows.Cast<DataRow>().GroupBy(d => (string) d["table_name"]);
                foreach (var primaryKeyColumns in primaryKeysByTable)
                {
                    var table = (SqlTableDefinition) this.Tables.FindByName(primaryKeyColumns.Key);
                    var columnDefinitions = primaryKeyColumns.Select(c => table.Columns.FindByName((string) c["column_name"])).ToArray();
                    table.PrimaryKey = new TableKeyDefinition(columnDefinitions);
                }

                var foreignKeys = connection.GetSchema("ForeignKeys", new string[] {null, null, null});
                foreach (DataRow row in foreignKeys.Rows)
                {
                    var tableName = row["TABLE_NAME"].ToString();
                    var table = (SqlTableDefinition) this.tables.FindByName(tableName);

                    // Query sys.foreign_keys for foreign key information for the current table
                    var query = "SELECT fk.name AS FK_NAME, " +
                                "col.name AS FK_COLUMN_NAME, " +
                                "OBJECT_NAME(fk.referenced_object_id) AS PK_TABLE_NAME, " +
                                "col_pk.name AS PK_COLUMN_NAME " +
                                "FROM sys.foreign_keys AS fk " +
                                "INNER JOIN sys.foreign_key_columns AS fkc " +
                                "ON fkc.constraint_object_id = fk.object_id " +
                                "INNER JOIN sys.columns AS col " +
                                "ON col.object_id = fkc.parent_object_id " +
                                "AND col.column_id = fkc.parent_column_id " +
                                "INNER JOIN sys.columns AS col_pk " +
                                "ON col_pk.object_id = fkc.referenced_object_id " +
                                "AND col_pk.column_id = fkc.referenced_column_id " +
                                $"WHERE OBJECT_NAME(fk.parent_object_id) = '{tableName}'";
                    var fkDataTable = new DataTable();
                    using (var selectCommand = new SqlCommand(query, connection))
                    {
                        var sqlDataAdapter = new SqlDataAdapter(selectCommand);
                        sqlDataAdapter.Fill(fkDataTable);
                    }

                    // Loop through the rows in the table and build foreign key information
                    foreach (DataRow fkRow in fkDataTable.Rows)
                    {
                        // Get foreign key columns
                        var fkColumns = fkRow["FK_COLUMN_NAME"].ToString();
                        var fkColumnDefinitions = table.Columns.FindByName(fkColumns).AsEnumerable();

                        // Get referenced primary key table
                        var fkReferenceTable = this.tables.FindByName(fkRow["PK_TABLE_NAME"].ToString());

                        // Get primary key columns
                        var fkReferenceColumns = fkRow["PK_COLUMN_NAME"].ToString().Split(',').Select(x => x.Trim())
                            .ToList();
                        var fkReferenceColumnDefinitions =
                            fkReferenceColumns.Select(x => fkReferenceTable.Columns.FindByName(x)).ToList();

                        // Zip the foreign key and primary key columns together and create a list of ForeignKeyPair objects
                        var fkPairs = fkColumnDefinitions
                            .Zip(fkReferenceColumnDefinitions, (c, r) => new ForeignKeyPair(c, r)).ToList();
                        var fkDefinition = new ForeignKeyDefinition(fkReferenceTable, fkPairs);
                        table.ForeignKeyCollection ??= new ForeignKeyDefinitionCollection();
                        ((ForeignKeyDefinitionCollection) table.ForeignKeyCollection).AddForeignKeys(fkDefinition);
                    }
                }
            }
        }

        public ITableDefinitionCollection Tables => tables;
    }
    public class SqlTableDefinition : ITableDefinition
    {
        private readonly ITableColumnDefinitionCollection columns;


        public SqlTableDefinition(string schemaName, string tableName)
        {
            this.columns = new TableColumnDefinitionCollection(this);
            this.Name = tableName;
            this.Schema = new SchemaDefinition(schemaName);
        }

    public ISchemaDefinition Schema { get; }
    public string Name { get; }

    public ITableColumnDefinitionCollection Columns => columns;

    public IForeignKeyDefinitionCollection ForeignKeyCollection { get; internal set; }
    public ITableKeyDefinition PrimaryKey { get; internal set; }
    public DatabaseObjectType ObjectType => DatabaseObjectType.Table;
}

public class SqlViewDefinition : ITableDefinition
{
    private readonly TableColumnDefinitionCollection columns;
    private readonly ITableKeyDefinition primaryKeyDefinition;

    public SqlViewDefinition(string schema, string viewName)
    {
        this.columns = new TableColumnDefinitionCollection(this);
        this.primaryKeyDefinition = new TableKeyDefinition();
        this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
        this.Name = viewName;
        this.Schema = new SchemaDefinition(schema);
    }

    public ISchemaDefinition Schema { get; }
    public string Name { get; }

    public ITableColumnDefinitionCollection Columns => columns;

    public IForeignKeyDefinitionCollection ForeignKeyCollection { get; internal set; }
    public ITableKeyDefinition PrimaryKey => primaryKeyDefinition;
    public DatabaseObjectType ObjectType => DatabaseObjectType.View;
}

public class SqlFunctionDefinition : ITableDefinition
{
    private readonly TableColumnDefinitionCollection columns;
    private readonly ITableKeyDefinition primaryKeyDefinition;

    public SqlFunctionDefinition(string schema, string name)
    {
        this.Name = name;
        this.Schema = new SchemaDefinition(schema);
        this.columns = new TableColumnDefinitionCollection(this);
        this.primaryKeyDefinition = new TableKeyDefinition();
        this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
    }

    public ISchemaDefinition Schema { get; }
    public string Name { get; }

    public ITableColumnDefinitionCollection Columns => columns;

    public IForeignKeyDefinitionCollection ForeignKeyCollection { get; internal set; }
    public ITableKeyDefinition PrimaryKey => primaryKeyDefinition;
    public DatabaseObjectType ObjectType => DatabaseObjectType.Function;
}
    public class SqlColumnDefinition : IColumnDefinition
    {
        private readonly DataRow row;
        private readonly ITableDefinition tableDefinition;

        public SqlColumnDefinition(DataRow row, ITableDefinition tableDefinition, bool isIdentity)
        {
            this.row = row;
            this.tableDefinition = tableDefinition;
            this.IsIdentity = isIdentity;
        }

        public string Name => (string) row["COLUMN_NAME"];
        public bool IsIdentity { get; }
        public ITableDefinition Table => this.tableDefinition;

        public string DataTypeDeclaration
        {
            get
            {
                string dataType = row["DATA_TYPE"].ToString();
                switch (dataType)
                {
                    case "bigint": return "bigint";
                    case "binary": return $"binary({row["CHARACTER_MAXIMUM_LENGTH"]})";
                    case "bit": return "bit";
                    case "char": return $"char({row["CHARACTER_MAXIMUM_LENGTH"]})";
                    case "date": return "date";
                    case "datetime": return "datetime";
                    case "datetime2": return $"datetime2({row["DATETIME_PRECISION"]})";
                    case "datetimeoffset": return $"datetimeoffset({row["DATETIME_PRECISION"]})";
                    case "decimal": return $"decimal({row["NUMERIC_PRECISION"]}, {row["NUMERIC_SCALE"]})";
                    case "float": return "float";
                    case "image": return "image";
                    case "int": return "int";
                    case "money": return "money";
                    case "nchar": return $"nchar({row["CHARACTER_MAXIMUM_LENGTH"]})";
                    case "ntext": return "ntext";
                    case "numeric": return $"numeric({row["NUMERIC_PRECISION"]}, {row["NUMERIC_SCALE"]})";
                    case "nvarchar": return $"nvarchar({row["CHARACTER_MAXIMUM_LENGTH"]})";
                    case "real": return "real";
                    case "smalldatetime": return "smalldatetime";
                    case "smallint": return "smallint";
                    case "smallmoney": return "smallmoney";
                    case "text": return "text";
                    case "time": return $"time({row["DATETIME_PRECISION"]})";
                    case "tinyint": return "tinyint";
                    case "uniqueidentifier": return "uniqueidentifier";
                    case "varbinary": return $"varbinary({row["CHARACTER_MAXIMUM_LENGTH"]})";
                    case "varchar": return $"varchar({row["CHARACTER_MAXIMUM_LENGTH"]})";
                    default: throw new NotImplementedException(dataType);
                }
            }
        }
    }
}