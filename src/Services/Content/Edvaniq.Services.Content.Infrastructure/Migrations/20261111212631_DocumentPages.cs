using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TextExtractedAt",
                table: "Documents",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Start = table.Column<int>(type: "int", nullable: false),
                    Length = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentPages_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPages_DocumentId_Number",
                table: "DocumentPages",
                columns: new[] { "DocumentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPages_OwnerId",
                table: "DocumentPages",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPages");

            migrationBuilder.DropColumn(
                name: "TextExtractedAt",
                table: "Documents");
        }
    }
}
