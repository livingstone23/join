using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeUserConnectionLogUserIdOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserConnectionLogs_Users_UserId",
                schema: "Security",
                table: "UserConnectionLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                schema: "Security",
                table: "UserConnectionLogs",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddForeignKey(
                name: "FK_UserConnectionLogs_Users_UserId",
                schema: "Security",
                table: "UserConnectionLogs",
                column: "UserId",
                principalSchema: "Security",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserConnectionLogs_Users_UserId",
                schema: "Security",
                table: "UserConnectionLogs");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                schema: "Security",
                table: "UserConnectionLogs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserConnectionLogs_Users_UserId",
                schema: "Security",
                table: "UserConnectionLogs",
                column: "UserId",
                principalSchema: "Security",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
