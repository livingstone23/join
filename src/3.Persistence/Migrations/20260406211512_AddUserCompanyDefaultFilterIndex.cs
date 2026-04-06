using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCompanyDefaultFilterIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ;WITH RankedDefaults AS (
                    SELECT [Id],
                           ROW_NUMBER() OVER (PARTITION BY [UserId] ORDER BY [Created], [Id]) AS [RowNumber]
                    FROM [Security].[UserCompanies]
                    WHERE [IsDefault] = 1 AND [GcRecord] = 0
                )
                UPDATE uc
                SET [IsDefault] = 0
                FROM [Security].[UserCompanies] uc
                INNER JOIN RankedDefaults rd ON rd.[Id] = uc.[Id]
                WHERE rd.[RowNumber] > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_UserCompanies_UserId_Default",
                schema: "Security",
                table: "UserCompanies",
                column: "UserId",
                unique: true,
                filter: "[IsDefault] = 1 AND [GcRecord] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_UserCompanies_UserId_Default",
                schema: "Security",
                table: "UserCompanies");
        }
    }
}
