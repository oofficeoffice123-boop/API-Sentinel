using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSentinel.Api.Migrations
{
    /// <inheritdoc />
    public partial class IndexRefreshTokenCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                table: "RefreshTokens");
        }
    }
}