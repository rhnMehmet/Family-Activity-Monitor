namespace FamilyMonitor.Infrastructure;

/// <summary>Serializes scans and deferred SQLite maintenance; normal durable writes never wait here.</summary>
public static class HeavyWorkCoordinator
{
    private static readonly object Gate = new();
    public static T Run<T>(Func<T> work) { lock (Gate) return work(); }
    public static void TryRun(Action work)
    {
        if (!Monitor.TryEnter(Gate)) return;
        try { work(); } finally { Monitor.Exit(Gate); }
    }
}
