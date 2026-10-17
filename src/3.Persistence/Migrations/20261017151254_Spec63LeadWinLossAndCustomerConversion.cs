using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec63LeadWinLossAndCustomerConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "competitorname",
                schema: "customer",
                table: "leads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "wonexchangerate",
                schema: "customer",
                table: "leads",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "wonvalue",
                schema: "customer",
                table: "leads",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "wonvaluebase",
                schema: "customer",
                table: "leads",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "competitorname",
                schema: "customer",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "wonexchangerate",
                schema: "customer",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "wonvalue",
                schema: "customer",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "wonvaluebase",
                schema: "customer",
                table: "leads");
        }
    }
}
