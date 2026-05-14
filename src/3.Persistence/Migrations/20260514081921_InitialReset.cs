using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Admin");

            migrationBuilder.EnsureSchema(
                name: "Common");

            migrationBuilder.EnsureSchema(
                name: "Security");

            migrationBuilder.EnsureSchema(
                name: "Messaging");

            migrationBuilder.EnsureSchema(
                name: "Support");

            migrationBuilder.CreateTable(
                name: "CommunicationChannels",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunicationChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WebSite = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsoCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntityStatuses",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Code = table.Column<int>(type: "int", nullable: false),
                    IsOperative = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentificationTypes",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ValidationPattern = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentificationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IsSystemDefault = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StreetTypes",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Abbreviation = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreetTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemModules",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Order = table.Column<int>(type: "int", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AvatarId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AvatarUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsMfaEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MfaSecretKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExternalProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExternalProviderId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsSuperAdmin = table.Column<bool>(type: "bit", nullable: false),
                    IsSuperAdminCompany = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketStatuses",
                schema: "Messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Code = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsInitial = table.Column<bool>(type: "bit", nullable: false),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
                    IsFinal = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketStatuses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeUnits",
                schema: "Messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Code = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeUnits_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regions_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "Common",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Areas",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Areas_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Areas_EntityStatuses_EntityStatusId",
                        column: x => x.EntityStatusId,
                        principalSchema: "Admin",
                        principalTable: "EntityStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EntityStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_EntityStatuses_EntityStatusId",
                        column: x => x.EntityStatusId,
                        principalSchema: "Admin",
                        principalTable: "EntityStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SecondLastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CommercialName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IdentificationTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentificationNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Persons_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Persons_IdentificationTypes_IdentificationTypeId",
                        column: x => x.IdentificationTypeId,
                        principalSchema: "Admin",
                        principalTable: "IdentificationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "Security",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyModules",
                schema: "Admin",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyModules", x => new { x.CompanyId, x.ModuleId });
                    table.ForeignKey(
                        name: "FK_CompanyModules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyModules_SystemModules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "Admin",
                        principalTable: "SystemModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemOptions",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ControllerName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CanRead = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CanCreate = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CanUpdate = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CanDelete = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemOptions_SystemModules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "Admin",
                        principalTable: "SystemModules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SystemOptions_SystemOptions_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "Security",
                        principalTable: "SystemOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCommunicationChannels",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommunicationChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelIdentifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsPreferred = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCommunicationChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCommunicationChannels_CommunicationChannels_CommunicationChannelId",
                        column: x => x.CommunicationChannelId,
                        principalSchema: "Common",
                        principalTable: "CommunicationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCommunicationChannels_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCommunicationChannels_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCompanies",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCompanies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCompanies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCompanies_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserConnectionLogs",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConnectionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActiveSession = table.Column<bool>(type: "bit", nullable: false),
                    DisconnectionDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserConnectionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserConnectionLogs_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                schema: "Security",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRefreshTokens",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoleCompanies",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleCompanies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoleCompanies_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleCompanies_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "Security",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoleCompanies_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "Security",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "Security",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                schema: "Security",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketComplexities",
                schema: "Messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Code = table.Column<int>(type: "int", nullable: false),
                    ResolutionTimeUnits = table.Column<int>(type: "int", nullable: false),
                    TimeUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketComplexities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketComplexities_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketComplexities_TimeUnits_TimeUnitId",
                        column: x => x.TimeUnitId,
                        principalSchema: "Messaging",
                        principalTable: "TimeUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Provinces",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Provinces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Provinces_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "Common",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Provinces_Regions_RegionId",
                        column: x => x.RegionId,
                        principalSchema: "Common",
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PersonContacts",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactType = table.Column<int>(type: "int", nullable: false),
                    ContactValue = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Comments = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonContacts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonContacts_Persons_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "Admin",
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPersons",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPersons_Persons_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "Admin",
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPersons_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleSystemOptions",
                schema: "Security",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SystemOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanRead = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanCreate = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanUpdate = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanDelete = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleSystemOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleSystemOptions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleSystemOptions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "Security",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoleSystemOptions_SystemOptions_SystemOptionId",
                        column: x => x.SystemOptionId,
                        principalSchema: "Security",
                        principalTable: "SystemOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    MaxDayTicketInactivity = table.Column<int>(type: "int", nullable: true),
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

            migrationBuilder.CreateTable(
                name: "Tickets",
                schema: "Messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TimeUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimatedTime = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ConsumedTime = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EffortPoints = table.Column<decimal>(type: "decimal(5,1)", nullable: true),
                    TicketStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketComplexityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrecedentTicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsVisibleToExternals = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tickets_Areas_AreaId",
                        column: x => x.AreaId,
                        principalSchema: "Admin",
                        principalTable: "Areas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tickets_CommunicationChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "Common",
                        principalTable: "CommunicationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_Persons_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "Admin",
                        principalTable: "Persons",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tickets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "Admin",
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tickets_TicketComplexities_TicketComplexityId",
                        column: x => x.TicketComplexityId,
                        principalSchema: "Messaging",
                        principalTable: "TicketComplexities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_TicketStatuses_TicketStatusId",
                        column: x => x.TicketStatusId,
                        principalSchema: "Messaging",
                        principalTable: "TicketStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_Tickets_PrecedentTicketId",
                        column: x => x.PrecedentTicketId,
                        principalSchema: "Messaging",
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_TimeUnits_TimeUnitId",
                        column: x => x.TimeUnitId,
                        principalSchema: "Messaging",
                        principalTable: "TimeUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tickets_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "Security",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Municipalities",
                schema: "Common",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Municipalities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Municipalities_Provinces_ProvinceId",
                        column: x => x.ProvinceId,
                        principalSchema: "Common",
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateTable(
                name: "TicketNotifications",
                schema: "Support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommunicationChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExternalProviderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NotificationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketNotifications_CommunicationChannels_CommunicationChannelId",
                        column: x => x.CommunicationChannelId,
                        principalSchema: "Common",
                        principalTable: "CommunicationChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketNotifications_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketNotifications_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalSchema: "Messaging",
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonAddresses",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ZipCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StreetTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProvinceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MunicipalityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonAddresses_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonAddresses_Countries_CountryId",
                        column: x => x.CountryId,
                        principalSchema: "Common",
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonAddresses_Municipalities_MunicipalityId",
                        column: x => x.MunicipalityId,
                        principalSchema: "Common",
                        principalTable: "Municipalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonAddresses_Persons_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "Admin",
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonAddresses_Provinces_ProvinceId",
                        column: x => x.ProvinceId,
                        principalSchema: "Common",
                        principalTable: "Provinces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonAddresses_Regions_RegionId",
                        column: x => x.RegionId,
                        principalSchema: "Common",
                        principalTable: "Regions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PersonAddresses_StreetTypes_StreetTypeId",
                        column: x => x.StreetTypeId,
                        principalSchema: "Common",
                        principalTable: "StreetTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Areas_CompanyId",
                schema: "Admin",
                table: "Areas",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_EntityStatusId",
                schema: "Admin",
                table: "Areas",
                column: "EntityStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationChannels_Name",
                schema: "Common",
                table: "CommunicationChannels",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_TaxId",
                schema: "Common",
                table: "Companies",
                column: "TaxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyModules_ModuleId",
                schema: "Admin",
                table: "CompanyModules",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_IsoCode",
                schema: "Common",
                table: "Countries",
                column: "IsoCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityStatuses_Code",
                schema: "Admin",
                table: "EntityStatuses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Municipalities_ProvinceId_Name",
                schema: "Common",
                table: "Municipalities",
                columns: new[] { "ProvinceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_CompanyId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_CountryId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_MunicipalityId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "MunicipalityId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_PersonId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_ProvinceId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_RegionId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_StreetTypeId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "StreetTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonContacts_CompanyId",
                schema: "Admin",
                table: "PersonContacts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonContacts_PersonId",
                schema: "Admin",
                table: "PersonContacts",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_CompanyId_IdentificationNumber",
                schema: "Admin",
                table: "Persons",
                columns: new[] { "CompanyId", "IdentificationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_IdentificationTypeId",
                schema: "Admin",
                table: "Persons",
                column: "IdentificationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_CompanyId",
                schema: "Admin",
                table: "Projects",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_EntityStatusId",
                schema: "Admin",
                table: "Projects",
                column: "EntityStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Provinces_CountryId_Code",
                schema: "Common",
                table: "Provinces",
                columns: new[] { "CountryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Provinces_RegionId",
                schema: "Common",
                table: "Provinces",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CountryId_Code",
                schema: "Common",
                table: "Regions",
                columns: new[] { "CountryId", "Code" },
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                schema: "Security",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "Security",
                table: "Roles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_CompanyId_RoleId_SystemOptionId",
                schema: "Security",
                table: "RoleSystemOptions",
                columns: new[] { "CompanyId", "RoleId", "SystemOptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_RoleId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleSystemOptions_SystemOptionId",
                schema: "Security",
                table: "RoleSystemOptions",
                column: "SystemOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_StreetTypes_Abbreviation",
                schema: "Common",
                table: "StreetTypes",
                column: "Abbreviation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StreetTypes_Name",
                schema: "Common",
                table: "StreetTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemModules_Name",
                schema: "Admin",
                table: "SystemModules",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemOptions_ModuleId_Route",
                schema: "Security",
                table: "SystemOptions",
                columns: new[] { "ModuleId", "Route" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemOptions_ParentId",
                schema: "Security",
                table: "SystemOptions",
                column: "ParentId");

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

            migrationBuilder.CreateIndex(
                name: "IX_TicketComplexities_TimeUnitId",
                schema: "Messaging",
                table: "TicketComplexities",
                column: "TimeUnitId");

            migrationBuilder.CreateIndex(
                name: "UX_TicketComplexities_Company_Name",
                schema: "Messaging",
                table: "TicketComplexities",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

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

            migrationBuilder.CreateIndex(
                name: "IX_TicketNotifications_CommunicationChannelId",
                schema: "Support",
                table: "TicketNotifications",
                column: "CommunicationChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketNotifications_CompanyId",
                schema: "Support",
                table: "TicketNotifications",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketNotifications_TicketId",
                schema: "Support",
                table: "TicketNotifications",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AreaId",
                schema: "Messaging",
                table: "Tickets",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_AssignedToUserId",
                schema: "Messaging",
                table: "Tickets",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ChannelId",
                schema: "Messaging",
                table: "Tickets",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Code",
                schema: "Messaging",
                table: "Tickets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CompanyId",
                schema: "Messaging",
                table: "Tickets",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CreatedByUserId",
                schema: "Messaging",
                table: "Tickets",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_PersonId",
                schema: "Messaging",
                table: "Tickets",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_PrecedentTicketId",
                schema: "Messaging",
                table: "Tickets",
                column: "PrecedentTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ProjectId",
                schema: "Messaging",
                table: "Tickets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TicketComplexityId",
                schema: "Messaging",
                table: "Tickets",
                column: "TicketComplexityId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TicketStatusId",
                schema: "Messaging",
                table: "Tickets",
                column: "TicketStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TimeUnitId",
                schema: "Messaging",
                table: "Tickets",
                column: "TimeUnitId");

            migrationBuilder.CreateIndex(
                name: "UX_TicketStatuses_Company_Final",
                schema: "Messaging",
                table: "TicketStatuses",
                columns: new[] { "CompanyId", "IsFinal" },
                unique: true,
                filter: "[IsFinal] = 1 AND [GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TicketStatuses_Company_Initial",
                schema: "Messaging",
                table: "TicketStatuses",
                columns: new[] { "CompanyId", "IsInitial" },
                unique: true,
                filter: "[IsInitial] = 1 AND [GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TicketStatuses_Company_Paused",
                schema: "Messaging",
                table: "TicketStatuses",
                columns: new[] { "CompanyId", "IsPaused" },
                unique: true,
                filter: "[IsPaused] = 1 AND [GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TimeUnits_Company_Name",
                schema: "Messaging",
                table: "TimeUnits",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                schema: "Security",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCommunicationChannels_CommunicationChannelId",
                schema: "Admin",
                table: "UserCommunicationChannels",
                column: "CommunicationChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCommunicationChannels_CompanyId_UserId_CommunicationChannelId",
                schema: "Admin",
                table: "UserCommunicationChannels",
                columns: new[] { "CompanyId", "UserId", "CommunicationChannelId" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserCommunicationChannels_UserId",
                schema: "Admin",
                table: "UserCommunicationChannels",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCompanies_CompanyId",
                schema: "Security",
                table: "UserCompanies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCompanies_UserId_CompanyId",
                schema: "Security",
                table: "UserCompanies",
                columns: new[] { "UserId", "CompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_UserCompanies_UserId_Default",
                schema: "Security",
                table: "UserCompanies",
                column: "UserId",
                unique: true,
                filter: "[IsDefault] = 1 AND [GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserConnectionLogs_IsActiveSession",
                schema: "Security",
                table: "UserConnectionLogs",
                column: "IsActiveSession");

            migrationBuilder.CreateIndex(
                name: "IX_UserConnectionLogs_UserId",
                schema: "Security",
                table: "UserConnectionLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                schema: "Security",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPersons_PersonId",
                schema: "Security",
                table: "UserPersons",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPersons_UserId_PersonId",
                schema: "Security",
                table: "UserPersons",
                columns: new[] { "UserId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_Token",
                schema: "Security",
                table: "UserRefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_UserId_IsRevoked",
                schema: "Security",
                table: "UserRefreshTokens",
                columns: new[] { "UserId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleCompanies_CompanyId",
                schema: "Security",
                table: "UserRoleCompanies",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleCompanies_RoleId",
                schema: "Security",
                table: "UserRoleCompanies",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleCompanies_UserId_RoleId_CompanyId",
                schema: "Security",
                table: "UserRoleCompanies",
                columns: new[] { "UserId", "RoleId", "CompanyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                schema: "Security",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "Security",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "Security",
                table: "Users",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyModules",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "PersonAddresses",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "PersonContacts",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "RoleClaims",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "RoleSystemOptions",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "TicketCompanyDefaults",
                schema: "Messaging");

            migrationBuilder.DropTable(
                name: "TicketLogs",
                schema: "Support");

            migrationBuilder.DropTable(
                name: "TicketNotifications",
                schema: "Support");

            migrationBuilder.DropTable(
                name: "UserClaims",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserCommunicationChannels",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "UserCompanies",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserConnectionLogs",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserLogins",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserPersons",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserRefreshTokens",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserRoleCompanies",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "UserTokens",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "Municipalities",
                schema: "Common");

            migrationBuilder.DropTable(
                name: "StreetTypes",
                schema: "Common");

            migrationBuilder.DropTable(
                name: "SystemOptions",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "Tickets",
                schema: "Messaging");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "Provinces",
                schema: "Common");

            migrationBuilder.DropTable(
                name: "SystemModules",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "Areas",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "CommunicationChannels",
                schema: "Common");

            migrationBuilder.DropTable(
                name: "Persons",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "Projects",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "TicketComplexities",
                schema: "Messaging");

            migrationBuilder.DropTable(
                name: "TicketStatuses",
                schema: "Messaging");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "Security");

            migrationBuilder.DropTable(
                name: "Regions",
                schema: "Common");

            migrationBuilder.DropTable(
                name: "IdentificationTypes",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "EntityStatuses",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "TimeUnits",
                schema: "Messaging");

            migrationBuilder.DropTable(
                name: "Countries",
                schema: "Common");

            migrationBuilder.DropTable(
                name: "Companies",
                schema: "Common");
        }
    }
}
