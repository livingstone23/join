using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanExportCanExecuteToRoleSystemOption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanExecute",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanExport",
                schema: "Security",
                table: "RoleSystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanExecute",
                schema: "Security",
                table: "RoleSystemOptions");

            migrationBuilder.DropColumn(
                name: "CanExport",
                schema: "Security",
                table: "RoleSystemOptions");
        }
    }
}
