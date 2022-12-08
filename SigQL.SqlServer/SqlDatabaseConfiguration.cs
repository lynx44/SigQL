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

                
                //var columns = connection.GetSchema("Columns", new string[] {null, null, null});

                var columnsQuery =
                    @"SELECT o.name TABLE_NAME, 
c.name COLUMN_NAME, 
c.max_length CHARACTER_MAXIMUM_LENGTH, 
c.precision ""PRECISION"", 
c.scale SCALE, 
c.is_nullable,
st.name AS DATA_TYPE
FROM sys.objects o
INNER JOIN sys.columns c ON o.object_id = c.object_id
INNER JOIN sys.types st ON c.system_type_id = st.system_type_id
AND c.user_type_id = st.user_type_id
WHERE o.is_ms_shipped = 0;";
                var columns = new DataTable();
                using (var selectCommand = new SqlCommand(columnsQuery, connection))
                {
                    var sqlDataAdapter = new SqlDataAdapter(selectCommand);
                    sqlDataAdapter.Fill(columns);
                }

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
                    var table = (ISqlObjectWithColumns) this.tables.FindByName(tableName);
                    ((TableColumnDefinitionCollection) table.Columns).AddColumns(
                        new SqlColumnDefinition(row, table, identityDataTable.Rows.Cast<DataRow>().Any(r => (string) r["TableName"] == tableName && (string) r["ColumnName"] == (string) row["COLUMN_NAME"])).AsEnumerable());
                }

                var primaryKeysByTable = pkTable.Rows.Cast<DataRow>().GroupBy(d => (string) d["table_name"]);
                foreach (var primaryKeyColumns in primaryKeysByTable)
                {
                    var table = (SqlTableDefinition) this.Tables.FindByName(primaryKeyColumns.Key);
                    var columnDefinitions = primaryKeyColumns.Select(c => table.Columns.FindByName((string) c["column_name"])).ToArray();
                    table.PrimaryKey = new TableKeyDefinition(columnDefinitions);
                }

                // Query sys.foreign_keys for foreign key information for the current table
                var query = "SELECT fk.name AS FK_NAME, " +
                            "col.name AS FK_COLUMN_NAME, " +
                            "OBJECT_NAME(fk.referenced_object_id) AS PK_TABLE_NAME, " +
                            "col_pk.name AS PK_COLUMN_NAME, " +
                            "OBJECT_NAME(fk.parent_object_id) AS FK_TABLE_NAME " +
                            "FROM sys.foreign_keys AS fk " +
                            "INNER JOIN sys.foreign_key_columns AS fkc " +
                            "ON fkc.constraint_object_id = fk.object_id " +
                            "INNER JOIN sys.columns AS col " +
                            "ON col.object_id = fkc.parent_object_id " +
                            "AND col.column_id = fkc.parent_column_id " +
                            "INNER JOIN sys.columns AS col_pk " +
                            "ON col_pk.object_id = fkc.referenced_object_id " +
                            "AND col_pk.column_id = fkc.referenced_column_id ";
                var fkDataTable = new DataTable();
                using (var selectCommand = new SqlCommand(query, connection))
                {
                    var sqlDataAdapter = new SqlDataAdapter(selectCommand);
                    sqlDataAdapter.Fill(fkDataTable);
                }

                //var foreignKeyNames = fkDataTable.Rows.Cast<DataRow>().Select(r => (string) r["FK_NAME"]).Distinct();
                foreach (var tableName in fkDataTable.Rows.Cast<DataRow>().Select(r => (string) r["FK_TABLE_NAME"]).Distinct().ToList())
                {
                    //var fkRows = fkDataTable.Rows.Cast<DataRow>().Where(r => (string) r["FK_NAME"] == fkName).ToList();
                    //var tableName = fkRows.First()["TABLE_NAME"].ToString();
                    var table = (SqlTableDefinition) this.tables.FindByName(tableName);

                    var tableFkRows = fkDataTable.Rows.Cast<DataRow>().Where(r => (string) r["FK_TABLE_NAME"] == tableName).ToList();
                    var tableFkGroups = tableFkRows.GroupBy(r => (string) r["FK_NAME"]);

                    // Loop through the rows in the table and build foreign key information
                    foreach (var fkRow in tableFkGroups)
                    {
                        // Get foreign key columns
                        var fkColumnDefinitions = fkRow.Select(r => table.Columns.FindByName((string) r["FK_COLUMN_NAME"])).ToList();

                        // Get referenced primary key table
                        var fkReferenceTable = this.tables.FindByName(fkRow.First()["PK_TABLE_NAME"].ToString());

                        // Get primary key columns
                        var fkReferenceColumnDefinitions =
                            fkRow.Select(r => fkReferenceTable.Columns.FindByName((string)r["PK_COLUMN_NAME"])).ToList(); ;

                        // Zip the foreign key and primary key columns together and create a list of ForeignKeyPair objects
                        var fkPairs = fkColumnDefinitions
                            .Zip(fkReferenceColumnDefinitions, (c, r) => new ForeignKeyPair(c, r)).ToList();
                        var fkDefinition = new ForeignKeyDefinition(fkReferenceTable, fkPairs);
                        ((ForeignKeyDefinitionCollection) table.ForeignKeyCollection).AddForeignKeys(fkDefinition);
                    }
                }
            }
        }

        public ITableDefinitionCollection Tables => tables;
    }

    internal interface ISqlObjectWithColumns: ITableDefinition {
        public TableColumnDefinitionCollection Columns { get; internal set; }
    }
    public class SqlTableDefinition : ITableDefinition, ISqlObjectWithColumns
    {
        private TableColumnDefinitionCollection columns;

        public SqlTableDefinition(string schemaName, string tableName)
        {
            this.columns = new TableColumnDefinitionCollection(this);
            this.Name = tableName;
            this.Schema = new SchemaDefinition(schemaName);
            this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
            this.PrimaryKey = new TableKeyDefinition();
        }

    public ISchemaDefinition Schema { get; }
    public string Name { get; }

    public ITableColumnDefinitionCollection Columns => columns;

    public IForeignKeyDefinitionCollection ForeignKeyCollection { get; internal set; }
    public ITableKeyDefinition PrimaryKey { get; internal set; }
    public DatabaseObjectType ObjectType => DatabaseObjectType.Table;

    TableColumnDefinitionCollection ISqlObjectWithColumns.Columns
    {
        get => columns;
        set => columns = value;
    }
}

