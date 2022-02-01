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
    }
}
