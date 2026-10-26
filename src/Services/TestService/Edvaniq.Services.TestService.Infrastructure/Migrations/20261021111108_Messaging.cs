using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace Edvaniq.Services.TestService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Messaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Consumer = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.MessageId, x.Consumer });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    MessageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "longtext", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_ReceivedAt",
                table: "inbox_messages",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_MessageId",
                table: "outbox_messages",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_SentAt",
                table: "outbox_messages",
                column: "SentAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
