using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonPendingConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personpendingconfirmations",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    communicationchannelid = table.Column<Guid>(type: "uuid", nullable: false),
                    channelidentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requestedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    revieweventid = table.Column<Guid>(type: "uuid", nullable: true),
                    customerid = table.Column<Guid>(type: "uuid", nullable: true),
                    resolvedbyuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    resolvedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolutionnotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personpendingconfirmations", x => x.id);
                    table.ForeignKey(
                        name: "fk_personpendingconfirmations_communicationchannels_communicat~",
                        column: x => x.communicationchannelid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personpendingconfirmations_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_personpendingconfirmations_customers_customerid",
                        column: x => x.customerid,
                        principalSchema: "admin",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personpendingconfirmations_events_revieweventid",
                        column: x => x.revieweventid,
                        principalSchema: "calendar",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personpendingconfirmations_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personpendingconfirmations_users_resolvedbyuserid",
                        column: x => x.resolvedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_personpendingconfirmations_communicationchannelid",
                schema: "admin",
                table: "personpendingconfirmations",
                column: "communicationchannelid");

            migrationBuilder.CreateIndex(
                name: "ix_personpendingconfirmations_company_status_requestedutc",
                schema: "admin",
                table: "personpendingconfirmations",
                columns: new[] { "companyid", "status", "requestedutc" },
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_personpendingconfirmations_customerid",
                schema: "admin",
                table: "personpendingconfirmations",
                column: "customerid");

            migrationBuilder.CreateIndex(
                name: "ix_personpendingconfirmations_personid",
                schema: "admin",
                table: "personpendingconfirmations",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_personpendingconfirmations_resolvedbyuserid",
                schema: "admin",
                table: "personpendingconfirmations",
                column: "resolvedbyuserid");

            migrationBuilder.CreateIndex(
                name: "ix_personpendingconfirmations_revieweventid",
                schema: "admin",
                table: "personpendingconfirmations",
                column: "revieweventid");

            migrationBuilder.CreateIndex(
                name: "ux_personpendingconfirmations_company_person_pending",
                schema: "admin",
                table: "personpendingconfirmations",
                columns: new[] { "companyid", "personid" },
                unique: true,
                filter: "\"status\" = 1 AND \"gcrecord\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personpendingconfirmations",
                schema: "admin");
        }
    }
}
