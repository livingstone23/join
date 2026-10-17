using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec61LeadsPipelineAndInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leads",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owneruserid = table.Column<Guid>(type: "uuid", nullable: false),
                    salesteamid = table.Column<Guid>(type: "uuid", nullable: true),
                    leadstageid = table.Column<Guid>(type: "uuid", nullable: false),
                    leadsourceid = table.Column<Guid>(type: "uuid", nullable: false),
                    relationshiptypeid = table.Column<Guid>(type: "uuid", nullable: false),
                    referredbypersonid = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    leadreasonid = table.Column<Guid>(type: "uuid", nullable: true),
                    statusnotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    currencyid = table.Column<Guid>(type: "uuid", nullable: false),
                    exchangerate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    exchangeratedate = table.Column<DateOnly>(type: "date", nullable: false),
                    estimatedvalue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimatedvaluebase = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    estimatedclosedate = table.Column<DateOnly>(type: "date", nullable: true),
                    inactivatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    customerid = table.Column<Guid>(type: "uuid", nullable: true),
                    projectid = table.Column<Guid>(type: "uuid", nullable: true),
                    areaid = table.Column<Guid>(type: "uuid", nullable: true),
                    lastinteractionat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_leads_areas_areaid",
                        column: x => x.areaid,
                        principalSchema: "admin",
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_currencies_currencyid",
                        column: x => x.currencyid,
                        principalSchema: "finance",
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_customers_customerid",
                        column: x => x.customerid,
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_leadreasons_leadreasonid",
                        column: x => x.leadreasonid,
                        principalSchema: "customer",
                        principalTable: "leadreasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_leadsources_leadsourceid",
                        column: x => x.leadsourceid,
                        principalSchema: "customer",
                        principalTable: "leadsources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_leadstages_leadstageid",
                        column: x => x.leadstageid,
                        principalSchema: "customer",
                        principalTable: "leadstages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_persons_referredbypersonid",
                        column: x => x.referredbypersonid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_projects_projectid",
                        column: x => x.projectid,
                        principalSchema: "admin",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_relationshiptypes_relationshiptypeid",
                        column: x => x.relationshiptypeid,
                        principalSchema: "customer",
                        principalTable: "relationshiptypes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_salesteams_salesteamid",
                        column: x => x.salesteamid,
                        principalSchema: "customer",
                        principalTable: "salesteams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_users_owneruserid",
                        column: x => x.owneruserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leadinteractions",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leadid = table.Column<Guid>(type: "uuid", nullable: false),
                    contacttypecompanyid = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    occurredat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    durationminutes = table.Column<int>(type: "integer", nullable: true),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leadinteractions", x => x.id);
                    table.ForeignKey(
                        name: "fk_leadinteractions_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadinteractions_contacttypecompanies_contacttypecompanyid",
                        column: x => x.contacttypecompanyid,
                        principalSchema: "customer",
                        principalTable: "contacttypecompanies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadinteractions_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leadownershiphistory",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leadid = table.Column<Guid>(type: "uuid", nullable: false),
                    fromuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    touserid = table.Column<Guid>(type: "uuid", nullable: false),
                    fromteamid = table.Column<Guid>(type: "uuid", nullable: true),
                    toteamid = table.Column<Guid>(type: "uuid", nullable: true),
                    cause = table.Column<int>(type: "integer", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leadownershiphistory", x => x.id);
                    table.ForeignKey(
                        name: "fk_leadownershiphistory_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadownershiphistory_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leadstagehistory",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leadid = table.Column<Guid>(type: "uuid", nullable: false),
                    fromstageid = table.Column<Guid>(type: "uuid", nullable: true),
                    tostageid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    changedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leadstagehistory", x => x.id);
                    table.ForeignKey(
                        name: "fk_leadstagehistory_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadstagehistory_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadstagehistory_leadstages_fromstageid",
                        column: x => x.fromstageid,
                        principalSchema: "customer",
                        principalTable: "leadstages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadstagehistory_leadstages_tostageid",
                        column: x => x.tostageid,
                        principalSchema: "customer",
                        principalTable: "leadstages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leadstatushistory",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leadid = table.Column<Guid>(type: "uuid", nullable: false),
                    fromstatus = table.Column<int>(type: "integer", nullable: true),
                    tostatus = table.Column<int>(type: "integer", nullable: false),
                    leadreasonid = table.Column<Guid>(type: "uuid", nullable: true),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leadstatushistory", x => x.id);
                    table.ForeignKey(
                        name: "fk_leadstatushistory_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadstatushistory_leadreasons_leadreasonid",
                        column: x => x.leadreasonid,
                        principalSchema: "customer",
                        principalTable: "leadreasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadstatushistory_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leadcomments",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leadid = table.Column<Guid>(type: "uuid", nullable: false),
                    leadinteractionid = table.Column<Guid>(type: "uuid", nullable: true),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leadcomments", x => x.id);
                    table.ForeignKey(
                        name: "fk_leadcomments_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadcomments_leadinteractions_leadinteractionid",
                        column: x => x.leadinteractionid,
                        principalSchema: "customer",
                        principalTable: "leadinteractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadcomments_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leadproducts",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leadid = table.Column<Guid>(type: "uuid", nullable: false),
                    productid = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unitprice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discountpercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    linetotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    offeredininteractionid = table.Column<Guid>(type: "uuid", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leadproducts", x => x.id);
                    table.ForeignKey(
                        name: "fk_leadproducts_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadproducts_leadinteractions_offeredininteractionid",
                        column: x => x.offeredininteractionid,
                        principalSchema: "customer",
                        principalTable: "leadinteractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadproducts_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leadproducts_products_productid",
                        column: x => x.productid,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leadcomments_companyid",
                schema: "customer",
                table: "leadcomments",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_leadcomments_lead_created",
                schema: "customer",
                table: "leadcomments",
                columns: new[] { "leadid", "created" });

            migrationBuilder.CreateIndex(
                name: "ix_leadcomments_leadinteractionid",
                schema: "customer",
                table: "leadcomments",
                column: "leadinteractionid");

            migrationBuilder.CreateIndex(
                name: "ix_leadinteractions_company_lead_occurredat",
                schema: "customer",
                table: "leadinteractions",
                columns: new[] { "companyid", "leadid", "occurredat" });

            migrationBuilder.CreateIndex(
                name: "ix_leadinteractions_contacttypecompanyid",
                schema: "customer",
                table: "leadinteractions",
                column: "contacttypecompanyid");

            migrationBuilder.CreateIndex(
                name: "ix_leadinteractions_leadid",
                schema: "customer",
                table: "leadinteractions",
                column: "leadid");

            migrationBuilder.CreateIndex(
                name: "ix_leadownershiphistory_companyid",
                schema: "customer",
                table: "leadownershiphistory",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_leadownershiphistory_lead_at",
                schema: "customer",
                table: "leadownershiphistory",
                columns: new[] { "leadid", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_leadproducts_companyid",
                schema: "customer",
                table: "leadproducts",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_leadproducts_lead_status",
                schema: "customer",
                table: "leadproducts",
                columns: new[] { "leadid", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leadproducts_offeredininteractionid",
                schema: "customer",
                table: "leadproducts",
                column: "offeredininteractionid");

            migrationBuilder.CreateIndex(
                name: "ix_leadproducts_product",
                schema: "customer",
                table: "leadproducts",
                column: "productid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_areaid",
                schema: "customer",
                table: "leads",
                column: "areaid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_company_owner_status",
                schema: "customer",
                table: "leads",
                columns: new[] { "companyid", "owneruserid", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_company_person_status",
                schema: "customer",
                table: "leads",
                columns: new[] { "companyid", "personid", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_company_status_stage",
                schema: "customer",
                table: "leads",
                columns: new[] { "companyid", "status", "leadstageid" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_company_team_status",
                schema: "customer",
                table: "leads",
                columns: new[] { "companyid", "salesteamid", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_currencyid",
                schema: "customer",
                table: "leads",
                column: "currencyid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_customerid",
                schema: "customer",
                table: "leads",
                column: "customerid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_leadreasonid",
                schema: "customer",
                table: "leads",
                column: "leadreasonid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_leadsourceid",
                schema: "customer",
                table: "leads",
                column: "leadsourceid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_leadstageid",
                schema: "customer",
                table: "leads",
                column: "leadstageid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_owneruserid",
                schema: "customer",
                table: "leads",
                column: "owneruserid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_personid",
                schema: "customer",
                table: "leads",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_projectid",
                schema: "customer",
                table: "leads",
                column: "projectid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_referredbypersonid",
                schema: "customer",
                table: "leads",
                column: "referredbypersonid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_relationshiptypeid",
                schema: "customer",
                table: "leads",
                column: "relationshiptypeid");

            migrationBuilder.CreateIndex(
                name: "ix_leads_salesteamid",
                schema: "customer",
                table: "leads",
                column: "salesteamid");

            migrationBuilder.CreateIndex(
                name: "ux_leads_company_code",
                schema: "customer",
                table: "leads",
                columns: new[] { "companyid", "code" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_leadstagehistory_companyid",
                schema: "customer",
                table: "leadstagehistory",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_leadstagehistory_fromstageid",
                schema: "customer",
                table: "leadstagehistory",
                column: "fromstageid");

            migrationBuilder.CreateIndex(
                name: "ix_leadstagehistory_lead_changedat",
                schema: "customer",
                table: "leadstagehistory",
                columns: new[] { "leadid", "changedat" });

            migrationBuilder.CreateIndex(
                name: "ix_leadstagehistory_tostageid",
                schema: "customer",
                table: "leadstagehistory",
                column: "tostageid");

            migrationBuilder.CreateIndex(
                name: "ix_leadstatushistory_companyid",
                schema: "customer",
                table: "leadstatushistory",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_leadstatushistory_lead_at",
                schema: "customer",
                table: "leadstatushistory",
                columns: new[] { "leadid", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_leadstatushistory_leadreasonid",
                schema: "customer",
                table: "leadstatushistory",
                column: "leadreasonid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leadcomments",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "leadownershiphistory",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "leadproducts",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "leadstagehistory",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "leadstatushistory",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "leadinteractions",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "leads",
                schema: "customer");
        }
    }
}
