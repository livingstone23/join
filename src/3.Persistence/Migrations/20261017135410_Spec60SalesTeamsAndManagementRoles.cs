using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec60SalesTeamsAndManagementRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "salesteams",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salesteams", x => x.id);
                    table.ForeignKey(
                        name: "fk_salesteams_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teamroles",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    leadscope = table.Column<int>(type: "integer", nullable: false),
                    isleader = table.Column<bool>(type: "boolean", nullable: false),
                    canreassign = table.Column<bool>(type: "boolean", nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teamroles", x => x.id);
                    table.ForeignKey(
                        name: "fk_teamroles_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "salesteammembers",
                schema: "customer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    salesteamid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    teamroleid = table.Column<Guid>(type: "uuid", nullable: false),
                    isprimary = table.Column<bool>(type: "boolean", nullable: false),
                    validfrom = table.Column<DateOnly>(type: "date", nullable: false),
                    validto = table.Column<DateOnly>(type: "date", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salesteammembers", x => x.id);
                    table.ForeignKey(
                        name: "fk_salesteammembers_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salesteammembers_salesteams_salesteamid",
                        column: x => x.salesteamid,
                        principalSchema: "customer",
                        principalTable: "salesteams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salesteammembers_teamroles_teamroleid",
                        column: x => x.teamroleid,
                        principalSchema: "customer",
                        principalTable: "teamroles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salesteammembers_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_salesteammembers_teamroleid",
                schema: "customer",
                table: "salesteammembers",
                column: "teamroleid");

            migrationBuilder.CreateIndex(
                name: "ix_salesteammembers_userid",
                schema: "customer",
                table: "salesteammembers",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ux_salesteammembers_company_user_primary",
                schema: "customer",
                table: "salesteammembers",
                columns: new[] { "companyid", "userid" },
                unique: true,
                filter: "\"isprimary\" = TRUE AND \"validto\" IS NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_salesteammembers_team_user_open",
                schema: "customer",
                table: "salesteammembers",
                columns: new[] { "salesteamid", "userid" },
                unique: true,
                filter: "\"validto\" IS NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_salesteams_company_name",
                schema: "customer",
                table: "salesteams",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_teamroles_company_code",
                schema: "customer",
                table: "teamroles",
                columns: new[] { "companyid", "code" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_teamroles_company_name",
                schema: "customer",
                table: "teamroles",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salesteammembers",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "salesteams",
                schema: "customer");

            migrationBuilder.DropTable(
                name: "teamroles",
                schema: "customer");
        }
    }
}
