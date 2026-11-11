using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Attempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    QuestionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AskedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Answered = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Option = table.Column<int>(type: "int", nullable: true),
                    Correct = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    ResponseTimeMs = table.Column<int>(type: "int", nullable: true),
                    AnsweredAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_OwnerId",
                table: "Attempts",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attempts");
        }
    }
}
