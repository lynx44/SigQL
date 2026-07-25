using System;
using System.Linq;

namespace SigQL.Schema
{
    /// <summary>
    /// Infers foreign key relationships from column naming conventions, for databases that do not declare
    /// foreign keys in their schema at all. A column named "{Table}Id" (case-insensitive) is treated as a
    /// foreign key referencing the "{Table}" table's primary key, provided that table exists in the
    /// <see cref="IDatabaseConfiguration"/> and has a single-column primary key.
    /// </summary>
    /// <remarks>
    /// Columns that don't resolve to a known table, or that resolve to a table with no primary key or a
    /// composite (multi-column) primary key, are silently skipped rather than treated as an error, since a
    /// "*Id"-suffixed column is not guaranteed to be a foreign key (it may simply be a same-table scalar
    /// value, e.g. an external identifier).
    /// </remarks>
    public class ConventionForeignKeyResolver : IForeignKeyResolver
    {
        private const string IdSuffix = "Id";
        private readonly IDatabaseConfiguration databaseConfiguration;

        public ConventionForeignKeyResolver(IDatabaseConfiguration databaseConfiguration)
        {
            this.databaseConfiguration = databaseConfiguration;
        }

        public IForeignKeyDefinitionCollection GetForeignKeys(ITableDefinition table)
        {
            var foreignKeys = table.Columns
                .Where(c => IsConventionalForeignKeyColumnName(c.Name))
                .Select(c => new
                {
                    Column = c,
                    ReferencedTable = this.databaseConfiguration.Tables.FindByName(
                        c.Name.Substring(0, c.Name.Length - IdSuffix.Length))
                })
                .Where(c => HasSingleColumnPrimaryKey(c.ReferencedTable))
                .Select(c => new ForeignKeyDefinition(c.ReferencedTable,
                    new ForeignKeyPair(c.Column, c.ReferencedTable.PrimaryKey.Columns.Single())))
                .ToArray();

            return new ForeignKeyDefinitionCollection().AddForeignKeys(foreignKeys);
        }

        private static bool IsConventionalForeignKeyColumnName(string columnName)
        {
            return columnName.Length > IdSuffix.Length &&
                   columnName.EndsWith(IdSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSingleColumnPrimaryKey(ITableDefinition table)
        {
            return table?.PrimaryKey != null && table.PrimaryKey.Columns.Count() == 1;
        }
    }
}
