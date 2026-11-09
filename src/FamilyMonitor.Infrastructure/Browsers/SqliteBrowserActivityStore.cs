using FamilyMonitor.Core;
using FamilyMonitor.Core.Browsers;
using Microsoft.Data.Sqlite;

namespace FamilyMonitor.Infrastructure.Browsers;

public sealed class SqliteBrowserActivityStore(string path) : IBrowserActivityStore
{
    private SqliteConnection Open()
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(path),Mode=SqliteOpenMode.ReadWrite,Pooling=true,DefaultTimeout=3}.ToString());
        try{c.Open();using var command=c.CreateCommand();command.CommandText="PRAGMA foreign_keys=ON;PRAGMA synchronous=FULL;PRAGMA wal_autocheckpoint=0;PRAGMA secure_delete=ON;";command.ExecuteNonQuery();return c;}catch{c.Dispose();throw;}
    }
    public void SaveBrowser(BrowserVisit visit)
    {using var c=Open();using var tx=c.BeginTransaction();SaveRecord(c,tx,visit);tx.Commit();}
    internal static void SaveRecord(SqliteConnection c,SqliteTransaction tx,BrowserVisit v)
    {
        if(v.Duration<=TimeSpan.Zero||v.Domain.Length>253||v.DetailUrl?.Length>1024||v.SearchQuery?.Length>240)throw new ArgumentException("Invalid browser visit.");
        using var command=c.CreateCommand();command.Transaction=tx;
        command.CommandText="""
            INSERT INTO browser_visits VALUES($id,$activity,$browser,$domain,$url,$query,$engine,$start,$end,$status,$attribution)
            ON CONFLICT(id) DO UPDATE SET start_utc_ticks=MAX(browser_visits.start_utc_ticks,excluded.start_utc_ticks),end_utc_ticks=excluded.end_utc_ticks,status=excluded.status;
            """;
        command.Parameters.AddWithValue("$id",v.Id.ToString("D"));command.Parameters.AddWithValue("$activity",v.ActivityId.ToString("D"));command.Parameters.AddWithValue("$browser",v.Browser.ToString());
        command.Parameters.AddWithValue("$domain",v.Domain);command.Parameters.AddWithValue("$url",(object?)v.DetailUrl??DBNull.Value);
        command.Parameters.AddWithValue("$query",(object?)v.SearchQuery??DBNull.Value);command.Parameters.AddWithValue("$engine",(object?)v.SearchEngine??DBNull.Value);
        command.Parameters.AddWithValue("$start",v.StartUtc.UtcTicks);command.Parameters.AddWithValue("$end",v.EndUtc.UtcTicks);
        command.Parameters.AddWithValue("$status",v.Status.ToString());command.Parameters.AddWithValue("$attribution",v.Attribution);command.ExecuteNonQuery();
    }
    public IReadOnlyList<BrowserVisit> ReadBrowser(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null,int limit=500)
    {
        using var c=Open();using var command=c.CreateCommand();
        command.CommandText="SELECT * FROM browser_visits WHERE end_utc_ticks>$from AND start_utc_ticks<$to AND ($browser IS NULL OR browser=$browser) ORDER BY start_utc_ticks DESC,id DESC LIMIT $limit;";
        AddBounds(command,from,to,browser);command.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,500));
        using var reader=command.ExecuteReader();var rows=new List<BrowserVisit>();while(reader.Read())rows.Add(Read(reader));return rows;
    }
    public IReadOnlyList<BrowserVisit> ReadByIds(IEnumerable<Guid> ids)
    {
        var keys=ids.Take(1024).ToArray();if(keys.Length==0)return [];
        using var c=Open();using var command=c.CreateCommand();
        var names=keys.Select((id,i)=>{var name="$i"+i;command.Parameters.AddWithValue(name,id.ToString("D"));return name;});
        command.CommandText="SELECT * FROM browser_visits WHERE id IN ("+string.Join(',',names)+");";
        using var reader=command.ExecuteReader();var rows=new List<BrowserVisit>();while(reader.Read())rows.Add(Read(reader));return rows;
    }
    private static BrowserVisit Read(SqliteDataReader r)=>new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),Enum.Parse<BrowserKind>(r.GetString(2)),r.GetString(3),
        r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),new(r.GetInt64(7),TimeSpan.Zero),new(r.GetInt64(8),TimeSpan.Zero),Enum.Parse<ActivityStatus>(r.GetString(9)),r.GetString(10));
    public IReadOnlyList<BrowserSiteUsage> GetBrowserUsage(DateTimeOffset from,DateTimeOffset to,BrowserKind? browser=null)
    {
        using var c=Open();using var command=c.CreateCommand();
        command.CommandText="SELECT browser,domain,SUM(MIN(end_utc_ticks,$to)-MAX(start_utc_ticks,$from)) AS used,COUNT(*) FROM browser_visits WHERE end_utc_ticks>$from AND start_utc_ticks<$to AND ($browser IS NULL OR browser=$browser) GROUP BY browser,domain ORDER BY used DESC;";
        AddBounds(command,from,to,browser);using var reader=command.ExecuteReader();var rows=new List<BrowserSiteUsage>();
        while(reader.Read())rows.Add(new(Enum.Parse<BrowserKind>(reader.GetString(0)),reader.GetString(1),TimeSpan.FromTicks(reader.GetInt64(2)),reader.GetInt32(3)));return rows;
    }
    private static void AddBounds(SqliteCommand command,DateTimeOffset from,DateTimeOffset to,BrowserKind? browser)
    {command.Parameters.AddWithValue("$from",from.UtcTicks);command.Parameters.AddWithValue("$to",to.UtcTicks);command.Parameters.AddWithValue("$browser",(object?)browser?.ToString()??DBNull.Value);}
    public void DeleteBrowser(){using var c=Open();using var command=c.CreateCommand();command.CommandText="DELETE FROM browser_visits;";command.ExecuteNonQuery();}
    public void PruneBrowser(DateTimeOffset before)
    {
        using var c=Open();using var tx=c.BeginTransaction();using var command=c.CreateCommand();command.Transaction=tx;
        command.CommandText="DELETE FROM browser_visits WHERE end_utc_ticks<=$before;UPDATE browser_visits SET start_utc_ticks=$before WHERE start_utc_ticks<$before AND end_utc_ticks>$before;";
        command.Parameters.AddWithValue("$before",before.UtcTicks);command.ExecuteNonQuery();tx.Commit();
    }
}
