using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// SPEC 69 — calendar activity comments and documents, plus the per-company attachment limits.
    /// Creates <c>Calendar.EventComments</c>, <c>Calendar.EventDocuments</c>, <c>Calendar.AttachmentSettings</c>,
    /// and extends <c>Calendar.EventLogs</c> with the comment / document ids used by the journal. The seed of the
    /// <c>ActivityRecords</c> menu option and its permissions lives in <c>DatabaseSeeder</c>.
    /// </summary>
    public partial class Spec69CalendarActivityCommentsAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "eventcomments",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendareventid = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    authoruserid = table.Column<Guid>(type: "uuid", nullable: false),
                    createdutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deletedbyuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    deletedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletereason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventcomments", x => x.id);
                    table.ForeignKey(
                        "fk_eventcomments_calendarevents_calendareventid",
                        x => x.calendareventid,
                        principalSchema: "calendar",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        "fk_eventcomments_companies_companyid",
                        x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        "fk_eventcomments_users_authoruserid",
                        x => x.authoruserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        "fk_eventcomments_users_deletedbyuserid",
                        x => x.deletedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventcomments_company",
                schema: "calendar",
                table: "eventcomments",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "IX_EventComments_Event_CreatedUtc",
                schema: "calendar",
                table: "eventcomments",
                columns: new[] { "calendareventid", "createdutc" });

            migrationBuilder.CreateTable(
                name: "eventdocuments",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendareventid = table.Column<Guid>(type: "uuid", nullable: false),
                    calendareventcommentid = table.Column<Guid>(type: "uuid", nullable: true),
                    documenttype = table.Column<int>(type: "integer", nullable: false),
                    originalname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    newname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storageprovider = table.Column<int>(type: "integer", nullable: false),
                    contenttype = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sizebytes = table.Column<long>(type: "bigint", nullable: false),
                    uploadedbyuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    deletedbyuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    deletedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletereason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventdocuments", x => x.id);
                    table.ForeignKey(
                        "fk_eventdocuments_calendarevents_calendareventid",
                        x => x.calendareventid,
                        principalSchema: "calendar",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        "fk_eventdocuments_calendareventcomments_calendareventcommentid",
                        x => x.calendareventcommentid,
                        principalSchema: "calendar",
                        principalTable: "eventcomments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        "fk_eventdocuments_companies_companyid",
                        x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        "fk_eventdocuments_users_uploadedbyuserid",
                        x => x.uploadedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        "fk_eventdocuments_users_deletedbyuserid",
                        x => x.deletedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_eventdocuments_company",
                schema: "calendar",
                table: "eventdocuments",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "IX_EventDocuments_Comment",
                schema: "calendar",
                table: "eventdocuments",
                column: "calendareventcommentid");

            migrationBuilder.CreateIndex(
                name: "IX_EventDocuments_Event_Created",
                schema: "calendar",
                table: "eventdocuments",
                columns: new[] { "calendareventid", "created" });

            migrationBuilder.CreateTable(
                name: "attachmentsettings",
                schema: "calendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alloweddocumenttypes = table.Column<int>(type: "integer", nullable: false),
                    maxfilesizebytes = table.Column<long>(type: "bigint", nullable: false),
                    maxfilespercomment = table.Column<int>(type: "integer", nullable: false),
                    maxfilesperday = table.Column<int>(type: "integer", nullable: true),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachmentsettings", x => x.id);
                    table.ForeignKey(
                        "fk_attachmentsettings_companies_companyid",
                        x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_CalendarAttachmentSettings_Company",
                schema: "calendar",
                table: "attachmentsettings",
                column: "companyid",
                unique: true,
                filter: "\"gcrecord\" = 0");

            // SPEC 69 — the journal gains two foreign-key columns so the bitácora page can show the
            // affected comment / document without joining the related tables.
            migrationBuilder.AddColumn<Guid>(
                name: "calendareventcommentid",
                schema: "calendar",
                table: "eventlogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "calendareventdocumentid",
                schema: "calendar",
                table: "eventlogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEventLogs_Comment",
                schema: "calendar",
                table: "eventlogs",
                column: "calendareventcommentid");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEventLogs_Document",
                schema: "calendar",
                table: "eventlogs",
                column: "calendareventdocumentid");

            migrationBuilder.AddForeignKey(
                name: "fk_eventlogs_calendareventcomments_calendareventcommentid",
                schema: "calendar",
                table: "eventlogs",
                column: "calendareventcommentid",
                principalSchema: "calendar",
                principalTable: "eventcomments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_eventlogs_calendareventdocuments_calendareventdocumentid",
                schema: "calendar",
                table: "eventlogs",
                column: "calendareventdocumentid",
                principalSchema: "calendar",
                principalTable: "eventdocuments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_eventlogs_calendareventdocuments_calendareventdocumentid",
                schema: "calendar",
                table: "eventlogs");

            migrationBuilder.DropForeignKey(
                name: "fk_eventlogs_calendareventcomments_calendareventcommentid",
                schema: "calendar",
                table: "eventlogs");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEventLogs_Document",
                schema: "calendar",
                table: "eventlogs");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEventLogs_Comment",
                schema: "calendar",
                table: "eventlogs");

            migrationBuilder.DropColumn(
                name: "calendareventdocumentid",
                schema: "calendar",
                table: "eventlogs");

            migrationBuilder.DropColumn(
                name: "calendareventcommentid",
                schema: "calendar",
                table: "eventlogs");

            migrationBuilder.DropTable(
                name: "attachmentsettings",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "eventdocuments",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "eventcomments",
                schema: "calendar");
        }
    }
}