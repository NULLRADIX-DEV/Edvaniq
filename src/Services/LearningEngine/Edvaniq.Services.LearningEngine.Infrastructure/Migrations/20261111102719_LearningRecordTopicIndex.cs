using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.LearningEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LearningRecordTopicIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LearningRecords_OwnerId_TopicId",
                table: "LearningRecords",
                columns: new[] { "OwnerId", "TopicId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LearningRecords_OwnerId_TopicId",
                table: "LearningRecords");
        }
    }
}
