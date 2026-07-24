using System;
using System.Collections.Generic;

namespace SigQL.Tests.Common.Databases.Labor
{
    // Child table with a many-to-one GUID foreign key (CategoryId) to Category.
    public class CategoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Guid? CategoryId { get; set; }
        public Category Category { get; set; }

        public interface ICategoryItemId
        {
            int Id { get; set; }
        }

        public interface ICategoryItemFields
        {
            int Id { get; set; }
            string Name { get; set; }
            Guid? CategoryId { get; set; }
        }

        // many-to-one: reference an existing (GUID-keyed) Category by key only. The Category
        // row must not be inserted or updated; only CategoryItem.CategoryId should be set.
        public class InsertFieldsWithExistingCategory
        {
            public string Name { get; set; }
            public Category.ICategoryId Category { get; set; }
        }

        public class UpsertFieldsWithExistingCategory
        {
            public int? Id { get; set; }
            public string Name { get; set; }
            public Category.ICategoryId Category { get; set; }
        }
    }
}
