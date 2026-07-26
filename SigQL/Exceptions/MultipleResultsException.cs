using System;

namespace SigQL.Exceptions
{
    /// <summary>
    /// Thrown when a method whose return type is a single projection receives more than one row.
    /// Derives from InvalidOperationException, which is what Enumerable.Single previously threw
    /// for this case.
    /// </summary>
    public class MultipleResultsException : InvalidOperationException
    {
        public MultipleResultsException(string message) : base(message)
        {
        }
    }
}
