using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.LearningEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MasteryCarries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MasteryCarries",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Value = table.Column<double>(type: "double", nullable: true),
                    At = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastCorrect = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    PreviousCorrect = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    LastAnswerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RuleVersion = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasteryCarries", x => new { x.OwnerId, x.TopicId });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LearningRecords_AnsweredAt",
                table: "LearningRecords",
                column: "AnsweredAt");

            migrationBuilder.CreateIndex(
                name: "IX_MasteryCarries_OwnerId",
                table: "MasteryCarries",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MasteryCarries");

            migrationBuilder.DropIndex(
                name: "IX_LearningRecords_AnsweredAt",
                table: "LearningRecords");
        }
    }
}
