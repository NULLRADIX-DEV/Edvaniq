using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TopicSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TopicSets",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    LearningGoalId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicSets", x => new { x.OwnerId, x.LearningGoalId });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TopicSets_OwnerId",
                table: "TopicSets",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TopicSets");
        }
    }
}
