using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BlockQuizPoolTooSmall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PoolTooSmall",
                table: "BlockQuizzes",
                type: "tinyint(1)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PoolTooSmall",
                table: "BlockQuizzes");
        }
    }
}
