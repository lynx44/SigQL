using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SigQL.Exceptions
{
    public class InvalidAttributeException : Exception
    {
        public Type AttributeType { get; }
        public IEnumerable<MemberInfo> Members { get; }

        public InvalidAttributeException(Type attributeType, IEnumerable<MemberInfo> members, string message) : base(message)
        {
            AttributeType = attributeType;
            Members = members;
        }
    }
}
