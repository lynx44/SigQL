using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SigQL.Schema;

namespace SigQL.Extensions
{
    internal static class IForeignKeyCollectionExtensions
    {
        public static IEnumerable<IColumnDefinition> GetAllForeignColumns(
            this IForeignKeyDefinitionCollection foreignKeyCollection)
        {
            return foreignKeyCollection.SelectMany(c => c.KeyPairs.Select(kp => kp.ForeignTableColumn)).ToList();
        }

        public static IColumnDefinition GetColumnForTable(this IForeignKeyPair keyPair, ITableDefinition table)
        {
            return TableEqualityComparer.Default.Equals(keyPair.ForeignTableColumn.Table, table) ? keyPair.ForeignTableColumn :
                TableEqualityComparer.Default.Equals(keyPair.PrimaryTableColumn.Table, table) ? keyPair.PrimaryTableColumn :
                throw new InvalidOperationException($"Table {table.Name} is associated with foreign key pair");
        }
    }
}
