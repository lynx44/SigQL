using System;

namespace SigQL.Types.Attributes
{
    public class ViaRelationAttribute : Attribute
    {
        public string Path { get; }

        public ViaRelationAttribute(string path)
        {
            Path = path;
        }
    }
}
