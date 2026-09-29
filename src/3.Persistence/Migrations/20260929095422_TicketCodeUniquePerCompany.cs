using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketCodeUniquePerCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_Code",
                schema: "Messaging",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_CompanyId",
                schema: "Messaging",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CompanyId_Code",
                schema: "Messaging",
                table: "Tickets",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_CompanyId_Code",
                schema: "Messaging",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Code",
                schema: "Messaging",
                table: "Tickets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CompanyId",
                schema: "Messaging",
                table: "Tickets",
                column: "CompanyId");
        }
    }
}
