using System;

namespace SigQL.Types.Attributes
{
    public class UpdateAttribute : Attribute
    {
        public string TableName { get; set; }
    }
}
