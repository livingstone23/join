using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260409193000_RepairTicketComplexityColumns")]
    public partial class RepairTicketComplexityColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH('Messaging.TicketComplexities', 'ResolutionTimeUnits') IS NULL
                BEGIN
                    ALTER TABLE [Messaging].[TicketComplexities]
                    ADD [ResolutionTimeUnits] int NOT NULL DEFAULT (1);
                END;
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('Messaging.TicketComplexities', 'TimeUnitId') IS NULL
                BEGIN
                    ALTER TABLE [Messaging].[TicketComplexities]
                    ADD [TimeUnitId] uniqueidentifier NULL;
                END;
                """);

            migrationBuilder.Sql(
                """
                DECLARE @DefaultTimeUnitId uniqueidentifier;
                SELECT TOP (1) @DefaultTimeUnitId = [Id]
                FROM [Messaging].[TimeUnits]
                WHERE [GcRecord] = 0
                ORDER BY [Created], [Name];

                IF @DefaultTimeUnitId IS NULL
                BEGIN
                    SELECT TOP (1) @DefaultTimeUnitId = [Id]
                    FROM [Messaging].[TimeUnits]
                    ORDER BY [Created], [Name];

                    IF @DefaultTimeUnitId IS NOT NULL
                    BEGIN
                        UPDATE [Messaging].[TimeUnits]
                        SET [GcRecord] = 0,
                            [IsActive] = 1
                        WHERE [Id] = @DefaultTimeUnitId;
                    END;
                END;

                IF @DefaultTimeUnitId IS NULL
                BEGIN
                    SET @DefaultTimeUnitId = NEWID();

                    INSERT INTO [Messaging].[TimeUnits] ([Id], [Name], [Code], [IsActive], [Created], [CreatedBy], [GcRecord])
                    VALUES (@DefaultTimeUnitId, N'Hours', 1, 1, SYSUTCDATETIME(), N'Migration_Repair', 0);
                END;

                UPDATE tc
                SET [TimeUnitId] = @DefaultTimeUnitId
                FROM [Messaging].[TicketComplexities] tc
                WHERE tc.[TimeUnitId] IS NULL
                   OR tc.[TimeUnitId] = '00000000-0000-0000-0000-000000000000'
                   OR NOT EXISTS (
                        SELECT 1
                        FROM [Messaging].[TimeUnits] tu
                        WHERE tu.[Id] = tc.[TimeUnitId]);
                """);

            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE [name] = N'TimeUnitId'
                      AND [object_id] = OBJECT_ID(N'[Messaging].[TicketComplexities]')
                )
                BEGIN
                    ALTER TABLE [Messaging].[TicketComplexities]
                    ALTER COLUMN [TimeUnitId] uniqueidentifier NOT NULL;
                END;
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_TicketComplexities_TimeUnitId'
                      AND [object_id] = OBJECT_ID(N'[Messaging].[TicketComplexities]')
                )
                BEGIN
                    CREATE INDEX [IX_TicketComplexities_TimeUnitId]
                    ON [Messaging].[TicketComplexities] ([TimeUnitId]);
                END;
                """);

            migrationBuilder.Sql(
                """
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE [name] = N'FK_TicketComplexities_TimeUnits_TimeUnitId'
                      AND [parent_object_id] = OBJECT_ID(N'[Messaging].[TicketComplexities]')
                )
                BEGIN
                    ALTER TABLE [Messaging].[TicketComplexities] WITH CHECK
                    ADD CONSTRAINT [FK_TicketComplexities_TimeUnits_TimeUnitId]
                    FOREIGN KEY ([TimeUnitId]) REFERENCES [Messaging].[TimeUnits] ([Id]) ON DELETE NO ACTION;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.foreign_keys
                    WHERE [name] = N'FK_TicketComplexities_TimeUnits_TimeUnitId'
                      AND [parent_object_id] = OBJECT_ID(N'[Messaging].[TicketComplexities]')
                )
                BEGIN
                    ALTER TABLE [Messaging].[TicketComplexities]
                    DROP CONSTRAINT [FK_TicketComplexities_TimeUnits_TimeUnitId];
                END;
                """);

            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_TicketComplexities_TimeUnitId'
                      AND [object_id] = OBJECT_ID(N'[Messaging].[TicketComplexities]')
                )
                BEGIN
                    DROP INDEX [IX_TicketComplexities_TimeUnitId]
                    ON [Messaging].[TicketComplexities];
                END;
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('Messaging.TicketComplexities', 'TimeUnitId') IS NOT NULL
                BEGIN
                    ALTER TABLE [Messaging].[TicketComplexities]
                    DROP COLUMN [TimeUnitId];
                END;
                """);

            migrationBuilder.Sql(
                """
                IF COL_LENGTH('Messaging.TicketComplexities', 'ResolutionTimeUnits') IS NOT NULL
                BEGIN
                    ALTER TABLE [Messaging].[TicketComplexities]
                    DROP COLUMN [ResolutionTimeUnits];
                END;
                """);
        }
    }
}
