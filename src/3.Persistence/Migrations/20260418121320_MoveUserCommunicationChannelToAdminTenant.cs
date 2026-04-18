using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveUserCommunicationChannelToAdminTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserCommunicationChannels_Users_ApplicationUserId",
                schema: "Messaging",
                table: "UserCommunicationChannels");

            migrationBuilder.DropIndex(
                name: "IX_UserCommunicationChannels_ApplicationUserId",
                schema: "Messaging",
                table: "UserCommunicationChannels");

            migrationBuilder.DropIndex(
                name: "IX_UserCommunicationChannels_UserId_CommunicationChannelId",
                schema: "Messaging",
                table: "UserCommunicationChannels");

            migrationBuilder.DropColumn(
                name: "ApplicationUserId",
                schema: "Messaging",
                table: "UserCommunicationChannels");

            migrationBuilder.RenameTable(
                name: "UserCommunicationChannels",
                schema: "Messaging",
                newName: "UserCommunicationChannels",
                newSchema: "Admin");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "Admin",
                table: "UserCommunicationChannels",
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
                    THROW 50000, 'No active company was found to backfill Admin.UserCommunicationChannels.CompanyId.', 1;
                END

                UPDATE ucc
                SET [CompanyId] = ISNULL(defaultCompany.[CompanyId], @JoinCompanyId)
                FROM [Admin].[UserCommunicationChannels] ucc
                OUTER APPLY
                (
                    SELECT TOP(1) uc.[CompanyId]
                    FROM [Security].[UserCompanies] uc
                    WHERE uc.[UserId] = ucc.[UserId]
                      AND uc.[GcRecord] = 0
                    ORDER BY uc.[IsDefault] DESC, uc.[Created], uc.[Id]
                ) AS defaultCompany
                WHERE ucc.[CompanyId] IS NULL
                   OR ucc.[CompanyId] = '00000000-0000-0000-0000-000000000000';

                ;WITH DuplicateMappings AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [CompanyId], [UserId], [CommunicationChannelId]
                            ORDER BY [IsPreferred] DESC, [Created], [Id]) AS [RowNumber]
                    FROM [Admin].[UserCommunicationChannels]
                    WHERE [GcRecord] = 0
                )
                UPDATE ucc
                SET [GcRecord] = 1
                FROM [Admin].[UserCommunicationChannels] ucc
                INNER JOIN DuplicateMappings dm ON dm.[Id] = ucc.[Id]
                WHERE dm.[RowNumber] > 1;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CompanyId",
                schema: "Admin",
                table: "UserCommunicationChannels",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_UserCommunicationChannels_Companies_CompanyId",
                schema: "Admin",
                table: "UserCommunicationChannels",
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
                name: "FK_UserCommunicationChannels_Companies_CompanyId",
                schema: "Admin",
                table: "UserCommunicationChannels");

            migrationBuilder.DropIndex(
                name: "IX_UserCommunicationChannels_CompanyId_UserId_CommunicationChannelId",
                schema: "Admin",
                table: "UserCommunicationChannels");

            migrationBuilder.DropIndex(
                name: "IX_UserCommunicationChannels_UserId",
                schema: "Admin",
                table: "UserCommunicationChannels");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "Admin",
                table: "UserCommunicationChannels");

            migrationBuilder.RenameTable(
                name: "UserCommunicationChannels",
                schema: "Admin",
                newName: "UserCommunicationChannels",
                newSchema: "Messaging");

            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationUserId",
                schema: "Messaging",
                table: "UserCommunicationChannels",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCommunicationChannels_ApplicationUserId",
                schema: "Messaging",
                table: "UserCommunicationChannels",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCommunicationChannels_UserId_CommunicationChannelId",
                schema: "Messaging",
                table: "UserCommunicationChannels",
                columns: new[] { "UserId", "CommunicationChannelId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserCommunicationChannels_Users_ApplicationUserId",
                schema: "Messaging",
                table: "UserCommunicationChannels",
                column: "ApplicationUserId",
                principalSchema: "Security",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
