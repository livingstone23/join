using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAttachmentsAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketAttachmentSettings",
                schema: "Messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllowedDocumentTypes = table.Column<int>(type: "int", nullable: false),
                    MaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MaxFilesPerTicket = table.Column<int>(type: "int", nullable: false),
                    MaxFilesPerDay = table.Column<int>(type: "int", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketAttachmentSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketAttachmentSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketDocuments",
                schema: "Support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketLogsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NewName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StorageProvider = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketDocuments_TicketLogs_TicketLogsId",
                        column: x => x.TicketLogsId,
                        principalSchema: "Support",
                        principalTable: "TicketLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketDocuments_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "Messaging",
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_TicketAttachmentSettings_Company_Active",
                schema: "Messaging",
                table: "TicketAttachmentSettings",
                column: "CompanyId",
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TicketDocuments_Company_Created",
                schema: "Support",
                table: "TicketDocuments",
                columns: new[] { "CompanyId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketDocuments_Ticket_Created",
                schema: "Support",
                table: "TicketDocuments",
                columns: new[] { "TicketId", "Created" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketDocuments_TicketLogsId",
                schema: "Support",
                table: "TicketDocuments",
                column: "TicketLogsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketAttachmentSettings",
                schema: "Messaging");

            migrationBuilder.DropTable(
                name: "TicketDocuments",
                schema: "Support");
        }
    }
}
