using System;

namespace SigQL.Exceptions
{
    public class InvalidOrderByException : Exception
    {
        public InvalidOrderByException(Type invalidType) : base(
            $"Invalid order specified for type {invalidType}.{(invalidType.GetProperties().Length > 1 ? $" Type \"{invalidType}\" specified for OrderBy<T> contains more than one property, which makes the sort order ambiguous." : string.Empty)}")
        {            
        }
    }
}
