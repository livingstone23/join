using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// SPEC 67 — data-only backfill: keeps the pair <c>(requesterpersonid, customerid)</c> of every
    /// <c>calendar.events</c> row coherent before the unified "Persona/Cliente" lookup is the only entry point on
    /// the frontend (FE-42). The two rules mirror the runtime resolution in
    /// <c>CalendarEventCommandProcessor.ValidateLinksAsync</c>:
    /// <list type="number">
    ///   <item>Rule 2 — when a row carries the person but no customer and the person has one in the company, fill
    ///   in the customer's id (live or inactive, SPEC 55 uniqueness guarantees at most one).</item>
    ///   <item>Rule 3 — when a row carries the customer but no person, fill in the customer's person id.</item>
    /// </list>
    /// Rows already coherent (both columns set or both null) are left untouched, and the migration also walks
    /// soft-deleted events (it does not filter on <c>gcrecord</c>). No-op <c>Down</c>: the migration only writes
    /// data, no schema to revert.
    /// </summary>
    public partial class Spec67CalendarEventPersonCustomerLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rule 2 — person → customer (SPEC 67). Customer must be live (matches the runtime check); at most
            // one per (company, person) thanks to UX_Customers_Company_Person (SPEC 55), so the UPDATE is unambiguous.
            migrationBuilder.Sql("""
                UPDATE "calendar"."events" AS e
                SET "customerid" = cust."id"
                FROM "customer"."customers" AS cust
                WHERE cust."companyid" = e."companyid"
                  AND cust."personid" = e."requesterpersonid"
                  AND cust."gcrecord" = 0
                  AND e."requesterpersonid" IS NOT NULL
                  AND e."customerid" IS NULL;
                """);

            // Rule 3 — customer → person (SPEC 67). No filter on the customer's gcrecord because the FK already
            // guarantees the row exists; we only need its personid.
            migrationBuilder.Sql("""
                UPDATE "calendar"."events" AS e
                SET "requesterpersonid" = cust."personid"
                FROM "customer"."customers" AS cust
                WHERE cust."id" = e."customerid"
                  AND e."customerid" IS NOT NULL
                  AND e."requesterpersonid" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: data-only backfill, no schema to revert. The two columns stay as they were before Up.
        }
    }
}