using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JOIN.Persistence.Migrations
{
    /// <summary>
    /// SPEC 99-post: data migration that adds the internal channels <c>Web</c> (<c>WEB</c>) and <c>App</c> (<c>APP</c>).
    /// The full seed only runs when a migration is pending, so without this migration an existing database
    /// would never receive them. Each row is inserted only when no channel (active or deleted) already uses
    /// its name or its code: the unique index on <c>name</c> is not filtered by <c>gcrecord</c>, and a deleted
    /// <c>WEB</c> is restored by a SuperAdmin (SPEC 41-post), never re-created here. PostgreSQL SQL (SPEC 39 naming).
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
            // Only the rows this migration inserted, and only while nothing references them (the four FKs to the channel).
            migrationBuilder.Sql($"""
                DELETE FROM common.communicationchannels c
                WHERE c.createdby = '{MigrationUser}'
                  AND c.code IN ('WEB', 'APP')
                  AND NOT EXISTS (SELECT 1 FROM messaging.tickets t WHERE t.channelid = c.id)
                  AND NOT EXISTS (SELECT 1 FROM messaging.ticketcompanydefaults d WHERE d.channeldefaultid = c.id)
                  AND NOT EXISTS (SELECT 1 FROM support.ticketnotifications n WHERE n.communicationchannelid = c.id)
                  AND NOT EXISTS (SELECT 1 FROM admin.usercommunicationchannels u WHERE u.communicationchannelid = c.id);
                """);
        }

        private static void InsertChannel(MigrationBuilder migrationBuilder, string name, string code)
            => migrationBuilder.Sql($"""
                INSERT INTO common.communicationchannels (id, name, provider, code, isactive, created, createdby, gcrecord)
                SELECT gen_random_uuid(), '{name}', 'Internal', '{code}', TRUE, NOW(), '{MigrationUser}', 0
                WHERE NOT EXISTS (SELECT 1 FROM common.communicationchannels c WHERE c.name = '{name}' OR c.code = '{code}');
                """);
    }
}
