using System;
using System.Collections.Generic;
using System.Text;

namespace SigQL.Tests.Common.Databases.Labor
{
    public class EmployeeStatuses
    {
        public int Id { get; set; }
    }

    public class EmployeeStatus
    {
        public interface IId
        {
            int Id { get; }
        }
    }
}
