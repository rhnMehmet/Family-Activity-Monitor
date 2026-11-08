using FamilyMonitor.Core;

namespace FamilyMonitor.Infrastructure.Games;

public readonly record struct MetadataStamp(long Length, long LastWriteTicks);
/// <summary>Bounded 8 MiB LRU metadata/listing cache. Stamps, not content hashes, validate entries.</summary>
public sealed class CachingGameFileSystem(IGameFileSystem inner, IClock clock, long byteBudget=8*1024*1024) : IGameFileSystem
{
    private sealed record Entry(MetadataStamp Stamp, TimeSpan Added, object Value, long Bytes, LinkedListNode<string> Node);
    private readonly Dictionary<string,Entry> entries=new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> order=new(); private long bytes;
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    public long RetainedBytes => bytes;
    private T Read<T>(string kind,string path,Func<T> load,Func<T,long> size)
    {
        var stamp=inner.Stamp(path); if(stamp is null)return load(); var key=kind+"|"+path;
        if(entries.TryGetValue(key,out var cached) && cached.Stamp==stamp && clock.Elapsed-cached.Added<TimeSpan.FromMinutes(10))
        {order.Remove(cached.Node);order.AddLast(cached.Node);Hits++;return (T)cached.Value;}
        Misses++;var value=load();var length=size(value);
        if(entries.Remove(key,out var previous)){bytes-=previous.Bytes;order.Remove(previous.Node);}
        if(length>byteBudget)return value;
        while((bytes+length>byteBudget || entries.Count>=1024)&&order.First is { } first)
        {bytes-=entries[first.Value].Bytes;entries.Remove(first.Value);order.RemoveFirst();}
        var node=order.AddLast(key);entries[key]=new(stamp.Value,clock.Elapsed,value!,length,node);bytes+=length;return value;
    }
    public void Clear(){entries.Clear();order.Clear();bytes=0;}
    public string ReadText(string path)=>Read("text",path,()=>inner.ReadText(path),s=>s.Length*2L+128);
    public string[] Files(string directory,string pattern)=>Read("files:"+pattern,directory,()=>inner.Files(directory,pattern),a=>a.Sum(x=>x.Length*2L+32)+128).ToArray();
    public string[] Directories(string directory)=>Read("dirs",directory,()=>inner.Directories(directory),a=>a.Sum(x=>x.Length*2L+32)+128).ToArray();
    public bool FileExists(string path)=>inner.FileExists(path);
    public PathAvailability DirectoryState(string directory)=>inner.DirectoryState(directory);
    public MetadataStamp? Stamp(string path)=>inner.Stamp(path);
}
