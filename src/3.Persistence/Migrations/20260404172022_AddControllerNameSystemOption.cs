using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddControllerNameSystemOption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemOptions_SystemModules_ModuleId",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.AddColumn<string>(
                name: "ControllerName",
                schema: "Security",
                table: "SystemOptions",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemOptions_SystemModules_ModuleId",
                schema: "Security",
                table: "SystemOptions",
                column: "ModuleId",
                principalSchema: "Admin",
                principalTable: "SystemModules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SystemOptions_SystemModules_ModuleId",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.DropColumn(
                name: "ControllerName",
                schema: "Security",
                table: "SystemOptions");

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
    }
}
