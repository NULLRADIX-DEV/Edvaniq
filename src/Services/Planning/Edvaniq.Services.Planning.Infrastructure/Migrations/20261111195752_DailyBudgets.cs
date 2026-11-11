using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DailyBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyBudgets",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    MondayMinutes = table.Column<int>(type: "int", nullable: false),
                    TuesdayMinutes = table.Column<int>(type: "int", nullable: false),
                    WednesdayMinutes = table.Column<int>(type: "int", nullable: false),
                    ThursdayMinutes = table.Column<int>(type: "int", nullable: false),
                    FridayMinutes = table.Column<int>(type: "int", nullable: false),
                    SaturdayMinutes = table.Column<int>(type: "int", nullable: false),
                    SundayMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyBudgets", x => x.OwnerId);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DailyBudgets_OwnerId",
                table: "DailyBudgets",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyBudgets");
        }
    }
}
