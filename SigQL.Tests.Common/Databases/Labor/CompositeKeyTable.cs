using System.Collections.Generic;

namespace SigQL.Tests.Common.Databases.Labor
{
    // Table with a two column primary key (FirstName, LastName). Used to prove that generated
    // join conditions - including the OFFSET/FETCH subquery join - AND their key comparisons
    // together instead of emitting them space separated.
    public class CompositeKeyTable
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }

        public interface IFields
        {
            string FirstName { get; }
            string LastName { get; }
        }

        public interface IFieldsWithChildren
        {
            string FirstName { get; }
            string LastName { get; }
            IEnumerable<CompositeForeignKeyTable.IFields> CompositeForeignKeyTables { get; }
        }
    }

    // Child table referencing CompositeKeyTable through a two column foreign key.
    public class CompositeForeignKeyTable
    {
        public int Id { get; set; }
        public string EFCompositeKeyTableFirstName { get; set; }
        public string EFCompositeKeyTableLastName { get; set; }

        public interface IFields
        {
            int Id { get; }
        }

        public interface IFieldsWithParent
        {
            int Id { get; }
            CompositeKeyTable.IFields CompositeKeyTable { get; }
        }
    }
}
