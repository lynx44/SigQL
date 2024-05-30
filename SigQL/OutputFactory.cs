using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            var returnType = finalReturnType;
            var rootOutputType = UnwrapType(finalReturnType);
            var outputInvocations = result;

            if (returnType.IsCollectionType())
            {
                var collectionType = returnType;
                if (collectionType.IsGenericType)
                {
                    if (collectionType.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) ||
                        collectionType.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>))
                    {
                        var asReadOnlyMethod =
                            typeof(List<>).MakeGenericType(collectionType.GenericTypeArguments.First()).GetMethod(nameof(List<object>.AsReadOnly), BindingFlags.Instance | BindingFlags.Public);
                        var list = MakeGenericList(rootOutputType, outputInvocations.AsEnumerable());
                        result = asReadOnlyMethod.Invoke(list, null);
                    }

                    if (collectionType.GetGenericTypeDefinition() == typeof(IList<>) ||
                        collectionType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        result = MakeGenericList(rootOutputType, outputInvocations.AsEnumerable());

                    }

                    if (collectionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        result = CastToGenericEnumerable(rootOutputType, outputInvocations.AsEnumerable());
                    }
                }
                else if (returnType.IsArray)
                {
                    var toArrayMethod =
                        typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray), BindingFlags.Static | BindingFlags.Public);
                    var toArrayMethodForType = toArrayMethod.MakeGenericMethod(rootOutputType);
                    var enumerable = CastToGenericEnumerable(rootOutputType, outputInvocations.AsEnumerable());
                    result = toArrayMethodForType.Invoke(null, new[] { enumerable });
                }
            }

            return result;
        }

        private static object MakeGenericList(Type rootOutputType, IEnumerable<object> outputInvocations)
        {
            object result;
            var toListMethod =
                typeof(Enumerable).GetMethod(nameof(Enumerable.ToList), BindingFlags.Static | BindingFlags.Public);
            var toListMethodForType = toListMethod.MakeGenericMethod(rootOutputType);
            var enumerable = CastToGenericEnumerable(rootOutputType, outputInvocations);
            result = toListMethodForType.Invoke(null, new[] { enumerable });
            return result;
        }

        private static object CastToGenericEnumerable(Type rootOutputType, IEnumerable<object> outputInvocations)
        {
            object result;
            var castMethod =
                typeof(Enumerable).GetMethod(nameof(Enumerable.Cast), BindingFlags.Static | BindingFlags.Public);
            var castMethodForType = castMethod.MakeGenericMethod(rootOutputType);
            result = castMethodForType.Invoke(null, new[] { outputInvocations });
            return result;
        }
    }
}