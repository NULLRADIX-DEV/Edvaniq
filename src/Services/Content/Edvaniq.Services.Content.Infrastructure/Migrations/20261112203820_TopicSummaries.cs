using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TopicSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TopicSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LearningGoalId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Length = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: true),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    PromptVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReadyAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AiOutages = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicSummaries", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TopicSummarySections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    SummaryId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Used = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicSummarySections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TopicSummarySections_TopicSummaries_SummaryId",
                        column: x => x.SummaryId,
                        principalTable: "TopicSummaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TopicSummaries_OwnerId",
                table: "TopicSummaries",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TopicSummaries_OwnerId_TopicId_Length",
                table: "TopicSummaries",
                columns: new[] { "OwnerId", "TopicId", "Length" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TopicSummaries_Status_NextAttemptAt",
                table: "TopicSummaries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TopicSummarySections_DocumentId",
                table: "TopicSummarySections",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TopicSummarySections_OwnerId",
                table: "TopicSummarySections",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TopicSummarySections_SummaryId",
                table: "TopicSummarySections",
                column: "SummaryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TopicSummarySections");

            migrationBuilder.DropTable(
                name: "TopicSummaries");
        }
    }
}
