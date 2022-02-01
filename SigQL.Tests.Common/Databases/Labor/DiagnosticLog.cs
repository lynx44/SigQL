using System;

namespace SigQL.Tests.Common.Databases.Labor
{
    public class DiagnosticLog
    {
        public string Message { get; set; }
        public string Severity { get; set; }
        public DateTime LoggedOn { get; set; }

        public interface IFields
        {
            string Message { get; }
            string Severity { get; }
            DateTime LoggedOn { get; }
        }
    }
}
