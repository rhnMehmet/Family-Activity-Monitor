namespace FamilyMonitor.Infrastructure.Games;

/// <summary>Only known metadata directories, no recursive payload watching. Events are debounced by discovery service.</summary>
public sealed class GameLibraryChangeMonitor(Func<IEnumerable<string>> directories,Action changed) : IDisposable
{
    private readonly List<FileSystemWatcher> watchers=[];
    private readonly object gate=new();
    private int generation;
    private bool disposed;
    public void Start(Func<bool>? allowed = null)
    {
        var current=Interlocked.Increment(ref generation);
        lock(gate){if(disposed)return;foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();}
        if(allowed?.Invoke()==false)return;
        foreach(var path in directories().Distinct(StringComparer.OrdinalIgnoreCase).Take(64))
        {
            if(current!=Volatile.Read(ref generation)||allowed?.Invoke()==false)return;
            if(!Directory.Exists(path))continue;
            try
            {
                var watcher=new FileSystemWatcher(path){IncludeSubdirectories=false,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite,InternalBufferSize=4096};
                watcher.Changed+=OnChange;watcher.Created+=OnChange;watcher.Deleted+=OnChange;watcher.Renamed+=OnChange;watcher.Error+=OnError;
                lock(gate)
                {
                    if(disposed||current!=generation||allowed?.Invoke()==false){watcher.Dispose();return;}
                    watcher.EnableRaisingEvents=true;watchers.Add(watcher);
                }
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException){ }
        }
    }
    private void OnChange(object sender,FileSystemEventArgs args)=>changed();
    private void OnError(object sender,ErrorEventArgs args)=>changed();
    public void Stop(){Interlocked.Increment(ref generation);lock(gate){foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();}}
    public void Dispose(){lock(gate)disposed=true;Stop();}
}
