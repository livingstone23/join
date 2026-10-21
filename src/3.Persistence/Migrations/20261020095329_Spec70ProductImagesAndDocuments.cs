using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec70ProductImagesAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "productattachmentsettings",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alloweddocumenttypes = table.Column<int>(type: "integer", nullable: false),
                    maxdocumentsizebytes = table.Column<long>(type: "bigint", nullable: false),
                    maxdocumentsperproduct = table.Column<int>(type: "integer", nullable: false),
                    allowedimageformats = table.Column<int>(type: "integer", nullable: false),
                    maximagesizebytes = table.Column<long>(type: "bigint", nullable: false),
                    maximagesperproduct = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_productattachmentsettings", x => x.id);
                    table.ForeignKey(
                        name: "fk_productattachmentsettings_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "productdocumenttypes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_productdocumenttypes", x => x.id);
                    table.ForeignKey(
                        name: "fk_productdocumenttypes_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "productimages",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    productid = table.Column<Guid>(type: "uuid", nullable: false),
                    originalname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    newname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storageprovider = table.Column<int>(type: "integer", nullable: false),
                    contenttype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sizebytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    sortorder = table.Column<int>(type: "integer", nullable: false),
                    isprimary = table.Column<bool>(type: "boolean", nullable: false),
                    uploadedbyuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    deletedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletedbyuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    deletereason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_productimages", x => x.id);
                    table.ForeignKey(
                        name: "fk_productimages_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_productimages_products_productid",
                        column: x => x.productid,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_productimages_users_uploadedbyuserid",
                        column: x => x.uploadedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "productdocuments",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    productid = table.Column<Guid>(type: "uuid", nullable: false),
                    productdocumenttypeid = table.Column<Guid>(type: "uuid", nullable: false),
                    displayname = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    documenttype = table.Column<int>(type: "integer", nullable: false),
                    originalname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    newname = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storageprovider = table.Column<int>(type: "integer", nullable: false),
                    contenttype = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sizebytes = table.Column<long>(type: "bigint", nullable: false),
                    uploadedbyuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    deletedutc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deletedbyuserid = table.Column<Guid>(type: "uuid", nullable: true),
                    deletereason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdby = table.Column<string>(type: "text", nullable: true),
                    lastmodified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lastmodifiedby = table.Column<string>(type: "text", nullable: true),
                    gcrecord = table.Column<int>(type: "integer", nullable: false),
                    companyid = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_productdocuments", x => x.id);
                    table.ForeignKey(
                        name: "fk_productdocuments_companies_companyid",
                        column: x => x.companyid,
                        principalSchema: "common",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_productdocuments_productdocumenttypes_productdocumenttypeid",
                        column: x => x.productdocumenttypeid,
                        principalSchema: "catalog",
                        principalTable: "productdocumenttypes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_productdocuments_products_productid",
                        column: x => x.productid,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_productdocuments_users_uploadedbyuserid",
                        column: x => x.uploadedbyuserid,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_productattachmentsettings_company",
                schema: "catalog",
                table: "productattachmentsettings",
                column: "companyid",
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_productdocuments_product",
                schema: "catalog",
                table: "productdocuments",
                columns: new[] { "companyid", "productid" });

            migrationBuilder.CreateIndex(
                name: "ix_productdocuments_productdocumenttypeid",
                schema: "catalog",
                table: "productdocuments",
                column: "productdocumenttypeid");

            migrationBuilder.CreateIndex(
                name: "ix_productdocuments_productid",
                schema: "catalog",
                table: "productdocuments",
                column: "productid");

            migrationBuilder.CreateIndex(
                name: "ix_productdocuments_uploadedbyuserid",
                schema: "catalog",
                table: "productdocuments",
                column: "uploadedbyuserid");

            migrationBuilder.CreateIndex(
                name: "ux_productdocumenttypes_company_code",
                schema: "catalog",
                table: "productdocumenttypes",
                columns: new[] { "companyid", "code" },
                unique: true,
                filter: "\"gcrecord\" = 0");

            migrationBuilder.CreateIndex(
                name: "ix_productimages_product",
                schema: "catalog",
                table: "productimages",
                columns: new[] { "companyid", "productid" });

            migrationBuilder.CreateIndex(
                name: "ix_productimages_product_sort",
                schema: "catalog",
                table: "productimages",
                columns: new[] { "companyid", "productid", "sortorder" });

            migrationBuilder.CreateIndex(
                name: "ix_productimages_uploadedbyuserid",
                schema: "catalog",
                table: "productimages",
                column: "uploadedbyuserid");

            migrationBuilder.CreateIndex(
                name: "ux_productimages_oneprimary",
                schema: "catalog",
                table: "productimages",
                column: "productid",
                unique: true,
                filter: "\"isprimary\" = TRUE AND \"gcrecord\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "productattachmentsettings",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "productdocuments",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "productimages",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "productdocumenttypes",
                schema: "catalog");
        }
    }
}
