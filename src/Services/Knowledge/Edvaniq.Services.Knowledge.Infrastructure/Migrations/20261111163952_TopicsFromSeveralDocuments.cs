using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TopicsFromSeveralDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "TopicSections",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "AnalysisName",
                table: "Topics",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true);

            // Until now a topic had sections of the document that made it only, under the name the analysis gave it
            // unless the user renamed it since.
            migrationBuilder.Sql(
                "UPDATE `TopicSections` AS s JOIN `Topics` AS t ON t.`Id` = s.`TopicId` "
                + "SET s.`DocumentId` = t.`DocumentId` WHERE t.`DocumentId` IS NOT NULL;");
            migrationBuilder.Sql("UPDATE `Topics` SET `AnalysisName` = `Name` WHERE `DocumentId` IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_TopicSections_DocumentId",
                table: "TopicSections",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TopicSections_DocumentId",
                table: "TopicSections");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "TopicSections");

            migrationBuilder.DropColumn(
                name: "AnalysisName",
                table: "Topics");
        }
    }
}