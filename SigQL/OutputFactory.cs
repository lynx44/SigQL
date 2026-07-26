using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SigQL.Exceptions;
using SigQL.Extensions;
using SigQL.Types;

namespace SigQL
{
    public class OutputFactory
    {
        public static Type UnwrapType(Type type)
        {
            var columnOutputType = type;

            if (columnOutputType.IsTask())
            {
                if (columnOutputType.IsGenericType)
                {
                    return UnwrapType(columnOutputType.GetGenericArguments().First());
                }

                return typeof(void);
            }

            if (columnOutputType.IsGenericType && columnOutputType.GetGenericTypeDefinition() == typeof(ITotalCountResult<>))
            {
                return UnwrapType(columnOutputType.GetGenericArguments().First());
            }

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
                    var genericTypeDefinition = collectionType.GetGenericTypeDefinition();
                    if (genericTypeDefinition == typeof(IReadOnlyCollection<>) ||
                        genericTypeDefinition == typeof(ReadOnlyCollection<>))
                    {
                        var asReadOnlyMethod =
                            typeof(List<>).MakeGenericType(collectionType.GenericTypeArguments.First()).GetMethod(nameof(List<object>.AsReadOnly), BindingFlags.Instance | BindingFlags.Public);
                        var list = MakeGenericList(rootOutputType, outputInvocations.AsEnumerable());
                        result = asReadOnlyMethod.Invoke(list, null);
                    }
                    // List<T> satisfies all of these
                    else if (genericTypeDefinition == typeof(IList<>) ||
                             genericTypeDefinition == typeof(List<>) ||
                             genericTypeDefinition == typeof(ICollection<>) ||
                             genericTypeDefinition == typeof(IReadOnlyList<>))
                    {
                        result = MakeGenericList(rootOutputType, outputInvocations.AsEnumerable());
                    }
                    else if (genericTypeDefinition == typeof(IEnumerable<>))
                    {
                        result = CastToGenericEnumerable(rootOutputType, outputInvocations.AsEnumerable());
                    }
                    else
                    {
                        throw new InvalidTypeException(
                            $"Unable to materialize collection type {collectionType.Name}. Supported collection types are IEnumerable<>, ICollection<>, IList<>, List<>, IReadOnlyList<>, IReadOnlyCollection<>, ReadOnlyCollection<> and arrays.", null);
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
            else
            {
                var rows = result.AsEnumerable().ToList();
                if (rows.Count > 1)
                {
                    throw new MultipleResultsException(
                        $"Expected at most one {rootOutputType.Name}, but the query returned {rows.Count} rows. Return a collection of {rootOutputType.Name}, or narrow the query with a filter or [Fetch] parameter.");
                }

                return rows.SingleOrDefault();
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