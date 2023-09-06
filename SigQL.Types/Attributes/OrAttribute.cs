using System;

namespace SigQL.Types.Attributes
{
    public class OrGroupAttribute : Attribute
    {
        public string Group { get; set; }
        internal string Scope { get; set; }

        public OrGroupAttribute(string group = "default")
        {
            if (@group == null)
                throw new ArgumentException($"Argument \"{nameof(group)}\" cannot be null", nameof(group));
            Group = @group;
        }
    }
}