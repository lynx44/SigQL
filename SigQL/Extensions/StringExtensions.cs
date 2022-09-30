using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Extensions
{
    internal static class StringExtensions
    {
        public static string NullForEmptyOrWhiteSpace(this string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
