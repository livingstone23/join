using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// SPEC 55: one customer per person and company, never deleted, active/inactive with a status history, and the
    /// funnel stage as a per-company catalog. Moves <c>admin.customers</c> to the new <c>customer</c> schema first
    /// (data, PK, FK and indexes are kept) and then follows the order of SPEC 55 A.2 and F.21.
    /// </summary>
    public partial class Spec55CustomerSinglePerPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // G.23 — move the existing table to the Customer schema before anything else.
            migrationBuilder.EnsureSchema(
                name: "customer");

            migrationBuilder.RenameTable(
                name: "customers",
                schema: "admin",
                newName: "customers",
                newSchema: "customer");

            // A.2.1 — more than one row per (companyid, personid), live or deleted: fail without touching data.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    duplicates text;
                BEGIN
                    SELECT string_agg(format('(companyid=%s, personid=%s, rows=%s)', companyid, personid, total), ', ')
                    INTO duplicates
                    FROM (
                        SELECT companyid, personid, COUNT(*) AS total
                        FROM customer.customers
                        GROUP BY companyid, personid
                        HAVING COUNT(*) > 1
                    ) d;

                    IF duplicates IS NOT NULL THEN
                        RAISE EXCEPTION 'SPEC 55: duplicated customers per (companyid, personid) must be resolved by hand before this migration: %', duplicates;
                    END IF;
                END $$;
                """);

            // A.2.2 — a "deleted" customer becomes an inactive, live customer.
            migrationBuilder.Sql("""
                UPDATE customer.customers
                SET gcrecord = 0,
                    isactive = FALSE,
                    deactivatedat = COALESCE(deactivatedat, NOW())
                WHERE gcrecord <> 0;
                """);

            migrationBuilder.DropIndex(
                name: "ix_customers_company_personlifecyclestage",
                schema: "customer",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ux_customers_company_person_user",
                schema: "customer",
                table: "customers");

            // F.21 — per-company stage catalog, with the four initial stages for every existing company.
            migrationBuilder.CreateTable(
                name: "customerstages",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    isdefault = table.Column<bool>(type: "boolean", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customerstages", x => x.id);
                    table.ForeignKey(
                        name: "fk_customerstages_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_customerstages_company_code",
                schema: "customer",
                table: "customerstages",
                columns: new[] { "companyid", "code" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_customerstages_company_default",
                schema: "customer",
                table: "customerstages",
                column: "companyid",
                unique: true,
                filter: "\"isdefault\" = TRUE AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_customerstages_company_name",
                schema: "customer",
                table: "customerstages",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.Sql("""
                INSERT INTO customer.customerstages
                    (id, companyid, code, name, description, sortorder, isdefault, isactive, created, createdby, gcrecord)
                SELECT gen_random_uuid(), c.id, s.code, s.name, NULL, s.sortorder, s.isdefault, TRUE, NOW(), 'Migration_Spec55', 0
                FROM common.companies c
                CROSS JOIN (VALUES
                    ('LEAD', 'En contacto', 1, FALSE),
                    ('PROSPECT', 'En proceso', 2, FALSE),
                    ('CUSTOMER', 'Cliente', 3, TRUE),
                    ('FORMER', 'Cliente anterior', 4, FALSE)
                ) AS s(code, name, sortorder, isdefault);
                """);

            // F.21 — each customer keeps the stage equivalent to its old enum value (1 LEAD, 2 PROSPECT,
            // 3 CUSTOMER, 4 FORMER); an unknown value falls back to the company's default stage.
            migrationBuilder.AddColumn<Guid>(
                name: "customerstageid",
                schema: "customer",
                table: "customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE customer.customers cust
                SET customerstageid = st.id
                FROM customer.customerstages st
                WHERE st.companyid = cust.companyid
                  AND st.gcrecord = 0
                  AND st.code = CASE cust.personlifecyclestage
                        WHEN 1 THEN 'LEAD'
                        WHEN 2 THEN 'PROSPECT'
                        WHEN 3 THEN 'CUSTOMER'
                        WHEN 4 THEN 'FORMER'
                        ELSE 'CUSTOMER'
                    END;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "customerstageid",
                schema: "customer",
                table: "customers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "personlifecyclestage",
                schema: "customer",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_stage",
                schema: "customer",
                table: "customers",
                columns: new[] { "companyid", "customerstageid" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_customerstageid",
                schema: "customer",
                table: "customers",
                column: "customerstageid");

            migrationBuilder.AddForeignKey(
                name: "fk_customers_customerstages_customerstageid",
                schema: "customer",
                table: "customers",
                column: "customerstageid",
                principalSchema: "customer",
                principalTable: "customerstages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // A.1 / A.2.3 — one customer per person and company, without filter (covers every row).
            migrationBuilder.CreateIndex(
                name: "ux_customers_company_person",
                schema: "customer",
                table: "customers",
                columns: new[] { "companyid", "personid" },
                unique: true);

            // C.7 — insert-only status history.
            migrationBuilder.CreateTable(
                name: "customerstatushistory",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customerid = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    sourcereferenceid = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    occurredat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customerstatushistory", x => x.id);
                    table.ForeignKey(
                        name: "fk_customerstatushistory_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customerstatushistory_customers_customerid",
                        column: x => x.customerid,
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customerstatushistory_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customerstatushistory_companyid",
                schema: "customer",
                table: "customerstatushistory",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_customerstatushistory_customer_occurredat",
                schema: "customer",
                table: "customerstatushistory",
                columns: new[] { "customerid", "occurredat" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_customerstatushistory_userid",
                schema: "customer",
                table: "customerstatushistory",
                column: "userid");

            // A.2.4 — rule 7: active customers of an inactive person become inactive.
            migrationBuilder.Sql("""
                UPDATE customer.customers cust
                SET isactive = FALSE,
                    deactivatedat = NOW()
                FROM admin.persons p
                WHERE p.id = cust.personid
                  AND cust.isactive = TRUE
                  AND p.isactive = FALSE;
                """);

            // A.2.5 — history of the existing customers: Created (source Migration = 3), plus Deactivated when inactive.
            migrationBuilder.Sql("""
                INSERT INTO customer.customerstatushistory
                    (id, companyid, customerid, action, source, sourcereferenceid, notes, userid, occurredat, created, createdby, gcrecord)
                SELECT gen_random_uuid(), cust.companyid, cust.id, 1, 3, NULL, NULL, cust.userid, cust.activatedat, NOW(), 'Migration_Spec55', 0
                FROM customer.customers cust;

                INSERT INTO customer.customerstatushistory
                    (id, companyid, customerid, action, source, sourcereferenceid, notes, userid, occurredat, created, createdby, gcrecord)
                SELECT gen_random_uuid(), cust.companyid, cust.id, 4, 3, NULL, NULL, cust.userid, COALESCE(cust.deactivatedat, NOW()), NOW(), 'Migration_Spec55', 0
                FROM customer.customers cust
                WHERE cust.isactive = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customerstatushistory",
                schema: "customer");

            migrationBuilder.DropIndex(
                name: "ux_customers_company_person",
                schema: "customer",
                table: "customers");

            migrationBuilder.AddColumn<int>(
                name: "personlifecyclestage",
                schema: "customer",
                table: "customers",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.Sql("""
                UPDATE customer.customers cust
                SET personlifecyclestage = CASE st.code
                        WHEN 'LEAD' THEN 1
                        WHEN 'PROSPECT' THEN 2
                        WHEN 'FORMER' THEN 4
                        ELSE 3
                    END
                FROM customer.customerstages st
                WHERE st.id = cust.customerstageid;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_customers_customerstages_customerstageid",
                schema: "customer",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_company_stage",
                schema: "customer",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_customerstageid",
                schema: "customer",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "customerstageid",
                schema: "customer",
                table: "customers");

            migrationBuilder.DropTable(
                name: "customerstages",
                schema: "customer");

            migrationBuilder.RenameTable(
                name: "customers",
                schema: "customer",
                newName: "customers",
                newSchema: "admin");

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_personlifecyclestage",
                schema: "admin",
                table: "customers",
                columns: new[] { "companyid", "personlifecyclestage" });

            migrationBuilder.CreateIndex(
                name: "ux_customers_company_person_user",
                schema: "admin",
                table: "customers",
                columns: new[] { "companyid", "personid", "userid" },
                unique: true,
                filter: "\"gcrecord\" = 0")
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
