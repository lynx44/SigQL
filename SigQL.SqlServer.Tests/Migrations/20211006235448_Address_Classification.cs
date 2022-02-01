using Microsoft.EntityFrameworkCore.Migrations;

namespace SigQL.SqlServer.Tests.Migrations
{
    public partial class Address_Classification : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Classification",
                table: "Address",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Classification",
                table: "Address");
        }
    }
}
