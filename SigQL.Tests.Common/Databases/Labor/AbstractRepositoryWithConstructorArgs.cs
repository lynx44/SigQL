using System.Collections.Generic;

namespace SigQL.Tests.Common.Databases.Labor
{
    public abstract class AbstractRepositoryWithConstructorArgs
    {
        private readonly int employeeId;
        private readonly string name;

        public AbstractRepositoryWithConstructorArgs(int employeeId, string name)
        {
            this.employeeId = employeeId;
            this.name = name;
        }

        public abstract IEnumerable<WorkLog.IWorkLogId> GetAllIds();

        public Employee.IEmployeeFields Get(int id)
        {
            return new EmployeeFields()
            {
                Id = employeeId,
                Name = name
            };
        }

        private class EmployeeFields : Employee.IEmployeeFields
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }
}
