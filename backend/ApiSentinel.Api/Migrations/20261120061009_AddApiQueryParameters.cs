using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApiQueryParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiQueryParameters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EncryptedValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSecret = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiQueryParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiQueryParameters_Apis_ApiId",
                        column: x => x.ApiId,
                        principalTable: "Apis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiQueryParameters_ApiId_SortOrder",
                table: "ApiQueryParameters",
                columns: new[] { "ApiId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiQueryParameters");
        }
    }
}