using FamilyMonitor.Core;
using Microsoft.Data.Sqlite;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;

namespace FamilyMonitor.Infrastructure;

public sealed class SqliteActivityStore : IActivityStore
{
    private readonly string connectionString;
    private readonly string databasePath;
    private readonly bool optimized;
    public bool MaintenancePending { get; private set; }

    public SqliteActivityStore(string databasePath, bool optimized = false)
    {
        this.optimized=optimized;this.databasePath=Path.GetFullPath(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath, Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = optimized, DefaultTimeout = 3
        }.ToString();
        using var connection = Open();
        SqliteSchema.Initialize(connection);
        using var command = connection.CreateCommand();
        command.CommandText = (optimized?"PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=0;":"PRAGMA journal_mode=DELETE;") + """
            PRAGMA secure_delete = ON;
            UPDATE activities SET status = 'Interrupted' WHERE status = 'Active';
            """;
        command.ExecuteNonQuery();
        if(optimized)
        {
            command.CommandText="UPDATE game_foreground_periods SET status='Interrupted' WHERE status='Active';";
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA secure_delete = ON; PRAGMA foreign_keys = ON;";
            if(optimized)command.CommandText+="PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=0;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public void Save(ActivityRecord record) => SaveBatch([record],[]);

    public void SaveBatch(IReadOnlyList<ActivityRecord> records, IReadOnlyList<GameForegroundPeriod> presence)
    {
        if(records.Count==0 && presence.Count==0)return;
        using var connection=Open();using var transaction=connection.BeginTransaction();
        foreach(var record in records)SaveRecord(connection,transaction,record);
        foreach(var period in presence)SqliteGameForegroundStore.SaveRecord(connection,transaction,period);
        transaction.Commit();
    }

    private static void SaveRecord(SqliteConnection connection,SqliteTransaction transaction,ActivityRecord record)
    {
        if (record.ActiveDuration <= TimeSpan.Zero || record.EndUtc < record.StartUtc ||
            record.ActiveDuration != record.EndUtc - record.StartUtc)
            throw new ArgumentException("Invalid activity interval.", nameof(record));
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activities (id,device_id,local_session_id,application_name,process_name,start_utc_ticks,
                end_utc_ticks,duration_ticks,status)
            VALUES ($id,$device,$session,$app,$process,$start,$end,$duration,$status)
            ON CONFLICT(id) DO UPDATE SET end_utc_ticks=excluded.end_utc_ticks,
                duration_ticks=excluded.duration_ticks, status=excluded.status;
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("$device", record.DeviceId);
        command.Parameters.AddWithValue("$session", record.LocalSessionId);
        command.Parameters.AddWithValue("$app", record.ApplicationName);
        command.Parameters.AddWithValue("$process", record.ProcessName);
        command.Parameters.AddWithValue("$start", record.StartUtc.UtcTicks);
        command.Parameters.AddWithValue("$end", record.EndUtc.UtcTicks);
        command.Parameters.AddWithValue("$duration", record.ActiveDuration.Ticks);
        command.Parameters.AddWithValue("$status", record.Status.ToString());
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        if (record.GameInstallationId is not null)
        {
            command.CommandText = "INSERT INTO activity_game_links VALUES($id,$game) ON CONFLICT(activity_id) DO UPDATE SET installation_id=excluded.installation_id;";
            command.Parameters.AddWithValue("$game", record.GameInstallationId);
        }
        else command.CommandText = "DELETE FROM activity_game_links WHERE activity_id=$id;";
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ActivityRecord> Read(DateTimeOffset fromUtc, DateTimeOffset toUtc)
        => ReadInternal(fromUtc,toUtc,null);
    public IReadOnlyList<ActivityRecord> ReadRecent(DateTimeOffset fromUtc, DateTimeOffset toUtc,int limit=500)
        => ReadInternal(fromUtc,toUtc,Math.Clamp(limit,1,500));
    private IReadOnlyList<ActivityRecord> ReadInternal(DateTimeOffset fromUtc,DateTimeOffset toUtc,int? limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.id,a.device_id,a.local_session_id,a.application_name,a.process_name,a.start_utc_ticks,
                a.end_utc_ticks,a.duration_ticks,a.status,l.installation_id
            FROM activities a LEFT JOIN activity_game_links l ON l.activity_id=a.id
            WHERE end_utc_ticks > $from AND start_utc_ticks < $to ORDER BY start_utc_ticks,a.id;
            """;
        if(limit is not null)command.CommandText=command.CommandText.Replace("ORDER BY start_utc_ticks,a.id;","ORDER BY start_utc_ticks DESC,a.id DESC LIMIT $limit;");
        if(limit is not null)command.Parameters.AddWithValue("$limit",limit.Value);
        command.Parameters.AddWithValue("$from", fromUtc.UtcTicks);
        command.Parameters.AddWithValue("$to", toUtc.UtcTicks);
        using var reader = command.ExecuteReader();
        var result = new List<ActivityRecord>();
        while (reader.Read())
            result.Add(new ActivityRecord(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
                new DateTimeOffset(reader.GetInt64(6), TimeSpan.Zero), TimeSpan.FromTicks(reader.GetInt64(7)),
                Enum.Parse<ActivityStatus>(reader.GetString(8)), reader.IsDBNull(9) ? null : reader.GetString(9)));
        if(limit is not null)result.Reverse();
        return result;
    }

    public IReadOnlyList<ApplicationUsage> GetDailyUsage(DateOnly day, TimeZoneInfo timeZone)
    {
        var start = DayBoundary(day, timeZone);
        var end = DayBoundary(day.AddDays(1), timeZone);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT application_name, process_name,
                SUM(MIN(end_utc_ticks,$to) - MAX(start_utc_ticks,$from)) AS used
            FROM activities WHERE end_utc_ticks > $from AND start_utc_ticks < $to
            GROUP BY application_name, process_name ORDER BY used DESC, application_name;
            """;
        command.Parameters.AddWithValue("$from", start.UtcTicks);
        command.Parameters.AddWithValue("$to", end.UtcTicks);
        using var reader = command.ExecuteReader();
        var result = new List<ApplicationUsage>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), TimeSpan.FromTicks(reader.GetInt64(2))));
        return result;
    }

