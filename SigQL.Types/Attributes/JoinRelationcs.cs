using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Types.Attributes
{
    public class JoinRelationAttribute : Attribute
    {
        public string Path { get; }

        public JoinRelationAttribute(string path)
        {
            Path = path;
        }
    }
}
