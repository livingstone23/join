using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// SPEC 99: data migration that adds the internal channels <c>Web</c> (<c>WEB</c>) and <c>App</c> (<c>APP</c>).
    /// The full seed only runs when a migration is pending, so without this migration an existing database
    /// would never receive them. Each row is inserted only when no channel (active or deleted) already uses
    /// its name or its code: the unique index on <c>Name</c> is not filtered by <c>GcRecord</c>, and a deleted
    /// <c>WEB</c> is restored by a SuperAdmin (SPEC 41), never re-created here.
    /// </summary>
    public partial class Spec99InternalCommunicationChannels : Migration
    {
        private const string MigrationUser = "Spec99_Migration";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            InsertChannel(migrationBuilder, "Web", "WEB");
            InsertChannel(migrationBuilder, "App", "APP");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the rows this migration inserted, and only while nothing references them.
            migrationBuilder.Sql($"""
                DELETE cc FROM [Common].[CommunicationChannels] cc
                WHERE cc.[CreatedBy] = '{MigrationUser}'
                  AND cc.[Code] IN ('WEB', 'APP')
                  AND NOT EXISTS (SELECT 1 FROM [Messaging].[Tickets] t WHERE t.[ChannelId] = cc.[Id])
                  AND NOT EXISTS (SELECT 1 FROM [Messaging].[TicketCompanyDefaults] d WHERE d.[ChannelDefaultId] = cc.[Id])
                  AND NOT EXISTS (SELECT 1 FROM [Support].[TicketNotifications] n WHERE n.[CommunicationChannelId] = cc.[Id])
                  AND NOT EXISTS (SELECT 1 FROM [Admin].[UserCommunicationChannels] u WHERE u.[CommunicationChannelId] = cc.[Id]);
                """);
        }

        private static void InsertChannel(MigrationBuilder migrationBuilder, string name, string code)
            => migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM [Common].[CommunicationChannels] WHERE [Name] = '{name}' OR [Code] = '{code}')
                    INSERT INTO [Common].[CommunicationChannels] ([Id], [Name], [Provider], [Code], [IsActive], [Created], [CreatedBy], [GcRecord])
                    VALUES (NEWID(), '{name}', 'Internal', '{code}', 1, SYSUTCDATETIME(), '{MigrationUser}', 0);
                """);
    }
}
