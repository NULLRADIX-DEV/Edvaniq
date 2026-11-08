using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TopicsFromAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "Topics",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Topics",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TopicSections",
                columns: table => new
                {
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicSections", x => new { x.TopicId, x.SectionId });
                    table.ForeignKey(
                        name: "FK_TopicSections_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_OwnerId_DocumentId",
                table: "Topics",
                columns: new[] { "OwnerId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TopicSections_OwnerId",
                table: "TopicSections",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TopicSections");

            migrationBuilder.DropIndex(
                name: "IX_Topics_OwnerId_DocumentId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Topics");
        }
    }
}