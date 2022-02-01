using System.Collections.Generic;

namespace SigQL.Tests.Common.Databases.Labor
{
    public abstract class AbstractRepository
    {
        public abstract IEnumerable<WorkLog.IWorkLogId> GetAllIds();
    }
}
