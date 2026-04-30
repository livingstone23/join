using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderToSystemModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerAddresses");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerContacts_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerContacts");

            migrationBuilder.AddColumn<int>(
                name: "Order",
                schema: "Admin",
                table: "SystemModules",
                type: "int",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerAddresses",
                column: "CustomerId",
                principalSchema: "Admin",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerContacts_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerContacts",
                column: "CustomerId",
                principalSchema: "Admin",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerAddresses");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomerContacts_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerContacts");

            migrationBuilder.DropColumn(
                name: "Order",
                schema: "Admin",
                table: "SystemModules");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAddresses_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerAddresses",
                column: "CustomerId",
                principalSchema: "Admin",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerContacts_Customers_CustomerId",
                schema: "Admin",
                table: "CustomerContacts",
                column: "CustomerId",
                principalSchema: "Admin",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
