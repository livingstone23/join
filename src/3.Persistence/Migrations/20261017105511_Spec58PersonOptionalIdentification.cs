using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec58PersonOptionalIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_persons_company_idtype_idnumber",
                schema: "admin",
                table: "persons");

            migrationBuilder.AlterColumn<Guid>(
                name: "identificationtypeid",
                schema: "admin",
                table: "persons",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "identificationnumber",
                schema: "admin",
                table: "persons",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "ux_persons_company_idtype_idnumber",
                schema: "admin",
                table: "persons",
                columns: new[] { "companyid", "identificationtypeid", "identificationnumber" },
                unique: true,
                filter: "\"identificationnumber\" IS NOT NULL AND \"gcrecord\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_persons_company_idtype_idnumber",
                schema: "admin",
                table: "persons");

            migrationBuilder.AlterColumn<Guid>(
                name: "identificationtypeid",
                schema: "admin",
                table: "persons",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "identificationnumber",
                schema: "admin",
                table: "persons",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_persons_company_idtype_idnumber",
                schema: "admin",
                table: "persons",
                columns: new[] { "companyid", "identificationtypeid", "identificationnumber" },
                unique: true,
                filter: "\"gcrecord\" = 0");
        }
    }
}
