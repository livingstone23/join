using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// SPEC 68 A — adds <c>CalendarUserConfiguration.CanViewOthersActivities</c> (default <c>false</c>) so the
    /// activity index can list the activities of every member of the company when an administrator flips it on
    /// per user. The seed of the <c>Activities</c> menu option and its per-role permissions live in
    /// <c>DatabaseSeeder</c> and apply on every tenant provisioning.
    /// </summary>
    public partial class Spec68CalendarActivityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "canviewothersactivities",
                schema: "calendar",
                table: "userconfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "canviewothersactivities",
                schema: "calendar",
                table: "userconfigurations");
        }
    }
}