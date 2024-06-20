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
    }
}
