using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeTimeUnitAndTicketComplexityTenantAware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "Messaging",
                table: "TimeUnits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "Messaging",
                table: "TicketComplexities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
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
                    THROW 50000, 'No active company was found to backfill Messaging.TimeUnits and Messaging.TicketComplexities.', 1;
                END

                UPDATE [Messaging].[TimeUnits]
                SET [CompanyId] = @JoinCompanyId
                WHERE [CompanyId] IS NULL
                   OR [CompanyId] = '00000000-0000-0000-0000-000000000000';

                UPDATE [Messaging].[TicketComplexities]
                SET [CompanyId] = @JoinCompanyId
                WHERE [CompanyId] IS NULL
                   OR [CompanyId] = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                schema: "Messaging",
                table: "TimeUnits",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                schema: "Messaging",
                table: "TicketComplexities",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.Sql(
                """
                ;WITH DuplicateTimeUnits AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [CompanyId], UPPER(LTRIM(RTRIM([Name])))
                            ORDER BY [Created], [Id]) AS [RowNumber]
                    FROM [Messaging].[TimeUnits]
                    WHERE [GcRecord] = 0
                )
                UPDATE tu
                SET [GcRecord] = 1
                FROM [Messaging].[TimeUnits] tu
                INNER JOIN DuplicateTimeUnits dtu ON dtu.[Id] = tu.[Id]
                WHERE dtu.[RowNumber] > 1;

                ;WITH DuplicateTicketComplexities AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [CompanyId], UPPER(LTRIM(RTRIM([Name])))
                            ORDER BY [Created], [Id]) AS [RowNumber]
                    FROM [Messaging].[TicketComplexities]
                    WHERE [GcRecord] = 0
                )
                UPDATE tc
                SET [GcRecord] = 1
                FROM [Messaging].[TicketComplexities] tc
                INNER JOIN DuplicateTicketComplexities dtc ON dtc.[Id] = tc.[Id]
                WHERE dtc.[RowNumber] > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_TimeUnits_Company_Name",
                schema: "Messaging",
                table: "TimeUnits",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_TicketComplexities_Company_Name",
                schema: "Messaging",
                table: "TicketComplexities",
                columns: new[] { "CompanyId", "Name" },
                unique: true,
                filter: "[GcRecord] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_TicketComplexities_Companies_CompanyId",
                schema: "Messaging",
                table: "TicketComplexities",
                column: "CompanyId",
                principalSchema: "Common",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeUnits_Companies_CompanyId",
                schema: "Messaging",
                table: "TimeUnits",
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
                name: "FK_TicketComplexities_Companies_CompanyId",
                schema: "Messaging",
                table: "TicketComplexities");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeUnits_Companies_CompanyId",
                schema: "Messaging",
                table: "TimeUnits");

            migrationBuilder.DropIndex(
                name: "UX_TimeUnits_Company_Name",
                schema: "Messaging",
                table: "TimeUnits");

            migrationBuilder.DropIndex(
                name: "UX_TicketComplexities_Company_Name",
                schema: "Messaging",
                table: "TicketComplexities");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "Messaging",
                table: "TimeUnits");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "Messaging",
                table: "TicketComplexities");
        }
    }
}