public class SqlViewDefinition : ITableDefinition, ISqlObjectWithColumns
{
    private TableColumnDefinitionCollection columns;
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
        
    TableColumnDefinitionCollection ISqlObjectWithColumns.Columns
    {
        get => columns;
        set => columns = value;
    }
    }

public class SqlFunctionDefinition : ITableDefinition, ISqlObjectWithColumns
{
    private TableColumnDefinitionCollection columns;
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
        
    TableColumnDefinitionCollection ISqlObjectWithColumns.Columns
    {
        get => columns;
        set => columns = value;
    }
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
                    case "binary": return $"binary({ConvertCharacterLength(row["CHARACTER_MAXIMUM_LENGTH"])})";
                    case "bit": return "bit";
                    case "char": return $"char({ConvertCharacterLength(row["CHARACTER_MAXIMUM_LENGTH"])})";
                    case "date": return "date";
                    case "datetime": return "datetime";
                    case "datetime2": return $"datetime2({row["SCALE"]})";
                    case "datetimeoffset": return $"datetimeoffset({row["SCALE"]})";
                    case "decimal": return $"decimal({row["PRECISION"]}, {row["SCALE"]})";
                    case "float": return "float";
                    case "image": return "image";
                    case "int": return "int";
                    case "money": return "money";
                    case "nchar": return $"nchar({ConvertCharacterLength(row["CHARACTER_MAXIMUM_LENGTH"])})";
                    case "ntext": return "ntext";
                    case "numeric": return $"numeric({row["PRECISION"]}, {row["SCALE"]})";
                    case "nvarchar": return $"nvarchar({ConvertCharacterLength(row["CHARACTER_MAXIMUM_LENGTH"])})";
                    case "real": return "real";
                    case "smalldatetime": return "smalldatetime";
                    case "smallint": return "smallint";
                    case "smallmoney": return "smallmoney";
                    case "text": return "text";
                    case "time": return $"time({row["SCALE"]})";
                    case "tinyint": return "tinyint";
                    case "uniqueidentifier": return "uniqueidentifier";
                    case "varbinary": return $"varbinary({ConvertCharacterLength(row["CHARACTER_MAXIMUM_LENGTH"])})";
                    case "varchar": return $"varchar({ConvertCharacterLength(row["CHARACTER_MAXIMUM_LENGTH"])})";
                    default: throw new NotImplementedException(dataType);
                }
            }
        }

        private string ConvertCharacterLength(object maxLength)
        {
            return maxLength != DBNull.Value ? 
                (Int16)maxLength >= 0 ?
                    maxLength.ToString() : 
                    "max" : 
                string.Empty;
        }
    }
}