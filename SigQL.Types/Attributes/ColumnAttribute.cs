using System;

namespace SigQL.Types.Attributes
{
    public class ColumnAttribute : Attribute
    {
        public string ColumnName { get; }

        public ColumnAttribute(string columnName)
        {
            ColumnName = columnName;
        }
    }
}
