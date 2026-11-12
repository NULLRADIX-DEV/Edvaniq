using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SessionAbort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentStep",
                table: "Sessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "Sessions",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Running");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentStep",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "Sessions");
        }
    }
}
