using FamilyMonitor.Infrastructure;
using Xunit;

namespace FamilyMonitor.Tests;

public sealed class LocalSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "FamilyMonitor.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConsentDefaultsToFalseAndPersistsExplicitChoice()
    {
        var store = new LocalSettingsStore(directory);
        var settings = store.Load();
        Assert.False(settings.ConsentGranted);
        store.Save(settings with { ConsentGranted = true, PrivacyVersion = LocalSettingsStore.CurrentPrivacyVersion });
        Assert.True(store.Load().ConsentGranted);
        store.Save(store.Load() with { ConsentGranted = false });
        Assert.False(new LocalSettingsStore(directory).Load().ConsentGranted);
        Assert.Equal(settings.DeviceId, store.Load().DeviceId);
    }

    [Theory]
    [InlineData("{invalid json")]
    [InlineData("null")]
    [InlineData("{\"ConsentGranted\":true,\"PrivacyVersion\":0}")]
    [InlineData("{\"ConsentGranted\":true,\"PrivacyVersion\":1,\"IdleThresholdSeconds\":0}")]
    public void InvalidOrOutdatedSettingsFailClosed(string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), text);
        Assert.False(new LocalSettingsStore(directory).Load().ConsentGranted);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
