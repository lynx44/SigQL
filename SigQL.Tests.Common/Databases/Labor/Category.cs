using System;
using System.Collections.Generic;

namespace SigQL.Tests.Common.Databases.Labor
{
    // Parent table whose primary key is a client-supplied (non-identity) uniqueidentifier.
    // Used to exercise many-to-one FK references against a GUID-keyed parent.
    public class Category
    {
        public Guid Id { get; set; }
        public string Name { get; set; }

        public interface ICategoryId
        {
            Guid Id { get; set; }
        }

        public class CategoryIdImpl : ICategoryId
        {
            public Guid Id { get; set; }
        }

        public class InsertFields
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }
    }
}
