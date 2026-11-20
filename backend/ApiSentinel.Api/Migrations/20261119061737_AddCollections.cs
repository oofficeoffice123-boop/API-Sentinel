using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Apis_UserId",
                table: "Apis");

            migrationBuilder.AddColumn<int>(
                name: "CollectionId",
                table: "Apis",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Apis",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Apis_CollectionId",
                table: "Apis",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Apis_UserId_CollectionId",
                table: "Apis",
                columns: new[] { "UserId", "CollectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_UserId_Name",
                table: "Collections",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Apis_Collections_CollectionId",
                table: "Apis",
                column: "CollectionId",
                principalTable: "Collections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Apis_Collections_CollectionId",
                table: "Apis");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Apis_CollectionId",
                table: "Apis");

            migrationBuilder.DropIndex(
                name: "IX_Apis_UserId_CollectionId",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "CollectionId",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Apis");

            migrationBuilder.CreateIndex(
                name: "IX_Apis_UserId",
                table: "Apis",
                column: "UserId");
        }
    }
}