using System.Diagnostics;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using Xunit;

namespace FamilyMonitor.UiTests;

public sealed class GamePagesTests
{
    [Fact]
    public void GameTabsAreAddedWithoutReplacingExistingTabsAndPermissionIsSeparate() => MainFormTests.OnUiThread(() =>
    {
        using var f = new UiFixture(withGames: true); f.Form.Show(); f.Form.RefreshView();
        Assert.Equal("Family Activity Monitor — 2.0.0-prototype", f.Form.Text);
        var tabs = f.Control<TabControl>("mainTabs"); Assert.Equal(5, tabs.TabCount);
        Assert.Contains(tabs.TabPages.Cast<TabPage>(), x => x.Text == "Uygulama süreleri");
        tabs.SelectedTab = f.Control<TabPage>("gameLibrary");
        Assert.False(f.GameDiscovery!.HasConsent); Assert.True(f.Tracker.HasConsent);
        Assert.False(f.Control<Button>("refreshGames").Enabled); Assert.False(f.Control<Button>("manualGame").Enabled);
        f.Control<Button>("gameConsent").PerformClick(); Assert.Equal(1, f.Dialogs.GameConsentRequests);
        Assert.False(f.GameDiscovery.HasConsent); Assert.False(f.SettingsStore.Load().GameDiscoveryConsentGranted);
        f.Dialogs.GameConsentResponse = true; f.Control<Button>("gameConsent").PerformClick();
        Assert.True(f.GameDiscovery.HasConsent); Assert.True(f.SettingsStore.Load().GameDiscoveryConsentGranted);
        Assert.True(f.Control<Button>("refreshGames").Enabled); Assert.False(f.Tracker.IsEnabled);
    });

    [Fact]
    public void RevokeGamePermissionStopsTrackingAndKeepsOrdinaryConsentAndCatalog() => MainFormTests.OnUiThread(() =>
    {
        using var f = new UiFixture(withGames: true); f.Form.Show(); f.Form.RefreshView();
        f.Control<TabControl>("mainTabs").SelectedTab = f.Control<TabPage>("gameLibrary");
        f.Dialogs.GameConsentResponse = true; f.Control<Button>("gameConsent").PerformClick();
        var game = Game(f.DirectoryPath); f.GameRepository!.ApplyScan(new("Test", [game], []));
        f.Use(2); f.Control<Button>("gameConsent").PerformClick();
        Assert.False(f.GameDiscovery!.HasConsent); Assert.False(f.Tracker.IsEnabled);
        Assert.True(f.Tracker.HasConsent); Assert.Single(f.GameRepository.GetInstallations());
        Assert.Equal("İzleme: Kapalı", f.Control<Label>("trackingStatus").Text);
        Assert.False(f.SettingsStore.Load().GameDiscoveryConsentGranted);
    });

    [Fact]
    public void GeneralConsentWithdrawalAlsoRevokesGameDiscovery() => MainFormTests.OnUiThread(() =>
    {
        using var f = new UiFixture(withGames: true); f.Form.Show(); f.Form.RefreshView();
        f.Control<TabControl>("mainTabs").SelectedTab = f.Control<TabPage>("gameLibrary");
        f.Dialogs.GameConsentResponse = true; f.Control<Button>("gameConsent").PerformClick(); f.Use(2);
        f.ShowSettings();
        MainFormTests.Descendants(f.Form).OfType<Button>().Single(x => x.Text == "İzni geri çek").PerformClick();
        Assert.False(f.Tracker.HasConsent); Assert.False(f.Tracker.IsEnabled); Assert.False(f.GameDiscovery!.HasConsent);
        Assert.False(f.SettingsStore.Load().ConsentGranted); Assert.False(f.SettingsStore.Load().GameDiscoveryConsentGranted);
    });

