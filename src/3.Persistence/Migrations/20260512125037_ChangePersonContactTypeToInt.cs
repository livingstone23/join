using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangePersonContactTypeToInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("SqlServer"))
            {
                migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Admin].[PersonContacts]', N'U') IS NULL
BEGIN
    RETURN;
END;

IF COL_LENGTH(N'[Admin].[PersonContacts]', N'ContactType_Temp') IS NULL
BEGIN
    ALTER TABLE [Admin].[PersonContacts]
    ADD [ContactType_Temp] int NOT NULL CONSTRAINT [DF_PersonContacts_ContactType_Temp] DEFAULT 0;
END;

DECLARE @ContactTypeDataType nvarchar(128);

SELECT @ContactTypeDataType = DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'Admin'
  AND TABLE_NAME = 'PersonContacts'
  AND COLUMN_NAME = 'ContactType';

IF @ContactTypeDataType IN ('nvarchar', 'varchar', 'nchar', 'char', 'text', 'ntext')
BEGIN
    UPDATE [Admin].[PersonContacts]
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
END
ELSE IF @ContactTypeDataType IN ('int', 'smallint', 'tinyint', 'bigint')
BEGIN
    UPDATE [Admin].[PersonContacts]
    SET [ContactType_Temp] = CONVERT(int, [ContactType])
    WHERE [ContactType] IS NOT NULL;
END;

IF COL_LENGTH(N'[Admin].[PersonContacts]', N'ContactType') IS NOT NULL
BEGIN
    ALTER TABLE [Admin].[PersonContacts] DROP COLUMN [ContactType];
END;

IF COL_LENGTH(N'[Admin].[PersonContacts]', N'ContactType_Temp') IS NOT NULL
    AND COL_LENGTH(N'[Admin].[PersonContacts]', N'ContactType') IS NULL
BEGIN
    EXEC sp_rename N'[Admin].[PersonContacts].[ContactType_Temp]', N'ContactType', 'COLUMN';
END;

IF OBJECT_ID(N'[Admin].[DF_PersonContacts_ContactType_Temp]', N'D') IS NOT NULL
BEGIN
    EXEC sp_rename N'[Admin].[DF_PersonContacts_ContactType_Temp]', N'DF_PersonContacts_ContactType', 'OBJECT';
END;
");

                return;
            }

            migrationBuilder.AddColumn<int>(
                name: "ContactType_Temp",
                schema: "Admin",
                table: "PersonContacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE ""Admin"".""PersonContacts""
                SET ""ContactType_Temp"" = CASE ""ContactType""
                    WHEN 'PrimaryEmail' THEN 1
                    WHEN 'AlternativeEmail' THEN 2
                    WHEN 'MobilePhone' THEN 3
                    WHEN 'Landline' THEN 4
                    WHEN 'WhatsApp' THEN 5
                    WHEN 'Other' THEN 99
                    ELSE 99
                END
                WHERE ""ContactType"" IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "ContactType",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.RenameColumn(
                name: "ContactType_Temp",
                schema: "Admin",
                table: "PersonContacts",
                newName: "ContactType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ContactType",
                schema: "Admin",
                table: "PersonContacts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
