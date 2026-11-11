using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CallId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CalledAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    Outcome = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAttempts", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiUsageDays",
                columns: table => new
                {
                    Day = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Calls = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    UnpricedAttempts = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageDays", x => new { x.Day, x.Purpose, x.Model, x.PromptVersion });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiAttempts_CalledAt",
                table: "AiAttempts",
                column: "CalledAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiAttempts_CallId_Attempt",
                table: "AiAttempts",
                columns: new[] { "CallId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiAttempts_DocumentId",
                table: "AiAttempts",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAttempts_OwnerId",
                table: "AiAttempts",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAttempts");

            migrationBuilder.DropTable(
                name: "AiUsageDays");
        }
    }
}
