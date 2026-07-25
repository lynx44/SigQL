namespace SigQL.Schema
{
    /// <summary>
    /// Resolves the foreign key relationships for a table. This is the extension point that allows
    /// relationships to be defined in code for databases that do not declare foreign keys in their schema,
    /// or that declare them in a way SigQL cannot read directly.
    /// </summary>
    /// <remarks>
    /// Implementations may be invoked repeatedly for the same table (for example while probing every table
    /// for a many-to-many bridge), so implementations that compute relationships should cache their results.
    /// A custom implementation that needs to inspect sibling tables (for example a convention based resolver
    /// that maps an "EmployeeId" column to the "Employee" table) should be constructed with the
    /// <see cref="IDatabaseConfiguration"/> so it can locate the referenced tables.
    /// </remarks>
    public interface IForeignKeyResolver
    {
        IForeignKeyDefinitionCollection GetForeignKeys(ITableDefinition table);
    }

    /// <summary>
    /// The default <see cref="IForeignKeyResolver"/>. Returns the foreign keys already declared on the
    /// table definition (populated from the database schema, or added in code via
    /// <see cref="ITableDefinitionExtensions.AddForeignKey(ITableDefinition, System.Func{ITableDefinition, IColumnDefinition}, IColumnDefinition)"/>).
    /// </summary>
    public class DefaultForeignKeyResolver : IForeignKeyResolver
    {
        public static IForeignKeyResolver Instance { get; }

        static DefaultForeignKeyResolver()
        {
            Instance = new DefaultForeignKeyResolver();
        }

        protected DefaultForeignKeyResolver()
        {
        }

        public IForeignKeyDefinitionCollection GetForeignKeys(ITableDefinition table)
        {
            return table.ForeignKeyCollection;
        }
    }
}
