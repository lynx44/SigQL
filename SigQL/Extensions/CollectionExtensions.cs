using System;
using System.Collections.Generic;
using System.Linq;

namespace SigQL.Extensions
{
    public static class CollectionExtensions
    {
        public static void ForEach<T>(this IList<T> collection, Action<T, int> action)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                action(collection[i], i);
            }
        }

        public static IEnumerable<T> AppendOne<T>(this IEnumerable<T> enumerable, T item)
        {
            return enumerable.Concat(new T[] {item});
        }

        public static TResult MinOrDefault<TSource, TResult>(this IEnumerable<TSource> enumerable, Func<TSource, TResult> selector, TResult defaultValue)
        {
            if (enumerable.Any())
                return enumerable.Min(selector);

            return defaultValue;
        }

        public static TResult MaxOrDefault<TSource, TResult>(this IEnumerable<TSource> enumerable, Func<TSource, TResult> selector, TResult defaultValue)
        {
            if (enumerable.Any())
                return enumerable.Max(selector);

            return defaultValue;
        }
    }
}
