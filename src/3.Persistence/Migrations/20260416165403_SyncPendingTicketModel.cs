using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncPendingTicketModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketLogs",
                schema: "Support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LogType = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UserRegisterLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TicketStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConsumedTime = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsOnlyForCreatedAndAssigned = table.Column<bool>(type: "bit", nullable: false),
                    NewAssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketLogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketLogs_TicketStatuses_PreviousStatusId",
                        column: x => x.PreviousStatusId,
                        principalSchema: "Messaging",
                        principalTable: "TicketStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketLogs_TicketStatuses_TicketStatusId",
                        column: x => x.TicketStatusId,
                        principalSchema: "Messaging",
                        principalTable: "TicketStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketLogs_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "Messaging",
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketLogs_TimeUnits_TimeUnitId",
                        column: x => x.TimeUnitId,
                        principalSchema: "Messaging",
                        principalTable: "TimeUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketLogs_Users_NewAssignedToUserId",
                        column: x => x.NewAssignedToUserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketLogs_Users_UserRegisterLogId",
                        column: x => x.UserRegisterLogId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketLog_Tenant_Ticket_Active",
                schema: "Support",
                table: "TicketLogs",
                columns: new[] { "CompanyId", "TicketId", "GcRecord" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketLogs_CompanyId_TicketId",
                schema: "Support",
                table: "TicketLogs",
                columns: new[] { "CompanyId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketLogs_NewAssignedToUserId",
                schema: "Support",
                table: "TicketLogs",
                column: "NewAssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLogs_PreviousStatusId",
                schema: "Support",
                table: "TicketLogs",
                column: "PreviousStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLogs_TicketId",
                schema: "Support",
                table: "TicketLogs",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLogs_TicketStatusId",
                schema: "Support",
                table: "TicketLogs",
                column: "TicketStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLogs_TimeUnitId",
                schema: "Support",
                table: "TicketLogs",
                column: "TimeUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLogs_UserRegisterLogId",
                schema: "Support",
                table: "TicketLogs",
                column: "UserRegisterLogId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketLogs",
                schema: "Support");
        }
    }
}
