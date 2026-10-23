using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Planning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OwnerIdCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The differ sees no change for a column that exists and the provider drops COLLATE from AlterColumn.
            migrationBuilder.Sql(
                "ALTER TABLE `ExampleItems` MODIFY COLUMN `OwnerId` varchar(128) COLLATE utf8mb4_0900_bin NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `ExampleItems` MODIFY COLUMN `OwnerId` varchar(128) COLLATE utf8mb4_0900_ai_ci NOT NULL;");
        }
    }
}
