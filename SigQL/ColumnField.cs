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
        public ColumnField Parent { get; set; }

        public IArgument AsArgument(DatabaseResolver databaseResolver)
        {
            if (Property != null)
            {
                return new PropertyArgument(this.Property, Parent.AsArgument(databaseResolver), databaseResolver);
            }

            return new ParameterArgument(this.Parameter, databaseResolver);
        }
    }
}
