using System.Diagnostics;
using FamilyMonitor.Core;

namespace FamilyMonitor.Infrastructure;

public sealed class SystemClock : IClock
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeSpan Elapsed => stopwatch.Elapsed;
}
