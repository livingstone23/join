using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketStatusTenantSeedUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "Messaging",
                table: "TicketStatuses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFinal",
                schema: "Messaging",
                table: "TicketStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInitial",
                schema: "Messaging",
                table: "TicketStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPaused",
                schema: "Messaging",
                table: "TicketStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxDayTicketInactivity",
                schema: "Messaging",
                table: "TicketCompanyDefaults",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [Messaging].[TicketStatuses]
                    WHERE [CompanyId] IS NULL
                )
                BEGIN
                    DECLARE @JoinCompanyId uniqueidentifier;

                    SELECT TOP(1) @JoinCompanyId = [Id]
                    FROM [Common].[Companies]
                    WHERE [GcRecord] = 0
                      AND [TaxId] = 'JOIN-001';

                    IF @JoinCompanyId IS NULL
                    BEGIN
                        SELECT TOP(1) @JoinCompanyId = [Id]
                        FROM [Common].[Companies]
                        WHERE [GcRecord] = 0
                        ORDER BY [Created];
                    END

                    IF @JoinCompanyId IS NULL
                    BEGIN
                        THROW 50000, 'No active company was found to backfill Messaging.TicketStatuses.CompanyId.', 1;
                    END

                    UPDATE [Messaging].[TicketStatuses]
                    SET [CompanyId] = @JoinCompanyId
                    WHERE [CompanyId] IS NULL
                       OR [CompanyId] = '00000000-0000-0000-0000-000000000000';
                END
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                schema: "Messaging",
                table: "TicketStatuses",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_TicketStatuses_Companies_CompanyId",
                schema: "Messaging",
                table: "TicketStatuses",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketStatuses_Companies_CompanyId",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropIndex(
                name: "UX_TicketStatuses_Company_Final",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropIndex(
                name: "UX_TicketStatuses_Company_Initial",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropIndex(
                name: "UX_TicketStatuses_Company_Paused",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropColumn(
                name: "IsFinal",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropColumn(
                name: "IsInitial",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropColumn(
                name: "IsPaused",
                schema: "Messaging",
                table: "TicketStatuses");

            migrationBuilder.DropColumn(
                name: "MaxDayTicketInactivity",
                schema: "Messaging",
                table: "TicketCompanyDefaults");
        }
    }
}
