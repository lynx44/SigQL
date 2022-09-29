using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SigQL
{
    // adapted from: https://alastaircrabtree.com/detecting-plurals-in-dot-net/
    public interface IPluralizationHelper
    {
        IEnumerable<string> DepluralizedCandidates(string plural);

        IEnumerable<string> PluralizedCandidates(string singular);
    }

    public class DefaultPluralizationHelper : IPluralizationHelper
    {
        public static IPluralizationHelper Instance { get; }

        static DefaultPluralizationHelper()
        {
            Instance = new DefaultPluralizationHelper();
        }

        protected DefaultPluralizationHelper()
        {
        }

        private static readonly Dictionary<string, string> KnownCommonPluralsDictionary = new Dictionary<string, string>
        {
            {"children", "child"},
            {"people", "person"}
        };

        /// <summary>
        ///     This is far from perfect but detects the majority of english language plurals
        ///     See https://en.wikipedia.org/wiki/English_plurals#Irregular_plurals and
        ///     and http://grammar.yourdictionary.com/grammar-rules-and-tips/irregular-plurals.html
        ///     for useful example cases.
        /// </summary>
        public IEnumerable<string> DepluralizedCandidates(string plural)
        {
            var candidates = new List<string>() { plural };
            // people => person and other "one off" edge cases
            if (KnownCommonPluralsDictionary.ContainsKey(plural))
                candidates.Add(KnownCommonPluralsDictionary[plural]);

            //series => series
            if (plural.EndsWith("ies"))
                candidates.Add(plural);

            //statuses => status
            if (plural.EndsWith("uses"))
                candidates.Add(ReplaceEnd(plural, "es"));

            //synopses => synopsis
            if (plural.EndsWith("ses"))
                candidates.Add(ReplaceEnd(plural, "es", "is"));

            // catches => catch
            if (plural.EndsWith("es"))
                candidates.Add(ReplaceEnd(plural, "es"));

            // dogs => dog
            if (plural.EndsWith("s"))
                candidates.Add(ReplaceEnd(plural, "s"));

            return candidates;
        }

        public IEnumerable<string> PluralizedCandidates(string singular)
        {
            var candidates = new List<string>() { singular };
            // babies => baby
            if (singular.EndsWith("y"))
                candidates.Add(ReplaceEnd(singular, "y", "ies"));

            //wives => wife
            if (singular.EndsWith("fe"))
                candidates.Add(ReplaceEnd(singular, "fe", "ves"));

            //halves => half
            if (singular.EndsWith("f"))
                candidates.Add(ReplaceEnd(singular, "f", "ves"));

            //women => woman
            if (singular.EndsWith("man"))
                candidates.Add(ReplaceEnd(singular, "man", "men"));

            //vertices => vertex
            if (singular.EndsWith("ex"))
                candidates.Add(ReplaceEnd(singular, "ex", "ices"));

            //matricies => matrix
            if (singular.EndsWith("ix"))
                candidates.Add(ReplaceEnd(singular, "ix", "ices"));

            //axes => axis
            if (singular.EndsWith("is"))
                candidates.Add(ReplaceEnd(singular, "is", "es"));

            //medium => media
            if (singular.EndsWith("um"))
                candidates.Add(ReplaceEnd(singular, "um", "a"));

            //status => statuses
            if (singular.EndsWith("s"))
                candidates.Add(ReplaceEnd(singular, "es"));

            //catch => catches
            candidates.Add(singular + "es");

            //dog => dogs
            candidates.Add(singular + "s");

            return candidates;
        }

        private static string ReplaceEnd(string word, string trimString, string replacement = "")
        {
            return Regex.Replace(word, $"{trimString}$", replacement);
        }
    }

    public class IgnorePluralizationHelper : IPluralizationHelper
    {
        public static IPluralizationHelper Instance { get; }

        static IgnorePluralizationHelper()
        {
            Instance = new IgnorePluralizationHelper();
        }

        protected IgnorePluralizationHelper()
        {
        }

        public IEnumerable<string> DepluralizedCandidates(string plural)
        {
            return new [] { plural };
        }

        public IEnumerable<string> PluralizedCandidates(string singular)
        {
            return new[] { singular };
        }
    }

    internal static class PluralizationHelperExtensions
    {
        public static IEnumerable<string> AllCandidates(this IPluralizationHelper helper, string word)
        {
            var depluralizedCandidates = helper.DepluralizedCandidates(word).ToList();
            var pluralizedCandidates = helper.PluralizedCandidates(word).ToList();

            return depluralizedCandidates.Concat(pluralizedCandidates).Distinct().ToList();
        }
    }
}
