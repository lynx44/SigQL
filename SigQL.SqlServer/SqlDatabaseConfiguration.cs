using System;
using System.Linq;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using SigQL.Schema;

namespace SigQL.SqlServer
{
    public class SqlDatabaseConfiguration : IDatabaseConfiguration
    {
        private ITableDefinitionCollection tables;

        public SqlDatabaseConfiguration(string connectionString)
        {
            var sqlConnBuilder = new SqlConnectionStringBuilder(connectionString);
            ReadDatabase(sqlConnBuilder.DataSource, sqlConnBuilder.InitialCatalog, sqlConnBuilder.UserID, sqlConnBuilder.Password);
        }

        public SqlDatabaseConfiguration(string serverName, string databaseName, string username = null, string password = null)
        {
            ReadDatabase(serverName, databaseName, username, password);
        }

        private void ReadDatabase(string serverName, string databaseName, string username = null, string password = null)
        {
            SqlConnectionInfo sqlConnectionInfo = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password) ? new SqlConnectionInfo(serverName, username, password) :  new SqlConnectionInfo(serverName);

            var server = new Server(new ServerConnection(sqlConnectionInfo));
            var database = server.Databases[databaseName];

            var allTables = database.Tables.Cast<Table>().Select(t => new SqlTableDefinition(t)).Cast<ITableDefinition>()
                .ToList();
            var allViews = database.Views.Cast<View>().Where(v => v.Schema != "sys").Select(t => new SqlViewDefinition(t))
                .Cast<ITableDefinition>().ToList();
            var allFunctions = database.UserDefinedFunctions.Cast<UserDefinedFunction>().Where(v => v.Schema != "sys").Select(t => new SqlFunctionDefinition(t))
                .Cast<ITableDefinition>().ToList();
            this.tables = new TableDefinitionCollection((allTables.Concat(allViews).Concat(allFunctions)).ToList());

            foreach (var sqlTable in database.Tables.Cast<Table>())
            {
                var tableDefinition = this.Tables.FindByName(sqlTable.Name) as SqlTableDefinition;
                var sqlTableForeignKeys = sqlTable.ForeignKeys.Cast<ForeignKey>().ToList();
                var foreignKeyDefinitions = sqlTableForeignKeys.Select(fk =>
                    new ForeignKeyDefinition(
                        this.tables.FindByName(fk.ReferencedTable),
                        fk.Columns.Cast<ForeignKeyColumn>().Select(c =>
                            new ForeignKeyPair(tableDefinition.Columns.FindByName(c.Name),
                                this.tables.FindByName(fk.ReferencedTable).Columns.FindByName(c.ReferencedColumn))).ToList())).ToList();

                tableDefinition.ForeignKeyCollection =
                    new ForeignKeyDefinitionCollection().Add(foreignKeyDefinitions.Cast<IForeignKeyDefinition>()
                        .ToArray());

                var primaryKeyColumnNames = sqlTable.Indexes.Cast<Index>()
                    .FirstOrDefault(i => i.IndexKeyType == IndexKeyType.DriPrimaryKey)?.IndexedColumns.Cast<IndexedColumn>()
                    .Select(c => c.Name).ToList();
                var primaryKeyColumns = tableDefinition.Columns
                    .Where(c => primaryKeyColumnNames != null && primaryKeyColumnNames.Contains(c.Name)).ToList();
                tableDefinition.PrimaryKey = new TableKeyDefinition() {Columns = primaryKeyColumns};
                // this.foreignKeyCollection = new ForeignKeyDefinitionCollection().AddForeignKeys();
            }
        }

