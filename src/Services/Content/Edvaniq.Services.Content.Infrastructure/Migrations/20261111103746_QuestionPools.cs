using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionPools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnalyzedAt",
                table: "Documents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuestionPools",
                columns: table => new
                {
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionPools", x => x.TopicId);
                    table.ForeignKey(
                        name: "FK_QuestionPools_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionPools_DocumentId",
                table: "QuestionPools",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionPools_OwnerId",
                table: "QuestionPools",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionPools");

            migrationBuilder.DropColumn(
                name: "AnalyzedAt",
                table: "Documents");
        }
    }
}
