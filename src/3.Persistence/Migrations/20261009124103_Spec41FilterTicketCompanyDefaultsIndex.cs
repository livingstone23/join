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
                name: "ix_ticketcompanydefaults_companyid",
                schema: "messaging",
                table: "ticketcompanydefaults");

            migrationBuilder.CreateIndex(
                name: "ux_ticketcompanydefaults_company_active",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "companyid",
                unique: true,
                filter: "\"gcrecord\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ticketcompanydefaults_company_active",
                schema: "messaging",
                table: "ticketcompanydefaults");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_companyid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "companyid",
                unique: true);
        }
    }
}
