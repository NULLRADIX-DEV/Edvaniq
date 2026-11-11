using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DoneBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoneBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    LearningGoalId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudyDay = table.Column<DateTime>(type: "date", nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    DoneAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoneBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoneBlocks_LearningGoals_LearningGoalId",
                        column: x => x.LearningGoalId,
                        principalTable: "LearningGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DoneBlocks_LearningGoalId",
                table: "DoneBlocks",
                column: "LearningGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_DoneBlocks_OwnerId",
                table: "DoneBlocks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_DoneBlocks_OwnerId_LearningGoalId_StudyDay",
                table: "DoneBlocks",
                columns: new[] { "OwnerId", "LearningGoalId", "StudyDay" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoneBlocks");
        }
    }
}
