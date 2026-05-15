using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PersonAddresses_Companies_CompanyId",
                schema: "Admin",
                table: "PersonAddresses");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonAddresses_Regions_RegionId",
                schema: "Admin",
                table: "PersonAddresses");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonContacts_Companies_CompanyId",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropIndex(
                name: "IX_Persons_CompanyId_IdentificationNumber",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_PersonContacts_CompanyId",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropIndex(
                name: "IX_PersonContacts_PersonId",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropIndex(
                name: "IX_PersonAddresses_CompanyId",
                schema: "Admin",
                table: "PersonAddresses");

            migrationBuilder.DropIndex(
                name: "IX_Regions_CountryId_Code",
                schema: "Common",
                table: "Regions");

            migrationBuilder.RenameTable(
                name: "Regions",
                schema: "Common",
                newName: "Regions",
                newSchema: "Admin");

            migrationBuilder.Sql(
                """
                UPDATE [Admin].[Persons]
                SET [PersonType] = CASE UPPER(LTRIM(RTRIM([PersonType])))
                    WHEN 'PHYSICAL' THEN '1'
                    WHEN 'LEGAL' THEN '2'
                    WHEN 'NATURAL' THEN '1'
                    WHEN 'JURIDICA' THEN '2'
                    WHEN 'JURÍDICA' THEN '2'
                    WHEN '1' THEN '1'
                    WHEN '2' THEN '2'
                    ELSE '1'
                END;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "PersonType",
                schema: "Admin",
                table: "Persons",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                schema: "Admin",
                table: "Persons",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Admin",
                table: "Persons",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "GenderId",
                schema: "Admin",
                table: "Persons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "Admin",
                table: "Persons",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsPrimary",
                schema: "Admin",
                table: "PersonContacts",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Admin",
                table: "PersonContacts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "Admin",
                table: "PersonContacts",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsDefault",
                schema: "Admin",
                table: "PersonAddresses",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Admin",
                table: "PersonAddresses",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "Admin",
                table: "PersonAddresses",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "Admin",
                table: "Regions",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Admin",
                table: "Regions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "Admin",
                table: "Regions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "Admin",
                table: "Regions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Genders",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Genders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncomeRanges",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MinimumValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeRanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomeRanges_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Industries",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Industries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Industries_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonEmployments",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JobTitle = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: true),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonEmployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonEmployments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonEmployments_Persons_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "Admin",
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxRegimes",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRegimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxRegimes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonFinancialProfiles",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncomeRangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceOfFunds = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    DeclaredDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonFinancialProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonFinancialProfiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonFinancialProfiles_IncomeRanges_IncomeRangeId",
                        column: x => x.IncomeRangeId,
                        principalSchema: "Admin",
                        principalTable: "IncomeRanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonFinancialProfiles_Persons_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "Admin",
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonBusinessProfiles",
                schema: "Admin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndustryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxRegimeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Website = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    FoundationDate = table.Column<DateTime>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastModified = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GcRecord = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonBusinessProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonBusinessProfiles_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "Common",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonBusinessProfiles_Industries_IndustryId",
                        column: x => x.IndustryId,
                        principalSchema: "Admin",
                        principalTable: "Industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonBusinessProfiles_Persons_PersonId",
                        column: x => x.PersonId,
                        principalSchema: "Admin",
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonBusinessProfiles_TaxRegimes_TaxRegimeId",
                        column: x => x.TaxRegimeId,
                        principalSchema: "Admin",
                        principalTable: "TaxRegimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Persons_Company_IdType_IdNumber_GcRecord",
                schema: "Admin",
                table: "Persons",
                columns: new[] { "CompanyId", "IdentificationTypeId", "IdentificationNumber", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_GenderId",
                schema: "Admin",
                table: "Persons",
                column: "GenderId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonContacts_Company_Person_Primary",
                schema: "Admin",
                table: "PersonContacts",
                columns: new[] { "CompanyId", "PersonId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonContacts_Unique_ValuePerPerson",
                schema: "Admin",
                table: "PersonContacts",
                columns: new[] { "PersonId", "ContactType", "ContactValue", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonAddresses_Company_Person_Default",
                schema: "Admin",
                table: "PersonAddresses",
                columns: new[] { "CompanyId", "PersonId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Company_Country_Code_GcRecord",
                schema: "Admin",
                table: "Regions",
                columns: new[] { "CompanyId", "CountryId", "Code", "GcRecord" },
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Company_Country_Name_GcRecord",
                schema: "Admin",
                table: "Regions",
                columns: new[] { "CompanyId", "CountryId", "Name", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CountryId",
                schema: "Admin",
                table: "Regions",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Genders_CompanyId_Code_GcRecord",
                schema: "Admin",
                table: "Genders",
                columns: new[] { "CompanyId", "Code", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genders_CompanyId_Name_GcRecord",
                schema: "Admin",
                table: "Genders",
                columns: new[] { "CompanyId", "Name", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomeRanges_CompanyId_DisplayName_GcRecord",
                schema: "Admin",
                table: "IncomeRanges",
                columns: new[] { "CompanyId", "DisplayName", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Industries_CompanyId_Code_GcRecord",
                schema: "Admin",
                table: "Industries",
                columns: new[] { "CompanyId", "Code", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Industries_CompanyId_Name_GcRecord",
                schema: "Admin",
                table: "Industries",
                columns: new[] { "CompanyId", "Name", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonBusinessProfiles_Company_Person_GcRecord",
                schema: "Admin",
                table: "PersonBusinessProfiles",
                columns: new[] { "CompanyId", "PersonId", "GcRecord" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonBusinessProfiles_IndustryId",
                schema: "Admin",
                table: "PersonBusinessProfiles",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonBusinessProfiles_PersonId",
                schema: "Admin",
                table: "PersonBusinessProfiles",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonBusinessProfiles_TaxRegimeId",
                schema: "Admin",
                table: "PersonBusinessProfiles",
                column: "TaxRegimeId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonEmployments_Company_Person_Current",
                schema: "Admin",
                table: "PersonEmployments",
                columns: new[] { "CompanyId", "PersonId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonEmployments_PersonId",
                schema: "Admin",
                table: "PersonEmployments",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonFinancialProfiles_Company_Person_Current",
                schema: "Admin",
                table: "PersonFinancialProfiles",
                columns: new[] { "CompanyId", "PersonId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonFinancialProfiles_IncomeRangeId",
                schema: "Admin",
                table: "PersonFinancialProfiles",
                column: "IncomeRangeId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonFinancialProfiles_PersonId",
                schema: "Admin",
                table: "PersonFinancialProfiles",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRegimes_CompanyId_Code_GcRecord",
                schema: "Admin",
                table: "TaxRegimes",
                columns: new[] { "CompanyId", "Code", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRegimes_CompanyId_Name_GcRecord",
                schema: "Admin",
                table: "TaxRegimes",
                columns: new[] { "CompanyId", "Name", "GcRecord" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonAddresses_Companies_CompanyId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonAddresses_Regions_RegionId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "RegionId",
                principalSchema: "Admin",
                principalTable: "Regions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonContacts_Companies_CompanyId",
                schema: "Admin",
                table: "PersonContacts",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Persons_Genders_GenderId",
                schema: "Admin",
                table: "Persons",
                column: "GenderId",
                principalSchema: "Admin",
                principalTable: "Genders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Regions_Companies_CompanyId",
                schema: "Admin",
                table: "Regions",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PersonAddresses_Companies_CompanyId",
                schema: "Admin",
                table: "PersonAddresses");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonAddresses_Regions_RegionId",
                schema: "Admin",
                table: "PersonAddresses");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonContacts_Companies_CompanyId",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropForeignKey(
                name: "FK_Persons_Genders_GenderId",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropForeignKey(
                name: "FK_Regions_Companies_CompanyId",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropTable(
                name: "Genders",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "PersonBusinessProfiles",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "PersonEmployments",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "PersonFinancialProfiles",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "Industries",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "TaxRegimes",
                schema: "Admin");

            migrationBuilder.DropTable(
                name: "IncomeRanges",
                schema: "Admin");

            migrationBuilder.DropIndex(
                name: "IX_Persons_Company_IdType_IdNumber_GcRecord",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_Persons_GenderId",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_PersonContacts_Company_Person_Primary",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropIndex(
                name: "IX_PersonContacts_Unique_ValuePerPerson",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropIndex(
                name: "IX_PersonAddresses_Company_Person_Default",
                schema: "Admin",
                table: "PersonAddresses");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Company_Country_Code_GcRecord",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Company_Country_Name_GcRecord",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_CountryId",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "GenderId",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "Admin",
                table: "PersonAddresses");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.RenameTable(
                name: "Regions",
                schema: "Admin",
                newName: "Regions",
                newSchema: "Common");

            migrationBuilder.AlterColumn<string>(
                name: "PersonType",
                schema: "Admin",
                table: "Persons",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                schema: "Admin",
                table: "Persons",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Admin",
                table: "Persons",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<bool>(
                name: "IsPrimary",
                schema: "Admin",
                table: "PersonContacts",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Admin",
                table: "PersonContacts",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<bool>(
                name: "IsDefault",
                schema: "Admin",
                table: "PersonAddresses",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Admin",
                table: "PersonAddresses",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "Common",
                table: "Regions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<int>(
                name: "GcRecord",
                schema: "Common",
                table: "Regions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "Common",
                table: "Regions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_CompanyId_IdentificationNumber",
                schema: "Admin",
                table: "Persons",
                columns: new[] { "CompanyId", "IdentificationNumber" },
                unique: true);

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
                name: "IX_PersonAddresses_CompanyId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CountryId_Code",
                schema: "Common",
                table: "Regions",
                columns: new[] { "CountryId", "Code" },
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonAddresses_Companies_CompanyId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonAddresses_Regions_RegionId",
                schema: "Admin",
                table: "PersonAddresses",
                column: "RegionId",
                principalSchema: "Common",
                principalTable: "Regions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PersonContacts_Companies_CompanyId",
                schema: "Admin",
                table: "PersonContacts",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
