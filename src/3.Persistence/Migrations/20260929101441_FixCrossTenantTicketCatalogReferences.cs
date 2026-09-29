using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// Data-only migration. DatabaseSeeder picked ticket complexities and time units without
    /// filtering by company, so seeded tickets (and their logs and the company ticket defaults)
    /// ended up referencing another tenant's catalog rows. Each such reference is re-pointed to
    /// the active row with the same <c>Code</c> in the owning company's own catalog. References
    /// with no same-code counterpart in the owning company are left untouched.
    /// </summary>
    /// <inheritdoc />
    public partial class FixCrossTenantTicketCatalogReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE t SET t.TicketComplexityId = own.Id
                FROM Messaging.Tickets t
                JOIN Messaging.TicketComplexities foreignRow ON foreignRow.Id = t.TicketComplexityId
                CROSS APPLY (
                    SELECT TOP 1 c.Id FROM Messaging.TicketComplexities c
                    WHERE c.CompanyId = t.CompanyId AND c.Code = foreignRow.Code AND c.GcRecord = 0
                    ORDER BY c.Created) own
                WHERE foreignRow.CompanyId <> t.CompanyId;
                """);

            migrationBuilder.Sql("""
                UPDATE t SET t.TimeUnitId = own.Id
                FROM Messaging.Tickets t
                JOIN Messaging.TimeUnits foreignRow ON foreignRow.Id = t.TimeUnitId
                CROSS APPLY (
                    SELECT TOP 1 u.Id FROM Messaging.TimeUnits u
                    WHERE u.CompanyId = t.CompanyId AND u.Code = foreignRow.Code AND u.GcRecord = 0
                    ORDER BY u.Created) own
                WHERE foreignRow.CompanyId <> t.CompanyId;
                """);

            migrationBuilder.Sql("""
                UPDATE l SET l.TimeUnitId = own.Id
                FROM Support.TicketLogs l
                JOIN Messaging.TimeUnits foreignRow ON foreignRow.Id = l.TimeUnitId
                CROSS APPLY (
                    SELECT TOP 1 u.Id FROM Messaging.TimeUnits u
                    WHERE u.CompanyId = l.CompanyId AND u.Code = foreignRow.Code AND u.GcRecord = 0
                    ORDER BY u.Created) own
                WHERE foreignRow.CompanyId <> l.CompanyId;
                """);

            migrationBuilder.Sql("""
                UPDATE d SET d.TicketComplexityDefaultId = own.Id
                FROM Messaging.TicketCompanyDefaults d
                JOIN Messaging.TicketComplexities foreignRow ON foreignRow.Id = d.TicketComplexityDefaultId
                CROSS APPLY (
                    SELECT TOP 1 c.Id FROM Messaging.TicketComplexities c
                    WHERE c.CompanyId = d.CompanyId AND c.Code = foreignRow.Code AND c.GcRecord = 0
                    ORDER BY c.Created) own
                WHERE foreignRow.CompanyId <> d.CompanyId;
                """);

            migrationBuilder.Sql("""
                UPDATE d SET d.TimeUnitDefaultId = own.Id
                FROM Messaging.TicketCompanyDefaults d
                JOIN Messaging.TimeUnits foreignRow ON foreignRow.Id = d.TimeUnitDefaultId
                CROSS APPLY (
                    SELECT TOP 1 u.Id FROM Messaging.TimeUnits u
                    WHERE u.CompanyId = d.CompanyId AND u.Code = foreignRow.Code AND u.GcRecord = 0
                    ORDER BY u.Created) own
                WHERE foreignRow.CompanyId <> d.CompanyId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: the original cross-tenant references were a data defect,
            // and restoring them would reintroduce tenant leakage.
        }
    }
}
