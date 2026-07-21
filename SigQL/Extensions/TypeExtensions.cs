using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

        public static bool IsTask(this Type type)
        {
            return type == typeof(Task) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>));
        }

        public static string GetSqlTypeName(this Type type)
        {
            var unwrapped = Nullable.GetUnderlyingType(type) ?? type;

            if (unwrapped == typeof(int)) return "int";
            if (unwrapped == typeof(long)) return "bigint";
            if (unwrapped == typeof(short)) return "smallint";
            if (unwrapped == typeof(byte)) return "tinyint";
            if (unwrapped == typeof(bool)) return "bit";
            if (unwrapped == typeof(string)) return "nvarchar(max)";
            if (unwrapped == typeof(Guid)) return "uniqueidentifier";
            if (unwrapped == typeof(DateTime)) return "datetime2";
            if (unwrapped == typeof(DateTimeOffset)) return "datetimeoffset";
            if (unwrapped == typeof(decimal)) return "decimal(38,18)";
            if (unwrapped == typeof(double)) return "float";
            if (unwrapped == typeof(float)) return "real";

            return "nvarchar(max)";
        }
    }
}
