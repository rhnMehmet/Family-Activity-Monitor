using System.Runtime.ExceptionServices;
using FamilyMonitor.Agent;
using FamilyMonitor.Core;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Tests;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FamilyMonitor.UiTests;

public sealed class MainFormTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void HistoryButtonIsFullyVisibleAtMinimumSize(float scale) => OnUiThread(() =>
    {
        using var fixture = new UiFixture();
        var form = fixture.Form;
        form.Size = form.MinimumSize;
        if (scale != 1)
        {
            // Exercise enlarged text as well as geometry. This is a layout simulation, not an OS DPI switch.
            var fonts = Descendants(form).Prepend(form).Select(c => (Control: c, Font: c.Font)).ToArray();
            form.Scale(new SizeF(scale, scale));
            foreach (var item in fonts)
                item.Control.Font = new Font(item.Font.FontFamily, item.Font.SizeInPoints * scale, item.Font.Style);
        }
        var tabs = Descendants(form).OfType<TabControl>().Single();
        tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(x => x.Text == "Ayarlar ve gizlilik");
        form.Show();
        form.PerformLayout();
        var clear = fixture.Control<Button>("deleteUsageHistory");
        Assert.Equal("Kullanım geçmişini sil", clear.Text);
        Assert.True(clear.Visible);
        foreach (var name in new[] { "deleteUsageHistory", "applyIdleThreshold", "idleThresholdSeconds", "idleThresholdStatus",
                     "trackingStatus", "userStatus", "idleDuration", "appliedIdleThreshold", "dailyTotal", "currentApplication" })
            AssertFullyVisible(fixture.Control<Control>(name), scale);
        SaveRender(form, $"settings-{(int)(scale * 100)}.png");
    });

    [Fact]
    public void EditingThresholdDoesNotApplyUntilSaveAndSavedValueSurvivesRestart() => OnUiThread(() =>
    {
        using var f = new UiFixture();
        f.ShowSettings();
        f.Tracker.Start();
        f.Control<NumericUpDown>("idleThresholdSeconds").Value = 5;
        Assert.Equal(TimeSpan.FromSeconds(180), f.Tracker.IdleThreshold);
        Assert.Contains("Henüz uygulanmadı", f.Control<Label>("idleThresholdStatus").Text);
        f.Control<Button>("applyIdleThreshold").PerformClick();
        Assert.Equal(5, f.SettingsStore.Load().IdleThresholdSeconds);
        Assert.Equal(TimeSpan.FromSeconds(5), f.Tracker.IdleThreshold);
        Assert.Equal("Hareketsizlik eşiği: 5 saniye", f.Control<Label>("appliedIdleThreshold").Text);
        f.Idle(15);
        Assert.Equal("İzleme: Açık", f.Control<Label>("trackingStatus").Text);
        Assert.Equal("Kullanıcı durumu: Hareketsiz", f.Control<Label>("userStatus").Text);
        Assert.Equal("Son fare/klavye girdisinden beri: 15 saniye", f.Control<Label>("idleDuration").Text);
        Assert.Empty(f.Store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        f.Source.IdleTime = TimeSpan.Zero;
        f.Clock.Advance(1);
        f.Tracker.Tick();
        f.Form.RefreshView();
        Assert.Equal("Kullanıcı durumu: Aktif", f.Control<Label>("userStatus").Text);
        var saved = new LocalSettingsStore(f.DirectoryPath).Load();
        var restarted = new ActivityTracker(f.Clock, f.Source, f.Store, saved.DeviceId, saved.LocalUserId,
            new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(saved.IdleThresholdSeconds) });
        Assert.Equal(TimeSpan.FromSeconds(5), restarted.IdleThreshold);
        Assert.False(restarted.IsEnabled);
    });

    [Fact]
    public void LabelsFollowActualTrackerPauseLockSleepIdleAndStopStates() => OnUiThread(() =>
    {
        using var f = new UiFixture();
        f.Form.Show();
        f.Form.RefreshView();
        Assert.Equal("İzleme: Kapalı", f.Control<Label>("trackingStatus").Text);
        f.Tracker.Start();
        f.Form.RefreshView();
        Assert.Equal("İzleme: Açık", f.Control<Label>("trackingStatus").Text);
        Assert.Equal("Kullanıcı durumu: Aktif", f.Control<Label>("userStatus").Text);
        Assert.Contains("Editor", f.Control<Label>("currentApplication").Text);
        f.Tracker.Pause();
        f.Form.RefreshView();
        Assert.Equal("İzleme: Duraklatıldı", f.Control<Label>("trackingStatus").Text);
        Assert.Contains("ölçülmüyor", f.Control<Label>("idleDuration").Text);
        f.Tracker.Resume();
        f.Tracker.SetLocked(true);
        f.Form.RefreshView();
        Assert.Equal("Kullanıcı durumu: Kilitli", f.Control<Label>("userStatus").Text);
        f.Tracker.SetLocked(false);
        f.Tracker.SetSuspended(true);
        f.Form.RefreshView();
        Assert.Equal("Kullanıcı durumu: Uykuda", f.Control<Label>("userStatus").Text);
        f.Tracker.SetSuspended(false);
        f.Tracker.UpdateOptions(new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(5) });
        f.Idle(5);
        Assert.Equal("Kullanıcı durumu: Hareketsiz", f.Control<Label>("userStatus").Text);
        SaveRender(f.Form, "idle.png");
        f.Tracker.Stop();
        f.Form.RefreshView();
        Assert.Equal("İzleme: Kapalı", f.Control<Label>("trackingStatus").Text);
        Assert.Contains("—", f.Control<Label>("currentApplication").Text);
    });

    [Fact]
    public void CancelledHistoryConfirmationLeavesSqliteAndTrackingUntouched() => OnUiThread(() =>
    {
        using var f = new UiFixture();
        f.Use(3);
        f.ShowSettings();
        Assert.Equal(0, f.Dialogs.Confirmations);
        var before = Assert.Single(f.Store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        f.Dialogs.ConfirmDeletion = false;
        f.Control<Button>("deleteUsageHistory").PerformClick();
        Assert.Equal(1, f.Dialogs.Confirmations);
        Assert.Equal(before, Assert.Single(f.Store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)));
        Assert.True(f.Tracker.IsEnabled);
    });

    [Fact]
    public void ConfirmedDeletionClearsSqliteAndDisplayedTotalsButPreservesSettingsAfterRestart() => OnUiThread(() =>
    {
        using var f = new UiFixture();
        f.Use(3);
        f.ShowSettings();
        var settingsBefore = File.ReadAllText(Path.Combine(f.DirectoryPath, "settings.json"));
        Assert.Single(f.Control<DataGridView>("dailyUsage").Rows.Cast<DataGridViewRow>());
        f.Dialogs.ConfirmDeletion = true;
        f.Control<Button>("deleteUsageHistory").PerformClick();
        Assert.Equal(1, f.Dialogs.Confirmations);
        Assert.False(f.Tracker.IsEnabled);
        Assert.Empty(f.Store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.Empty(f.Control<DataGridView>("dailyUsage").Rows.Cast<DataGridViewRow>());
        Assert.Empty(f.Control<DataGridView>("activityHistory").Rows.Cast<DataGridViewRow>());
        Assert.Contains("00:00:00", f.Control<Label>("dailyTotal").Text);
        Assert.Equal("İzleme: Kapalı", f.Control<Label>("trackingStatus").Text);
        Assert.Contains("silindi", f.Control<Label>("operationMessage").Text);
        f.Clock.Advance(2);
        f.Tracker.Tick();
        f.Tracker.Stop();
        Assert.Equal(settingsBefore, File.ReadAllText(Path.Combine(f.DirectoryPath, "settings.json")));
        var reopened = new SqliteActivityStore(Path.Combine(f.DirectoryPath, "test.db"));
        Assert.Empty(reopened.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        Assert.True(new LocalSettingsStore(f.DirectoryPath).Load().ConsentGranted);
    });

    [Fact]
    public void UnrequestedDeletionDoesNotPromptOrRemoveAnyRecord() => OnUiThread(() =>
    {
        using var f = new UiFixture();
        f.Use(3);
        f.ShowSettings();
        f.Form.RefreshView();
        Assert.Equal(0, f.Dialogs.Confirmations);
        Assert.Single(f.Store.Read(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    });

    private static void AssertFullyVisible(Control control, float scale)
    {
        Assert.True(control.Visible, control.Name);
        var rectangle = control.ClientRectangle;
        Control child = control;
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            rectangle.Offset(child.Left, child.Top);
            Assert.True(parent.ClientRectangle.Contains(rectangle), $"{scale}x: {control.Name} clipped by {parent.GetType().Name}: {rectangle} vs {parent.ClientRectangle}");
            child = parent;
        }
    }

    private static void SaveRender(Form form, string name)
    {
        var path = Environment.GetEnvironmentVariable("FAMILYMONITOR_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(path, name), System.Drawing.Imaging.ImageFormat.Png);
    }

    internal static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    internal static void OnUiThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "UI test exceeded its deadline.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

internal sealed class FakeDialogs : IUserDialogs
{
    public bool GameConsentResponse { get; set; }
    public int GameConsentRequests { get; private set; }
    public bool RequestGameDiscoveryConsent(IWin32Window owner) { GameConsentRequests++; return GameConsentResponse; }
    public bool ConfirmDeletion { get; set; }
    public int Confirmations { get; private set; }
    public bool ConfirmHistoryDeletion(IWin32Window owner) { Confirmations++; return ConfirmDeletion; }
    public bool RequestConsent(IWin32Window owner) => false;
}

internal sealed class UiFixture : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "FamilyMonitor.UiTests", Guid.NewGuid().ToString("N"));
    public FakeClock Clock { get; } = new();
    public FakeSource Source { get; } = new();
    public SqliteActivityStore Store { get; }
    public LocalSettingsStore SettingsStore { get; }
    public LocalSettings Settings { get; }
    public ActivityTracker Tracker { get; }
    public FakeDialogs Dialogs { get; } = new();
    public MainForm Form { get; }
    public SqliteGameCatalogRepository? GameRepository { get; }
    public GameDiscoveryService? GameDiscovery { get; }

    public UiFixture(bool withGames = false)
    {
        SettingsStore = new LocalSettingsStore(DirectoryPath);
        Settings = new LocalSettings { ConsentGranted = true, PrivacyVersion = LocalSettingsStore.CurrentPrivacyVersion };
        SettingsStore.Save(Settings);
        Store = new SqliteActivityStore(Path.Combine(DirectoryPath, "test.db"));
        if (withGames)
        {
            GameRepository = new SqliteGameCatalogRepository(Path.Combine(DirectoryPath, "test.db"));
            GameDiscovery = new GameDiscoveryService([], GameRepository, Clock);
        }
        Tracker = new ActivityTracker(Clock, Source, Store, Settings.DeviceId, Settings.LocalUserId);
        Tracker.GrantConsent(); // Synthetic provider only; real Windows input is never read.
        Form = new MainForm(Tracker, Store, SettingsStore, Settings, Dialogs, enableWindowsIntegration: false,
            gameDiscovery: GameDiscovery, gameRepository: GameRepository)
        {
            ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000)
        };
        Control<DateTimePicker>("").Value = Clock.UtcNow.LocalDateTime;
    }

    public T Control<T>(string name) where T : Control => MainFormTests.Descendants(Form).OfType<T>().Single(x => x.Name == name);

    public void ShowSettings()
    {
        Control<TabControl>("mainTabs").SelectedTab = Control<TabPage>("privacySettings");
        Form.Show();
        Form.RefreshView();
    }

    public void Use(int seconds)
    {
        Tracker.Start();
        for (var i = 0; i < seconds; i++) { Source.IdleTime = TimeSpan.Zero; Clock.Advance(1); Tracker.Tick(); }
        Form.RefreshView();
    }

    public void Idle(int seconds)
    {
        for (var i = 0; i < seconds; i++) { Source.IdleTime += TimeSpan.FromSeconds(1); Clock.Advance(1); Tracker.Tick(); }
        Form.RefreshView();
    }

    public void Dispose()
    {
        Form.Close();
        Form.Dispose();
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}
