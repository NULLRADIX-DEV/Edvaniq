using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssessmentBlockQuizzes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "QuizId",
                table: "Attempts",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlockQuizzes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    BlockId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockQuizzes", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_QuizId",
                table: "Attempts",
                column: "QuizId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockQuizzes_OwnerId",
                table: "BlockQuizzes",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockQuizzes_OwnerId_BlockId",
                table: "BlockQuizzes",
                columns: new[] { "OwnerId", "BlockId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Attempts_BlockQuizzes_QuizId",
                table: "Attempts",
                column: "QuizId",
                principalTable: "BlockQuizzes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attempts_BlockQuizzes_QuizId",
                table: "Attempts");

            migrationBuilder.DropTable(
                name: "BlockQuizzes");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_QuizId",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "QuizId",
                table: "Attempts");
        }
    }
}
