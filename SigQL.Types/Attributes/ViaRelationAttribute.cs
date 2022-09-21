using System;

namespace SigQL.Types.Attributes
{
    public class ViaRelationAttribute : Attribute
    {
        public string Path { get; }
        public string Column { get; }

        public ViaRelationAttribute(string path, string column)
        {
            Path = path;
            Column = column;
        }
    }
}
