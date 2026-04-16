using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateTicketCompanyDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketCompanyDefaults",
                schema: "Messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CodeSequenceLength = table.Column<int>(type: "int", nullable: false),
                    UsePersonalizedCode = table.Column<bool>(type: "bit", nullable: false),
                    TicketStatusDefaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TicketComplexityDefaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TimeUnitDefaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AreaDefaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectDefaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChannelDefaultId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketCompanyDefaults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketCompanyDefaults_Areas_AreaDefaultId",
                        column: x => x.AreaDefaultId,
                        principalSchema: "Admin",
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketCompanyDefaults_CommunicationChannels_ChannelDefaultId",
                        column: x => x.ChannelDefaultId,
                        principalSchema: "Common",
                        principalTable: "CommunicationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketCompanyDefaults_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketCompanyDefaults_Projects_ProjectDefaultId",
                        column: x => x.ProjectDefaultId,
                        principalSchema: "Admin",
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketCompanyDefaults_TicketComplexities_TicketComplexityDefaultId",
                        column: x => x.TicketComplexityDefaultId,
                        principalSchema: "Messaging",
                        principalTable: "TicketComplexities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketCompanyDefaults_TicketStatuses_TicketStatusDefaultId",
                        column: x => x.TicketStatusDefaultId,
                        principalSchema: "Messaging",
                        principalTable: "TicketStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketCompanyDefaults_TimeUnits_TimeUnitDefaultId",
                        column: x => x.TimeUnitDefaultId,
                        principalSchema: "Messaging",
                        principalTable: "TimeUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_AreaDefaultId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "AreaDefaultId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_ChannelDefaultId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "ChannelDefaultId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_CompanyId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_ProjectDefaultId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "ProjectDefaultId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_TicketComplexityDefaultId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "TicketComplexityDefaultId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_TicketStatusDefaultId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "TicketStatusDefaultId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCompanyDefaults_TimeUnitDefaultId",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                column: "TimeUnitDefaultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketCompanyDefaults",
                schema: "Messaging");
        }
    }
}
