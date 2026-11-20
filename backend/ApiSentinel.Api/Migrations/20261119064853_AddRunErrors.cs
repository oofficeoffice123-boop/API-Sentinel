using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "Runs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Error",
                table: "Runs");
        }
    }
}