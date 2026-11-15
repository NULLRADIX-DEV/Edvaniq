using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.LearningEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IntroductionUnitStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UnitStartedAt",
                table: "TopicIntroductions",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitStartedAt",
                table: "TopicIntroductions");
        }
    }
}
