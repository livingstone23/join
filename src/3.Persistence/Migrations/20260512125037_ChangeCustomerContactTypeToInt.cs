using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeCustomerContactTypeToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Create a temporary column of type int
            migrationBuilder.AddColumn<int>(
                name: "ContactType_Temp",
                schema: "Admin",
                table: "CustomerContacts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Step 2: Copy data from old column to new column with conversion
            migrationBuilder.Sql(@"
                UPDATE [Admin].[CustomerContacts]
                SET [ContactType_Temp] = CASE [ContactType]
                    WHEN 'PrimaryEmail' THEN 1
                    WHEN 'AlternativeEmail' THEN 2
                    WHEN 'MobilePhone' THEN 3
                    WHEN 'Landline' THEN 4
                    WHEN 'WhatsApp' THEN 5
                    WHEN 'Other' THEN 99
                    ELSE 99
                END
                WHERE [ContactType] IS NOT NULL;
            ");

            // Step 3: Drop the old column
            migrationBuilder.DropColumn(
                name: "ContactType",
                schema: "Admin",
                table: "CustomerContacts");

            // Step 4: Rename the temporary column to the original name
            migrationBuilder.RenameColumn(
                name: "ContactType_Temp",
                schema: "Admin",
                table: "CustomerContacts",
                newName: "ContactType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ContactType",
                schema: "Admin",
                table: "CustomerContacts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
