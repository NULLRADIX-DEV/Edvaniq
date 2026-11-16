using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PoolQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Target",
                table: "QuestionPools",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PoolQuestions",
                columns: table => new
                {
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Key = table.Column<string>(type: "char(16)", fixedLength: true, maxLength: 16, nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolQuestions", x => new { x.TopicId, x.Key });
                    table.ForeignKey(
                        name: "FK_PoolQuestions_QuestionPools_TopicId",
                        column: x => x.TopicId,
                        principalTable: "QuestionPools",
                        principalColumn: "TopicId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PoolQuestions_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PoolQuestions_OwnerId",
                table: "PoolQuestions",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PoolQuestions_SectionId",
                table: "PoolQuestions",
                column: "SectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoolQuestions");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "QuestionPools");
        }
    }
}
