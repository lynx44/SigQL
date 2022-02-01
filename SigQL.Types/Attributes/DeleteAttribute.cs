using System;

namespace SigQL.Types.Attributes
{
    public class DeleteAttribute : Attribute
    {
        public string TableName { get; set; }
    }
}
