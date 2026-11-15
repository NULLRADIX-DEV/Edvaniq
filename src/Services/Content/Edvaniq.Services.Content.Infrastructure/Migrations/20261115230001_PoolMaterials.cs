using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PoolMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Rounds",
                table: "QuestionPools",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Before, a top-up charged its round by TopUps, so the next round number starts above those.
            migrationBuilder.Sql("UPDATE `QuestionPools` SET `Rounds` = `TopUps`;");

            migrationBuilder.CreateTable(
                name: "PoolMaterials",
                columns: table => new
                {
                    TopicId = table.Column<Guid>(type: "char(36)", nullable: false),
                    SectionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    QuestionKeys = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoolMaterials", x => new { x.TopicId, x.SectionId });
                    table.ForeignKey(
                        name: "FK_PoolMaterials_QuestionPools_TopicId",
                        column: x => x.TopicId,
                        principalTable: "QuestionPools",
                        principalColumn: "TopicId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PoolMaterials_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PoolMaterials_OwnerId",
                table: "PoolMaterials",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PoolMaterials_SectionId",
                table: "PoolMaterials",
                column: "SectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoolMaterials");

            migrationBuilder.DropColumn(
                name: "Rounds",
                table: "QuestionPools");
        }
    }
}
