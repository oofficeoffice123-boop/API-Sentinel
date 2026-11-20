using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TestCaseId",
                table: "Runs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TestCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedStatus = table.Column<int>(type: "int", nullable: true),
                    MaxDurationMs = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestCases_Apis_ApiId",
                        column: x => x.ApiId,
                        principalTable: "Apis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assertions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TestCaseId = table.Column<int>(type: "int", nullable: false),
                    JsonPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExpectedJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assertions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assertions_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_TestCaseId",
                table: "Runs",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Assertions_TestCaseId",
                table: "Assertions",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_ApiId",
                table: "TestCases",
                column: "ApiId");

            migrationBuilder.AddForeignKey(
                name: "FK_Runs_TestCases_TestCaseId",
                table: "Runs",
                column: "TestCaseId",
                principalTable: "TestCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Runs_TestCases_TestCaseId",
                table: "Runs");

            migrationBuilder.DropTable(
                name: "Assertions");

            migrationBuilder.DropTable(
                name: "TestCases");

            migrationBuilder.DropIndex(
                name: "IX_Runs_TestCaseId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "TestCaseId",
                table: "Runs");
        }
    }
}