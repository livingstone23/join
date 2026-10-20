using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec72PersonCustomerLeadEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "personemailmessageid",
                schema: "customer",
                table: "leadinteractions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "emailcompanysettings",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fromemail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    fromname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    maxemailsperuserperday = table.Column<int>(type: "integer", nullable: false),
                    maxattachmentstotalbytes = table.Column<long>(type: "bigint", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emailcompanysettings", x => x.id);
                    table.ForeignKey(
                        name: "fk_emailcompanysettings_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personemailmessages",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    customerid = table.Column<Guid>(type: "uuid", nullable: true),
                    leadid = table.Column<Guid>(type: "uuid", nullable: true),
                    leadinteractionid = table.Column<Guid>(type: "uuid", nullable: true),
                    emailtemplateid = table.Column<Guid>(type: "uuid", nullable: true),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    toaddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ccaddresses = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    fromaddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    replytoaddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    bodyhtml = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    providermessageid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    errormessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sentbyuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    sentat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personemailmessages", x => x.id);
                    table.ForeignKey(
                        name: "fk_personemailmessages_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personemailmessages_customers_customerid",
                        column: x => x.customerid,
                        principalSchema: "customer",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personemailmessages_emailtemplates_emailtemplateid",
                        column: x => x.emailtemplateid,
                        principalSchema: "admin",
                        principalTable: "emailtemplates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personemailmessages_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personemailmessages_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personemailmessageattachments",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personemailmessageid = table.Column<Guid>(type: "uuid", nullable: false),
                    sourcekind = table.Column<int>(type: "integer", nullable: false),
                    sourceid = table.Column<Guid>(type: "uuid", nullable: false),
                    filename = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contenttype = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sizebytes = table.Column<long>(type: "bigint", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personemailmessageattachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_personemailmessageattachments_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personemailmessageattachments_personemailmessages_personema~",
                        column: x => x.personemailmessageid,
                        principalSchema: "admin",
                        principalTable: "personemailmessages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_emailcompanysettings_company",
                schema: "admin",
                table: "emailcompanysettings",
                column: "companyid",
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessageattachments_companyid",
                schema: "admin",
                table: "personemailmessageattachments",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessageattachments_message",
                schema: "admin",
                table: "personemailmessageattachments",
                column: "personemailmessageid");

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessages_company_lead_sentat",
                schema: "admin",
                table: "personemailmessages",
                columns: new[] { "companyid", "leadid", "sentat" });

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessages_company_person_sentat",
                schema: "admin",
                table: "personemailmessages",
                columns: new[] { "companyid", "personid", "sentat" });

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessages_company_user_sentat",
                schema: "admin",
                table: "personemailmessages",
                columns: new[] { "companyid", "sentbyuserid", "sentat" });

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessages_customerid",
                schema: "admin",
                table: "personemailmessages",
                column: "customerid");

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessages_emailtemplateid",
                schema: "admin",
                table: "personemailmessages",
                column: "emailtemplateid");

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessages_leadid",
                schema: "admin",
                table: "personemailmessages",
                column: "leadid");

            migrationBuilder.CreateIndex(
                name: "ix_personemailmessages_personid",
                schema: "admin",
                table: "personemailmessages",
                column: "personid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "emailcompanysettings",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "personemailmessageattachments",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "personemailmessages",
                schema: "admin");

            migrationBuilder.DropColumn(
                name: "personemailmessageid",
                schema: "customer",
                table: "leadinteractions");
        }
    }
}
