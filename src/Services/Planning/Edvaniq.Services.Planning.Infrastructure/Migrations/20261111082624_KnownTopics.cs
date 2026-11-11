using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KnownTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnownTopics",
                columns: table => new
                {
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    LearningGoalId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Hidden = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownTopics", x => x.TopicId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TopicsSnapshots",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    LearningGoalId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicsSnapshots", x => new { x.OwnerId, x.LearningGoalId });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_KnownTopics_OwnerId",
                table: "KnownTopics",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownTopics_OwnerId_LearningGoalId",
                table: "KnownTopics",
                columns: new[] { "OwnerId", "LearningGoalId" });

            migrationBuilder.CreateIndex(
                name: "IX_TopicsSnapshots_OwnerId",
                table: "TopicsSnapshots",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnownTopics");

            migrationBuilder.DropTable(
                name: "TopicsSnapshots");
        }
    }
}
