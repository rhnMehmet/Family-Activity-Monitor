using FamilyMonitor.Core;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Windows;
using Microsoft.Win32;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Core.Performance;
using FamilyMonitor.Infrastructure.Games;

namespace FamilyMonitor.Agent;

internal sealed class MainForm : Form
{
    private readonly ActivityTracker tracker;
    private readonly IActivityStore store;
    private readonly LocalSettingsStore settingsStore;
    private LocalSettings settings;
    private readonly IUserDialogs dialogs;
    private readonly bool enableWindowsIntegration;
    private readonly GamePages? games;
    private bool showingGameNote;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Label stateLabel = SummaryLabel("trackingStatus");
    private readonly Label userStateLabel = SummaryLabel("userStatus");
    private readonly Label idleLabel = SummaryLabel("idleDuration");
    private readonly Label thresholdLabel = SummaryLabel("appliedIdleThreshold");
    private readonly Label currentLabel = SummaryLabel("currentApplication");
    private readonly Label totalLabel = SummaryLabel("dailyTotal");
    private readonly Label thresholdSettingLabel = SummaryLabel("idleThresholdStatus");
    private readonly Label consentLabel = new() { AutoSize = true };
    private readonly Label messageLabel = SummaryLabel("operationMessage");
    private readonly Button startButton = MakeButton("İzlemeyi başlat");
    private readonly Button pauseButton = MakeButton("Duraklat");
    private readonly Button stopButton = MakeButton("İzlemeyi durdur");
    private readonly Button consentButton = MakeButton("İzin ver");
    private readonly NumericUpDown idleSeconds = new() { Name = "idleThresholdSeconds", Minimum = 5, Maximum = 3600, Width = 100 };
    private readonly DateTimePicker datePicker = new() { Format = DateTimePickerFormat.Short, Width = 140 };
    private readonly DataGridView usageGrid = MakeGrid("dailyUsage");
    private readonly DataGridView historyGrid = MakeGrid("activityHistory");
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayState = new() { Enabled = false };
    private readonly ToolStripMenuItem trayPause = new("Duraklat");
    private readonly ToolStripMenuItem trayStop = new("İzlemeyi durdur");
    private bool sessionNotificationsAvailable;
    private readonly IClock refreshClock = new SystemClock();
    private GameModePolicy gameMode = new(new SystemClock());
    private GameLibraryChangeMonitor? libraryMonitor;
    private bool watchingLibraries;
    private readonly Label gameModeLabel = SummaryLabel("gameModeStatus");
    private readonly CheckBox automaticGameMode = new() { Name = "automaticGameMode", Text = "Otomatik oyun modu", AutoSize = true };
    private TabControl tabs = null!;
    private TimeSpan nextRead;
    private long requestVersion;
    private bool reading, reloadRequested;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool UseAsyncQueries { get; set; }
    internal Task? PendingDatabaseRead { get; private set; }
    internal bool IsReading => reading;
    internal bool IsGameModeActive => gameMode.IsActive;

