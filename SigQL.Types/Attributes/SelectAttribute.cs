using System;

namespace SigQL.Types.Attributes
{
    /// <summary>
    /// Projects a single database column as the method's return value, allowing a method to
    /// return a scalar (or a collection of scalars) instead of a projection class.
    ///
    /// Both TableName and ColumnName are required, since a scalar return type carries no
    /// information about which table or column it maps to.
    /// </summary>
    /// <example>
    /// [Select(TableName = nameof(Employee), ColumnName = "Name")]
    /// string GetEmployeeName(int id);
    /// </example>
    public class SelectAttribute : Attribute
    {
        public string TableName { get; set; }
        public string ColumnName { get; set; }
    }
}
