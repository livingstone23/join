using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec62LeadInteractionDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leadattachmentsettings",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alloweddocumenttypes = table.Column<int>(type: "integer", nullable: false),
                    maxfilesizebytes = table.Column<long>(type: "bigint", nullable: false),
                    maxfilesperinteraction = table.Column<int>(type: "integer", nullable: false),
                    maxfilesperday = table.Column<int>(type: "integer", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leadattachmentsettings", x => x.id);
                    table.ForeignKey(
                        name: "fk_leadattachmentsettings_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leaddocuments",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    leadid = table.Column<Guid>(type: "uuid", nullable: false),
                    leadinteractionid = table.Column<Guid>(type: "uuid", nullable: true),
                    documenttype = table.Column<int>(type: "integer", nullable: false),
                    originalname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    newname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storageprovider = table.Column<int>(type: "integer", nullable: false),
                    contenttype = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sizebytes = table.Column<long>(type: "bigint", nullable: false),
                    uploadedbyuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaddocuments", x => x.id);
                    table.ForeignKey(
                        name: "fk_leaddocuments_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leaddocuments_leadinteractions_leadinteractionid",
                        column: x => x.leadinteractionid,
                        principalSchema: "customer",
                        principalTable: "leadinteractions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leaddocuments_leads_leadid",
                        column: x => x.leadid,
                        principalSchema: "customer",
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leaddocuments_users_uploadedbyuserid",
                        column: x => x.uploadedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_leadattachmentsettings_company",
                schema: "customer",
                table: "leadattachmentsettings",
                column: "companyid",
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_leaddocuments_company_created",
                schema: "customer",
                table: "leaddocuments",
                columns: new[] { "companyid", "created" });

            migrationBuilder.CreateIndex(
                name: "ix_leaddocuments_interaction",
                schema: "customer",
                table: "leaddocuments",
                column: "leadinteractionid");

            migrationBuilder.CreateIndex(
                name: "ix_leaddocuments_lead_created",
                schema: "customer",
                table: "leaddocuments",
                columns: new[] { "leadid", "created" });

            migrationBuilder.CreateIndex(
                name: "ix_leaddocuments_uploadedbyuserid",
                schema: "customer",
                table: "leaddocuments",
                column: "uploadedbyuserid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leadattachmentsettings",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "leaddocuments",
                schema: "customer");
        }
    }
}
