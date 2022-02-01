namespace SigQL.Types
{
    public class Like : IWhereClauseFilterParameter
    {
        private readonly string value;

        /// <summary>
        /// Initiates a LIKE comparison.
        /// </summary>
        /// <param name="auditedValue">The raw LIKE string with wildcards. DO NOT pass user input directly to this function without sanitizing or escaping it first.</param>
        /// <returns></returns>
        public static Like FromUnsafeRawValue(string auditedValue)
        {
            return new Like(auditedValue);
        }

        internal Like(string value)
        {
            this.value = value;
        }

        public object SqlValue => this.value;

        protected static string Sanitize(string value)
        {
            return value?.Replace("%", string.Empty);
        }
    }

    public class StartsWith : Like
    {
        public StartsWith(string value) : base(value == null ? null : Sanitize(value) + "%")
        {
        }

        public static implicit operator StartsWith(string value) => new StartsWith(value);
    }

    public class Contains : Like
    {
        public Contains(string value) : base(value == null ? null : "%" + Sanitize(value) + "%")
        {
        }

        public static implicit operator Contains(string value) => new Contains(value);
    }

    public class EndsWith : Like
    {
        public EndsWith(string value) : base(value == null ? null : "%" + Sanitize(value))
        {
        }

        public static implicit operator EndsWith(string value) => new EndsWith(value);
    }
}
