using System;

namespace SigQL.Types.Attributes
{
    public class UpdateByKeyAttribute : Attribute, IUpsertAttribute
    {
        public string TableName { get; set; }
        public string KeyColumns { get; set; }
    }
}
