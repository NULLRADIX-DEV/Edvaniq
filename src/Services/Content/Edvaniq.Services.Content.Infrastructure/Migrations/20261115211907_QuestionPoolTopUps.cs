using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionPoolTopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GenerationCharges_TopicId",
                table: "GenerationCharges");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextTopUpAt",
                table: "QuestionPools",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionKeys",
                table: "QuestionPools",
                type: "varchar(340)",
                maxLength: 340,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Questions",
                table: "QuestionPools",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TopUpOutages",
                table: "QuestionPools",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TopUpStopped",
                table: "QuestionPools",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TopUps",
                table: "QuestionPools",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "GenerationCharges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_QuestionPools_Questions_NextTopUpAt",
                table: "QuestionPools",
                columns: new[] { "Questions", "NextTopUpAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationCharges_TopicId_Round",
                table: "GenerationCharges",
                columns: new[] { "TopicId", "Round" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QuestionPools_Questions_NextTopUpAt",
                table: "QuestionPools");

            migrationBuilder.DropIndex(
                name: "IX_GenerationCharges_TopicId_Round",
                table: "GenerationCharges");

            migrationBuilder.DropColumn(
                name: "NextTopUpAt",
                table: "QuestionPools");

            migrationBuilder.DropColumn(
                name: "QuestionKeys",
                table: "QuestionPools");

            migrationBuilder.DropColumn(
                name: "Questions",
                table: "QuestionPools");

            migrationBuilder.DropColumn(
                name: "TopUpOutages",
                table: "QuestionPools");

            migrationBuilder.DropColumn(
                name: "TopUpStopped",
                table: "QuestionPools");

            migrationBuilder.DropColumn(
                name: "TopUps",
                table: "QuestionPools");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "GenerationCharges");

            migrationBuilder.CreateIndex(
                name: "IX_GenerationCharges_TopicId",
                table: "GenerationCharges",
                column: "TopicId",
                unique: true);
        }
    }
}
