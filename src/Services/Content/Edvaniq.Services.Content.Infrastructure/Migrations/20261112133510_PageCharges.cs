using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PageCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PageCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudyDay = table.Column<DateOnly>(type: "date", nullable: false),
                    Pages = table.Column<int>(type: "int", nullable: false),
                    ChargedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageCharges", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PageCharges_DocumentId",
                table: "PageCharges",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PageCharges_OwnerId",
                table: "PageCharges",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PageCharges_OwnerId_StudyDay",
                table: "PageCharges",
                columns: new[] { "OwnerId", "StudyDay" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageCharges");
        }
    }
}
