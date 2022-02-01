using Microsoft.EntityFrameworkCore.Migrations;

namespace SigQL.SqlServer.Tests.Migrations
{
    public partial class itvf_GetWorkLogsByEmployeeId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE FUNCTION itvf_GetWorkLogsByEmployeeId
                (	
	                -- Add the parameters for the function here
	                @employeeId int
                )
                RETURNS TABLE 
                AS
                RETURN 
                (
	                -- Add the SELECT statement with parameter references here
	                select * from WorkLog where EmployeeId=@employeeId
                )
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION itvf_GetWorkLogsByEmployeeId");
        }
    }
}
