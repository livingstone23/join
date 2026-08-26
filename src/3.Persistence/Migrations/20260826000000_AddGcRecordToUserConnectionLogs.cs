using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGcRecordToUserConnectionLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SPEC 30 (F1): Bring Security.UserConnectionLogs to soft-delete parity with the
            // rest of the Security.* schema. AddBaseAuditableEntity columns so that
            // RoleUserSessionRepository's GcRecord = 0 filter no longer trips SQL 207
            // ("Invalid column name 'GcRecord'"). All five columns are additive; no
            // existing column is dropped or altered.
            //
            // Backfill: GcRecord defaults to 0 and Created defaults to GETUTCDATE() via
            // DEFAULT constraint, so historical rows are stamped as "active at migration
            // time" without requiring a separate data migration.

            migrationBuilder.AddColumn<int>(
                name: "GcRecord",
                schema: "Security",
                table: "UserConnectionLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                schema: "Security",
                table: "UserConnectionLogs",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "Security",
                table: "UserConnectionLogs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModified",
                schema: "Security",
                table: "UserConnectionLogs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedBy",
                schema: "Security",
                table: "UserConnectionLogs",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastModifiedBy",
                schema: "Security",
                table: "UserConnectionLogs");

            migrationBuilder.DropColumn(
                name: "LastModified",
                schema: "Security",
                table: "UserConnectionLogs");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "Security",
                table: "UserConnectionLogs");

            migrationBuilder.DropColumn(
                name: "Created",
                schema: "Security",
                table: "UserConnectionLogs");

            migrationBuilder.DropColumn(
                name: "GcRecord",
                schema: "Security",
                table: "UserConnectionLogs");
        }
    }
}
