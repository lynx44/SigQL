using System;

namespace SigQL.Types.Attributes
{
    /// <summary>
    /// Configures command-level options for a repository method. Currently supports setting the
    /// command timeout for the generated SQL.
    /// </summary>
    /// <example>
    /// // wait up to 120 seconds for this query before timing out
    /// [Command(Timeout = 120)]
    /// IEnumerable&lt;WorkLog&gt; GetWorkLogs();
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class CommandAttribute : Attribute
    {
        // -1 is the "not specified" sentinel. Attribute arguments cannot be nullable value types,
        // so a sentinel is used to distinguish "unset" from an explicit 0 (which ADO.NET treats as
        // an infinite/no timeout).
        private const int UnsetTimeout = -1;

        /// <summary>
        /// The command timeout, in seconds, applied to the generated SQL command. A value of 0 means
        /// no timeout (wait indefinitely). Leave unset to use the provider/global default.
        /// </summary>
        public int Timeout { get; set; } = UnsetTimeout;

        /// <summary>
        /// The command timeout as a nullable value, returning null when <see cref="Timeout"/> was not set.
        /// </summary>
        public int? CommandTimeout => Timeout >= 0 ? Timeout : (int?)null;
    }
}
