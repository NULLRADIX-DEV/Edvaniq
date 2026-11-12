using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KnownTopicRankAndSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "KnownTopics",
                type: "int",
                nullable: true);

            // Known topics from before count as size 1 until the next TopicsChanged, as the planner did.
            migrationBuilder.AddColumn<int>(
                name: "Size",
                table: "KnownTopics",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rank",
                table: "KnownTopics");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "KnownTopics");
        }
    }
}
