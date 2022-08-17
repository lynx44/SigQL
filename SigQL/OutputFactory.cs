using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SigQL.Extensions;

namespace SigQL
{
    public class OutputFactory
    {
        public static Type UnwrapType(Type type)
        {
            var columnOutputType = type;

            if (columnOutputType.IsCollectionType())
            {
                if (columnOutputType.IsGenericType)
                {
                    columnOutputType = columnOutputType.GetGenericArguments().First();
                }
                else if(columnOutputType.IsArray)
                {
                    columnOutputType = columnOutputType.GetElementType();
                }
                
            }

            return columnOutputType;
        }

        public static object Cast(object result, Type finalReturnType)
        {
            if (finalReturnType.IsCollectionType())
            {
                var collectionType = finalReturnType;
                if (collectionType.IsGenericType)
                {
                    if (collectionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        var castMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.Cast), BindingFlags.Static | BindingFlags.Public);
                        var castMethodForType = castMethod.MakeGenericMethod(UnwrapType(finalReturnType));
                        return castMethodForType.Invoke(null, result.AsEnumerable().ToArray());
                    }
                }
            }
            
            return result.AsEnumerable().Single();
        }

        public static object ToGenericEnumerable(object collection, Type unwrappedType)
        {
            var castMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.Cast), BindingFlags.Static | BindingFlags.Public);
            var castMethodForType = castMethod.MakeGenericMethod(unwrappedType);
            return castMethodForType.Invoke(null, collection.AsEnumerable().ToArray());
        }
    }
}