using Microsoft.Data.Sqlite;

namespace FamilyMonitor.Infrastructure.Browsers;

public static class BrowserDatabaseUpgradeService
{
    public const int Version=4;
    public static bool EnsureReady(string path,Func<DatabaseUpgradePlan,bool> confirm)
    {
        path=Path.GetFullPath(path);var exists=File.Exists(path);var version=0;
        if(exists){using var source=Open(path,SqliteOpenMode.ReadOnly);using var command=source.CreateCommand();command.CommandText="PRAGMA user_version;";version=Convert.ToInt32(command.ExecuteScalar());}
        if(version>Version)throw new InvalidOperationException("Unsupported future database.");
        if(version==Version)return true;
        if(exists)
        {
            var backup=Path.Combine(Path.GetDirectoryName(path)!,"Backups",$"activity-before-2.0.0-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            if(!confirm(new(path,backup,version,Version)))return false;
            CopyVerified(path,backup);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection=Open(path,SqliteOpenMode.ReadWriteCreate);SqliteSchema.Initialize(connection);
        using var transaction=connection.BeginTransaction();using var create=connection.CreateCommand();create.Transaction=transaction;
        create.CommandText=DatabaseUpgradeService.Version3Sql+"""
            CREATE TABLE IF NOT EXISTS browser_visits(
                id TEXT PRIMARY KEY,activity_id TEXT NOT NULL REFERENCES activities(id) ON DELETE CASCADE,
                browser TEXT NOT NULL CHECK(browser IN ('Chrome','Edge','Firefox')),
                domain TEXT NOT NULL CHECK(length(domain) BETWEEN 1 AND 253),detail_url TEXT NULL CHECK(length(detail_url)<=1024),
                search_query TEXT NULL CHECK(length(search_query)<=240),search_engine TEXT NULL,
                start_utc_ticks INTEGER NOT NULL,end_utc_ticks INTEGER NOT NULL CHECK(end_utc_ticks>start_utc_ticks),
                status TEXT NOT NULL CHECK(status IN ('Active','Completed','Interrupted')),attribution TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_browser_time ON browser_visits(start_utc_ticks,end_utc_ticks);
            CREATE INDEX IF NOT EXISTS ix_browser_site ON browser_visits(browser,domain,end_utc_ticks);
            CREATE INDEX IF NOT EXISTS ix_browser_activity ON browser_visits(activity_id);
            PRAGMA user_version=4;
            """;
        create.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));create.ExecuteNonQuery();transaction.Commit();return true;
    }

    /// <summary>Creates a new verified file. Never overwrites/restores a live user database.</summary>
    public static void CopyVerified(string sourcePath,string destinationPath)
    {
        if(Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(destinationPath),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Distinct files required.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        using(var reserve=new FileStream(destinationPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){ }
        using var source=Open(sourcePath,SqliteOpenMode.ReadOnly);using var destination=Open(destinationPath,SqliteOpenMode.ReadWrite);
        source.BackupDatabase(destination);using var check=destination.CreateCommand();check.CommandText="PRAGMA quick_check;";
        if(!string.Equals(check.ExecuteScalar()?.ToString(),"ok",StringComparison.Ordinal))throw new IOException("Backup verification failed.");
        check.CommandText="PRAGMA foreign_key_check;";using var foreign=check.ExecuteReader();if(foreign.Read())throw new IOException("Backup foreign keys invalid; migration cancelled.");
    }
    private static SqliteConnection Open(string path,SqliteOpenMode mode)
    {var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=mode,Pooling=false,DefaultTimeout=3}.ToString());try{c.Open();return c;}catch{c.Dispose();throw;}}
}
