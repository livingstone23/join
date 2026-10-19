using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// SPEC 67 — schema for the unified "Persona/Cliente" lookup (FE-42): every <c>calendar.events</c> row
    /// gets <c>requesterpersonid</c> and <c>customerid</c> so the activity index can render the chosen link without
    /// extra calls. The follow-up data backfill lives in the same migration name and runs immediately after
    /// (rules 2-3 mirror the runtime resolution in <c>CalendarEventCommandProcessor.ValidateLinksAsync</c>):
    /// rows with only the person get the customer; rows with only the customer get the person; rows already
    /// coherent are left untouched, and the migration also walks soft-deleted events (no
    /// <c>gcrecord</c> filter). No-op <c>Down</c>: only data + the two nullable columns are written.
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
            migrationBuilder.Sql("""
                UPDATE "calendar"."events" AS e
                SET "customerid" = NULL
                FROM "customer"."customers" AS cust
                WHERE cust."id" = e."customerid"
                """);
            migrationBuilder.Sql("""
                UPDATE "calendar"."events" AS e
                SET "requesterpersonid" = NULL
                FROM "admin"."persons" AS p
                WHERE p."id" = e."requesterpersonid"
                """);
        }
    }
}