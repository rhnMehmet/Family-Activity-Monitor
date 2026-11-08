using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using Microsoft.Data.Sqlite;

namespace FamilyMonitor.Infrastructure.Games;

public sealed class SqliteGameForegroundStore(string path, bool optimized = false) : IGameForegroundStore
{
    private SqliteConnection Open()
    {
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=optimized,DefaultTimeout=3}.ToString());
        try
        {
            connection.Open();using var command=connection.CreateCommand();
            command.CommandText="PRAGMA foreign_keys=ON;"+(optimized?"PRAGMA synchronous=FULL; PRAGMA wal_autocheckpoint=0;":"");command.ExecuteNonQuery();return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    public void SaveForeground(GameForegroundPeriod period)
    {using var connection=Open();using var transaction=connection.BeginTransaction();SaveRecord(connection,transaction,period);transaction.Commit();}
    internal static void SaveRecord(SqliteConnection connection,SqliteTransaction transaction,GameForegroundPeriod period)
    {
        if(period.Duration<=TimeSpan.Zero)throw new ArgumentException("Invalid foreground period.");
        using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="INSERT INTO game_foreground_periods VALUES($id,$game,$start,$end,$status) ON CONFLICT(id) DO UPDATE SET end_utc_ticks=excluded.end_utc_ticks,status=excluded.status;";
        command.Parameters.AddWithValue("$id",period.Id.ToString("D"));command.Parameters.AddWithValue("$game",period.InstallationId);
        command.Parameters.AddWithValue("$start",period.StartUtc.UtcTicks);command.Parameters.AddWithValue("$end",period.EndUtc.UtcTicks);command.Parameters.AddWithValue("$status",period.Status.ToString());command.ExecuteNonQuery();
    }
    public IReadOnlyList<GameForegroundPeriod> ReadForeground(DateTimeOffset fromUtc,DateTimeOffset toUtc)
    {
        using var connection=Open();using var command=connection.CreateCommand();
        command.CommandText="SELECT * FROM game_foreground_periods WHERE end_utc_ticks>$from AND start_utc_ticks<$to ORDER BY start_utc_ticks;";
        command.Parameters.AddWithValue("$from",fromUtc.UtcTicks);command.Parameters.AddWithValue("$to",toUtc.UtcTicks);
        using var reader=command.ExecuteReader();var result=new List<GameForegroundPeriod>();
        while(reader.Read())result.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),new(reader.GetInt64(2),TimeSpan.Zero),new(reader.GetInt64(3),TimeSpan.Zero),Enum.Parse<ActivityStatus>(reader.GetString(4))));return result;
    }
    public IReadOnlyDictionary<string,TimeSpan> GetForegroundDurations(DateTimeOffset fromUtc,DateTimeOffset toUtc)
    {
        using var connection=Open();using var command=connection.CreateCommand();command.CommandText="SELECT installation_id,SUM(MIN(end_utc_ticks,$to)-MAX(start_utc_ticks,$from)) FROM game_foreground_periods WHERE end_utc_ticks>$from AND start_utc_ticks<$to GROUP BY installation_id;";
        command.Parameters.AddWithValue("$from",fromUtc.UtcTicks);command.Parameters.AddWithValue("$to",toUtc.UtcTicks);
        using var reader=command.ExecuteReader();var result=new Dictionary<string,TimeSpan>();while(reader.Read())result[reader.GetString(0)]=TimeSpan.FromTicks(reader.GetInt64(1));return result;
    }
    public IReadOnlyList<GameForegroundPeriod> ReadByIds(IEnumerable<Guid> ids)
    {
        var keys=ids.Take(1024).ToArray(); if(keys.Length==0)return [];
        using var connection=Open();using var command=connection.CreateCommand();
        var parameters=keys.Select((id,i)=>{var name="$i"+i;command.Parameters.AddWithValue(name,id.ToString("D"));return name;});
        command.CommandText="SELECT * FROM game_foreground_periods WHERE id IN ("+string.Join(',',parameters)+");";
        using var reader=command.ExecuteReader();var result=new List<GameForegroundPeriod>();
        while(reader.Read())result.Add(new(Guid.Parse(reader.GetString(0)),reader.GetString(1),new(reader.GetInt64(2),TimeSpan.Zero),new(reader.GetInt64(3),TimeSpan.Zero),Enum.Parse<ActivityStatus>(reader.GetString(4))));return result;
    }
}
