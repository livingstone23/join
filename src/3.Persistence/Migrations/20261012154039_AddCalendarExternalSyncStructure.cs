using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarExternalSyncStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "externalconnections",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    externalaccountemail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    externalcalendarid = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sourcecalendarid = table.Column<Guid>(type: "uuid", nullable: true),
                    encryptedrefreshtoken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    grantedscopes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    connectedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lastsyncutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lasterror = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    consecutivefailures = table.Column<int>(type: "integer", nullable: false),
                    inboundsynctoken = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    watchchannelid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    watchresourceid = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    watchchanneltoken = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    watchexpiresutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_externalconnections", x => x.id);
                    table.ForeignKey(
                        name: "fk_externalconnections_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_externalconnections_usercalendars_sourcecalendarid",
                        column: x => x.sourcecalendarid,
                        principalSchema: "calendar",
                        principalTable: "usercalendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_externalconnections_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "externaleventlinks",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendareventid = table.Column<Guid>(type: "uuid", nullable: false),
                    connectionid = table.Column<Guid>(type: "uuid", nullable: false),
                    externaleventid = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    externalicaluid = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    externaletag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    externalupdatedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastsyncedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    syncstate = table.Column<int>(type: "integer", nullable: false),
                    lasterror = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_externaleventlinks", x => x.id);
                    table.ForeignKey(
                        name: "fk_externaleventlinks_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_externaleventlinks_events_calendareventid",
                        column: x => x.calendareventid,
                        principalSchema: "calendar",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_externaleventlinks_externalconnections_connectionid",
                        column: x => x.connectionid,
                        principalSchema: "calendar",
                        principalTable: "externalconnections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "syncoutbox",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendareventid = table.Column<Guid>(type: "uuid", nullable: false),
                    connectionid = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    nextattemptutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enqueuedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lockeduntilutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lasterror = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_syncoutbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_syncoutbox_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_syncoutbox_events_calendareventid",
                        column: x => x.calendareventid,
                        principalSchema: "calendar",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_syncoutbox_externalconnections_connectionid",
                        column: x => x.connectionid,
                        principalSchema: "calendar",
                        principalTable: "externalconnections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendarexternalconnections_company_status_connectedutc",
                schema: "calendar",
                table: "externalconnections",
                columns: new[] { "companyid", "status", "connectedutc" },
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_externalconnections_sourcecalendarid",
                schema: "calendar",
                table: "externalconnections",
                column: "sourcecalendarid");

            migrationBuilder.CreateIndex(
                name: "ix_externalconnections_userid",
                schema: "calendar",
                table: "externalconnections",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ux_calendarexternalconnections_company_user_provider_live",
                schema: "calendar",
                table: "externalconnections",
                columns: new[] { "companyid", "userid", "provider" },
                unique: true,
                filter: "\"gcrecord\" = 0 AND \"status\" <> 2");

            migrationBuilder.CreateIndex(
                name: "ux_calendarexternalconnections_watchchannel_active",
                schema: "calendar",
                table: "externalconnections",
                column: "watchchannelid",
                unique: true,
                filter: "\"watchchannelid\" IS NOT NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_externaleventlinks_calendareventid",
                schema: "calendar",
                table: "externaleventlinks",
                column: "calendareventid");

            migrationBuilder.CreateIndex(
                name: "ix_externaleventlinks_companyid",
                schema: "calendar",
                table: "externaleventlinks",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ux_calendarexternaleventlinks_connection_event_active",
                schema: "calendar",
                table: "externaleventlinks",
                columns: new[] { "connectionid", "calendareventid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_calendarexternaleventlinks_connection_externalevent_active",
                schema: "calendar",
                table: "externaleventlinks",
                columns: new[] { "connectionid", "externaleventid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendarsyncoutbox_status_nextattemptutc_active",
                schema: "calendar",
                table: "syncoutbox",
                columns: new[] { "status", "nextattemptutc" },
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_syncoutbox_calendareventid",
                schema: "calendar",
                table: "syncoutbox",
                column: "calendareventid");

            migrationBuilder.CreateIndex(
                name: "ix_syncoutbox_companyid",
                schema: "calendar",
                table: "syncoutbox",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ux_calendarsyncoutbox_connection_event_pending",
                schema: "calendar",
                table: "syncoutbox",
                columns: new[] { "connectionid", "calendareventid" },
                unique: true,
                filter: "\"status\" = 1 AND \"gcrecord\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "externaleventlinks",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "syncoutbox",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "externalconnections",
                schema: "calendar");
        }
    }
}
