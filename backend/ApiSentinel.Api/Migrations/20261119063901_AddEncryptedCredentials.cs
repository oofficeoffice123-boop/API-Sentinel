using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthHeaderName",
                table: "Apis",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthenticationType",
                table: "Apis",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCredential",
                table: "Apis",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthHeaderName",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "AuthenticationType",
                table: "Apis");

            migrationBuilder.DropColumn(
                name: "EncryptedCredential",
                table: "Apis");
        }
    }
}