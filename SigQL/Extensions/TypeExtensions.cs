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

        public static bool IsSqlType(this Type t)
        {
            Type type = t;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                type = t.GetGenericArguments().First();
            }

            return sqlTypes.Contains(type);
        }

        public static bool IsSqlParameterType(this Type t)
        {
            Type type = t;
            if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Nullable<>) || typeof(System.Collections.IEnumerable).IsAssignableFrom(t.GetGenericTypeDefinition())))
            {
                type = t.GetGenericArguments().First();
            }

            return sqlTypes.Contains(type);
        }

        public static bool TryGetActionType(this Type type, out Type actionType)
        {
            if (type.IsGenericType &&
                type.GenericTypeArguments.Length == 1 &&
                type.GetGenericTypeDefinition().IsAssignableFrom(typeof(Action<>)))
            {
                actionType = type.GenericTypeArguments.First();
                return true;
            }

            actionType = null;
            return false;
        }

        public static bool IsCollectionType(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(typeof(IEnumerable<>));
        }
    }
}
