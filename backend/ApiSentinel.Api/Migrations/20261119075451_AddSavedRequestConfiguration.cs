using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedRequestConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "Apis",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestBody",
                table: "Apis",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApiRequestHeaders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptedValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSecret = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRequestHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiRequestHeaders_Apis_ApiId",
                        column: x => x.ApiId,
                        principalTable: "Apis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestHeaders_ApiId_Name",
                table: "ApiRequestHeaders",
                columns: new[] { "ApiId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiRequestHeaders");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "RequestBody",
                table: "Apis");
        }
    }
}