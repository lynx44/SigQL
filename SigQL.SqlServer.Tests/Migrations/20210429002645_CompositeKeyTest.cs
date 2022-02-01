using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SigQL.SqlServer.Tests.Migrations
{
    public partial class CompositeKeyTest : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Address",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StreetAddress = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Address", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompositeKeyTable",
                columns: table => new
                {
                    FirstName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompositeKeyTable", x => new { x.FirstName, x.LastName });
                });

            migrationBuilder.CreateTable(
                name: "Employee",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employee", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Location",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddressId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Location", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Location_Address_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Address",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompositeForeignKeyTable",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EFCompositeKeyTableFirstName = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    EFCompositeKeyTableLastName = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompositeForeignKeyTable", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompositeForeignKeyTable_CompositeKeyTable_EFCompositeKeyTableFirstName_EFCompositeKeyTableLastName",
                        columns: x => new { x.EFCompositeKeyTableFirstName, x.EFCompositeKeyTableLastName },
                        principalTable: "CompositeKeyTable",
                        principalColumns: new[] { "FirstName", "LastName" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EFAddressEFEmployee",
                columns: table => new
                {
                    AddressesId = table.Column<int>(type: "int", nullable: false),
                    EmployeesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EFAddressEFEmployee", x => new { x.AddressesId, x.EmployeesId });
                    table.ForeignKey(
                        name: "FK_EFAddressEFEmployee_Address_AddressesId",
                        column: x => x.AddressesId,
                        principalTable: "Address",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EFAddressEFEmployee_Employee_EmployeesId",
                        column: x => x.EmployeesId,
                        principalTable: "Employee",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    LocationId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkLog_Employee_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employee",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkLog_Location_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompositeForeignKeyTable_EFCompositeKeyTableFirstName_EFCompositeKeyTableLastName",
                table: "CompositeForeignKeyTable",
                columns: new[] { "EFCompositeKeyTableFirstName", "EFCompositeKeyTableLastName" });

            migrationBuilder.CreateIndex(
                name: "IX_EFAddressEFEmployee_EmployeesId",
                table: "EFAddressEFEmployee",
                column: "EmployeesId");

            migrationBuilder.CreateIndex(
                name: "IX_Location_AddressId",
                table: "Location",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLog_EmployeeId",
                table: "WorkLog",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLog_LocationId",
                table: "WorkLog",
                column: "LocationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompositeForeignKeyTable");

            migrationBuilder.DropTable(
                name: "EFAddressEFEmployee");

            migrationBuilder.DropTable(
                name: "WorkLog");

            migrationBuilder.DropTable(
                name: "CompositeKeyTable");

            migrationBuilder.DropTable(
                name: "Employee");

            migrationBuilder.DropTable(
                name: "Location");

            migrationBuilder.DropTable(
                name: "Address");
        }
    }
}