    public MainForm(ActivityTracker tracker, IActivityStore store, LocalSettingsStore settingsStore, LocalSettings settings,
        IUserDialogs? dialogs = null, bool enableWindowsIntegration = true,
        GameDiscoveryService? gameDiscovery = null, IGameCatalogRepository? gameRepository = null)
    {
        this.tracker = tracker;
        this.store = store;
        this.settingsStore = settingsStore;
        this.settings = settings;
        this.dialogs = dialogs ?? new UserDialogs();
        this.enableWindowsIntegration = enableWindowsIntegration;
        UseAsyncQueries = enableWindowsIntegration;
        Text = "Family Activity Monitor — 1.2.1";
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(1020, 760);
        MinimumSize = new Size(900, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        BackColor = Color.FromArgb(245, 247, 250);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(22), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "Family Activity Monitor", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true });
        var summary = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 6 };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { stateLabel, userStateLabel, currentLabel, totalLabel, thresholdLabel, idleLabel })
        {
            summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            summary.Controls.Add(label);
        }
        root.Controls.Add(summary);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var minimizeButton = MakeButton("Tepsiye küçült");
        actions.Controls.AddRange([startButton, pauseButton, stopButton, minimizeButton]);
        root.Controls.Add(actions);
        var dateRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        dateRow.Controls.Add(new Label { Text = "Görüntülenen gün:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        dateRow.Controls.Add(datePicker);
        root.Controls.Add(dateRow);

        tabs = new TabControl { Name = "mainTabs", Dock = DockStyle.Fill, MinimumSize = new Size(0, 240) };
        var usagePage = new TabPage("Uygulama süreleri");
        usageGrid.Columns.Add("Application", "Uygulama");
        usageGrid.Columns.Add("Process", "Süreç");
        usageGrid.Columns.Add("Duration", "Aktif süre");
        usagePage.Controls.Add(usageGrid);
        var historyPage = new TabPage("Kronolojik kayıtlar");
        foreach (var column in new[] { "Uygulama", "Süreç", "Başlangıç (yerel)", "Bitiş (yerel)", "Aktif süre", "Kayıt durumu" })
            historyGrid.Columns.Add(column, column);
        historyPage.Controls.Add(historyGrid);
        tabs.TabPages.AddRange([usagePage, historyPage, BuildSettingsPage()]);
        if (gameDiscovery is not null && gameRepository is not null)
        {
            games = new GamePages(tracker, gameDiscovery, gameRepository, () => this.settings,
                value => { settingsStore.Save(value); this.settings = value; watchingLibraries = false; }, () => DateOnly.FromDateTime(datePicker.Value),
                this.dialogs, this, enableWindowsIntegration, RefreshView, store as IGameForegroundStore);
            tabs.TabPages.AddRange([games.LibraryPage, games.StatisticsPage]);
        }
        root.Controls.Add(tabs);
        root.Controls.Add(messageLabel);
        Controls.Add(root);

        var menu = new ContextMenuStrip();
        menu.Items.Add(trayState);
        menu.Items.Add("Pencereyi göster", null, (_, _) => ShowWindow());
        menu.Items.Add("İzlemeyi başlat", null, (_, _) => StartTracking());
        menu.Items.Add(trayPause);
        menu.Items.Add(trayStop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => Close());
        tray = new NotifyIcon { Text = "Family Activity Monitor — Kapalı", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = enableWindowsIntegration };
        tray.DoubleClick += (_, _) => ShowWindow();
        trayPause.Click += (_, _) => TogglePause();
        trayStop.Click += (_, _) => RunControl(tracker.Stop);
        startButton.Click += (_, _) => StartTracking();
        pauseButton.Click += (_, _) => TogglePause();
        stopButton.Click += (_, _) => RunControl(tracker.Stop);
        minimizeButton.Click += (_, _) =>
        {
            Hide();
            tray.ShowBalloonTip(3000, "Family Activity Monitor", "Uygulama sistem tepsisinde görünür. Menüden izlemeyi durdurabilir veya çıkabilirsiniz.", ToolTipIcon.Info);
        };
        datePicker.ValueChanged += (_, _) => RefreshView();
        tabs.SelectedIndexChanged += (_, _) => RefreshView();
        timer.Tick += (_, _) => SampleAndRefresh();
        VisibleChanged += (_, _) => { requestVersion++; if (Visible) RefreshView(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Normal && Visible) RefreshView(); };
        Shown += (_, _) =>
        {
            if (!tracker.HasConsent) RequestConsent();
            RefreshView();
            if (enableWindowsIntegration) timer.Start();
            games?.Start();
        };
        if (enableWindowsIntegration) SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    internal void ConfigurePerformance(GameModePolicy policy, GameLibraryChangeMonitor? monitor = null)
    { gameMode = policy; libraryMonitor = monitor; gameMode.Enabled = settings.AutomaticGameMode; }

    internal void SampleAndRefresh()
    {
        tracker.Tick();
        if (store is BufferedActivityStore { LastWriteError: not null } buffered)
        { tracker.Stop(); messageLabel.Text = $"Kayıt yazılamadı ({buffered.LastWriteError}); izleme durduruldu."; }
        RefreshCore(false);
    }

    private TabPage BuildSettingsPage()
    {
        var page = new TabPage("Ayarlar ve gizlilik") { Name = "privacySettings", AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(consentLabel);
        var privacyActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        privacyActions.Controls.Add(consentButton);
        layout.Controls.Add(privacyActions);
        consentButton.Click += (_, _) =>
        {
            if (!tracker.HasConsent) { RequestConsent(); return; }
            tracker.RevokeConsent(); // Stop in memory even if settings cannot be written.
            settings = settings with { ConsentGranted = false, GameDiscoveryConsentGranted = false };
            RunControl(() => { games?.RevokeConsent(); settingsStore.Save(settings); });
        };
        var historyActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var clear = MakeButton("Kullanım geçmişini sil");
        clear.Name = "deleteUsageHistory";
        clear.AccessibleDescription = "Onayınızla yalnızca yerel kullanım geçmişini siler; ayarlar ve izin korunur.";
        historyActions.Controls.Add(clear);
        historyActions.Controls.Add(new Label { Text = "Ayarlar ve izin tercihiniz korunur.", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        layout.Controls.Add(historyActions);
        clear.Click += (_, _) =>
        {
            if (dialogs.ConfirmHistoryDeletion(this))
                RunControl(tracker.ClearHistory, "Kullanım geçmişi silindi. İzleme kapalı; ayarlar ve izin tercihiniz korundu.");
        };
        var idleRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        idleSeconds.Value = settings.IdleThresholdSeconds;
        idleRow.Controls.Add(new Label { Text = "Hareketsizlik eşiği (saniye):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        var save = MakeButton("Eşiği uygula ve kaydet");
        save.Name = "applyIdleThreshold";
        idleRow.Controls.AddRange([idleSeconds, save]);
        layout.Controls.Add(idleRow);
        layout.Controls.Add(thresholdSettingLabel);
        automaticGameMode.Checked = settings.AutomaticGameMode;
        automaticGameMode.CheckedChanged += (_, _) => RunControl(() =>
        {
            var updated = settings with { AutomaticGameMode = automaticGameMode.Checked };
            settingsStore.Save(updated); settings = updated; gameMode.Enabled = updated.AutomaticGameMode;
        });
        layout.Controls.Add(automaticGameMode);
        layout.Controls.Add(gameModeLabel);
        idleSeconds.ValueChanged += (_, _) => RefreshThresholdSetting();
        save.Click += (_, _) => RunControl(() =>
        {
            var updated = settings with { IdleThresholdSeconds = (int)idleSeconds.Value };
            settingsStore.Save(updated);
            settings = updated;
            tracker.UpdateOptions(new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(settings.IdleThresholdSeconds) });
        }, "Hareketsizlik eşiği uygulandı ve kaydedildi.");
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(760, 0),
            Text = "Fare hareketi ve klavye girdisi sayacı sıfırlar. Eşiği denemek için kaydettikten sonra giriş yapmadan bekleyin. " +
                   "Girdisiz süre sonraki girişte doğrulanır; eşik aşılırsa aktif kullanıma eklenmez." });
        layout.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 165, ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window, Text = ConsentDialog.PrivacyText.ReplaceLineEndings() });
        layout.Controls.Add(new Label { Text = "Veri konumu: %LOCALAPPDATA%\\FamilyActivityMonitor  •  Otomatik başlatma yok", AutoSize = true });
        page.Controls.Add(layout);
        return page;
    }

    private void RequestConsent()
    {
        if (!dialogs.RequestConsent(this)) { RefreshView(); return; }
        RunControl(() =>
        {
            var granted = settings with { ConsentGranted = true, PrivacyVersion = LocalSettingsStore.CurrentPrivacyVersion };
            settingsStore.Save(granted); // Persist first; storage failure must not grant consent.
            settings = granted;
            tracker.GrantConsent();
        });
    }

    private void StartTracking()
    {
        if (!sessionNotificationsAvailable)
        {
            messageLabel.Text = "Windows oturum bildirimleri kullanılamıyor. Uygulamayı yeniden açın.";
            return;
        }
        if (!tracker.HasConsent) RequestConsent();
        RunControl(() => { tracker.Start(); });
    }

    private void TogglePause() => RunControl(() => { if (tracker.IsPaused) tracker.Resume(); else tracker.Pause(); });

    private void RunControl(Action action, string? successMessage = null)
    {
        try { action(); showingGameNote = false; messageLabel.ForeColor = SystemColors.ControlText; messageLabel.Text = successMessage ?? ""; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            tracker.Stop();
            messageLabel.ForeColor = Color.Firebrick;
            messageLabel.Text = $"İşlem tamamlanamadı ({error.GetType().Name}). Yerel veri klasörünü kontrol edin.";
        }
        RefreshView();
    }

    internal void RefreshView() { requestVersion++; RefreshCore(true); }

    private void RefreshCore(bool force)
    {
        if (IsDisposed) return;
        gameMode.Enabled = settings.AutomaticGameMode;
        gameMode.Observe(tracker.IsEnabled, tracker.IsPaused, tracker.IsSessionLocked, tracker.IsSuspended, tracker.HasForegroundGame);
        games?.UpdateRuntime(gameMode.IsActive);
        gameModeLabel.Text = gameMode.IsActive ? "Oyun modu: Açık — ağır taramalar ve bakım erteleniyor." : "Oyun modu: Kapalı";
        if (libraryMonitor is not null && watchingLibraries != (games?.HasConsent == true))
        {
            watchingLibraries = games?.HasConsent == true;
            if (watchingLibraries)
                _ = Task.Run(() => LowPriorityWork.Run(() => { libraryMonitor.Start(() => games?.HasConsent == true && !IsDisposed); return true; }));
            else libraryMonitor.Stop();
        }
        var view = TrackingViewState.From(tracker);
        stateLabel.Text = view.TrackingText;
        userStateLabel.Text = view.UserText;
        idleLabel.Text = (tracker.ControllerStatus is null ? view.IdleText :
            view.IdleText.Replace("Son fare/klavye girdisinden beri:", "Son doğrulanan fare/klavye veya XInput etkileşiminden beri:")) +
            (tracker.ControllerStatus is null ? "" : $" • {tracker.ControllerStatus}");
        thresholdLabel.Text = view.ThresholdText;
        stateLabel.ForeColor = tracker.State == TrackingState.Active ? Color.DarkGreen : Color.FromArgb(70, 75, 85);
        currentLabel.Text = view.CurrentApplicationText;
        RefreshThresholdSetting();
        consentLabel.Text = $"Veri toplama izni: {(tracker.HasConsent ? "Verildi" : "Verilmedi / geri çekildi")}";
        consentButton.Text = tracker.HasConsent ? "İzni geri çek" : "İzin ver";
        startButton.Enabled = !tracker.IsEnabled;
        pauseButton.Enabled = trayPause.Enabled = tracker.IsEnabled;
        stopButton.Enabled = trayStop.Enabled = tracker.IsEnabled;
        pauseButton.Text = trayPause.Text = tracker.IsPaused ? "Devam et" : "Duraklat";
        trayState.Text = $"{view.TrackingText} / {view.UserText}";
        tray.Text = view.TrayText;
        if (tracker.LastError is not null) messageLabel.Text = $"İzleme sırasında hata: {tracker.LastError}. Yeniden başlatabilirsiniz.";
        else if (tracker.State == TrackingState.Unavailable) messageLabel.Text = "Aktif uygulama okunamıyor; bu aralığa süre yazılmıyor.";
        if (tracker.State == TrackingState.Active && tracker.CurrentGameNote is not null)
        { messageLabel.Text = tracker.CurrentGameNote; showingGameNote = true; }
        else if (showingGameNote) { messageLabel.Text = ""; showingGameNote = false; }
        if (UseAsyncQueries && (!Visible || WindowState == FormWindowState.Minimized)) return;
        if (UseAsyncQueries && !force && (refreshClock.Elapsed < nextRead || !tracker.IsEnabled)) return;
        if (reading) { reloadRequested |= force; return; }
        nextRead = refreshClock.Elapsed + gameMode.UiRefreshInterval;
        ReadDisplay();
    }

    private sealed record DisplaySnapshot(IReadOnlyList<ApplicationUsage> Usage, IReadOnlyList<ActivityRecord>? History, GamePages.DisplaySnapshot? Games);

    private async void ReadDisplay()
    {
        reading = true; reloadRequested = false;
        var version = requestVersion;
        var day = DateOnly.FromDateTime(datePicker.Value);
        var all = !UseAsyncQueries;
        var showUsage = all || tabs.SelectedIndex == 0;
        var showHistory = all || tabs.SelectedIndex == 1;
        var showLibrary = all || tabs.SelectedTab == games?.LibraryPage;
        var showStatistics = all || tabs.SelectedTab == games?.StatisticsPage;
        var week = games?.PeriodIsWeek == true;
        DisplaySnapshot Load()
        {
            var usage = store.GetDailyUsage(day, TimeZoneInfo.Local);
            IReadOnlyList<ActivityRecord>? records = null;
            if (showHistory)
            {
                var start = GameUsageService.LocalDayBoundary(day, TimeZoneInfo.Local);
                var end = GameUsageService.LocalDayBoundary(day.AddDays(1), TimeZoneInfo.Local);
                records = store switch
                {
                    BufferedActivityStore buffered => buffered.ReadRecent(start, end),
                    SqliteActivityStore sqlite => sqlite.ReadRecent(start, end),
                    _ => store.Read(start, end).TakeLast(500).ToArray()
                };
            }
            return new(usage, records, games?.LoadDisplay(day, week, showLibrary, showStatistics));
        }
        try
        {
            DisplaySnapshot snapshot;
            if (UseAsyncQueries)
            {
                var work = Task.Run(() => LowPriorityWork.Run(Load));
                PendingDatabaseRead = work;
                snapshot = await work;
                if (IsDisposed || Disposing || version != requestVersion || !Visible || WindowState == FormWindowState.Minimized) return;
            }
            else snapshot = Load();
            var total = TimeSpan.FromTicks(snapshot.Usage.Sum(x => x.Duration.Ticks));
            totalLabel.Text = $"Günlük toplam aktif kullanım: {FormatDuration(total)} (seçilen gün, son girdiyle doğrulanan)";
            if (showUsage)
            {
                usageGrid.Rows.Clear();
                foreach (var row in snapshot.Usage) usageGrid.Rows.Add(row.ApplicationName, row.ProcessName, FormatDuration(row.Duration));
            }
            if (snapshot.History is not null)
            {
                historyGrid.Rows.Clear();
                foreach (var record in snapshot.History)
                historyGrid.Rows.Add(record.ApplicationName, record.ProcessName,
                    record.StartUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), record.EndUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                    FormatDuration(record.ActiveDuration), record.Status switch { ActivityStatus.Active => "Devam ediyor", ActivityStatus.Completed => "Tamamlandı", _ => "Kesintiye uğradı" });
            }
            if (snapshot.Games is not null) games?.ApplyDisplay(snapshot.Games);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (IsDisposed || Disposing) return;
            tracker.Stop();
            messageLabel.Text = $"Kayıtlar okunamadı ({error.GetType().Name}); izleme durduruldu.";
        }
        finally
        {
            reading = false;
            if (reloadRequested && !IsDisposed && !Disposing) RefreshCore(true);
        }
    }

    private void RefreshThresholdSetting()
    {
        var applied = (decimal)tracker.IdleThreshold.TotalSeconds;
        thresholdSettingLabel.Text = idleSeconds.Value == applied
            ? $"Uygulanan eşik: {applied:0} saniye."
            : $"Henüz uygulanmadı. Geçerli eşik: {applied:0} saniye; “Eşiği uygula ve kaydet”e basın.";
        thresholdSettingLabel.ForeColor = idleSeconds.Value == applied ? SystemColors.ControlText : Color.Firebrick;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        sessionNotificationsAvailable = !enableWindowsIntegration || WindowsSessionNotifications.Register(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        tracker.Stop();
        if (enableWindowsIntegration) WindowsSessionNotifications.Unregister(Handle);
        sessionNotificationsAvailable = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WindowsSessionNotifications.SessionChangeMessage)
        {
            switch (message.WParam.ToInt32())
            {
                case 2: case 4: case 7: QueueSystemEvent(() => tracker.SetLocked(true)); break;
                case 1: case 3: case 8: QueueSystemEvent(() => tracker.SetLocked(false)); break;
                case 6: QueueSystemEvent(tracker.Stop); break;
            }
        }
        base.WndProc(ref message);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) QueueSystemEvent(() => tracker.SetSuspended(true));
        else if (e.Mode == PowerModes.Resume) QueueSystemEvent(() => tracker.SetSuspended(false));
    }

    private void QueueSystemEvent(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(() => RunControl(action)); }
        catch (InvalidOperationException) { /* Window is closing. */ }
    }

    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        requestVersion++;
        tracker.Stop();
        libraryMonitor?.Dispose();
        games?.Dispose();
        if (enableWindowsIntegration) SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        tray.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { requestVersion++; timer.Dispose(); tray.Dispose(); libraryMonitor?.Dispose(); games?.Dispose(); }
        base.Dispose(disposing);
    }

    private static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    private static Button MakeButton(string text) => new() { Text = text, AutoSize = true, Height = 36, Padding = new Padding(8, 3, 8, 3) };
    private static Label SummaryLabel(string name) => new() { Name = name, AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(800, 0) };
    private static DataGridView MakeGrid(string name) => new()
    {
        Name = name,
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.None,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
    };
}
