using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveShadowPropertyApplicationRoleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_Roles_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropIndex(
                name: "IX_RoleSystemOptions_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropColumn(
                name: "ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "ApplicationRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoleSystemOptions_Roles_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "ApplicationRoleId",
                principalSchema: "Security",
                principalTable: "Roles",
                principalColumn: "Id");
        }
    }
}
