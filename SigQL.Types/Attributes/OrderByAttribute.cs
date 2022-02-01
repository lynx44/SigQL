using System;

namespace SigQL.Types.Attributes
{
    public class OrderByAttribute : Attribute
    {
        public string Table { get; }
        public string Column { get; }

        public OrderByAttribute(string table, string column)
        {
            Table = table;
            Column = column;
        }
    }
}
