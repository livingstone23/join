using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "calendar");

            migrationBuilder.CreateTable(
                name: "activities",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    defaultdurationminutes = table.Column<int>(type: "integer", nullable: false),
                    allowsoverlap = table.Column<bool>(type: "boolean", nullable: false),
                    requiresbusinesshours = table.Column<bool>(type: "boolean", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "companynonworkingdays",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    isrecurringyearly = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companynonworkingdays", x => x.id);
                    table.ForeignKey(
                        name: "fk_companynonworkingdays_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "statuses",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    blockstime = table.Column<bool>(type: "boolean", nullable: false),
                    isfinal = table.Column<bool>(type: "boolean", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usercalendars",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owneruserid = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usercalendars", x => x.id);
                    table.ForeignKey(
                        name: "fk_usercalendars_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usercalendars_users_owneruserid",
                        column: x => x.owneruserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usernonworkingperiods",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    startdate = table.Column<DateOnly>(type: "date", nullable: false),
                    enddate = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usernonworkingperiods", x => x.id);
                    table.ForeignKey(
                        name: "fk_usernonworkingperiods_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usernonworkingperiods_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statuscompanies",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendarstatusid = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statuscompanies", x => x.id);
                    table.ForeignKey(
                        name: "fk_statuscompanies_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_statuscompanies_statuses_calendarstatusid",
                        column: x => x.calendarstatusid,
                        principalSchema: "calendar",
                        principalTable: "statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userconfigurations",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    defaultcalendarid = table.Column<Guid>(type: "uuid", nullable: false),
                    businesshoursstart = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    businesshoursend = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    workdays = table.Column<int>(type: "integer", nullable: false),
                    defaultview = table.Column<int>(type: "integer", nullable: false),
                    weekstartson = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userconfigurations", x => x.id);
                    table.ForeignKey(
                        name: "fk_userconfigurations_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_userconfigurations_usercalendars_defaultcalendarid",
                        column: x => x.defaultcalendarid,
                        principalSchema: "calendar",
                        principalTable: "usercalendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_userconfigurations_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "activitycompanies",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendaractivityid = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    defaultdurationminutes = table.Column<int>(type: "integer", nullable: false),
                    allowsoverlap = table.Column<bool>(type: "boolean", nullable: false),
                    requiresbusinesshours = table.Column<bool>(type: "boolean", nullable: false),
                    initialcalendarstatuscompanyid = table.Column<Guid>(type: "uuid", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activitycompanies", x => x.id);
                    table.ForeignKey(
                        name: "fk_activitycompanies_activities_calendaractivityid",
                        column: x => x.calendaractivityid,
                        principalSchema: "calendar",
                        principalTable: "activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_activitycompanies_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_activitycompanies_statuscompanies_initialcalendarstatuscomp~",
                        column: x => x.initialcalendarstatuscompanyid,
                        principalSchema: "calendar",
                        principalTable: "statuscompanies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calendarcompanies",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    defaultactivityuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    isopen24hours = table.Column<bool>(type: "boolean", nullable: false),
                    businesshoursstart = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    businesshoursend = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    defaultcalendarname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rescheduledbyothersstatuscompanyid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendarcompanies", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendarcompanies_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_calendarcompanies_statuscompanies_rescheduledbyothersstatus~",
                        column: x => x.rescheduledbyothersstatuscompanyid,
                        principalSchema: "calendar",
                        principalTable: "statuscompanies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_calendarcompanies_users_defaultactivityuserid",
                        column: x => x.defaultactivityuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendarid = table.Column<Guid>(type: "uuid", nullable: false),
                    owneruserid = table.Column<Guid>(type: "uuid", nullable: false),
                    calendaractivitycompanyid = table.Column<Guid>(type: "uuid", nullable: false),
                    calendarstatuscompanyid = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    videocallurl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isallday = table.Column<bool>(type: "boolean", nullable: false),
                    startutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    endutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recurrencerule = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recurrenceendutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    seriesmasterid = table.Column<Guid>(type: "uuid", nullable: true),
                    originalstartutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    originchannelid = table.Column<Guid>(type: "uuid", nullable: false),
                    origintype = table.Column<int>(type: "integer", nullable: false),
                    originuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    originidentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    originreference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requesterpersonid = table.Column<Guid>(type: "uuid", nullable: true),
                    customerid = table.Column<Guid>(type: "uuid", nullable: true),
                    ticketid = table.Column<Guid>(type: "uuid", nullable: true),
                    projectid = table.Column<Guid>(type: "uuid", nullable: true),
                    statuschangedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    statuschangedbyuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_events_activitycompanies_calendaractivitycompanyid",
                        column: x => x.calendaractivitycompanyid,
                        principalSchema: "calendar",
                        principalTable: "activitycompanies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_communicationchannels_originchannelid",
                        column: x => x.originchannelid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_events_customers_customerid",
                        column: x => x.customerid,
                        principalSchema: "admin",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_events_seriesmasterid",
                        column: x => x.seriesmasterid,
                        principalSchema: "calendar",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_persons_requesterpersonid",
                        column: x => x.requesterpersonid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_projects_projectid",
                        column: x => x.projectid,
                        principalSchema: "admin",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_statuscompanies_calendarstatuscompanyid",
                        column: x => x.calendarstatuscompanyid,
                        principalSchema: "calendar",
                        principalTable: "statuscompanies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_tickets_ticketid",
                        column: x => x.ticketid,
                        principalSchema: "messaging",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_usercalendars_calendarid",
                        column: x => x.calendarid,
                        principalSchema: "calendar",
                        principalTable: "usercalendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_users_originuserid",
                        column: x => x.originuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_users_owneruserid",
                        column: x => x.owneruserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_users_statuschangedbyuserid",
                        column: x => x.statuschangedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventlogs",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendareventid = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    previousstartutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    previousendutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    newstartutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    newendutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    previousstatuscompanyid = table.Column<Guid>(type: "uuid", nullable: true),
                    newstatuscompanyid = table.Column<Guid>(type: "uuid", nullable: true),
                    channelid = table.Column<Guid>(type: "uuid", nullable: false),
                    actoruserid = table.Column<Guid>(type: "uuid", nullable: true),
                    actoridentifier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurredutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventlogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_eventlogs_communicationchannels_channelid",
                        column: x => x.channelid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventlogs_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_eventlogs_events_calendareventid",
                        column: x => x.calendareventid,
                        principalSchema: "calendar",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_eventlogs_users_actoruserid",
                        column: x => x.actoruserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_calendaractivities_code_active",
                schema: "calendar",
                table: "activities",
                column: "code",
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_activitycompanies_calendaractivityid",
                schema: "calendar",
                table: "activitycompanies",
                column: "calendaractivityid");

            migrationBuilder.CreateIndex(
                name: "ix_activitycompanies_initialcalendarstatuscompanyid",
                schema: "calendar",
                table: "activitycompanies",
                column: "initialcalendarstatuscompanyid");

            migrationBuilder.CreateIndex(
                name: "ux_calendaractivitycompanies_company_activity_active",
                schema: "calendar",
                table: "activitycompanies",
                columns: new[] { "companyid", "calendaractivityid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendarcompanies_defaultactivityuserid",
                schema: "calendar",
                table: "calendarcompanies",
                column: "defaultactivityuserid");

            migrationBuilder.CreateIndex(
                name: "ix_calendarcompanies_rescheduledbyothersstatuscompanyid",
                schema: "calendar",
                table: "calendarcompanies",
                column: "rescheduledbyothersstatuscompanyid");

            migrationBuilder.CreateIndex(
                name: "ux_calendarcompanies_company_active",
                schema: "calendar",
                table: "calendarcompanies",
                column: "companyid",
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_calendarcompanynonworkingdays_company_date_active",
                schema: "calendar",
                table: "companynonworkingdays",
                columns: new[] { "companyid", "date" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendareventlogs_company_event_occurred",
                schema: "calendar",
                table: "eventlogs",
                columns: new[] { "companyid", "calendareventid", "occurredutc" },
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_eventlogs_actoruserid",
                schema: "calendar",
                table: "eventlogs",
                column: "actoruserid");

            migrationBuilder.CreateIndex(
                name: "ix_eventlogs_calendareventid",
                schema: "calendar",
                table: "eventlogs",
                column: "calendareventid");

            migrationBuilder.CreateIndex(
                name: "ix_eventlogs_channelid",
                schema: "calendar",
                table: "eventlogs",
                column: "channelid");

            migrationBuilder.CreateIndex(
                name: "ix_calendarevents_calendar_range",
                schema: "calendar",
                table: "events",
                columns: new[] { "calendarid", "startutc" },
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendarevents_owner_range",
                schema: "calendar",
                table: "events",
                columns: new[] { "companyid", "owneruserid", "startutc", "endutc" },
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendarevents_owner_recurring",
                schema: "calendar",
                table: "events",
                columns: new[] { "companyid", "owneruserid", "recurrenceendutc" },
                filter: "\"recurrencerule\" IS NOT NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendarevents_requesterperson",
                schema: "calendar",
                table: "events",
                column: "requesterpersonid",
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendarevents_ticket",
                schema: "calendar",
                table: "events",
                column: "ticketid",
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_events_calendaractivitycompanyid",
                schema: "calendar",
                table: "events",
                column: "calendaractivitycompanyid");

            migrationBuilder.CreateIndex(
                name: "ix_events_calendarstatuscompanyid",
                schema: "calendar",
                table: "events",
                column: "calendarstatuscompanyid");

            migrationBuilder.CreateIndex(
                name: "ix_events_customerid",
                schema: "calendar",
                table: "events",
                column: "customerid");

            migrationBuilder.CreateIndex(
                name: "ix_events_originchannelid",
                schema: "calendar",
                table: "events",
                column: "originchannelid");

            migrationBuilder.CreateIndex(
                name: "ix_events_originuserid",
                schema: "calendar",
                table: "events",
                column: "originuserid");

            migrationBuilder.CreateIndex(
                name: "ix_events_owneruserid",
                schema: "calendar",
                table: "events",
                column: "owneruserid");

            migrationBuilder.CreateIndex(
                name: "ix_events_projectid",
                schema: "calendar",
                table: "events",
                column: "projectid");

            migrationBuilder.CreateIndex(
                name: "ix_events_statuschangedbyuserid",
                schema: "calendar",
                table: "events",
                column: "statuschangedbyuserid");

            migrationBuilder.CreateIndex(
                name: "ux_calendarevents_company_channel_originreference_active",
                schema: "calendar",
                table: "events",
                columns: new[] { "companyid", "originchannelid", "originreference" },
                unique: true,
                filter: "\"originreference\" IS NOT NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_calendarevents_seriesmaster_originalstart_active",
                schema: "calendar",
                table: "events",
                columns: new[] { "seriesmasterid", "originalstartutc" },
                unique: true,
                filter: "\"seriesmasterid\" IS NOT NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_statuscompanies_calendarstatusid",
                schema: "calendar",
                table: "statuscompanies",
                column: "calendarstatusid");

            migrationBuilder.CreateIndex(
                name: "ux_calendarstatuscompanies_company_status_active",
                schema: "calendar",
                table: "statuscompanies",
                columns: new[] { "companyid", "calendarstatusid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_calendarstatuses_code_active",
                schema: "calendar",
                table: "statuses",
                column: "code",
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_usercalendars_owneruserid",
                schema: "calendar",
                table: "usercalendars",
                column: "owneruserid");

            migrationBuilder.CreateIndex(
                name: "ux_usercalendars_company_owner_name_active",
                schema: "calendar",
                table: "usercalendars",
                columns: new[] { "companyid", "owneruserid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_userconfigurations_defaultcalendarid",
                schema: "calendar",
                table: "userconfigurations",
                column: "defaultcalendarid");

            migrationBuilder.CreateIndex(
                name: "ix_userconfigurations_userid",
                schema: "calendar",
                table: "userconfigurations",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ux_calendaruserconfigurations_company_user_active",
                schema: "calendar",
                table: "userconfigurations",
                columns: new[] { "companyid", "userid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_calendarusernonworkingperiods_company_user_range",
                schema: "calendar",
                table: "usernonworkingperiods",
                columns: new[] { "companyid", "userid", "startdate", "enddate" },
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_usernonworkingperiods_userid",
                schema: "calendar",
                table: "usernonworkingperiods",
                column: "userid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendarcompanies",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "companynonworkingdays",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "eventlogs",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "userconfigurations",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "usernonworkingperiods",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "events",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "activitycompanies",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "usercalendars",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "activities",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "statuscompanies",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "statuses",
                schema: "calendar");
        }
    }
}
