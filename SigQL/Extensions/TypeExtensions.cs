using System;
using System.Collections.Generic;
using System.Linq;

namespace SigQL.Extensions
{
    public static class TypeExtensions
    {
        private readonly static List<Type> sqlTypes = new List<Type>()
        {
            typeof(long),
            typeof(byte[]),
            typeof(bool),
            typeof(string),
            typeof(DateTime),
            typeof(decimal),
            typeof(double),
            typeof(int),
            typeof(float),
            typeof(Guid),
            typeof(short),
            typeof(byte),
            typeof(DateTimeOffset)
        };
        
        public static bool IsCollectionType(this Type type)
        {
            return type.IsArray || 
                   (type.IsGenericType && (type.GetGenericTypeDefinition().IsAssignableFrom(typeof(IEnumerable<>)) || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))));
        }
    }
}
