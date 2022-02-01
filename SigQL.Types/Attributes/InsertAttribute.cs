using System;

namespace SigQL.Types.Attributes
{
    public class InsertAttribute : Attribute
    {
        public string TableName { get; set; }
    }
}
