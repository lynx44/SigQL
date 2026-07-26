using System;

namespace SigQL.Exceptions
{
    /// <summary>
    /// Thrown when a scalar select declared with a non-nullable return type produces no row, or
    /// produces a row whose value is null. Returning default(T) in that case would silently turn
    /// "no value" into 0 / false / default, so the caller is told instead.
    /// </summary>
    public class NullScalarValueException : Exception
    {
        public NullScalarValueException(string message) : base(message)
        {
        }
    }
}
