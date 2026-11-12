using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    PromptVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Answer = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiAnswers_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnswers_DocumentId",
                table: "AiAnswers",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnswers_OwnerId",
                table: "AiAnswers",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAnswers_OwnerId_Key",
                table: "AiAnswers",
                columns: new[] { "OwnerId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAnswers");
        }
    }
}
