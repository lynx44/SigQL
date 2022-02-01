using System.Collections.Generic;

namespace SigQL.Tests.Common.Databases.Labor
{
    public interface IWorkLogRepository
    {
        IEnumerable<WorkLog.IWorkLogId> GetAllIds();
        // IEnumerable<WorkLog> GetByEmployee(int employeeId);
        // // IEnumerable<WorkLog> Get(BetweenParameter<DateTime, WorkLog.IStartDate, WorkLog.IEndDate> date);
        // IEnumerable<WorkLog> Get(int locationId, int employeeId);
        // // *GOOD* IN
        // IEnumerable<WorkLog> Get(IEnumerable<int> employeeIds);

        // IOrderedEnumerable<WorkLog, IAscendingDirection<WorkLog.ILocationId>> GetOrderedByLocation(int limit = 10);
        // IOrderedEnumerable<WorkLog, IAscendingDirection<WorkLog.ILocationId>> GetPage(int skip, int take);
        // IGroupedEnumerable<WorkLog.ILocationId, WorkLog> GetGroupedByLocation();
        // IGroupedEnumerable<WorkLog.ILocationIdAndEmployeeId, WorkLog> GetGroupedByLocationAndEmployee();
        //
        // // group sum
        // IGroupedEnumerable<WorkLog.IEmployeeId, ICountFunction<WorkLog.IEmployeeId>> CountByEmployee();
        // // max StartDate for each employee
        // IGroupedEnumerable<WorkLog.IEmployeeId, IMaxFunction<WorkLog.IStartDate>> EmployeeMaxStartDate();
        //
        // IEnumerable<WorkLog.IWorkLogWithEmployee> GetWithEmployees();
        // void UpdateWorkLog(DateTime setStartDate, DateTime setEndDate, int whereId);
        // int Count();
        //
        // // nested navigation parameters
        // IEnumerable<WorkLog> Get(string locationName);
    }
}