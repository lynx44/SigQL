using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Types.Attributes
{
    public class SqlIdentifierAttribute : Attribute
    {
        public string Name { get; }

        public SqlIdentifierAttribute(string name)
        {
            Name = name;
        }
    }
}
