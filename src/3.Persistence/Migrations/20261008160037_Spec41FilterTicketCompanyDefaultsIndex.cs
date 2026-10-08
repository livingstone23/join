using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec41FilterTicketCompanyDefaultsIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TicketCompanyDefaults_CompanyId",
                schema: "Messaging",
                table: "TicketCompanyDefaults");

            migrationBuilder.CreateIndex(
                name: "UX_TicketCompanyDefaults_Company_Active",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "CompanyId",
                unique: true,
                filter: "[GcRecord] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TicketCompanyDefaults_Company_Active",
                schema: "Messaging",
                table: "TicketCompanyDefaults");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_CompanyId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "CompanyId",
                unique: true);
        }
    }
}
