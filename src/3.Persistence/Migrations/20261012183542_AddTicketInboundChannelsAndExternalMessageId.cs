using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketInboundChannelsAndExternalMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "externalmessageid",
                schema: "messaging",
                table: "tickets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticketinboundchannels",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channelid = table.Column<Guid>(type: "uuid", nullable: false),
                    webhooktoken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketinboundchannels", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketinboundchannels_communicationchannels_channelid",
                        column: x => x.channelid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketinboundchannels_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_tickets_company_channel_externalmessageid_active",
                schema: "messaging",
                table: "tickets",
                columns: new[] { "companyid", "channelid", "externalmessageid" },
                unique: true,
                filter: "\"externalmessageid\" IS NOT NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_ticketinboundchannels_channelid",
                schema: "messaging",
                table: "ticketinboundchannels",
                column: "channelid");

            migrationBuilder.CreateIndex(
                name: "ux_ticketinboundchannels_company_channel_active",
                schema: "messaging",
                table: "ticketinboundchannels",
                columns: new[] { "companyid", "channelid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_ticketinboundchannels_token_active",
                schema: "messaging",
                table: "ticketinboundchannels",
                column: "webhooktoken",
                unique: true,
                filter: "\"gcrecord\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticketinboundchannels",
                schema: "messaging");

            migrationBuilder.DropIndex(
                name: "ux_tickets_company_channel_externalmessageid_active",
                schema: "messaging",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "externalmessageid",
                schema: "messaging",
                table: "tickets");
        }
    }
}
