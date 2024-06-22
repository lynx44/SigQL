using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SigQL.Extensions
{
    public static class ObjectExtensions
    {
        public static IEnumerable<T> AsEnumerable<T>(this T item)
        {
            if ((item?.GetType().IsCollectionType()).GetValueOrDefault(false) || typeof(T).IsCollectionType())
            {
                var currentEnumerable = item as IEnumerable<T>;
                return currentEnumerable ?? ((IEnumerable) item).Cast<T>();
            }

            return new T[] {item};
        }

        internal static object ConvertToClr(this object o)
        {
            return o is System.DBNull ? null : o;
        }

        public static IDictionary<string, object> ToDictionary(this object obj)
        {
            return obj.GetType().GetProperties().ToDictionary(prop => prop.Name, prop => prop.GetValue(obj, null));
        }

        public static bool IsEmpty(this object value)
        {
            if (value == null)
                return false;

            // if it's a string, determine if it's empty
            var stringValue = value as string;
            if (stringValue != null)
                return stringValue.Length == 0;

            // if it's a collection, determine if it's empty
            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                var allItems = enumerable?.Cast<object>();
                return !allItems.Any();
            }
            
            return false;
        }

        public static IEnumerable<T> SelectRecursive<T>(this T o, Func<T, T> next)
        where T: class
        {
            var nextChild = o;
            var children = new List<T>();
            while (nextChild != null)
            {
                children.Add(nextChild);
                nextChild = next(nextChild);
            }

            return children;
        }

        public static IEnumerable<T> SelectManyRecursive<T>(this IEnumerable<T> o, Func<T, IEnumerable<T>> next)
        where T: class
        {
            var nextChild = o;
            var children = new List<T>();
            while ((nextChild?.Any()).GetValueOrDefault(false))
            {
                children.AddRange(nextChild);
                nextChild = nextChild.SelectMany(n => next(n));
            }

            return children;
        }

        public static TOut Map<TIn, TOut>(this TIn t, Func<TIn, TOut> ifNotNull, TOut defaultValue = default(TOut))
        {
            if (t != null)
            {
                return ifNotNull(t);
            }

            return defaultValue;
        }
    }
}
