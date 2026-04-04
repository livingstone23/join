using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixSecuritySchemaAndRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemOptions_SystemModules_ModuleId",
                schema: "Admin",
                table: "SystemOptions");

            migrationBuilder.RenameTable(
                name: "SystemOptions",
                schema: "Admin",
                newName: "SystemOptions",
                newSchema: "Security");

            migrationBuilder.RenameTable(
                name: "RoleSystemOptions",
                schema: "Admin",
                newName: "RoleSystemOptions",
                newSchema: "Security");

            migrationBuilder.AddColumn<string>(
                name: "ExternalProvider",
                schema: "Security",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProviderId",
                schema: "Security",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMfaEnabled",
                schema: "Security",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuperAdmin",
                schema: "Security",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuperAdminCompany",
                schema: "Security",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecretKey",
                schema: "Security",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "ApplicationRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_CompanyId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoleSystemOptions_Companies_CompanyId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleSystemOptions_Roles_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "ApplicationRoleId",
                principalSchema: "Security",
                principalTable: "Roles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemOptions_SystemModules_ModuleId",
                schema: "Security",
                table: "SystemOptions",
                column: "ModuleId",
                principalSchema: "Admin",
                principalTable: "SystemModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_Companies_CompanyId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_Roles_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemOptions_SystemModules_ModuleId",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.DropIndex(
                name: "IX_RoleSystemOptions_ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropIndex(
                name: "IX_RoleSystemOptions_CompanyId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropColumn(
                name: "ExternalProvider",
                schema: "Security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExternalProviderId",
                schema: "Security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsMfaEnabled",
                schema: "Security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSuperAdmin",
                schema: "Security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSuperAdminCompany",
                schema: "Security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaSecretKey",
                schema: "Security",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApplicationRoleId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.RenameTable(
                name: "SystemOptions",
                schema: "Security",
                newName: "SystemOptions",
                newSchema: "Admin");

            migrationBuilder.RenameTable(
                name: "RoleSystemOptions",
                schema: "Security",
                newName: "RoleSystemOptions",
                newSchema: "Admin");

            migrationBuilder.AddForeignKey(
                name: "FK_SystemOptions_SystemModules_ModuleId",
                schema: "Admin",
                table: "SystemOptions",
                column: "ModuleId",
                principalSchema: "Admin",
                principalTable: "SystemModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
