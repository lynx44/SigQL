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
    }
}
