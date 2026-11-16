using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ModelId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Front = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Back = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_DocumentId",
                table: "Cards",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_OwnerId",
                table: "Cards",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_TopicId",
                table: "Cards",
                column: "TopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cards");
        }
    }
}