        public ITableDefinitionCollection Tables => tables;
    }

    public class SqlTableDefinition : ITableDefinition
    {
        internal Table SqlTable => this.table;
        private readonly Table table;
        private readonly ITableColumnDefinitionCollection columns;

        public SqlTableDefinition(Table table)
        {
            this.table = table;

            var allColumns = this.table.Columns.Cast<Column>().Select(c => new SqlColumnDefinition(c, this)).ToList();
            this.columns = new TableColumnDefinitionCollection(this).AddColumns(allColumns);
        }

        public ISchemaDefinition Schema => new SchemaDefinition(table.Schema);
        public string Name => table.Name;

        public ITableColumnDefinitionCollection Columns => columns;

        public IForeignKeyDefinitionCollection ForeignKeyCollection { get; internal set; }
        public ITableKeyDefinition PrimaryKey { get; internal set; }
        public DatabaseObjectType ObjectType => DatabaseObjectType.Table;
    }

    public class SqlViewDefinition : ITableDefinition
    {
        internal View SqlView  => this.view;
        private readonly View view;
        private Lazy<TableColumnDefinitionCollection> columnsLazy;
        private Lazy<ITableKeyDefinition> primaryKeyDefinitionLazy;

        public SqlViewDefinition(View table)
        {
            this.view = table;
            
            columnsLazy = new Lazy<TableColumnDefinitionCollection>(() => this.BuildColumns());
            primaryKeyDefinitionLazy = new Lazy<ITableKeyDefinition>(() => this.BuildPrimaryKey());
            this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
        }

        private TableColumnDefinitionCollection BuildColumns()
        {
            var allColumns = this.view.Columns.Cast<Column>().Select(c => new SqlColumnDefinition(c, this)).ToList();
            return new TableColumnDefinitionCollection(this).AddColumns(allColumns);
        }

        private ITableKeyDefinition BuildPrimaryKey()
        {
            return new TableKeyDefinition();
        }

        public ISchemaDefinition Schema => new SchemaDefinition(view.Schema);
        public string Name => view.Name;

        public ITableColumnDefinitionCollection Columns => columnsLazy.Value;

        public IForeignKeyDefinitionCollection ForeignKeyCollection { get; internal set; }
        public ITableKeyDefinition PrimaryKey => primaryKeyDefinitionLazy.Value;
        public DatabaseObjectType ObjectType => DatabaseObjectType.View;
    }

    public class SqlFunctionDefinition : ITableDefinition
    {
        internal UserDefinedFunction SqlView  => this.view;
        private readonly UserDefinedFunction view;
        private Lazy<TableColumnDefinitionCollection> columnsLazy;
        private Lazy<ITableKeyDefinition> primaryKeyDefinitionLazy;

        public SqlFunctionDefinition(UserDefinedFunction table)
        {
            this.view = table;
            
            columnsLazy = new Lazy<TableColumnDefinitionCollection>(() => this.BuildColumns());
            primaryKeyDefinitionLazy = new Lazy<ITableKeyDefinition>(() => this.BuildPrimaryKey());
            this.ForeignKeyCollection = new ForeignKeyDefinitionCollection();
        }

        private TableColumnDefinitionCollection BuildColumns()
        {
            var allColumns = this.view.Columns.Cast<Column>().Select(c => new SqlColumnDefinition(c, this)).ToList();
            return new TableColumnDefinitionCollection(this).AddColumns(allColumns);
        }

        private ITableKeyDefinition BuildPrimaryKey()
        {
            return new TableKeyDefinition();
        }

        public ISchemaDefinition Schema => new SchemaDefinition(view.Schema);
        public string Name => view.Name;

        public ITableColumnDefinitionCollection Columns => columnsLazy.Value;

        public IForeignKeyDefinitionCollection ForeignKeyCollection { get; internal set; }
        public ITableKeyDefinition PrimaryKey => primaryKeyDefinitionLazy.Value;
        public DatabaseObjectType ObjectType => DatabaseObjectType.Function;
    }

    public class SqlColumnDefinition : IColumnDefinition
    {
        private readonly Column column;
        private readonly ITableDefinition tableDefinition;

        public SqlColumnDefinition(Column column, ITableDefinition tableDefinition)
        {
            this.column = column;
            var getTypeDefinitionScriptMethod = typeof(UserDefinedDataType).GetMethod("GetTypeDefinitionScript", BindingFlags.Static | BindingFlags.NonPublic);
            this.DataTypeDeclaration = getTypeDefinitionScriptMethod.Invoke(null,
                new object[] {new ScriptMaker().Preferences, column, "DataType", false}) as string;
            this.tableDefinition = tableDefinition;
        }

        public string Name => column.Name;
        public string DataTypeDeclaration { get; }
        public bool IsIdentity => column.Identity;
        public ITableDefinition Table => tableDefinition;
    }
}
