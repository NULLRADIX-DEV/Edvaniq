using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.LearningEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TopicIntroductions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TopicIntroductions",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SessionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    IntroducedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicIntroductions", x => new { x.OwnerId, x.TopicId });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TopicIntroductions_OwnerId",
                table: "TopicIntroductions",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TopicIntroductions");
        }
    }
}
