using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Windows;
using Xunit;

namespace FamilyMonitor.UiTests;

public sealed class NativeGameApiTests
{
    [Fact]
    public void ExecutableQueryWorksForOwnTestProcessAndInvalidProcessIsControlled()
    {
        // This is our test host, never the user's foreground app or installed game library.
        var api = new NativeWindowsActivityApi();
        var path = api.GetExecutablePath((uint)Environment.ProcessId);
        Assert.NotNull(path);
        Assert.True(GameIdentity.SamePath(Environment.ProcessPath!, path));
        _ = api.GetPackageFamilyName((uint)Environment.ProcessId);
        Assert.Null(api.GetExecutablePath(uint.MaxValue));
        Assert.Null(api.GetPackageFamilyName(uint.MaxValue));
    }
}
