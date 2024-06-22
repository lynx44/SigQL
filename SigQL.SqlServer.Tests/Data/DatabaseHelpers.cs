using Microsoft.Data.SqlClient;

namespace SigQL.SqlServer.Tests.Data
{
    class DatabaseHelpers
    {
        public static void RunCommand(SqlCommand sqlCommand)
        {
            sqlCommand.Connection.Open();
            try
            {
                
                sqlCommand.ExecuteNonQuery();
            }
            finally
            {
                sqlCommand.Connection.Close();
            }
        }

        public static void DropAllObjects(SqlConnection connection)
        {
            var sqlCommand = new SqlCommand(@"
            DECLARE @sql NVARCHAR(MAX);
            SET @sql = N'';

            SELECT @sql = @sql + N'
            ALTER TABLE ' + QUOTENAME(s.name) + N'.'
            + QUOTENAME(t.name) + N' DROP CONSTRAINT '
                + QUOTENAME(c.name) + ';'
            FROM sys.objects AS c
                INNER JOIN sys.tables AS t
                ON c.parent_object_id = t.[object_id]
            INNER JOIN sys.schemas AS s 
                ON t.[schema_id] = s.[schema_id]
            WHERE c.[type] IN ('D','C','F','PK','UQ')
            ORDER BY c.[type];

            EXEC sys.sp_executesql @sql;

            -- Then drop all tables

                exec sp_MSforeachtable 'DROP TABLE ?'

            --drop all views
            DECLARE @crlf VARCHAR(2) = CHAR(13) + CHAR(10) ;
            set @sql = '';
            SELECT @sql = @sql + 'DROP VIEW ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(v.name) +';' + @crlf
            FROM   sys.views v

            EXEC(@sql);

            --drop all functons
            set @sql = '';
            SELECT @sql = @sql + 'DROP FUNCTION [' + name + '];' + @crlf
              FROM sys.sql_modules m 
            INNER JOIN sys.objects o 
                    ON m.object_id=o.object_id
            WHERE type_desc like '%function%'

            EXEC(@sql);
", connection);

            connection.Open();
            try
            {
                sqlCommand.ExecuteNonQuery();
            }
            finally
            {
                connection.Close();
            }
        }
    }
}
