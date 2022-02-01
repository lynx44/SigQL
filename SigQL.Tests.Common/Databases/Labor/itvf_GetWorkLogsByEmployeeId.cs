using System;

namespace SigQL.Tests.Common.Databases.Labor
{
    public class itvf_GetWorkLogsByEmployeeId : itvf_GetWorkLogsByEmployeeId.IFields
    {
        public interface IFields
        {
            int Id { get; }
            DateTime? StartDate { get; }
            DateTime? EndDate { get; }
            int? EmployeeId { get; }
            int? LocationId { get; }
        }
        public interface IId
        {
            int Id { get; }
        }

        public int Id { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int? EmployeeId { get; set; }
        public int? LocationId { get; set; }
    }
}
