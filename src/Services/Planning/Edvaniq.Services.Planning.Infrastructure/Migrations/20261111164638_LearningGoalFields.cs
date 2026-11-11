using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LearningGoalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExamDate",
                table: "LearningGoals",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamType",
                table: "LearningGoals",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Goal",
                table: "LearningGoals",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "LearningGoals",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Level",
                table: "LearningGoals",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "LearningGoals",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputLanguage",
                table: "LearningGoals",
                type: "varchar(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExamDate",
                table: "LearningGoals");

            migrationBuilder.DropColumn(
                name: "ExamType",
                table: "LearningGoals");

            migrationBuilder.DropColumn(
                name: "Goal",
                table: "LearningGoals");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "LearningGoals");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "LearningGoals");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "LearningGoals");

            migrationBuilder.DropColumn(
                name: "OutputLanguage",
                table: "LearningGoals");
        }
    }
}
