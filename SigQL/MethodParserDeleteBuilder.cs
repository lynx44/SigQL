using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SigQL.Extensions;
using SigQL.Schema;
using SigQL.Types.Attributes;

namespace SigQL
{
    public partial class MethodParser
    {
        private MethodSqlStatement BuildDeleteStatement(DeleteSpec deleteSpec)
        {
            var tokens = new List<TokenPath>();
            var parameterPaths = new List<ParameterPath>();
            var methodInfo = deleteSpec.RootMethodInfo;
            var parameters = this.databaseResolver.BuildDetectedParameters(deleteSpec.Table, methodInfo.GetParameters()).ToList();
            var primaryTable = deleteSpec.Table;
            var whereClause = BuildWhereClauseFromTargetTablePerspective(
                new RelationalTable() {Label = primaryTable.Name}, primaryTable, parameters, parameterPaths,
                tokens);

            var statement = new Delete()
            {
                FromClause = new FromClause().SetArgs(new FromClauseNode().SetArgs(new TableIdentifier().SetArgs(new RelationalTable() { Label = primaryTable.Name }))),
                WhereClause = whereClause
            };

            var sqlStatement = new MethodSqlStatement()
            {
                CommandAst = statement.AsEnumerable(),
                SqlBuilder = this.builder,
                ReturnType = methodInfo.ReturnType,
                UnwrappedReturnType = null,
                Parameters = parameterPaths,
                Tokens = tokens,
                // ColumnAliasRelations = columnAliasForeignKeyDefinitions,
                TargetTablePrimaryKey = null,
                TablePrimaryKeyDefinitions = null
            };

            return sqlStatement;
        }

        private bool IsDeleteMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(DeleteAttribute), false)?.Any()).GetValueOrDefault(false);
        }

        private bool IsUpdateMethod(MethodInfo methodInfo)
        {
            return (methodInfo.GetCustomAttributes(typeof(UpdateAttribute), false)?.Any()).GetValueOrDefault(false);
        }

        private DeleteSpec GetDeleteSpec(MethodInfo methodInfo)
        {
            var deleteAttribute = methodInfo.GetCustomAttributes(typeof(DeleteAttribute), false).Cast<DeleteAttribute>().FirstOrDefault();
            if (deleteAttribute != null)
            {
                var deleteSpec = new DeleteSpec();
                if (!string.IsNullOrEmpty(deleteAttribute.TableName))
                {
                    deleteSpec.Table = this.databaseConfiguration.Tables.FindByName(deleteAttribute.TableName);
                }

                deleteSpec.RootMethodInfo = methodInfo;

                return deleteSpec;
            }

            return null;
        }

        private class DeleteSpec
        {
            public ITableDefinition Table { get; set; }
            public MethodInfo RootMethodInfo { get; set; }
        }
        
        private class DeleteColumnParameter
        {
            public IColumnDefinition Column { get; set; }
            public ParameterPath ParameterPath { get; set; }
        }
    }
}
