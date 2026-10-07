using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FilteredUniqueIndexesForSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaxRegimes_CompanyId_Code_GcRecord",
                schema: "Admin",
                table: "TaxRegimes");

            migrationBuilder.DropIndex(
                name: "IX_TaxRegimes_CompanyId_Name_GcRecord",
                schema: "Admin",
                table: "TaxRegimes");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Company_Country_Code_GcRecord",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Company_Country_Name_GcRecord",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Persons_Company_IdType_IdNumber_GcRecord",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "IX_PersonContacts_Unique_ValuePerPerson",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropIndex(
                name: "IX_Industries_CompanyId_Code_GcRecord",
                schema: "Admin",
                table: "Industries");

            migrationBuilder.DropIndex(
                name: "IX_Industries_CompanyId_Name_GcRecord",
                schema: "Admin",
                table: "Industries");

            migrationBuilder.DropIndex(
                name: "IX_IncomeRanges_CompanyId_DisplayName_GcRecord",
                schema: "Admin",
                table: "IncomeRanges");

            migrationBuilder.DropIndex(
                name: "IX_IncomeRanges_CompanyId_DisplayOrder_GcRecord",
                schema: "Admin",
                table: "IncomeRanges");

            migrationBuilder.DropIndex(
                name: "IX_Genders_CompanyId_Code_GcRecord",
                schema: "Admin",
                table: "Genders");

            migrationBuilder.DropIndex(
                name: "IX_Genders_CompanyId_Name_GcRecord",
                schema: "Admin",
                table: "Genders");

            migrationBuilder.DropIndex(
                name: "IX_Customers_Company_CustomerCode_GcRecord",
                schema: "Admin",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_Company_Person_User_GcRecord",
                schema: "Admin",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "UX_TaxRegimes_Company_Code",
                schema: "Admin",
                table: "TaxRegimes",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TaxRegimes_Company_Name",
                schema: "Admin",
                table: "TaxRegimes",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Regions_Company_Country_Code",
                schema: "Admin",
                table: "Regions",
                columns: new[] { "CompanyId", "CountryId", "Code" },
                unique: true,
                filter: "[Code] IS NOT NULL AND [GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Regions_Company_Country_Name",
                schema: "Admin",
                table: "Regions",
                columns: new[] { "CompanyId", "CountryId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Persons_Company_IdType_IdNumber",
                schema: "Admin",
                table: "Persons",
                columns: new[] { "CompanyId", "IdentificationTypeId", "IdentificationNumber" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_PersonContacts_Person_Type_Value",
                schema: "Admin",
                table: "PersonContacts",
                columns: new[] { "PersonId", "ContactType", "ContactValue" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Industries_Company_Code",
                schema: "Admin",
                table: "Industries",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Industries_Company_Name",
                schema: "Admin",
                table: "Industries",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_IncomeRanges_Company_DisplayName",
                schema: "Admin",
                table: "IncomeRanges",
                columns: new[] { "CompanyId", "DisplayName" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_IncomeRanges_Company_DisplayOrder",
                schema: "Admin",
                table: "IncomeRanges",
                columns: new[] { "CompanyId", "DisplayOrder" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Genders_Company_Code",
                schema: "Admin",
                table: "Genders",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Genders_Company_Name",
                schema: "Admin",
                table: "Genders",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Customers_Company_CustomerCode",
                schema: "Admin",
                table: "Customers",
                columns: new[] { "CompanyId", "CustomerCode" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Customers_Company_Person_User",
                schema: "Admin",
                table: "Customers",
                columns: new[] { "CompanyId", "PersonId", "UserId" },
                unique: true,
                filter: "[GcRecord] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TaxRegimes_Company_Code",
                schema: "Admin",
                table: "TaxRegimes");

            migrationBuilder.DropIndex(
                name: "UX_TaxRegimes_Company_Name",
                schema: "Admin",
                table: "TaxRegimes");

            migrationBuilder.DropIndex(
                name: "UX_Regions_Company_Country_Code",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "UX_Regions_Company_Country_Name",
                schema: "Admin",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "UX_Persons_Company_IdType_IdNumber",
                schema: "Admin",
                table: "Persons");

            migrationBuilder.DropIndex(
                name: "UX_PersonContacts_Person_Type_Value",
                schema: "Admin",
                table: "PersonContacts");

            migrationBuilder.DropIndex(
                name: "UX_Industries_Company_Code",
                schema: "Admin",
                table: "Industries");

            migrationBuilder.DropIndex(
                name: "UX_Industries_Company_Name",
                schema: "Admin",
                table: "Industries");

            migrationBuilder.DropIndex(
                name: "UX_IncomeRanges_Company_DisplayName",
                schema: "Admin",
                table: "IncomeRanges");

            migrationBuilder.DropIndex(
                name: "UX_IncomeRanges_Company_DisplayOrder",
                schema: "Admin",
                table: "IncomeRanges");

            migrationBuilder.DropIndex(
                name: "UX_Genders_Company_Code",
                schema: "Admin",
                table: "Genders");

            migrationBuilder.DropIndex(
                name: "UX_Genders_Company_Name",
                schema: "Admin",
                table: "Genders");

            migrationBuilder.DropIndex(
                name: "UX_Customers_Company_CustomerCode",
                schema: "Admin",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "UX_Customers_Company_Person_User",
                schema: "Admin",
                table: "Customers");

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
                name: "IX_Persons_Company_IdType_IdNumber_GcRecord",
                schema: "Admin",
                table: "Persons",
                columns: new[] { "CompanyId", "IdentificationTypeId", "IdentificationNumber", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonContacts_Unique_ValuePerPerson",
                schema: "Admin",
                table: "PersonContacts",
                columns: new[] { "PersonId", "ContactType", "ContactValue", "GcRecord" },
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
                name: "IX_IncomeRanges_CompanyId_DisplayName_GcRecord",
                schema: "Admin",
                table: "IncomeRanges",
                columns: new[] { "CompanyId", "DisplayName", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomeRanges_CompanyId_DisplayOrder_GcRecord",
                schema: "Admin",
                table: "IncomeRanges",
                columns: new[] { "CompanyId", "DisplayOrder", "GcRecord" },
                unique: true);

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
                name: "IX_Customers_Company_CustomerCode_GcRecord",
                schema: "Admin",
                table: "Customers",
                columns: new[] { "CompanyId", "CustomerCode", "GcRecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Company_Person_User_GcRecord",
                schema: "Admin",
                table: "Customers",
                columns: new[] { "CompanyId", "PersonId", "UserId", "GcRecord" },
                unique: true);
        }
    }
}
