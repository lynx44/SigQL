using System;

namespace SigQL.Types.Attributes
{
    public class UpdateByKeyAttribute : Attribute
    {
        public string TableName { get; set; }
    }
}