    [Fact]
    public void GameStatisticsAndHistoryUseExistingRecordsAndClearTogether() => MainFormTests.OnUiThread(() =>
    {
        using var f = new UiFixture(withGames: true); var game = Game(f.DirectoryPath);
        f.GameRepository!.ApplyScan(new("Test", [game], [])); var start = f.Clock.UtcNow;
        f.Store.Save(new(Guid.NewGuid(), "d", "s", game.Name, "runtime", start, start.AddSeconds(7), TimeSpan.FromSeconds(7), ActivityStatus.Completed, game.Id));
        f.Form.Show(); f.Form.RefreshView(); f.Control<TabControl>("mainTabs").SelectedTab = f.Control<TabPage>("gameStatistics");
        Assert.Single(f.Control<DataGridView>("gameUsage").Rows.Cast<DataGridViewRow>());
        Assert.Single(f.Control<DataGridView>("gameHistory").Rows.Cast<DataGridViewRow>());
        Assert.Contains("00:00:07", f.Control<Label>("selectedGameTotal").Text);
        Assert.Contains("00:00:07", f.Control<Label>("dailyTotal").Text);
        var artifactDirectory = Environment.GetEnvironmentVariable("FAMILYMONITOR_UI_ARTIFACTS");
        if (artifactDirectory is not null)
        {
            Directory.CreateDirectory(artifactDirectory); using var bitmap = new Bitmap(f.Form.Width, f.Form.Height);
            f.Form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(artifactDirectory, "game-statistics.png"));
        }
        f.Control<ComboBox>("gamePeriod").SelectedIndex = 1;
        Assert.Contains("haftanın", f.Control<Label>("selectedGameTotal").Text);
        f.ShowSettings(); f.Dialogs.ConfirmDeletion = true; f.Control<Button>("deleteUsageHistory").PerformClick();
        Assert.Empty(f.Control<DataGridView>("gameUsage").Rows.Cast<DataGridViewRow>());
        Assert.Empty(f.Control<DataGridView>("gameHistory").Rows.Cast<DataGridViewRow>());
        Assert.Single(f.GameRepository.GetInstallations());
    });

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void GameControlsFitScaledMinimumWindow(float scale) => MainFormTests.OnUiThread(() =>
    {
        using var f = new UiFixture(withGames: true); var form = f.Form; form.Size = form.MinimumSize;
        if (scale != 1)
        {
            var fonts = MainFormTests.Descendants(form).Prepend(form).Select(c => (c, c.Font)).ToArray(); form.Scale(new SizeF(scale, scale));
            foreach (var (control, font) in fonts) control.Font = new Font(font.FontFamily, font.SizeInPoints * scale, font.Style);
        }
        f.Control<TabControl>("mainTabs").SelectedTab = f.Control<TabPage>("gameLibrary"); form.Show(); form.PerformLayout();
        foreach (var name in new[] { "gameConsent", "refreshGames", "manualGame", "gameFolders" })
        {
            var control = f.Control<Button>(name); var rectangle = control.ClientRectangle; Control child = control;
            for (var parent = control.Parent; parent is not null; parent = parent.Parent)
            { rectangle.Offset(child.Left, child.Top); Assert.True(parent.ClientRectangle.Contains(rectangle), $"{name}: {rectangle} vs {parent.ClientRectangle}"); child = parent; }
        }
        var folder = Environment.GetEnvironmentVariable("FAMILYMONITOR_UI_ARTIFACTS");
        if (folder is not null)
        {
            Directory.CreateDirectory(folder); using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, $"games-{(int)(scale * 100)}.png"));
        }
    });

    [Fact]
    public void RefreshButtonExecutesOnlyAfterConsent() => MainFormTests.OnUiThread(() =>
    {
        using var f = new UiFixture(withGames: true); f.Form.Show(); f.Form.RefreshView();
        f.Control<TabControl>("mainTabs").SelectedTab = f.Control<TabPage>("gameLibrary");
        Assert.Null(f.GameDiscovery!.LastAttemptUtc);
        f.Dialogs.GameConsentResponse = true; f.Control<Button>("gameConsent").PerformClick();
        f.Control<Button>("refreshGames").PerformClick();
        var deadline = Stopwatch.StartNew();
        while (!f.Control<Button>("refreshGames").Enabled && deadline.Elapsed < TimeSpan.FromSeconds(5)) { Application.DoEvents(); Thread.Yield(); }
        Assert.True(f.Control<Button>("refreshGames").Enabled); Assert.NotNull(f.GameDiscovery.LastAttemptUtc);
    });

    private static GameInstallation Game(string directory)
    {
        var id = Guid.NewGuid().ToString("N");
        return GameIdentity.Create(id, "Sentetik_" + id, GamePlatform.Custom, "Test", directory, Path.Combine(directory, id),
            [Path.Combine(directory, id, "runtime.exe")], GameInstallationState.Installed);
    }
}
