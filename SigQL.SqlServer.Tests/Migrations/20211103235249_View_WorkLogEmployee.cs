using Microsoft.EntityFrameworkCore.Migrations;

namespace SigQL.SqlServer.Tests.Migrations
{
    public partial class View_WorkLogEmployee : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"CREATE VIEW [dbo].[WorkLogEmployeeView]
                    AS
                    (
                    select WorkLog.Id WorkLogId, WorkLog.StartDate, WorkLog.EndDate, Employee.Id EmployeeId, Employee.Name EmployeeName
                    from WorkLog
                    inner join Employee on WorkLog.EmployeeId = Employee.Id
                    )");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP VIEW [dbo].[WorkLogEmployeeView]");
        }
    }
}
