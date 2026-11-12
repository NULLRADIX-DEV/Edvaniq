using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BudgetExhaustedFor",
                table: "AiSwitches",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BudgetWarnedFor",
                table: "AiSwitches",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyBudget",
                table: "AiSwitches",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BudgetExhaustedFor",
                table: "AiSwitches");

            migrationBuilder.DropColumn(
                name: "BudgetWarnedFor",
                table: "AiSwitches");

            migrationBuilder.DropColumn(
                name: "MonthlyBudget",
                table: "AiSwitches");
        }
    }
}
