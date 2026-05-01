using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleSystemOptionFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_Companies_CompanyId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_Roles_RoleId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_SystemOptions_SystemOptionId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RoleSystemOptions",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropIndex(
                name: "IX_RoleSystemOptions_CompanyId",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.RenameTable(
                name: "RoleSystemOptions",
                schema: "Security",
                newName: "RoleSystemOptions",
                newSchema: "Admin");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RoleSystemOptions",
                schema: "Admin",
                table: "RoleSystemOptions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_CompanyId_RoleId_SystemOptionId",
                schema: "Admin",
                table: "RoleSystemOptions",
                columns: new[] { "CompanyId", "RoleId", "SystemOptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_RoleId",
                schema: "Admin",
                table: "RoleSystemOptions",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoleSystemOptions_Companies_CompanyId",
                schema: "Admin",
                table: "RoleSystemOptions",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleSystemOptions_Roles_RoleId",
                schema: "Admin",
                table: "RoleSystemOptions",
                column: "RoleId",
                principalSchema: "Security",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleSystemOptions_SystemOptions_SystemOptionId",
                schema: "Admin",
                table: "RoleSystemOptions",
                column: "SystemOptionId",
                principalSchema: "Security",
                principalTable: "SystemOptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_Companies_CompanyId",
                schema: "Admin",
                table: "RoleSystemOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_Roles_RoleId",
                schema: "Admin",
                table: "RoleSystemOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleSystemOptions_SystemOptions_SystemOptionId",
                schema: "Admin",
                table: "RoleSystemOptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RoleSystemOptions",
                schema: "Admin",
                table: "RoleSystemOptions");

            migrationBuilder.DropIndex(
                name: "IX_RoleSystemOptions_CompanyId_RoleId_SystemOptionId",
                schema: "Admin",
                table: "RoleSystemOptions");

            migrationBuilder.DropIndex(
                name: "IX_RoleSystemOptions_RoleId",
                schema: "Admin",
                table: "RoleSystemOptions");

            migrationBuilder.RenameTable(
                name: "RoleSystemOptions",
                schema: "Admin",
                newName: "RoleSystemOptions",
                newSchema: "Security");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RoleSystemOptions",
                schema: "Security",
                table: "RoleSystemOptions",
                columns: new[] { "RoleId", "SystemOptionId" });

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
                name: "FK_RoleSystemOptions_Roles_RoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "RoleId",
                principalSchema: "Security",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleSystemOptions_SystemOptions_SystemOptionId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "SystemOptionId",
                principalSchema: "Security",
                principalTable: "SystemOptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
