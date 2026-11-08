using System.Text.Json;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using Microsoft.Data.Sqlite;

namespace FamilyMonitor.Infrastructure.Games;

public sealed class SqliteGameCatalogRepository : IGameCatalogRepository
{
    private readonly string connectionString;
    private readonly bool optimized;
    public SqliteGameCatalogRepository(string path, bool optimized = false)
    {
        this.optimized = optimized;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Pooling = optimized, DefaultTimeout = 3 }.ToString();
        using var connection = Open();
        SqliteSchema.Initialize(connection);
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=ON;" + (optimized ? "PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=0;" : ""); command.ExecuteNonQuery(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    public IReadOnlyList<GameInstallation> GetInstallations()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH played AS (SELECT l.installation_id,MAX(a.end_utc_ticks) AS last_end FROM activity_game_links l JOIN activities a ON a.id=l.activity_id GROUP BY l.installation_id)
            SELECT g.id,g.game_id,COALESCE(n.name,g.name),g.platform,g.provider_id,g.scope,g.install_directory,
                g.executables_json,g.state,g.confirmed,g.package_family,
                p.last_end
            FROM game_installations g LEFT JOIN manual_game_names n ON n.installation_id=g.id LEFT JOIN played p ON p.installation_id=g.id ORDER BY g.name, g.platform, g.id;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<GameInstallation>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            Enum.Parse<GamePlatform>(reader.GetString(3)), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [], Enum.Parse<GameInstallationState>(reader.GetString(8)),
            reader.GetBoolean(9), reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : new DateTimeOffset(reader.GetInt64(11), TimeSpan.Zero)));
        return result;
    }

    public IReadOnlyList<ManualGameMapping> GetMappings()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT executable_path, installation_id FROM manual_game_mappings;";
        using var reader = command.ExecuteReader(); var result = new List<ManualGameMapping>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1)));
        return result;
    }

    public void ApplyScan(GameLibraryScan scan)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        foreach (var game in scan.Installations) Save(connection, transaction, game);
        transaction.Commit();
    }

    private static void Save(SqliteConnection connection, SqliteTransaction transaction, GameInstallation game)
    {
        if (string.IsNullOrWhiteSpace(game.Id) || string.IsNullOrWhiteSpace(game.Name)) throw new ArgumentException("Invalid game metadata.");
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO game_installations VALUES ($id,$game,$name,$platform,$provider,$scope,$directory,$exe,$state,$confirmed,$package)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name, install_directory=excluded.install_directory,
                executables_json=excluded.executables_json, state=excluded.state,
                confirmed=MAX(game_installations.confirmed,excluded.confirmed), package_family=excluded.package_family
            WHERE game_installations.name IS NOT excluded.name OR game_installations.install_directory IS NOT excluded.install_directory
                OR game_installations.executables_json IS NOT excluded.executables_json OR game_installations.state IS NOT excluded.state
                OR game_installations.confirmed<excluded.confirmed OR game_installations.package_family IS NOT excluded.package_family;
            """;
        command.Parameters.AddWithValue("$id", game.Id); command.Parameters.AddWithValue("$game", game.GameId);
        command.Parameters.AddWithValue("$name", game.Name); command.Parameters.AddWithValue("$platform", game.Platform.ToString());
        command.Parameters.AddWithValue("$provider", game.ProviderId); command.Parameters.AddWithValue("$scope", game.Scope);
        command.Parameters.AddWithValue("$directory", game.InstallDirectory);
        command.Parameters.AddWithValue("$exe", JsonSerializer.Serialize(game.ExecutablePaths.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)));
        command.Parameters.AddWithValue("$state", game.State.ToString()); command.Parameters.AddWithValue("$confirmed", game.IsConfirmed);
        command.Parameters.AddWithValue("$package", (object?)game.PackageFamilyName ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void SaveManualGame(GameInstallation installation, string executablePath)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        Save(connection, transaction, installation with { IsConfirmed = true });
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            command.CommandText = """
                INSERT INTO manual_game_mappings VALUES ($path,$id)
                ON CONFLICT(executable_path) DO UPDATE SET installation_id=excluded.installation_id;
                """;
            command.Parameters.AddWithValue("$path", GameIdentity.NormalizePath(executablePath));
            command.Parameters.AddWithValue("$id", installation.Id); command.ExecuteNonQuery();
        }
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO manual_game_names VALUES($id,$name) ON CONFLICT(installation_id) DO UPDATE SET name=excluded.name;";
        command.Parameters.AddWithValue("$id", installation.Id); command.Parameters.AddWithValue("$name", installation.Name);
        command.ExecuteNonQuery(); transaction.Commit();
    }

    public IReadOnlyList<GameUsage> GetUsage(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.game_id, COALESCE(MAX(n.name),MIN(g.name)), g.platform, SUM(MIN(a.end_utc_ticks,$to)-MAX(a.start_utc_ticks,$from)) AS used
            FROM activities a JOIN activity_game_links l ON l.activity_id=a.id JOIN game_installations g ON l.installation_id=g.id
            LEFT JOIN manual_game_names n ON n.installation_id=g.id
            WHERE a.end_utc_ticks>$from AND a.start_utc_ticks<$to
            GROUP BY g.game_id,g.platform ORDER BY used DESC,2;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.UtcTicks); command.Parameters.AddWithValue("$to", toUtc.UtcTicks);
        using var reader = command.ExecuteReader(); var result = new List<GameUsage>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), Enum.Parse<GamePlatform>(reader.GetString(2)), TimeSpan.FromTicks(reader.GetInt64(3))));
        return result;
    }

    public IReadOnlyList<GameUsageHistory> GetHistory(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(n.name,g.name),g.platform,a.id,a.device_id,a.local_session_id,a.application_name,a.process_name,
                a.start_utc_ticks,a.end_utc_ticks,a.duration_ticks,a.status,l.installation_id
            FROM activities a JOIN activity_game_links l ON l.activity_id=a.id JOIN game_installations g ON l.installation_id=g.id
            LEFT JOIN manual_game_names n ON n.installation_id=g.id
            WHERE a.end_utc_ticks>$from AND a.start_utc_ticks<$to ORDER BY a.start_utc_ticks DESC LIMIT 500;
            """;
        command.Parameters.AddWithValue("$from", fromUtc.UtcTicks); command.Parameters.AddWithValue("$to", toUtc.UtcTicks);
        using var reader = command.ExecuteReader(); var result = new List<GameUsageHistory>();
        while (reader.Read()) result.Add(new(reader.GetString(0), Enum.Parse<GamePlatform>(reader.GetString(1)),
            new ActivityRecord(Guid.Parse(reader.GetString(2)), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                new(reader.GetInt64(7), TimeSpan.Zero), new(reader.GetInt64(8), TimeSpan.Zero), TimeSpan.FromTicks(reader.GetInt64(9)),
                Enum.Parse<ActivityStatus>(reader.GetString(10)), reader.GetString(11))));
        return result;
    }
}
