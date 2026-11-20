using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunAssertions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TestRunId = table.Column<int>(type: "int", nullable: false),
                    JsonPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Operator = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Actual = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Expected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunAssertions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunAssertions_Runs_TestRunId",
                        column: x => x.TestRunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunResponseHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TestRunId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunResponseHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunResponseHeaders_Runs_TestRunId",
                        column: x => x.TestRunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunAssertions_TestRunId",
                table: "RunAssertions",
                column: "TestRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RunResponseHeaders_TestRunId_Name",
                table: "RunResponseHeaders",
                columns: new[] { "TestRunId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunAssertions");

            migrationBuilder.DropTable(
                name: "RunResponseHeaders");
        }
    }
}