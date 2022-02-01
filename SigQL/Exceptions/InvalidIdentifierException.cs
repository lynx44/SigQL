using System;

namespace SigQL.Exceptions
{
    public class InvalidIdentifierException : Exception
    {
        public InvalidIdentifierException(string message) : base(message)
        {
        }
    }
}
