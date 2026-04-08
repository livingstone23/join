using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixCompanyModuleRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyModules_SystemModules_SystemModuleId",
                schema: "Admin",
                table: "CompanyModules");

            migrationBuilder.DropIndex(
                name: "IX_CompanyModules_SystemModuleId",
                schema: "Admin",
                table: "CompanyModules");

            migrationBuilder.DropColumn(
                name: "SystemModuleId",
                schema: "Admin",
                table: "CompanyModules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SystemModuleId",
                schema: "Admin",
                table: "CompanyModules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyModules_SystemModuleId",
                schema: "Admin",
                table: "CompanyModules",
                column: "SystemModuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyModules_SystemModules_SystemModuleId",
                schema: "Admin",
                table: "CompanyModules",
                column: "SystemModuleId",
                principalSchema: "Admin",
                principalTable: "SystemModules",
                principalColumn: "Id");
        }
    }
}
