using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCanExportCanExecuteToSystemOption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanExecute",
                schema: "Security",
                table: "SystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanExport",
                schema: "Security",
                table: "SystemOptions",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanExecute",
                schema: "Security",
                table: "SystemOptions");

            migrationBuilder.DropColumn(
                name: "CanExport",
                schema: "Security",
                table: "SystemOptions");
        }
    }
}
