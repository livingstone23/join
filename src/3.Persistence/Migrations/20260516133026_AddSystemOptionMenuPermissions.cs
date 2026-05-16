using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemOptionMenuPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanDownload",
                schema: "Security",
                table: "SystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleMenu",
                schema: "Security",
                table: "SystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderMenu",
                schema: "Security",
                table: "SystemOptions",
                type: "int",
                nullable: true,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CanDownload",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleMenu",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OrderMenu",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemOptions_ModuleId_OrderMenu",
                schema: "Security",
                table: "SystemOptions",
                columns: new[] { "ModuleId", "OrderMenu" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemOptions_ModuleId_OrderMenu",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.DropColumn(
                name: "CanDownload",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.DropColumn(
                name: "IsVisibleMenu",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.DropColumn(
                name: "OrderMenu",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.DropColumn(
                name: "CanDownload",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropColumn(
                name: "IsVisibleMenu",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropColumn(
                name: "OrderMenu",
                schema: "Security",
                table: "RoleSystemOptions");
        }
    }
}
