using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRoleSystemOptionSchemaToSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "RoleSystemOptions",
                schema: "Admin",
                newName: "RoleSystemOptions",
                newSchema: "Security");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "RoleSystemOptions",
                schema: "Security",
                newName: "RoleSystemOptions",
                newSchema: "Admin");
        }
    }
}
