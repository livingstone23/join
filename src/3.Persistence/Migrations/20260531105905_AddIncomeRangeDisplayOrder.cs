using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeRangeDisplayOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                schema: "Admin",
                table: "IncomeRanges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                WITH RankedIncomeRanges AS (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY CompanyId
                            ORDER BY MinimumValue ASC, DisplayName ASC) AS NewDisplayOrder
                    FROM Admin.IncomeRanges
                    WHERE GcRecord = 0
                )
                UPDATE ir
                SET DisplayOrder = ranked.NewDisplayOrder
                FROM Admin.IncomeRanges ir
                INNER JOIN RankedIncomeRanges ranked ON ranked.Id = ir.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_IncomeRanges_CompanyId_DisplayOrder_GcRecord",
                schema: "Admin",
                table: "IncomeRanges",
                columns: new[] { "CompanyId", "DisplayOrder", "GcRecord" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomeRanges_CompanyId_DisplayOrder_GcRecord",
                schema: "Admin",
                table: "IncomeRanges");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                schema: "Admin",
                table: "IncomeRanges");
        }
    }
}
