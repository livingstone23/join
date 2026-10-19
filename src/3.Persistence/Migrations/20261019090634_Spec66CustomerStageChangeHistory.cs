using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Spec66CustomerStageChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "fromcustomerstageid",
                schema: "customer",
                table: "customerstatushistory",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tocustomerstageid",
                schema: "customer",
                table: "customerstatushistory",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customerstatushistory_fromcustomerstageid",
                schema: "customer",
                table: "customerstatushistory",
                column: "fromcustomerstageid");

            migrationBuilder.CreateIndex(
                name: "ix_customerstatushistory_tocustomerstageid",
                schema: "customer",
                table: "customerstatushistory",
                column: "tocustomerstageid");

            migrationBuilder.AddForeignKey(
                name: "fk_customerstatushistory_customerstages_fromcustomerstageid",
                schema: "customer",
                table: "customerstatushistory",
                column: "fromcustomerstageid",
                principalSchema: "customer",
                principalTable: "customerstages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_customerstatushistory_customerstages_tocustomerstageid",
                schema: "customer",
                table: "customerstatushistory",
                column: "tocustomerstageid",
                principalSchema: "customer",
                principalTable: "customerstages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customerstatushistory_customerstages_fromcustomerstageid",
                schema: "customer",
                table: "customerstatushistory");

            migrationBuilder.DropForeignKey(
                name: "fk_customerstatushistory_customerstages_tocustomerstageid",
                schema: "customer",
                table: "customerstatushistory");

            migrationBuilder.DropIndex(
                name: "ix_customerstatushistory_fromcustomerstageid",
                schema: "customer",
                table: "customerstatushistory");

            migrationBuilder.DropIndex(
                name: "ix_customerstatushistory_tocustomerstageid",
                schema: "customer",
                table: "customerstatushistory");

            migrationBuilder.DropColumn(
                name: "fromcustomerstageid",
                schema: "customer",
                table: "customerstatushistory");

            migrationBuilder.DropColumn(
                name: "tocustomerstageid",
                schema: "customer",
                table: "customerstatushistory");
        }
    }
}
