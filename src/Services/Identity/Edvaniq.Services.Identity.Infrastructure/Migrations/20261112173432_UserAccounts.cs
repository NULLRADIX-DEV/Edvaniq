using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Edvaniq.Services.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Accounts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Accounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirthYear",
                table: "Accounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "Accounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Accounts",
                type: "varchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Accounts",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Team");

            migrationBuilder.AddColumn<string>(
                name: "Pseudonym",
                table: "Accounts",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Accounts",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Email",
                table: "Accounts",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_Email",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "BirthYear",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Pseudonym",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Accounts");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Accounts",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }
    }
}
