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
                name: "ix_taxregimes_companyid_code_gcrecord",
                schema: "admin",
                table: "taxregimes");

            migrationBuilder.DropIndex(
                name: "ix_taxregimes_companyid_name_gcrecord",
                schema: "admin",
                table: "taxregimes");

            migrationBuilder.DropIndex(
                name: "ix_regions_company_country_code_gcrecord",
                schema: "admin",
                table: "regions");

            migrationBuilder.DropIndex(
                name: "ix_regions_company_country_name_gcrecord",
                schema: "admin",
                table: "regions");

            migrationBuilder.DropIndex(
                name: "ix_persons_company_idtype_idnumber_gcrecord",
                schema: "admin",
                table: "persons");

            migrationBuilder.DropIndex(
                name: "ix_personcontacts_unique_valueperperson",
                schema: "admin",
                table: "personcontacts");

            migrationBuilder.DropIndex(
                name: "ix_industries_companyid_code_gcrecord",
                schema: "admin",
                table: "industries");

            migrationBuilder.DropIndex(
                name: "ix_industries_companyid_name_gcrecord",
                schema: "admin",
                table: "industries");

            migrationBuilder.DropIndex(
                name: "ix_incomeranges_companyid_displayname_gcrecord",
                schema: "admin",
                table: "incomeranges");

            migrationBuilder.DropIndex(
                name: "ix_incomeranges_companyid_displayorder_gcrecord",
                schema: "admin",
                table: "incomeranges");

            migrationBuilder.DropIndex(
                name: "ix_genders_companyid_code_gcrecord",
                schema: "admin",
                table: "genders");

            migrationBuilder.DropIndex(
                name: "ix_genders_companyid_name_gcrecord",
                schema: "admin",
                table: "genders");

            migrationBuilder.DropIndex(
                name: "ix_customers_company_customercode_gcrecord",
                schema: "admin",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_company_person_user_gcrecord",
                schema: "admin",
                table: "customers");

            migrationBuilder.CreateIndex(
                name: "ux_taxregimes_company_code",
                schema: "admin",
                table: "taxregimes",
                columns: new[] { "companyid", "code" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_taxregimes_company_name",
                schema: "admin",
                table: "taxregimes",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_regions_company_country_code",
                schema: "admin",
                table: "regions",
                columns: new[] { "companyid", "countryid", "code" },
                unique: true,
                filter: "\"code\" IS NOT NULL AND \"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_regions_company_country_name",
                schema: "admin",
                table: "regions",
                columns: new[] { "companyid", "countryid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_persons_company_idtype_idnumber",
                schema: "admin",
                table: "persons",
                columns: new[] { "companyid", "identificationtypeid", "identificationnumber" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_personcontacts_person_type_value",
                schema: "admin",
                table: "personcontacts",
                columns: new[] { "personid", "contacttype", "contactvalue" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_industries_company_code",
                schema: "admin",
                table: "industries",
                columns: new[] { "companyid", "code" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_industries_company_name",
                schema: "admin",
                table: "industries",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_incomeranges_company_displayname",
                schema: "admin",
                table: "incomeranges",
                columns: new[] { "companyid", "displayname" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_incomeranges_company_displayorder",
                schema: "admin",
                table: "incomeranges",
                columns: new[] { "companyid", "displayorder" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_genders_company_code",
                schema: "admin",
                table: "genders",
                columns: new[] { "companyid", "code" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_genders_company_name",
                schema: "admin",
                table: "genders",
                columns: new[] { "companyid", "name" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_customers_company_customercode",
                schema: "admin",
                table: "customers",
                columns: new[] { "companyid", "customercode" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ux_customers_company_person_user",
                schema: "admin",
                table: "customers",
                columns: new[] { "companyid", "personid", "userid" },
                unique: true,
                filter: "\"gcrecord\" = 0")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_taxregimes_company_code",
                schema: "admin",
                table: "taxregimes");

            migrationBuilder.DropIndex(
                name: "ux_taxregimes_company_name",
                schema: "admin",
                table: "taxregimes");

            migrationBuilder.DropIndex(
                name: "ux_regions_company_country_code",
                schema: "admin",
                table: "regions");

            migrationBuilder.DropIndex(
                name: "ux_regions_company_country_name",
                schema: "admin",
                table: "regions");

            migrationBuilder.DropIndex(
                name: "ux_persons_company_idtype_idnumber",
                schema: "admin",
                table: "persons");

            migrationBuilder.DropIndex(
                name: "ux_personcontacts_person_type_value",
                schema: "admin",
                table: "personcontacts");

            migrationBuilder.DropIndex(
                name: "ux_industries_company_code",
                schema: "admin",
                table: "industries");

            migrationBuilder.DropIndex(
                name: "ux_industries_company_name",
                schema: "admin",
                table: "industries");

            migrationBuilder.DropIndex(
                name: "ux_incomeranges_company_displayname",
                schema: "admin",
                table: "incomeranges");

            migrationBuilder.DropIndex(
                name: "ux_incomeranges_company_displayorder",
                schema: "admin",
                table: "incomeranges");

            migrationBuilder.DropIndex(
                name: "ux_genders_company_code",
                schema: "admin",
                table: "genders");

            migrationBuilder.DropIndex(
                name: "ux_genders_company_name",
                schema: "admin",
                table: "genders");

            migrationBuilder.DropIndex(
                name: "ux_customers_company_customercode",
                schema: "admin",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ux_customers_company_person_user",
                schema: "admin",
                table: "customers");

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
                name: "ix_persons_company_idtype_idnumber_gcrecord",
                schema: "admin",
                table: "persons",
                columns: new[] { "companyid", "identificationtypeid", "identificationnumber", "gcrecord" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_personcontacts_unique_valueperperson",
                schema: "admin",
                table: "personcontacts",
                columns: new[] { "personid", "contacttype", "contactvalue", "gcrecord" },
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
        }
    }
}
