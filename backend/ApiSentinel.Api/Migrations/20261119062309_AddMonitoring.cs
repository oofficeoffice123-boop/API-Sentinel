using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Monitors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TestCaseId = table.Column<int>(type: "int", nullable: false),
                    FrequencyMinutes = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Monitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Monitors_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Monitors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonitorHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MonitorId = table.Column<int>(type: "int", nullable: true),
                    MonitorName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitorHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitorHistory_Monitors_MonitorId",
                        column: x => x.MonitorId,
                        principalTable: "Monitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_UserId_CreatedAt",
                table: "Alerts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitorHistory_MonitorId_CreatedAt",
                table: "MonitorHistory",
                columns: new[] { "MonitorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Monitors_TestCaseId",
                table: "Monitors",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Monitors_UserId",
                table: "Monitors",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "MonitorHistory");

            migrationBuilder.DropTable(
                name: "Monitors");
        }
    }
}