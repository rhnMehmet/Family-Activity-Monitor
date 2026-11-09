using Microsoft.Data.Sqlite;

namespace FamilyMonitor.Infrastructure;

internal static class SqliteSchema
{
    public const int CurrentVersion = 2;
    public static void Initialize(SqliteConnection connection)
    {
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());
        if (version > 4) throw new InvalidOperationException("Database schema is newer than this application.");
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS activities (
                id TEXT PRIMARY KEY, device_id TEXT NOT NULL, local_session_id TEXT NOT NULL,
                application_name TEXT NOT NULL, process_name TEXT NOT NULL,
                start_utc_ticks INTEGER NOT NULL, end_utc_ticks INTEGER NOT NULL,
                duration_ticks INTEGER NOT NULL CHECK(duration_ticks > 0),
                status TEXT NOT NULL CHECK(status IN ('Active','Completed','Interrupted')),
                CHECK(end_utc_ticks >= start_utc_ticks), CHECK(duration_ticks = end_utc_ticks - start_utc_ticks));
            CREATE INDEX IF NOT EXISTS ix_activities_time ON activities(start_utc_ticks, end_utc_ticks);
            CREATE TABLE IF NOT EXISTS game_installations (
                id TEXT PRIMARY KEY, game_id TEXT NOT NULL, name TEXT NOT NULL, platform TEXT NOT NULL,
                provider_id TEXT NOT NULL, scope TEXT NOT NULL, install_directory TEXT NOT NULL,
                executables_json TEXT NOT NULL, state TEXT NOT NULL, confirmed INTEGER NOT NULL,
                package_family TEXT NULL);
            CREATE TABLE IF NOT EXISTS manual_game_mappings (
                executable_path TEXT PRIMARY KEY COLLATE NOCASE,
                installation_id TEXT NOT NULL REFERENCES game_installations(id));
            CREATE TABLE IF NOT EXISTS manual_game_names (
                installation_id TEXT PRIMARY KEY REFERENCES game_installations(id), name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS activity_game_links (
                activity_id TEXT PRIMARY KEY REFERENCES activities(id) ON DELETE CASCADE,
                installation_id TEXT NOT NULL REFERENCES game_installations(id));
            CREATE INDEX IF NOT EXISTS ix_activity_game_installation ON activity_game_links(installation_id);
            """;
        command.ExecuteNonQuery();
        command.CommandText = """
            DELETE FROM activity_game_links WHERE activity_id NOT IN (SELECT id FROM activities);
            """;
        command.ExecuteNonQuery();
        if(version<2){command.CommandText="PRAGMA user_version=2;";command.ExecuteNonQuery();}
        transaction.Commit();
    }
}
