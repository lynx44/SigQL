using System;

namespace SigQL.Types.Attributes
{
    public class InsertAttribute : Attribute, IUpsertAttribute
    {
        public string TableName { get; set; }
        public string KeyColumns { get; set; }
    }

    internal interface IUpsertAttribute
    {
        string TableName { get; }
        string KeyColumns { get; }
    }
}
