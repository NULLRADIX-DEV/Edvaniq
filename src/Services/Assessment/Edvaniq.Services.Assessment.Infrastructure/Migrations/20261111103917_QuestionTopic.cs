using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TopicId",
                table: "Questions",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_OwnerId_TopicId",
                table: "Questions",
                columns: new[] { "OwnerId", "TopicId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_OwnerId_TopicId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "TopicId",
                table: "Questions");
        }
    }
}