    private static DateTimeOffset DayBoundary(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A few time zones move clocks at midnight. Use the first existing instant of that day.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    public DateTimeOffset? GetLatestEnd()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = optimized ? "SELECT MAX(end_utc_ticks) FROM (SELECT end_utc_ticks FROM activities UNION ALL SELECT end_utc_ticks FROM game_foreground_periods);" : "SELECT MAX(end_utc_ticks) FROM activities;";
        var value = command.ExecuteScalar();
        return value is long ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    public void DeleteAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var foregroundSql="";
        using(var exists=connection.CreateCommand())
        {
            exists.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='game_foreground_periods';";
            if((long)exists.ExecuteScalar()!>0)foregroundSql="DELETE FROM game_foreground_periods;";
        }
        command.CommandText = "BEGIN IMMEDIATE;"+foregroundSql+" DELETE FROM activity_game_links; DELETE FROM activities; COMMIT;"+(optimized?"":" VACUUM;");
        command.ExecuteNonQuery();
        MaintenancePending=optimized;
    }

    public IReadOnlyList<ActivityRecord> ReadByIds(IEnumerable<Guid> ids)
    {
        var keys=ids.Take(1024).ToArray();if(keys.Length==0)return [];
        using var connection=Open();using var command=connection.CreateCommand();
        var names=keys.Select((id,i)=>{var key="$i"+i;command.Parameters.AddWithValue(key,id.ToString("D"));return key;}).ToArray();
        command.CommandText="SELECT a.id,a.device_id,a.local_session_id,a.application_name,a.process_name,a.start_utc_ticks,a.end_utc_ticks,a.duration_ticks,a.status,l.installation_id FROM activities a LEFT JOIN activity_game_links l ON l.activity_id=a.id WHERE a.id IN ("+string.Join(',',names)+");";
        using var reader=command.ExecuteReader();var result=new List<ActivityRecord>();
        while(reader.Read())result.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),new(reader.GetInt64(5),TimeSpan.Zero),new(reader.GetInt64(6),TimeSpan.Zero),TimeSpan.FromTicks(reader.GetInt64(7)),Enum.Parse<ActivityStatus>(reader.GetString(8)),reader.IsDBNull(9)?null:reader.GetString(9)));
        return result;
    }
    public void PerformMaintenance()
    {
        using var connection=Open();using var command=connection.CreateCommand();
        if(optimized){command.CommandText="PRAGMA wal_checkpoint(PASSIVE);";command.ExecuteNonQuery();}
        if(MaintenancePending)
        {
            command.CommandText="VACUUM;";command.ExecuteNonQuery();
            if(optimized)
            {
                command.CommandText="PRAGMA wal_checkpoint(TRUNCATE);";using var reader=command.ExecuteReader();
                MaintenancePending=reader.Read()&&reader.GetInt64(0)!=0;
            }
            else MaintenancePending=false;
        }
    }
    public bool CheckpointNeeded => optimized && File.Exists(databasePath+"-wal") && new FileInfo(databasePath+"-wal").Length>4*1024*1024;
}
