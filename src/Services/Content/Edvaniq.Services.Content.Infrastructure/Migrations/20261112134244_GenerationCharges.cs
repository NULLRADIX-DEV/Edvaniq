using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GenerationCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GenerationCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    StudyDay = table.Column<DateOnly>(type: "date", nullable: false),
                    ChargedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationCharges", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationCharges_OwnerId",
                table: "GenerationCharges",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationCharges_OwnerId_StudyDay",
                table: "GenerationCharges",
                columns: new[] { "OwnerId", "StudyDay" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationCharges_TopicId",
                table: "GenerationCharges",
                column: "TopicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GenerationCharges");
        }
    }
}
