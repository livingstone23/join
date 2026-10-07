using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "admin");

            migrationBuilder.EnsureSchema(
                name: "security");

            migrationBuilder.EnsureSchema(
                name: "common");

            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "auditlogs",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entityname = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entityid = table.Column<Guid>(type: "uuid", nullable: false),
                    entitylabel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false),
                    changedby = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    changedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ipaddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    oldvaluesjson = table.Column<string>(type: "text", nullable: true),
                    newvaluesjson = table.Column<string>(type: "text", nullable: true),
                    metadatajson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditlogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "communicationchannels",
                schema: "common",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_communicationchannels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "common",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    taxid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "countries",
                schema: "common",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    isocode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_countries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entitystatuses",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    code = table.Column<int>(type: "integer", nullable: false),
                    isoperative = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entitystatuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "identificationtypes",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    validationpattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identificationtypes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    issystemdefault = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalizedname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrencystamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seedstate",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lastappliedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seedstate", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "streettypes",
                schema: "common",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streettypes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "systemmodules",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    order = table.Column<int>(type: "integer", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_systemmodules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firstname = table.Column<string>(type: "text", nullable: false),
                    lastname = table.Column<string>(type: "text", nullable: false),
                    avatarid = table.Column<Guid>(type: "uuid", nullable: true),
                    avatarurl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    ismfaenabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfasecretkey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    statuschangereason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    mfaenabledatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    isemailotpenabled = table.Column<bool>(type: "boolean", nullable: false),
                    preferredmfamethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    externalprovider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    externalproviderid = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    issuperadmin = table.Column<bool>(type: "boolean", nullable: false),
                    issuperadmincompany = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalizedusername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalizedemail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    emailconfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    passwordhash = table.Column<string>(type: "text", nullable: true),
                    securitystamp = table.Column<string>(type: "text", nullable: true),
                    concurrencystamp = table.Column<string>(type: "text", nullable: true),
                    phonenumber = table.Column<string>(type: "text", nullable: true),
                    phonenumberconfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    twofactorenabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockoutend = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockoutenabled = table.Column<bool>(type: "boolean", nullable: false),
                    accessfailedcount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "genders",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genders", x => x.id);
                    table.ForeignKey(
                        name: "fk_genders_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "incomeranges",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    displayname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    minimumvalue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    maximumvalue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currencycode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    displayorder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incomeranges", x => x.id);
                    table.ForeignKey(
                        name: "fk_incomeranges_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "industries",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_industries", x => x.id);
                    table.ForeignKey(
                        name: "fk_industries_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "taxregimes",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taxregimes", x => x.id);
                    table.ForeignKey(
                        name: "fk_taxregimes_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticketattachmentsettings",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alloweddocumenttypes = table.Column<int>(type: "integer", nullable: false),
                    maxfilesizebytes = table.Column<long>(type: "bigint", nullable: false),
                    maxfilesperticket = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_ticketattachmentsettings", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketattachmentsettings_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticketstatuses",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    code = table.Column<int>(type: "integer", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false),
                    isinitial = table.Column<bool>(type: "boolean", nullable: false),
                    ispaused = table.Column<bool>(type: "boolean", nullable: false),
                    isfinal = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketstatuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketstatuses_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "timeunits",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    code = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_timeunits", x => x.id);
                    table.ForeignKey(
                        name: "fk_timeunits_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regions",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    countryid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regions", x => x.id);
                    table.ForeignKey(
                        name: "fk_regions_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_regions_countries_countryid",
                        column: x => x.countryid,
                        principalSchema: "common",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "areas",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entitystatusid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_areas", x => x.id);
                    table.ForeignKey(
                        name: "fk_areas_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_areas_entitystatuses_entitystatusid",
                        column: x => x.entitystatusid,
                        principalSchema: "admin",
                        principalTable: "entitystatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    entitystatusid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_projects_entitystatuses_entitystatusid",
                        column: x => x.entitystatusid,
                        principalSchema: "admin",
                        principalTable: "entitystatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roleclaims",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    roleid = table.Column<Guid>(type: "uuid", nullable: false),
                    claimtype = table.Column<string>(type: "text", nullable: true),
                    claimvalue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roleclaims", x => x.id);
                    table.ForeignKey(
                        name: "fk_roleclaims_roles_roleid",
                        column: x => x.roleid,
                        principalSchema: "security",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rolecompanies",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    roleid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rolecompanies", x => x.id);
                    table.ForeignKey(
                        name: "fk_rolecompanies_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rolecompanies_roles_roleid",
                        column: x => x.roleid,
                        principalSchema: "security",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "companymodules",
                schema: "admin",
                columns: table => new
                {
                    companyid = table.Column<Guid>(type: "uuid", nullable: false),
                    moduleid = table.Column<Guid>(type: "uuid", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companymodules", x => new { x.companyid, x.moduleid });
                    table.ForeignKey(
                        name: "fk_companymodules_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_companymodules_systemmodules_moduleid",
                        column: x => x.moduleid,
                        principalSchema: "admin",
                        principalTable: "systemmodules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "systemoptions",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    moduleid = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    route = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    icon = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    parentid = table.Column<Guid>(type: "uuid", nullable: true),
                    controllername = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    canread = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    cancreate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    canupdate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    candelete = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    candownload = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    canexport = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    canexecute = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    isvisiblemenu = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ordermenu = table.Column<int>(type: "integer", nullable: true, defaultValue: 0),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_systemoptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_systemoptions_systemmodules_moduleid",
                        column: x => x.moduleid,
                        principalSchema: "admin",
                        principalTable: "systemmodules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_systemoptions_systemoptions_parentid",
                        column: x => x.parentid,
                        principalSchema: "security",
                        principalTable: "systemoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "emailotpenablecodes",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    codehash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expiresatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attemptcount = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_emailotpenablecodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_emailotpenablecodes_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mfaloginchallenges",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    targetcompanyid = table.Column<Guid>(type: "uuid", nullable: true),
                    tokenhash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expiresatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attemptcount = table.Column<int>(type: "integer", nullable: false),
                    emailcodehash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    emailcodeexpiresatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    emailsentatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfaloginchallenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfaloginchallenges_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "phoneverificationcodes",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: true),
                    phonenumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codehash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expiresatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attemptcount = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_phoneverificationcodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_phoneverificationcodes_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "securityeventlogs",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: true),
                    eventtype = table.Column<int>(type: "integer", nullable: false),
                    occurredatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ipaddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    useragent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    metadatajson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_securityeventlogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_securityeventlogs_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ticketusercompanies",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    issuperadminticket = table.Column<bool>(type: "boolean", nullable: false),
                    canfinishticket = table.Column<bool>(type: "boolean", nullable: false),
                    canresolveticket = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketusercompanies", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketusercompanies_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketusercompanies_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userclaims",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    claimtype = table.Column<string>(type: "text", nullable: true),
                    claimvalue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userclaims", x => x.id);
                    table.ForeignKey(
                        name: "fk_userclaims_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usercommunicationchannels",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    communicationchannelid = table.Column<Guid>(type: "uuid", nullable: false),
                    channelidentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ispreferred = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usercommunicationchannels", x => x.id);
                    table.ForeignKey(
                        name: "fk_usercommunicationchannels_communicationchannels_communicati~",
                        column: x => x.communicationchannelid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usercommunicationchannels_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usercommunicationchannels_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usercompanies",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    isdefault = table.Column<bool>(type: "boolean", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usercompanies", x => x.id);
                    table.ForeignKey(
                        name: "fk_usercompanies_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usercompanies_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "userconnectionlogs",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: true),
                    ipaddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    useragent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    connectiondate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    isactivesession = table.Column<bool>(type: "boolean", nullable: false),
                    disconnectiondate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userconnectionlogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_userconnectionlogs_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "userlogins",
                schema: "security",
                columns: table => new
                {
                    loginprovider = table.Column<string>(type: "text", nullable: false),
                    providerkey = table.Column<string>(type: "text", nullable: false),
                    providerdisplayname = table.Column<string>(type: "text", nullable: true),
                    userid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userlogins", x => new { x.loginprovider, x.providerkey });
                    table.ForeignKey(
                        name: "fk_userlogins_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usermfarecoverycodes",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: true),
                    codehash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    createdatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usedatutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usermfarecoverycodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_usermfarecoverycodes_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "userrefreshtokens",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expirydate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    isrevoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userrefreshtokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_userrefreshtokens_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "userrolecompanies",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    roleid = table.Column<Guid>(type: "uuid", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userrolecompanies", x => x.id);
                    table.ForeignKey(
                        name: "fk_userrolecompanies_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_userrolecompanies_roles_roleid",
                        column: x => x.roleid,
                        principalSchema: "security",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_userrolecompanies_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "userroles",
                schema: "security",
                columns: table => new
                {
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    roleid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userroles", x => new { x.userid, x.roleid });
                    table.ForeignKey(
                        name: "fk_userroles_roles_roleid",
                        column: x => x.roleid,
                        principalSchema: "security",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_userroles_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usertokens",
                schema: "security",
                columns: table => new
                {
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    loginprovider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usertokens", x => new { x.userid, x.loginprovider, x.name });
                    table.ForeignKey(
                        name: "fk_usertokens_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "persons",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    persontype = table.Column<int>(type: "integer", nullable: false),
                    genderid = table.Column<Guid>(type: "uuid", nullable: true),
                    firstname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middlename = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lastname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    secondlastname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    commercialname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    identificationtypeid = table.Column<Guid>(type: "uuid", nullable: false),
                    identificationnumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persons", x => x.id);
                    table.ForeignKey(
                        name: "fk_persons_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_persons_genders_genderid",
                        column: x => x.genderid,
                        principalSchema: "admin",
                        principalTable: "genders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_persons_identificationtypes_identificationtypeid",
                        column: x => x.identificationtypeid,
                        principalSchema: "admin",
                        principalTable: "identificationtypes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticketstatustransitions",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fromstatusid = table.Column<Guid>(type: "uuid", nullable: false),
                    tostatusid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketstatustransitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketstatustransitions_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketstatustransitions_ticketstatuses_fromstatusid",
                        column: x => x.fromstatusid,
                        principalSchema: "messaging",
                        principalTable: "ticketstatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketstatustransitions_ticketstatuses_tostatusid",
                        column: x => x.tostatusid,
                        principalSchema: "messaging",
                        principalTable: "ticketstatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticketcomplexities",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    code = table.Column<int>(type: "integer", nullable: false),
                    resolutiontimeunits = table.Column<int>(type: "integer", nullable: false),
                    timeunitid = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_ticketcomplexities", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketcomplexities_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketcomplexities_timeunits_timeunitid",
                        column: x => x.timeunitid,
                        principalSchema: "messaging",
                        principalTable: "timeunits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provinces",
                schema: "common",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    countryid = table.Column<Guid>(type: "uuid", nullable: false),
                    regionid = table.Column<Guid>(type: "uuid", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provinces", x => x.id);
                    table.ForeignKey(
                        name: "fk_provinces_countries_countryid",
                        column: x => x.countryid,
                        principalSchema: "common",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_provinces_regions_regionid",
                        column: x => x.regionid,
                        principalSchema: "admin",
                        principalTable: "regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rolesystemoptions",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    roleid = table.Column<Guid>(type: "uuid", nullable: false),
                    systemoptionid = table.Column<Guid>(type: "uuid", nullable: false),
                    canread = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    cancreate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    canupdate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    candelete = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    candownload = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    canexport = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    canexecute = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isvisiblemenu = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ordermenu = table.Column<int>(type: "integer", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rolesystemoptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_rolesystemoptions_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rolesystemoptions_roles_roleid",
                        column: x => x.roleid,
                        principalSchema: "security",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rolesystemoptions_systemoptions_systemoptionid",
                        column: x => x.systemoptionid,
                        principalSchema: "security",
                        principalTable: "systemoptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    customercode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    personlifecyclestage = table.Column<int>(type: "integer", nullable: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    activatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deactivatedat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_customers_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personbusinessprofiles",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    industryid = table.Column<Guid>(type: "uuid", nullable: false),
                    taxregimeid = table.Column<Guid>(type: "uuid", nullable: false),
                    website = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    foundationdate = table.Column<DateTime>(type: "date", nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personbusinessprofiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_personbusinessprofiles_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personbusinessprofiles_industries_industryid",
                        column: x => x.industryid,
                        principalSchema: "admin",
                        principalTable: "industries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personbusinessprofiles_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personbusinessprofiles_taxregimes_taxregimeid",
                        column: x => x.taxregimeid,
                        principalSchema: "admin",
                        principalTable: "taxregimes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personcontacts",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    contacttype = table.Column<int>(type: "integer", nullable: false),
                    contactvalue = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    isprimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    comments = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personcontacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_personcontacts_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personcontacts_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personemployments",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    employername = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    jobtitle = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    startdate = table.Column<DateTime>(type: "date", nullable: false),
                    enddate = table.Column<DateTime>(type: "date", nullable: true),
                    iscurrent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personemployments", x => x.id);
                    table.ForeignKey(
                        name: "fk_personemployments_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personemployments_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personfinancialprofiles",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    incomerangeid = table.Column<Guid>(type: "uuid", nullable: false),
                    sourceoffunds = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    declareddate = table.Column<DateTime>(type: "timestamp", nullable: false),
                    iscurrent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personfinancialprofiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_personfinancialprofiles_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personfinancialprofiles_incomeranges_incomerangeid",
                        column: x => x.incomerangeid,
                        principalSchema: "admin",
                        principalTable: "incomeranges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personfinancialprofiles_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "userpersons",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_userpersons", x => x.id);
                    table.ForeignKey(
                        name: "fk_userpersons_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_userpersons_users_userid",
                        column: x => x.userid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticketcompanydefaults",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    startcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codesequencelength = table.Column<int>(type: "integer", nullable: false),
                    usepersonalizedcode = table.Column<bool>(type: "boolean", nullable: false),
                    ticketstatusdefaultid = table.Column<Guid>(type: "uuid", nullable: true),
                    ticketcomplexitydefaultid = table.Column<Guid>(type: "uuid", nullable: true),
                    timeunitdefaultid = table.Column<Guid>(type: "uuid", nullable: true),
                    areadefaultid = table.Column<Guid>(type: "uuid", nullable: true),
                    projectdefaultid = table.Column<Guid>(type: "uuid", nullable: true),
                    channeldefaultid = table.Column<Guid>(type: "uuid", nullable: true),
                    maxdayticketinactivity = table.Column<int>(type: "integer", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketcompanydefaults", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketcompanydefaults_areas_areadefaultid",
                        column: x => x.areadefaultid,
                        principalSchema: "admin",
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketcompanydefaults_communicationchannels_channeldefaultid",
                        column: x => x.channeldefaultid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketcompanydefaults_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticketcompanydefaults_projects_projectdefaultid",
                        column: x => x.projectdefaultid,
                        principalSchema: "admin",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketcompanydefaults_ticketcomplexities_ticketcomplexityde~",
                        column: x => x.ticketcomplexitydefaultid,
                        principalSchema: "messaging",
                        principalTable: "ticketcomplexities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketcompanydefaults_ticketstatuses_ticketstatusdefaultid",
                        column: x => x.ticketstatusdefaultid,
                        principalSchema: "messaging",
                        principalTable: "ticketstatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketcompanydefaults_timeunits_timeunitdefaultid",
                        column: x => x.timeunitdefaultid,
                        principalSchema: "messaging",
                        principalTable: "timeunits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "messaging",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    timeunitid = table.Column<Guid>(type: "uuid", nullable: false),
                    estimatedtime = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    consumedtime = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    effortpoints = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    ticketstatusid = table.Column<Guid>(type: "uuid", nullable: false),
                    ticketcomplexityid = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: true),
                    projectid = table.Column<Guid>(type: "uuid", nullable: true),
                    areaid = table.Column<Guid>(type: "uuid", nullable: true),
                    channelid = table.Column<Guid>(type: "uuid", nullable: false),
                    precedentticketid = table.Column<Guid>(type: "uuid", nullable: true),
                    isvisibletoexternals = table.Column<bool>(type: "boolean", nullable: false),
                    createdbyuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    assignedtouserid = table.Column<Guid>(type: "uuid", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_tickets_areas_areaid",
                        column: x => x.areaid,
                        principalSchema: "admin",
                        principalTable: "areas",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tickets_communicationchannels_channelid",
                        column: x => x.channelid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tickets_projects_projectid",
                        column: x => x.projectid,
                        principalSchema: "admin",
                        principalTable: "projects",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_tickets_ticketcomplexities_ticketcomplexityid",
                        column: x => x.ticketcomplexityid,
                        principalSchema: "messaging",
                        principalTable: "ticketcomplexities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_tickets_precedentticketid",
                        column: x => x.precedentticketid,
                        principalSchema: "messaging",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_ticketstatuses_ticketstatusid",
                        column: x => x.ticketstatusid,
                        principalSchema: "messaging",
                        principalTable: "ticketstatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_timeunits_timeunitid",
                        column: x => x.timeunitid,
                        principalSchema: "messaging",
                        principalTable: "timeunits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_users_assignedtouserid",
                        column: x => x.assignedtouserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_users_createdbyuserid",
                        column: x => x.createdbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "municipalities",
                schema: "common",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    provinceid = table.Column<Guid>(type: "uuid", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_municipalities", x => x.id);
                    table.ForeignKey(
                        name: "fk_municipalities_provinces_provinceid",
                        column: x => x.provinceid,
                        principalSchema: "common",
                        principalTable: "provinces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticketlogs",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticketid = table.Column<Guid>(type: "uuid", nullable: false),
                    logtype = table.Column<int>(type: "integer", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    userregisterlogid = table.Column<Guid>(type: "uuid", nullable: false),
                    previousstatusid = table.Column<Guid>(type: "uuid", nullable: true),
                    ticketstatusid = table.Column<Guid>(type: "uuid", nullable: false),
                    timeunitid = table.Column<Guid>(type: "uuid", nullable: true),
                    consumedtime = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    isonlyforcreatedandassigned = table.Column<bool>(type: "boolean", nullable: false),
                    newassignedtouserid = table.Column<Guid>(type: "uuid", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketlogs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketlogs_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticketlogs_tickets_ticketid",
                        column: x => x.ticketid,
                        principalSchema: "messaging",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticketlogs_ticketstatuses_previousstatusid",
                        column: x => x.previousstatusid,
                        principalSchema: "messaging",
                        principalTable: "ticketstatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketlogs_ticketstatuses_ticketstatusid",
                        column: x => x.ticketstatusid,
                        principalSchema: "messaging",
                        principalTable: "ticketstatuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketlogs_timeunits_timeunitid",
                        column: x => x.timeunitid,
                        principalSchema: "messaging",
                        principalTable: "timeunits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketlogs_users_newassignedtouserid",
                        column: x => x.newassignedtouserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketlogs_users_userregisterlogid",
                        column: x => x.userregisterlogid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticketnotifications",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticketid = table.Column<Guid>(type: "uuid", nullable: true),
                    communicationchannelid = table.Column<Guid>(type: "uuid", nullable: false),
                    messagesummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    externalproviderid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notificationtype = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sentat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketnotifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketnotifications_communicationchannels_communicationchan~",
                        column: x => x.communicationchannelid,
                        principalSchema: "common",
                        principalTable: "communicationchannels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketnotifications_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticketnotifications_tickets_ticketid",
                        column: x => x.ticketid,
                        principalSchema: "messaging",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "personaddresses",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    personid = table.Column<Guid>(type: "uuid", nullable: false),
                    addressline1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    addressline2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    zipcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    streettypeid = table.Column<Guid>(type: "uuid", nullable: false),
                    countryid = table.Column<Guid>(type: "uuid", nullable: false),
                    regionid = table.Column<Guid>(type: "uuid", nullable: true),
                    provinceid = table.Column<Guid>(type: "uuid", nullable: false),
                    municipalityid = table.Column<Guid>(type: "uuid", nullable: false),
                    isdefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    isactive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personaddresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_personaddresses_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personaddresses_countries_countryid",
                        column: x => x.countryid,
                        principalSchema: "common",
                        principalTable: "countries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personaddresses_municipalities_municipalityid",
                        column: x => x.municipalityid,
                        principalSchema: "common",
                        principalTable: "municipalities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personaddresses_persons_personid",
                        column: x => x.personid,
                        principalSchema: "admin",
                        principalTable: "persons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personaddresses_provinces_provinceid",
                        column: x => x.provinceid,
                        principalSchema: "common",
                        principalTable: "provinces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personaddresses_regions_regionid",
                        column: x => x.regionid,
                        principalSchema: "admin",
                        principalTable: "regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_personaddresses_streettypes_streettypeid",
                        column: x => x.streettypeid,
                        principalSchema: "common",
                        principalTable: "streettypes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticketdocuments",
                schema: "support",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticketid = table.Column<Guid>(type: "uuid", nullable: false),
                    ticketlogsid = table.Column<Guid>(type: "uuid", nullable: false),
                    documenttype = table.Column<int>(type: "integer", nullable: false),
                    originalname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    newname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storageprovider = table.Column<int>(type: "integer", nullable: false),
                    contenttype = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sizebytes = table.Column<long>(type: "bigint", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticketdocuments", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticketdocuments_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticketdocuments_ticketlogs_ticketlogsid",
                        column: x => x.ticketlogsid,
                        principalSchema: "support",
                        principalTable: "ticketlogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ticketdocuments_tickets_ticketid",
                        column: x => x.ticketid,
                        principalSchema: "messaging",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_areas_companyid",
                schema: "admin",
                table: "areas",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_areas_entitystatusid",
                schema: "admin",
                table: "areas",
                column: "entitystatusid");

            migrationBuilder.CreateIndex(
                name: "ix_auditlogs_company_changedat",
                schema: "security",
                table: "auditlogs",
                columns: new[] { "companyid", "changedatutc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_auditlogs_entity",
                schema: "security",
                table: "auditlogs",
                columns: new[] { "entityname", "entityid", "changedatutc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_communicationchannels_name",
                schema: "common",
                table: "communicationchannels",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_taxid",
                schema: "common",
                table: "companies",
                column: "taxid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companymodules_moduleid",
                schema: "admin",
                table: "companymodules",
                column: "moduleid");

            migrationBuilder.CreateIndex(
                name: "ix_countries_isocode",
                schema: "common",
                table: "countries",
                column: "isocode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_customercode_gcrecord",
                schema: "admin",
                table: "customers",
                columns: new[] { "companyid", "customercode", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_person_user_gcrecord",
                schema: "admin",
                table: "customers",
                columns: new[] { "companyid", "personid", "userid", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_personlifecyclestage",
                schema: "admin",
                table: "customers",
                columns: new[] { "companyid", "personlifecyclestage" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_personid",
                schema: "admin",
                table: "customers",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_customers_userid",
                schema: "admin",
                table: "customers",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_emailotpenablecodes_userid_expiresatutc",
                schema: "security",
                table: "emailotpenablecodes",
                columns: new[] { "userid", "expiresatutc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_entitystatuses_code",
                schema: "admin",
                table: "entitystatuses",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_genders_companyid_code_gcrecord",
                schema: "admin",
                table: "genders",
                columns: new[] { "companyid", "code", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_genders_companyid_name_gcrecord",
                schema: "admin",
                table: "genders",
                columns: new[] { "companyid", "name", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incomeranges_companyid_displayname_gcrecord",
                schema: "admin",
                table: "incomeranges",
                columns: new[] { "companyid", "displayname", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_incomeranges_companyid_displayorder_gcrecord",
                schema: "admin",
                table: "incomeranges",
                columns: new[] { "companyid", "displayorder", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_industries_companyid_code_gcrecord",
                schema: "admin",
                table: "industries",
                columns: new[] { "companyid", "code", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_industries_companyid_name_gcrecord",
                schema: "admin",
                table: "industries",
                columns: new[] { "companyid", "name", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfaloginchallenges_tokenhash",
                schema: "security",
                table: "mfaloginchallenges",
                column: "tokenhash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfaloginchallenges_userid_expiresatutc",
                schema: "security",
                table: "mfaloginchallenges",
                columns: new[] { "userid", "expiresatutc" });

            migrationBuilder.CreateIndex(
                name: "ix_municipalities_provinceid_name",
                schema: "common",
                table: "municipalities",
                columns: new[] { "provinceid", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personaddresses_company_person_default",
                schema: "admin",
                table: "personaddresses",
                columns: new[] { "companyid", "personid", "isdefault" });

            migrationBuilder.CreateIndex(
                name: "ix_personaddresses_countryid",
                schema: "admin",
                table: "personaddresses",
                column: "countryid");

            migrationBuilder.CreateIndex(
                name: "ix_personaddresses_municipalityid",
                schema: "admin",
                table: "personaddresses",
                column: "municipalityid");

            migrationBuilder.CreateIndex(
                name: "ix_personaddresses_personid",
                schema: "admin",
                table: "personaddresses",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_personaddresses_provinceid",
                schema: "admin",
                table: "personaddresses",
                column: "provinceid");

            migrationBuilder.CreateIndex(
                name: "ix_personaddresses_regionid",
                schema: "admin",
                table: "personaddresses",
                column: "regionid");

            migrationBuilder.CreateIndex(
                name: "ix_personaddresses_streettypeid",
                schema: "admin",
                table: "personaddresses",
                column: "streettypeid");

            migrationBuilder.CreateIndex(
                name: "ix_personbusinessprofiles_company_person_gcrecord",
                schema: "admin",
                table: "personbusinessprofiles",
                columns: new[] { "companyid", "personid", "gcrecord" });

            migrationBuilder.CreateIndex(
                name: "ix_personbusinessprofiles_industryid",
                schema: "admin",
                table: "personbusinessprofiles",
                column: "industryid");

            migrationBuilder.CreateIndex(
                name: "ix_personbusinessprofiles_personid",
                schema: "admin",
                table: "personbusinessprofiles",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_personbusinessprofiles_taxregimeid",
                schema: "admin",
                table: "personbusinessprofiles",
                column: "taxregimeid");

            migrationBuilder.CreateIndex(
                name: "ix_personcontacts_company_person_primary",
                schema: "admin",
                table: "personcontacts",
                columns: new[] { "companyid", "personid", "isprimary" });

            migrationBuilder.CreateIndex(
                name: "ix_personcontacts_unique_valueperperson",
                schema: "admin",
                table: "personcontacts",
                columns: new[] { "personid", "contacttype", "contactvalue", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personemployments_company_person_current",
                schema: "admin",
                table: "personemployments",
                columns: new[] { "companyid", "personid", "iscurrent" });

            migrationBuilder.CreateIndex(
                name: "ix_personemployments_personid",
                schema: "admin",
                table: "personemployments",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_personfinancialprofiles_company_person_current",
                schema: "admin",
                table: "personfinancialprofiles",
                columns: new[] { "companyid", "personid", "iscurrent" });

            migrationBuilder.CreateIndex(
                name: "ix_personfinancialprofiles_incomerangeid",
                schema: "admin",
                table: "personfinancialprofiles",
                column: "incomerangeid");

            migrationBuilder.CreateIndex(
                name: "ix_personfinancialprofiles_personid",
                schema: "admin",
                table: "personfinancialprofiles",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_persons_company_idtype_idnumber_gcrecord",
                schema: "admin",
                table: "persons",
                columns: new[] { "companyid", "identificationtypeid", "identificationnumber", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_persons_genderid",
                schema: "admin",
                table: "persons",
                column: "genderid");

            migrationBuilder.CreateIndex(
                name: "ix_persons_identificationtypeid",
                schema: "admin",
                table: "persons",
                column: "identificationtypeid");

            migrationBuilder.CreateIndex(
                name: "ix_phoneverificationcodes_userid_expiresatutc",
                schema: "security",
                table: "phoneverificationcodes",
                columns: new[] { "userid", "expiresatutc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_projects_companyid",
                schema: "admin",
                table: "projects",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_projects_entitystatusid",
                schema: "admin",
                table: "projects",
                column: "entitystatusid");

            migrationBuilder.CreateIndex(
                name: "ix_provinces_countryid_code",
                schema: "common",
                table: "provinces",
                columns: new[] { "countryid", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provinces_regionid",
                schema: "common",
                table: "provinces",
                column: "regionid");

            migrationBuilder.CreateIndex(
                name: "ix_regions_company_country_code_gcrecord",
                schema: "admin",
                table: "regions",
                columns: new[] { "companyid", "countryid", "code", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regions_company_country_name_gcrecord",
                schema: "admin",
                table: "regions",
                columns: new[] { "companyid", "countryid", "name", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regions_countryid",
                schema: "admin",
                table: "regions",
                column: "countryid");

            migrationBuilder.CreateIndex(
                name: "ix_roleclaims_roleid",
                schema: "security",
                table: "roleclaims",
                column: "roleid");

            migrationBuilder.CreateIndex(
                name: "ix_rolecompanies_companyid",
                schema: "security",
                table: "rolecompanies",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_rolecompanies_roleid_companyid",
                schema: "security",
                table: "rolecompanies",
                columns: new[] { "roleid", "companyid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "rolenameindex",
                schema: "security",
                table: "roles",
                column: "normalizedname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rolesystemoptions_companyid_roleid_systemoptionid",
                schema: "security",
                table: "rolesystemoptions",
                columns: new[] { "companyid", "roleid", "systemoptionid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rolesystemoptions_roleid",
                schema: "security",
                table: "rolesystemoptions",
                column: "roleid");

            migrationBuilder.CreateIndex(
                name: "ix_rolesystemoptions_systemoptionid",
                schema: "security",
                table: "rolesystemoptions",
                column: "systemoptionid");

            migrationBuilder.CreateIndex(
                name: "ix_securityeventlogs_userid_occurredatutc",
                schema: "security",
                table: "securityeventlogs",
                columns: new[] { "userid", "occurredatutc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_seedstate_companyid",
                schema: "security",
                table: "seedstate",
                column: "companyid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_streettypes_abbreviation",
                schema: "common",
                table: "streettypes",
                column: "abbreviation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_streettypes_name",
                schema: "common",
                table: "streettypes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_systemmodules_name",
                schema: "admin",
                table: "systemmodules",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_systemoptions_moduleid_ordermenu",
                schema: "security",
                table: "systemoptions",
                columns: new[] { "moduleid", "ordermenu" });

            migrationBuilder.CreateIndex(
                name: "ix_systemoptions_moduleid_route",
                schema: "security",
                table: "systemoptions",
                columns: new[] { "moduleid", "route" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_systemoptions_parentid",
                schema: "security",
                table: "systemoptions",
                column: "parentid");

            migrationBuilder.CreateIndex(
                name: "ix_taxregimes_companyid_code_gcrecord",
                schema: "admin",
                table: "taxregimes",
                columns: new[] { "companyid", "code", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_taxregimes_companyid_name_gcrecord",
                schema: "admin",
                table: "taxregimes",
                columns: new[] { "companyid", "name", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ticketattachmentsettings_company_active",
                schema: "messaging",
                table: "ticketattachmentsettings",
                column: "companyid",
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_areadefaultid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "areadefaultid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_channeldefaultid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "channeldefaultid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_companyid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "companyid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_projectdefaultid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "projectdefaultid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_ticketcomplexitydefaultid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "ticketcomplexitydefaultid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_ticketstatusdefaultid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "ticketstatusdefaultid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcompanydefaults_timeunitdefaultid",
                schema: "messaging",
                table: "ticketcompanydefaults",
                column: "timeunitdefaultid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketcomplexities_timeunitid",
                schema: "messaging",
                table: "ticketcomplexities",
                column: "timeunitid");

            migrationBuilder.CreateIndex(
                name: "ux_ticketcomplexities_company_name",
                schema: "messaging",
                table: "ticketcomplexities",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_ticketdocuments_company_created",
                schema: "support",
                table: "ticketdocuments",
                columns: new[] { "companyid", "created" });

            migrationBuilder.CreateIndex(
                name: "ix_ticketdocuments_ticket_created",
                schema: "support",
                table: "ticketdocuments",
                columns: new[] { "ticketid", "created" });

            migrationBuilder.CreateIndex(
                name: "ix_ticketdocuments_ticketlogsid",
                schema: "support",
                table: "ticketdocuments",
                column: "ticketlogsid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketlog_tenant_ticket_active",
                schema: "support",
                table: "ticketlogs",
                columns: new[] { "companyid", "ticketid", "gcrecord" });

            migrationBuilder.CreateIndex(
                name: "ix_ticketlogs_companyid_ticketid",
                schema: "support",
                table: "ticketlogs",
                columns: new[] { "companyid", "ticketid" });

            migrationBuilder.CreateIndex(
                name: "ix_ticketlogs_newassignedtouserid",
                schema: "support",
                table: "ticketlogs",
                column: "newassignedtouserid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketlogs_previousstatusid",
                schema: "support",
                table: "ticketlogs",
                column: "previousstatusid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketlogs_ticketid",
                schema: "support",
                table: "ticketlogs",
                column: "ticketid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketlogs_ticketstatusid",
                schema: "support",
                table: "ticketlogs",
                column: "ticketstatusid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketlogs_timeunitid",
                schema: "support",
                table: "ticketlogs",
                column: "timeunitid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketlogs_userregisterlogid",
                schema: "support",
                table: "ticketlogs",
                column: "userregisterlogid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketnotifications_communicationchannelid",
                schema: "support",
                table: "ticketnotifications",
                column: "communicationchannelid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketnotifications_companyid",
                schema: "support",
                table: "ticketnotifications",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketnotifications_ticketid",
                schema: "support",
                table: "ticketnotifications",
                column: "ticketid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_areaid",
                schema: "messaging",
                table: "tickets",
                column: "areaid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_assignedtouserid",
                schema: "messaging",
                table: "tickets",
                column: "assignedtouserid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_channelid",
                schema: "messaging",
                table: "tickets",
                column: "channelid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_companyid_code",
                schema: "messaging",
                table: "tickets",
                columns: new[] { "companyid", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_createdbyuserid",
                schema: "messaging",
                table: "tickets",
                column: "createdbyuserid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_personid",
                schema: "messaging",
                table: "tickets",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_precedentticketid",
                schema: "messaging",
                table: "tickets",
                column: "precedentticketid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_projectid",
                schema: "messaging",
                table: "tickets",
                column: "projectid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_ticketcomplexityid",
                schema: "messaging",
                table: "tickets",
                column: "ticketcomplexityid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_ticketstatusid",
                schema: "messaging",
                table: "tickets",
                column: "ticketstatusid");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_timeunitid",
                schema: "messaging",
                table: "tickets",
                column: "timeunitid");

            migrationBuilder.CreateIndex(
                name: "ux_ticketstatuses_company_final",
                schema: "messaging",
                table: "ticketstatuses",
                columns: new[] { "companyid", "isfinal" },
                unique: true,
                filter: "\"isfinal\" = TRUE AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_ticketstatuses_company_initial",
                schema: "messaging",
                table: "ticketstatuses",
                columns: new[] { "companyid", "isinitial" },
                unique: true,
                filter: "\"isinitial\" = TRUE AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_ticketstatuses_company_paused",
                schema: "messaging",
                table: "ticketstatuses",
                columns: new[] { "companyid", "ispaused" },
                unique: true,
                filter: "\"ispaused\" = TRUE AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_ticketstatustransitions_fromstatusid",
                schema: "messaging",
                table: "ticketstatustransitions",
                column: "fromstatusid");

            migrationBuilder.CreateIndex(
                name: "ix_ticketstatustransitions_tostatusid",
                schema: "messaging",
                table: "ticketstatustransitions",
                column: "tostatusid");

            migrationBuilder.CreateIndex(
                name: "ux_ticketstatustransitions_company_from_to",
                schema: "messaging",
                table: "ticketstatustransitions",
                columns: new[] { "companyid", "fromstatusid", "tostatusid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_ticketusercompanies_companyid",
                schema: "messaging",
                table: "ticketusercompanies",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ux_ticketusercompanies_user_company",
                schema: "messaging",
                table: "ticketusercompanies",
                columns: new[] { "userid", "companyid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_timeunits_company_name",
                schema: "messaging",
                table: "timeunits",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_userclaims_userid",
                schema: "security",
                table: "userclaims",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_usercommunicationchannels_communicationchannelid",
                schema: "admin",
                table: "usercommunicationchannels",
                column: "communicationchannelid");

            migrationBuilder.CreateIndex(
                name: "ix_usercommunicationchannels_companyid_userid_communicationcha~",
                schema: "admin",
                table: "usercommunicationchannels",
                columns: new[] { "companyid", "userid", "communicationchannelid" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_usercommunicationchannels_userid",
                schema: "admin",
                table: "usercommunicationchannels",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_usercompanies_companyid",
                schema: "security",
                table: "usercompanies",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_usercompanies_userid_companyid",
                schema: "security",
                table: "usercompanies",
                columns: new[] { "userid", "companyid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_usercompanies_userid_default",
                schema: "security",
                table: "usercompanies",
                column: "userid",
                unique: true,
                filter: "\"isdefault\" = TRUE AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_userconnectionlogs_isactivesession",
                schema: "security",
                table: "userconnectionlogs",
                column: "isactivesession");

            migrationBuilder.CreateIndex(
                name: "ix_userconnectionlogs_userid",
                schema: "security",
                table: "userconnectionlogs",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_userlogins_userid",
                schema: "security",
                table: "userlogins",
                column: "userid");

            migrationBuilder.CreateIndex(
                name: "ix_usermfarecoverycodes_userid_usedatutc",
                schema: "security",
                table: "usermfarecoverycodes",
                columns: new[] { "userid", "usedatutc" });

            migrationBuilder.CreateIndex(
                name: "ix_userpersons_personid",
                schema: "security",
                table: "userpersons",
                column: "personid");

            migrationBuilder.CreateIndex(
                name: "ix_userpersons_userid_personid",
                schema: "security",
                table: "userpersons",
                columns: new[] { "userid", "personid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userrefreshtokens_token",
                schema: "security",
                table: "userrefreshtokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userrefreshtokens_userid_isrevoked",
                schema: "security",
                table: "userrefreshtokens",
                columns: new[] { "userid", "isrevoked" });

            migrationBuilder.CreateIndex(
                name: "ix_userrolecompanies_companyid",
                schema: "security",
                table: "userrolecompanies",
                column: "companyid");

            migrationBuilder.CreateIndex(
                name: "ix_userrolecompanies_roleid",
                schema: "security",
                table: "userrolecompanies",
                column: "roleid");

            migrationBuilder.CreateIndex(
                name: "ix_userrolecompanies_userid_roleid_companyid",
                schema: "security",
                table: "userrolecompanies",
                columns: new[] { "userid", "roleid", "companyid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_userroles_roleid",
                schema: "security",
                table: "userroles",
                column: "roleid");

            migrationBuilder.CreateIndex(
                name: "emailindex",
                schema: "security",
                table: "users",
                column: "normalizedemail");

            migrationBuilder.CreateIndex(
                name: "usernameindex",
                schema: "security",
                table: "users",
                column: "normalizedusername",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditlogs",
                schema: "security");

            migrationBuilder.DropTable(
                name: "companymodules",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "emailotpenablecodes",
                schema: "security");

            migrationBuilder.DropTable(
                name: "mfaloginchallenges",
                schema: "security");

            migrationBuilder.DropTable(
                name: "personaddresses",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "personbusinessprofiles",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "personcontacts",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "personemployments",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "personfinancialprofiles",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "phoneverificationcodes",
                schema: "security");

            migrationBuilder.DropTable(
                name: "roleclaims",
                schema: "security");

            migrationBuilder.DropTable(
                name: "rolecompanies",
                schema: "security");

            migrationBuilder.DropTable(
                name: "rolesystemoptions",
                schema: "security");

            migrationBuilder.DropTable(
                name: "securityeventlogs",
                schema: "security");

            migrationBuilder.DropTable(
                name: "seedstate",
                schema: "security");

            migrationBuilder.DropTable(
                name: "ticketattachmentsettings",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "ticketcompanydefaults",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "ticketdocuments",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticketnotifications",
                schema: "support");

            migrationBuilder.DropTable(
                name: "ticketstatustransitions",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "ticketusercompanies",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "userclaims",
                schema: "security");

            migrationBuilder.DropTable(
                name: "usercommunicationchannels",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "usercompanies",
                schema: "security");

            migrationBuilder.DropTable(
                name: "userconnectionlogs",
                schema: "security");

            migrationBuilder.DropTable(
                name: "userlogins",
                schema: "security");

            migrationBuilder.DropTable(
                name: "usermfarecoverycodes",
                schema: "security");

            migrationBuilder.DropTable(
                name: "userpersons",
                schema: "security");

            migrationBuilder.DropTable(
                name: "userrefreshtokens",
                schema: "security");

            migrationBuilder.DropTable(
                name: "userrolecompanies",
                schema: "security");

            migrationBuilder.DropTable(
                name: "userroles",
                schema: "security");

            migrationBuilder.DropTable(
                name: "usertokens",
                schema: "security");

            migrationBuilder.DropTable(
                name: "municipalities",
                schema: "common");

            migrationBuilder.DropTable(
                name: "streettypes",
                schema: "common");

            migrationBuilder.DropTable(
                name: "industries",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "taxregimes",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "incomeranges",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "systemoptions",
                schema: "security");

            migrationBuilder.DropTable(
                name: "ticketlogs",
                schema: "support");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "security");

            migrationBuilder.DropTable(
                name: "provinces",
                schema: "common");

            migrationBuilder.DropTable(
                name: "systemmodules",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "regions",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "areas",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "communicationchannels",
                schema: "common");

            migrationBuilder.DropTable(
                name: "persons",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "ticketcomplexities",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "ticketstatuses",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "users",
                schema: "security");

            migrationBuilder.DropTable(
                name: "countries",
                schema: "common");

            migrationBuilder.DropTable(
                name: "genders",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "identificationtypes",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "entitystatuses",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "timeunits",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "common");
        }
    }
}
