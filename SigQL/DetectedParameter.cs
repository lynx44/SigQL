using System;
using System.Reflection;
using SigQL.Sql;

namespace SigQL
{
    internal class DetectedParameter
    {
        public string Name { get; }
        public Type Type { get; }
        public ParameterInfo ParameterInfo { get; }
        public TableRelations TableRelations { get; set; }

        public DetectedParameter(string name, Type type, ParameterInfo parameterInfo)
        {
            Name = name;
            Type = type;
            ParameterInfo = parameterInfo;
        }
    }
}