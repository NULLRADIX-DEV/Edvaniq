using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttemptRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BlockQuizzes_CreatedAt",
                table: "BlockQuizzes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_AskedAt",
                table: "Attempts",
                column: "AskedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlockQuizzes_CreatedAt",
                table: "BlockQuizzes");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_AskedAt",
                table: "Attempts");
        }
    }
}
