using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Exceptions
{
    public class InvalidTypeException : Exception
    {
        public InvalidTypeException(string message, Exception innerException) : base(message, innerException)
        {
            
        }
    }
}
