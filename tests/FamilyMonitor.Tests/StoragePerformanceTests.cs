using System.Security.Cryptography;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Games;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FamilyMonitor.Tests;

public sealed class StoragePerformanceTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"FamilyMonitor.PerformanceTests",Guid.NewGuid().ToString("N"));
    private string Database=>Path.Combine(root,"activity.db");
    private static readonly DateTimeOffset Start=new(2026,11,7,9,0,0,TimeSpan.Zero);
    private static ActivityRecord Record(int seconds=1)=>new(Guid.NewGuid(),"d","s","Synthetic","runtime",Start,Start.AddSeconds(seconds),TimeSpan.FromSeconds(seconds),ActivityStatus.Active);
    private SqliteActivityStore Optimized(){Assert.True(DatabaseUpgradeService.EnsureReady(Database,_=>throw new InvalidOperationException("New test database requires no approval.")));return new(Database,true);}

    [Fact]
    public void BundledSqliteHasWalResetFixAndWalUsesFullDurability()
    {
        var store=Optimized();
        // Inspect the actual configured connection, since synchronous/autocheckpoint are connection-local.
        using var connection=(SqliteConnection)typeof(SqliteActivityStore).GetMethod("Open",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(store,null)!;
        using var command=connection.CreateCommand();command.CommandText="SELECT sqlite_version();";
        var version=Version.Parse((string)command.ExecuteScalar()!);Assert.True(version>=new Version(3,51,3),$"SQLite {version} requires WAL-reset patch review.");
        command.CommandText="PRAGMA journal_mode;";Assert.Equal("wal",command.ExecuteScalar());
        command.CommandText="PRAGMA synchronous;";Assert.Equal(2L,command.ExecuteScalar());
        command.CommandText="PRAGMA wal_autocheckpoint;";Assert.Equal(0L,command.ExecuteScalar());
    }

    [Fact]
    public void DecliningMigrationLeavesExistingDatabaseByteIdentical()
    {
        var old=new SqliteActivityStore(Database);old.Save(Record());var hash=SHA256.HashData(File.ReadAllBytes(Database));var calls=0;
        Assert.False(DatabaseUpgradeService.EnsureReady(Database,plan=>{calls++;Assert.Equal(2,plan.CurrentVersion);return false;}));
        Assert.Equal(1,calls);Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(Database)));Assert.False(Directory.Exists(Path.Combine(root,"Backups")));
    }

    [Fact]
    public void ApprovedMigrationVerifiesVersionTwoBackupAndPreservesHistoryCatalogAndSettings()
    {
        var old=new SqliteActivityStore(Database);var record=Record();old.Save(record);var catalog=new SqliteGameCatalogRepository(Database);var game=Game();catalog.SaveManualGame(game,game.ExecutablePaths[0]);
        var settingsStore=new LocalSettingsStore(root);var settings=new LocalSettings{ConsentGranted=true,PrivacyVersion=1,GameDiscoveryConsentGranted=true,GameDiscoveryPrivacyVersion=1};settingsStore.Save(settings);
        DatabaseUpgradePlan? approved=null;Assert.True(DatabaseUpgradeService.EnsureReady(Database,plan=>{approved=plan;Assert.False(File.Exists(plan.BackupPath));return true;}));
        Assert.NotNull(approved);Assert.Equal(2,Scalar(approved.BackupPath,"PRAGMA user_version;"));Assert.Equal(3,Scalar(Database,"PRAGMA user_version;"));
        Assert.Equal(1,Scalar(approved.BackupPath,"SELECT COUNT(*) FROM activities;"));Assert.Equal(1,Scalar(Database,"SELECT COUNT(*) FROM activities;"));
        Assert.Equal(game.Id,Assert.Single(catalog.GetInstallations()).Id);Assert.Single(catalog.GetMappings());Assert.Equal(settings.DeviceId,settingsStore.Load().DeviceId);Assert.True(settingsStore.Load().ConsentGranted);
        Assert.True(DatabaseUpgradeService.EnsureReady(Database,_=>throw new InvalidOperationException("Already upgraded.")));
    }

    [Fact]
    public void FutureDatabaseIsRejectedWithoutApprovalOrChanges()
    {
        _=new SqliteActivityStore(Database);Execute("PRAGMA user_version=99;");var hash=SHA256.HashData(File.ReadAllBytes(Database));
        Assert.Throws<InvalidOperationException>(()=>DatabaseUpgradeService.EnsureReady(Database,_=>throw new Exception()));Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(Database)));
    }

    [Fact]
    public void BufferCoalescesOneHundredSamplesIntoOneDurableTransactionAndReadsPendingTotals()
    {
        var persistent=Optimized();var clock=new FakeClock();using var buffer=new BufferedActivityStore(persistent,clock,startWorker:false);var row=Record();
        for(var i=1;i<=100;i++)buffer.Save(row with{EndUtc=Start.AddSeconds(i),ActiveDuration=TimeSpan.FromSeconds(i)});
        Assert.Equal(1,buffer.PendingCount);Assert.Empty(persistent.Read(Start,Start.AddDays(1)));
        Assert.Equal(100,Assert.Single(buffer.GetDailyUsage(new(2026,11,7),TimeZoneInfo.Utc)).Duration.TotalSeconds);
        clock.Advance(5);buffer.FlushIfDue();Assert.Equal(1,buffer.Transactions);Assert.Equal(0,buffer.PendingCount);Assert.Equal(100,Assert.Single(persistent.Read(Start,Start.AddDays(1))).ActiveDuration.TotalSeconds);
        var latest=row with{EndUtc=Start.AddSeconds(100),ActiveDuration=TimeSpan.FromSeconds(100)};buffer.Save(latest);Assert.Equal(0,buffer.PendingCount);
        Assert.Equal(100,Assert.Single(buffer.GetDailyUsage(new(2026,11,7),TimeZoneInfo.Utc)).Duration.TotalSeconds);
    }

    [Fact]
    public void PendingOverlaySubtractsPersistedVersionExactlyOnce()
    {
        var persistent=Optimized();using var buffer=new BufferedActivityStore(persistent,new FakeClock(),startWorker:false);var row=Record(3);buffer.Save(row);buffer.Flush();
        buffer.Save(row with{EndUtc=Start.AddSeconds(8),ActiveDuration=TimeSpan.FromSeconds(8)});
        Assert.Equal(8,Assert.Single(buffer.GetDailyUsage(new(2026,11,7),TimeZoneInfo.Utc)).Duration.TotalSeconds);buffer.Flush();
        Assert.Equal(8,Assert.Single(buffer.GetDailyUsage(new(2026,11,7),TimeZoneInfo.Utc)).Duration.TotalSeconds);
    }

    [Fact]
    public void CloseDuringGameModeFlushesBothMetricsAndRestartKeepsThem()
    {
        var persistent=Optimized();var catalog=new SqliteGameCatalogRepository(Database,true);var game=Game();catalog.ApplyScan(new("Test",[game],[]));var presence=new SqliteGameForegroundStore(Database,true);
        var clock=new FakeClock();var source=new FakeSource{Application=new("Game","runtime",1,GameInstallationId:game.Id,GameName:game.Name)};
        using(var buffer=new BufferedActivityStore(persistent,clock,presence,()=>false,startWorker:false))
        {
            var tracker=new ActivityTracker(clock,source,buffer,"d","s",foregroundStore:buffer);tracker.GrantConsent();tracker.Start();clock.Advance(3);tracker.Tick();tracker.Stop();
        }
        var reopened=new SqliteActivityStore(Database,true);
        Assert.Equal(3,Assert.Single(reopened.Read(Start,Start.AddDays(1))).ActiveDuration.TotalSeconds);Assert.Equal(3,Assert.Single(presence.ReadForeground(Start,Start.AddDays(1))).Duration.TotalSeconds);
        Assert.Equal(ActivityStatus.Completed,Assert.Single(presence.ReadForeground(Start,Start.AddDays(1))).Status);
    }

    [Fact]
    public void CrashModelRetainsCommittedSnapshotAndMarksBothMetricsInterrupted()
    {
        var persistent=Optimized();var catalog=new SqliteGameCatalogRepository(Database,true);var game=Game();catalog.ApplyScan(new("Test",[game],[]));var presence=new SqliteGameForegroundStore(Database,true);
        var clock=new FakeClock();using var buffer=new BufferedActivityStore(persistent,clock,presence,()=>false,startWorker:false);var row=Record(5) with{GameInstallationId=game.Id};var fg=new GameForegroundPeriod(Guid.NewGuid(),game.Id,Start,Start.AddSeconds(5),ActivityStatus.Active);
        buffer.Save(row);buffer.SaveForeground(fg);clock.Advance(5);buffer.FlushIfDue();
        buffer.Save(row with{EndUtc=Start.AddSeconds(9),ActiveDuration=TimeSpan.FromSeconds(9)});buffer.SaveForeground(fg with{EndUtc=Start.AddSeconds(9)});
        // Backup API copies only committed SQLite data, modelling an abrupt process loss without killing any user process.
        var crash=Path.Combine(root,"crash.db");using(var source=Open(Database))using(var target=Open(crash))source.BackupDatabase(target);
        var restarted=new SqliteActivityStore(crash,true);Assert.Equal(5,Assert.Single(restarted.Read(Start,Start.AddDays(1))).ActiveDuration.TotalSeconds);
        var recovered=Assert.Single(new SqliteGameForegroundStore(crash).ReadForeground(Start,Start.AddDays(1)));Assert.Equal(5,recovered.Duration.TotalSeconds);Assert.Equal(ActivityStatus.Interrupted,recovered.Status);
    }

    [Fact]
    public void DeleteClearsBufferedAndPersistedMetricsButKeepsCatalogMappingsAndBackup()
    {
        var old=new SqliteActivityStore(Database);old.Save(Record());DatabaseUpgradePlan? plan=null;DatabaseUpgradeService.EnsureReady(Database,p=>{plan=p;return true;});
        var persistent=new SqliteActivityStore(Database,true);var catalog=new SqliteGameCatalogRepository(Database,true);var game=Game();catalog.SaveManualGame(game,game.ExecutablePaths[0]);var presence=new SqliteGameForegroundStore(Database,true);
        using(var buffer=new BufferedActivityStore(persistent,new FakeClock(),presence,()=>false,startWorker:false))
        {
            buffer.Save(Record(2) with{GameInstallationId=game.Id});buffer.SaveForeground(new(Guid.NewGuid(),game.Id,Start,Start.AddSeconds(3),ActivityStatus.Active));
            buffer.DeleteAll();buffer.Flush();Assert.Empty(buffer.ReadForeground(Start,Start.AddDays(1)));Assert.Empty(buffer.Read(Start,Start.AddDays(1)));Assert.Equal(0,buffer.PendingCount);
        }
        Assert.Empty(new SqliteActivityStore(Database,true).Read(Start,Start.AddDays(1)));Assert.Single(catalog.GetInstallations());Assert.Single(catalog.GetMappings());Assert.True(File.Exists(plan!.BackupPath));Assert.True(persistent.MaintenancePending);
    }

    [Fact]
    public void InvalidBatchRollsBackAllActivityAndForegroundRows()
    {
        var persistent=Optimized();var valid=Record();var invalid=Record() with{ActiveDuration=TimeSpan.Zero};
        Assert.Throws<ArgumentException>(()=>persistent.SaveBatch([valid,invalid],[]));Assert.Empty(persistent.Read(Start,Start.AddDays(1)));
        Assert.Throws<SqliteException>(()=>persistent.SaveBatch([valid],[new(Guid.NewGuid(),"missing-installation",Start,Start.AddSeconds(1),ActivityStatus.Active)]));Assert.Empty(persistent.Read(Start,Start.AddDays(1)));
    }

    [Fact]
    public void FailedDeletionKeepsPendingDataUntilDatabaseBecomesWritable()
    {
        var persistent=Optimized();using var buffer=new BufferedActivityStore(persistent,new FakeClock(),startWorker:false);buffer.Save(Record(2));
        using(var connection=Open(Database))using(var transaction=connection.BeginTransaction())
        {Assert.Throws<SqliteException>(buffer.DeleteAll);Assert.Equal(1,buffer.PendingCount);transaction.Rollback();}
        buffer.Flush();Assert.Equal(2,Assert.Single(persistent.Read(Start,Start.AddDays(1))).ActiveDuration.TotalSeconds);
    }

    [Fact]
    public void DeletedDataMaintenanceWaitsForPolicyAndTruncatesWalAfterward()
    {
        var persistent=Optimized();var clock=new FakeClock();var allowed=false;using var buffer=new BufferedActivityStore(persistent,clock,mayMaintain:()=>allowed,startWorker:false);
        buffer.Save(Record(5));buffer.Flush();buffer.DeleteAll();clock.Advance(5);buffer.FlushIfDue();Assert.True(persistent.MaintenancePending);
        allowed=true;clock.Advance(5);buffer.FlushIfDue();Assert.False(persistent.MaintenancePending);
        Assert.True(!File.Exists(Database+"-wal")||new FileInfo(Database+"-wal").Length==0);Assert.Empty(buffer.Read(Start,Start.AddDays(1)));
    }

    [Fact]
    public void TenThousandRowsUseSingleBatchAndRecentQueryIsBounded()
    {
        var persistent=Optimized();var rows=Enumerable.Range(0,10000).Select(i=>Record() with{StartUtc=Start.AddSeconds(i),EndUtc=Start.AddSeconds(i+1),Status=ActivityStatus.Completed}).ToArray();
        persistent.SaveBatch(rows,[]);Assert.Equal(10000,Scalar(Database,"SELECT COUNT(*) FROM activities;"));
        var recent=persistent.ReadRecent(Start,Start.AddDays(1));Assert.Equal(500,recent.Count);Assert.Equal(Start.AddSeconds(9500),recent[0].StartUtc);
        Assert.Equal(10000,Assert.Single(persistent.GetDailyUsage(new(2026,11,7),TimeZoneInfo.Utc)).Duration.TotalSeconds);
    }

    [Fact]
    public void QueueHasHardBoundInsteadOfUnboundedMemoryGrowth()
    {
        var persistent=Optimized();using var buffer=new BufferedActivityStore(persistent,new FakeClock(),mayMaintain:()=>false,startWorker:false);
        for(var i=0;i<1024;i++)buffer.Save(Record());Assert.Equal(1024,buffer.PendingCount);Assert.Throws<IOException>(()=>buffer.Save(Record()));
    }

    [Fact]
    public void ForegroundAggregatesMergePendingWithoutDoubleCountingAndOldMetricsAreNotInvented()
    {
        var persistent=Optimized();var catalog=new SqliteGameCatalogRepository(Database,true);var game=Game();catalog.ApplyScan(new("Test",[game],[]));var foreground=new SqliteGameForegroundStore(Database,true);
        using var buffer=new BufferedActivityStore(persistent,new FakeClock(),foreground,startWorker:false);
        persistent.Save(Record(3) with{GameInstallationId=game.Id});var service=new GameUsageService(catalog,buffer);
        Assert.Null(Assert.Single(service.GetUsage(new(2026,11,7),false,TimeZoneInfo.Utc)).ForegroundDuration);
        var p=new GameForegroundPeriod(Guid.NewGuid(),game.Id,Start,Start.AddSeconds(5),ActivityStatus.Active);buffer.SaveForeground(p);buffer.Flush();buffer.SaveForeground(p with{EndUtc=Start.AddSeconds(8)});
        var row=Assert.Single(service.GetUsage(new(2026,11,7),false,TimeZoneInfo.Utc));Assert.Equal(3,row.Duration.TotalSeconds);Assert.Equal(8,row.ForegroundDuration!.Value.TotalSeconds);
    }

    private GameInstallation Game()=>GameIdentity.Create("synthetic","Synthetic",GamePlatform.Custom,"Test",root,Path.Combine(root,"game"),[Path.Combine(root,"game","runtime.exe")],GameInstallationState.Installed);
    private static SqliteConnection Open(string path){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());c.Open();return c;}
    private static long Scalar(string path,string sql){using var c=Open(path);using var command=c.CreateCommand();command.CommandText=sql;return Convert.ToInt64(command.ExecuteScalar());}
    private void Execute(string sql){using var c=Open(Database);using var command=c.CreateCommand();command.CommandText=sql;command.ExecuteNonQuery();}
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();var full=Path.GetFullPath(root);var parent=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FamilyMonitor.PerformanceTests"))+Path.DirectorySeparatorChar;
        if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe test cleanup.");if(Directory.Exists(full))Directory.Delete(full,true);
    }
}
