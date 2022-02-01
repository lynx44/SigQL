using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SigQL.SqlServer.Tests.Infrastructure
{
    public static class CustomAssert
    {
        public static void AreEquivalent<T>(IEnumerable expected, IEnumerable<T> actual)
        {
            AreEquivalent<T>(expected, (IEnumerable)actual);
        }

        public static void AreEquivalent<T>(IEnumerable expected, IEnumerable actual)
        {
            Assert.AreEqual(expected != null, actual != null);
            Assert.AreEqual(expected?.Cast<T>().Count(), actual?.Cast<T>().Count());

            if (expected != null && actual != null)
            {
                var typedExpected = expected.Cast<T>().Select(ToDictionary).ToList();
                var typedActual = actual.Cast<T>().Select(ToDictionary).ToList();

                Assert.IsTrue(typedExpected.Any(e => typedActual.Any(a => DictionaryEqual(e, a))), $"Elements differ between lists.");
            }
        }

        private static IDictionary<string, object> ToDictionary<T>(this T item)
        {
            return typeof(T)
                .GetProperties()
                .ToDictionary(p => p.Name, p => p.GetValue(item, null));
            // dynamic eo = dictionary.Aggregate(new ExpandoObject() as IDictionary<string, Object>,
            //     (a, p) => { a.Add(p); return a; });
            // return eo;
        }

        private static bool DictionaryEqual(IDictionary<string, object> dict, IDictionary<string, object> dict2)
        {
            var mismatchingKeyValuePairs = dict2.Where(entry => !((dict["Title"] == null && dict2["Title"] == null) || dict["Title"].Equals(dict2["Title"])));
            return !mismatchingKeyValuePairs.Any();
        }
    }
}
