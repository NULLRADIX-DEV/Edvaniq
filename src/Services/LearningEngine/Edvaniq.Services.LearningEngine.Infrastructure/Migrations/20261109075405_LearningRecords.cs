using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.LearningEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LearningRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExampleItems");

            migrationBuilder.CreateTable(
                name: "LearningRecords",
                columns: table => new
                {
                    AnswerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SessionId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Correct = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Difficulty = table.Column<int>(type: "int", nullable: true),
                    ResponseTimeMs = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningRecords", x => x.AnswerId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LearningRecords_OwnerId",
                table: "LearningRecords",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearningRecords");

            migrationBuilder.CreateTable(
                name: "ExampleItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExampleItems", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExampleItems_OwnerId",
                table: "ExampleItems",
                column: "OwnerId");
        }
    }
}
