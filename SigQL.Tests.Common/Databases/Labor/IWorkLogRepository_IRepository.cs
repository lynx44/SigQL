using System.Collections.Generic;
using SigQL.Types;

namespace SigQL.Tests.Common.Databases.Labor
{
    public interface IWorkLogRepository_IRepository : IRepository<WorkLog>
    {
        IEnumerable<WorkLog.IWorkLogId> GetAllIds();
    }
}