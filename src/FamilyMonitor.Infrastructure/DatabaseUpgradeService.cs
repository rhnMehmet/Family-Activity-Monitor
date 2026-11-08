using Microsoft.Data.Sqlite;

namespace FamilyMonitor.Infrastructure;

public sealed record DatabaseUpgradePlan(string DatabasePath,string BackupPath,int CurrentVersion,int TargetVersion);
public static class DatabaseUpgradeService
{
    public static bool EnsureReady(string path,Func<DatabaseUpgradePlan,bool> confirm)
    {
        path=Path.GetFullPath(path);var exists=File.Exists(path);var version=0;
        if(exists){using var source=Open(path,SqliteOpenMode.ReadOnly);using var command=source.CreateCommand();command.CommandText="PRAGMA user_version;";version=Convert.ToInt32(command.ExecuteScalar());}
        if(version>3)throw new InvalidOperationException("Unsupported future database.");
        if(version==3)return true;
        if(exists)
        {
            var backup=Path.Combine(Path.GetDirectoryName(path)!,"Backups",$"activity-before-1.2.1-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            if(!confirm(new(path,backup,version,3)))return false;
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            using(var reserve=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)){ }
            using(var source=Open(path,SqliteOpenMode.ReadOnly))using(var destination=Open(backup,SqliteOpenMode.ReadWrite))
            {source.BackupDatabase(destination);using var check=destination.CreateCommand();check.CommandText="PRAGMA quick_check;";if(!string.Equals(check.ExecuteScalar()?.ToString(),"ok",StringComparison.Ordinal))throw new IOException("Backup verification failed; migration cancelled.");}
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using(var connection=Open(path,SqliteOpenMode.ReadWriteCreate))
        {
            SqliteSchema.Initialize(connection);using var transaction=connection.BeginTransaction();using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText="""
                CREATE TABLE IF NOT EXISTS game_foreground_periods(id TEXT PRIMARY KEY,installation_id TEXT NOT NULL REFERENCES game_installations(id),
                    start_utc_ticks INTEGER NOT NULL,end_utc_ticks INTEGER NOT NULL CHECK(end_utc_ticks>start_utc_ticks),status TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_game_foreground_time ON game_foreground_periods(start_utc_ticks,end_utc_ticks);
                CREATE INDEX IF NOT EXISTS ix_game_foreground_installation ON game_foreground_periods(installation_id,end_utc_ticks);
                CREATE TABLE IF NOT EXISTS metric_metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                INSERT OR IGNORE INTO metric_metadata VALUES('foreground_capture_started_utc',$now);
                PRAGMA user_version=3;
                """;
            command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));command.ExecuteNonQuery();transaction.Commit();
        }
        return true;
    }
    private static SqliteConnection Open(string path,SqliteOpenMode mode){var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=mode,Pooling=false}.ToString());try{connection.Open();return connection;}catch{connection.Dispose();throw;}}
}
