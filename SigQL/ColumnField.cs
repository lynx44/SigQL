using System;
using System.Reflection;

namespace SigQL
{
    internal class ColumnField
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public PropertyInfo Property { get; set; }
        public ParameterInfo Parameter { get; set; }
    }
}
