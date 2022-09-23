using System;

namespace SigQL.Tests.Common.Databases.Labor
{
    public class WorkLogEmployeeView
    {
        public int WorkLogId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }

        public interface IFields
        {
            int WorkLogId { get; }
            DateTime StartDate { get; }
            DateTime EndDate { get; }
            int EmployeeId { get; }
            string EmployeeName { get; }
        }
        public interface IFieldsMismatchingCase
        {
            int WorkLogID { get; }
            DateTime StartDate { get; }
            DateTime EndDate { get; }
            int EmployeeID { get; }
            string EmployeeName { get; }
        }
    }
}
