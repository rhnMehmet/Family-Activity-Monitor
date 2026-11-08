using FamilyMonitor.Infrastructure.Windows;
using Xunit;

namespace FamilyMonitor.Tests;

public sealed class WindowActivitySourceTests
{
    private sealed class FakeApi : IWindowsActivityApi
    {
        public nint Window { get; set; } = 1;
        public uint ProcessId { get; set; } = 123;
        public string? ProcessName { get; set; } = "chrome";
        public bool DesktopAvailable { get; set; } = true;
        public TimeSpan GetIdleTime() => TimeSpan.FromSeconds(2);
        public bool IsInputDesktopAvailable() => DesktopAvailable;
        public nint GetForegroundWindow() => Window;
        public uint GetProcessId(nint window) => ProcessId;
        public string? GetProcessName(uint processId) => ProcessName;
    }

    [Theory]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("msedge", "Microsoft Edge")]
    [InlineData("firefox", "Mozilla Firefox")]
    public void BrowsersAreOnlyReportedAsApplications(string process, string application)
    {
        var sample = new WindowActivitySource(new FakeApi { ProcessName = process }).Read();
        Assert.Equal(application, sample.Application?.ApplicationName);
        Assert.Equal(process, sample.Application?.ProcessName);
    }

    [Fact]
    public void InvalidWindowReturnsUnavailable()
    {
        Assert.Null(new WindowActivitySource(new FakeApi { Window = 0 }).Read().Application);
    }

    [Fact]
    public void InvalidProcessIdReturnsUnavailable()
    {
        Assert.Null(new WindowActivitySource(new FakeApi { ProcessId = 0 }).Read().Application);
    }

    [Fact]
    public void ExitedOrInaccessibleProcessReturnsUnavailable()
    {
        Assert.Null(new WindowActivitySource(new FakeApi { ProcessName = null }).Read().Application);
    }

    [Fact]
    public void SecureDesktopReportsLocked()
    {
        Assert.True(new WindowActivitySource(new FakeApi { DesktopAvailable = false }).Read().IsSessionLocked);
    }
}
