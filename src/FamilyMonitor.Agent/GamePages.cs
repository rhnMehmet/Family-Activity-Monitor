using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure;

namespace FamilyMonitor.Agent;

internal sealed class GamePages : IDisposable
{
    private readonly ActivityTracker tracker;
    private readonly GameDiscoveryService discovery;
    private readonly IGameCatalogRepository repository;
    private readonly GameUsageService usage;
    private readonly Func<LocalSettings> getSettings;
    private readonly Action<LocalSettings> saveSettings;
    private readonly Func<DateOnly> selectedDate;
    private readonly IUserDialogs dialogs;
    private readonly IWin32Window owner;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 60000 };
    private readonly Label consent = new() { Name = "gameConsentStatus", AutoSize = true };
    private readonly Label status = new() { Name = "gameDiscoveryStatus", AutoSize = true, MaximumSize = new Size(850, 0) };
    private readonly Button permit = Button("Oyun keşfine izin ver", "gameConsent");
    private readonly Button refresh = Button("Oyunları yenile", "refreshGames");
    private readonly Button add = Button("Manuel oyun ekle / eşleştir", "manualGame");
    private readonly Button folders = Button("Oyun klasörlerini düzenle", "gameFolders");
    private readonly DataGridView library = Grid("installedGames");
    private readonly DataGridView totals = Grid("gameUsage");
    private readonly DataGridView history = Grid("gameHistory");
    private readonly Label todayTotal = new() { Name = "todayGameTotal", AutoSize = true };
    private readonly Label selectedTotal = new() { Name = "selectedGameTotal", AutoSize = true };
    private readonly ComboBox period = new() { Name = "gamePeriod", DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private bool refreshing;
    private bool changingConsent;
    private bool editing;
    private bool disposed;
    private readonly bool automaticRefresh;
    private readonly Action refreshHost;
    public TabPage LibraryPage { get; }
    public TabPage StatisticsPage { get; }
    public bool HasConsent => discovery.HasConsent;
    public bool PeriodIsWeek => period.SelectedIndex == 1;

    public GamePages(ActivityTracker tracker, GameDiscoveryService discovery, IGameCatalogRepository repository,
        Func<LocalSettings> getSettings, Action<LocalSettings> saveSettings, Func<DateOnly> selectedDate,
        IUserDialogs dialogs, IWin32Window owner, bool automaticRefresh, Action refreshHost, IGameForegroundStore? foreground = null)
    {
        this.tracker = tracker; this.discovery = discovery; this.repository = repository; usage = new(repository, foreground);
        this.getSettings = getSettings; this.saveSettings = saveSettings; this.selectedDate = selectedDate;
        this.dialogs = dialogs; this.owner = owner; this.automaticRefresh = automaticRefresh;
        this.refreshHost = refreshHost;
        LibraryPage = new TabPage("Yüklü Oyunlar") { Name = "gameLibrary" };
        StatisticsPage = new TabPage("Oyun İstatistikleri") { Name = "gameStatistics" };
        var layout = Layout();
        layout.Controls.Add(consent);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        actions.Controls.AddRange([permit, refresh, add, folders]); layout.Controls.Add(actions);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(850, 0), Text = "Katalog yereldir. Aktif oyun süresi genel kullanımın alt kümesidir. Keşif: açılışta, elle, anlamlı değişikliklerde ve saatte bir. Oyun modunda ertelenir. " +
            "Onay bekleyen adaylar otomatik oyun sayılmaz. Java gibi ortak süreçler otomatik eşlenmez." });
        layout.Controls.Add(status);
        foreach (var name in new[] { "Oyun adı", "Platform", "Kurulum durumu", "Son oynanma (yerel)", "Kurulum konumu", "Exe eşleştirmeleri" }) library.Columns.Add(name, name);
        layout.Controls.Add(library); SizeRows(layout, 4);
        LibraryPage.Controls.Add(layout);
        var stats = Layout();
        stats.Controls.Add(todayTotal); stats.Controls.Add(selectedTotal);
        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        filter.Controls.Add(new Label { Text = "Üstte seçilen gün için:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        period.Items.AddRange(["Günlük", "Haftalık (Pazartesi–Pazar)"]); period.SelectedIndex = 0; filter.Controls.Add(period);
        stats.Controls.Add(filter);
        foreach (var name in new[] { "Oyun", "Platform", "Girişle doğrulanan aktif süre", "Ön planda kalma süresi" }) totals.Columns.Add(name, name);
        stats.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(850, 0), Text = "Ön plan ölçümü 1.2.1 ile başlar; eski kayıtlara eklenmez. Ön planda kalmak etkileşim kanıtı değildir. XInput dışı veya örnekler arasında kalan kontrolcü girişleri doğrulanamayabilir." });
        foreach (var name in new[] { "Oyun", "Platform", "Başlangıç (yerel)", "Bitiş (yerel)", "Dönemin aktif süresi" }) history.Columns.Add(name, name);
        var split = new SplitContainer { Size = new Size(400, 220), Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel1MinSize = 70, Panel2MinSize = 70 };
        split.Panel1.Controls.Add(totals); split.Panel2.Controls.Add(history); stats.Controls.Add(split);
        SizeRows(stats, 4); StatisticsPage.Controls.Add(stats);
        permit.Click += async (_, _) =>
        {
            if(changingConsent)return;
            try
            {
                if (discovery.HasConsent) { RevokeConsent(); return; }
                if (!dialogs.RequestGameDiscoveryConsent(owner)) return;
                changingConsent=true;permit.Enabled=false;
                status.Text="Oyun kataloğu yükleniyor…";
                saveSettings(getSettings() with { GameDiscoveryConsentGranted = true, GameDiscoveryPrivacyVersion = LocalSettingsStore.CurrentGameDiscoveryPrivacyVersion });
                if(automaticRefresh) { if(!await discovery.GrantConsentAsync())return; }
                else discovery.SetConsent(true);
                if(disposed)return;
                refreshHost();
                if (automaticRefresh) _ = RefreshAsync();
            }
            catch (Exception error) when (error is not OutOfMemoryException) { if(!disposed)status.Text = $"İzin işlemi tamamlanamadı ({error.GetType().Name})."; }
            finally{changingConsent=false;if(!disposed)permit.Enabled=true;}
        };
        refresh.Click += async (_, _) => await RefreshAsync();
        folders.Click += (_, _) => EditFolders();
        add.Click += async (_, _) =>
        {
            if (editing || !discovery.HasConsent) return;
            try
            {
                editing=true;add.Enabled=false;
                var catalog=await Task.Run(()=>LowPriorityWork.Run(repository.GetInstallations));
                if(disposed||!discovery.HasConsent)return;
                using var dialog = new ManualGameDialog(catalog);
                if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Installation is null) return;
                tracker.Pause();refreshHost();
                var installation=dialog.Installation;var executablePath=dialog.ExecutablePath;
                await Task.Run(()=>LowPriorityWork.Run(()=>{discovery.AddManualGame(installation,executablePath);return true;}));
                if(disposed)return;
                status.Text = "Manuel eşleştirme kaydedildi. İzlemeyi başlatın veya devam ettirin."; refreshHost();
            }
            catch (Exception error) when (error is not OutOfMemoryException) { if(!disposed)status.Text = $"Eşleştirme kaydedilemedi ({error.GetType().Name})."; }
            finally{editing=false;if(!disposed)add.Enabled=discovery.HasConsent;}
        };
        period.SelectedIndexChanged += (_, _) => refreshHost();
        timer.Tick += async (_, _) => { if (discovery.IsDue) await RefreshAsync(); };
    }

    public void Start()
    {
        refreshHost();
        if (automaticRefresh) { timer.Start(); if (discovery.HasConsent) _ = RefreshAsync(); }
    }

    public void RevokeConsent()
    {
        tracker.Stop(); // Close any classified period before switching back to ordinary app tracking.
        discovery.SetConsent(false);
        try
        {
            saveSettings(getSettings() with { GameDiscoveryConsentGranted = false });
            status.Text = "Oyun keşfi durduruldu. Katalog ve eski geçmiş korundu; izleme kapalı.";
        }
        finally { refreshHost(); }
    }

    internal async Task RefreshAsync()
    {
        if (disposed || refreshing || !discovery.HasConsent) return;
        refreshing = true; refresh.Enabled = false; status.Text = "Oyun kütüphaneleri yenileniyor…";
        try
        {
            var report = await Task.Run(() => LowPriorityWork.Run(() => HeavyWorkCoordinator.Run(discovery.Refresh)));
            if (disposed) return;
            status.Text = string.Join("  ", report.Messages.Take(10));
            refreshHost();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!disposed) status.Text = $"Yenileme tamamlanamadı ({error.GetType().Name}); eski kayıtlar korundu."; }
        finally { refreshing = false; if (!disposed) refresh.Enabled = discovery.HasConsent; }
    }

    private void EditFolders()
    {
        if (!discovery.HasConsent) return;
        using var dialog = new Form { Text = "Seçili oyun klasörleri", ClientSize = new Size(650, 300), StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 10) };
        var list = new ListBox { Dock = DockStyle.Fill };
        list.Items.AddRange(getSettings().GameFolders.Cast<object>().ToArray());
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var insert = Button("Klasör ekle…", "addGameFolder"); var remove = Button("Seçili klasörü çıkar", "removeGameFolder");
        var save = Button("Kaydet", "saveGameFolders"); actions.Controls.AddRange([insert, remove, save]);
        insert.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Yalnızca oyun klasörünüzü seçin; disk kökü seçmeyin." };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            if (picker.SelectedPath.StartsWith(@"\\", StringComparison.Ordinal)) { MessageBox.Show(dialog, "Yalnızca yerel disk klasörleri desteklenir."); return; }
            var path = GameIdentity.NormalizePath(picker.SelectedPath);
            if (GameIdentity.SamePath(path, Path.GetPathRoot(path)!)) { MessageBox.Show(dialog, "Tam disk taraması yapılmaz; alt klasör seçin."); return; }
            if (!list.Items.Cast<string>().Any(p => GameIdentity.SamePath(p, path))) list.Items.Add(path);
        };
        remove.Click += (_, _) => { if (list.SelectedIndex >= 0) list.Items.RemoveAt(list.SelectedIndex); };
        save.Click += (_, _) =>
        {
            try { saveSettings(getSettings() with { GameFolders = list.Items.Cast<string>().Take(64).ToArray() }); dialog.DialogResult = DialogResult.OK; dialog.Close(); }
            catch (Exception error) when (error is not OutOfMemoryException) { MessageBox.Show(dialog, $"Klasörler kaydedilemedi ({error.GetType().Name})."); }
        };
        dialog.Controls.Add(list); dialog.Controls.Add(actions);
        if (dialog.ShowDialog(owner) == DialogResult.OK) { status.Text = "Klasörler kaydedildi. Kapsamdan çıkarılan eski katalog kayıtları korunur."; _ = RefreshAsync(); }
    }

    public void UpdateRuntime(bool gameMode)
    {
        if (disposed) return;
        discovery.SetGaming(gameMode);
        consent.Text = $"Oyun kütüphanesi keşfi: {(discovery.HasConsent ? "İzin verildi" : "İzin verilmedi / geri çekildi")} • Oyun modu: {(gameMode ? "Açık" : "Kapalı")}";
        permit.Text = discovery.HasConsent ? "Oyun keşfi iznini geri çek" : "Oyun keşfine izin ver";
        refresh.Enabled = !refreshing && discovery.HasConsent; add.Enabled = !editing && discovery.HasConsent; folders.Enabled = discovery.HasConsent;
    }

    internal sealed record DisplaySnapshot(IReadOnlyList<GameInstallation>? Library, IReadOnlyList<GameUsage>? Current,
        IReadOnlyList<GameUsage>? Today, IReadOnlyList<GameUsageHistory>? History, bool Week);

    internal DisplaySnapshot LoadDisplay(DateOnly date, bool week, bool showLibrary, bool showStatistics)
    {
        if (disposed) return new(null, null, null, null, week);
        var current = showStatistics ? usage.GetUsage(date, week, TimeZoneInfo.Local) : null;
        var today = showStatistics ? usage.GetUsage(DateOnly.FromDateTime(DateTime.Today), false, TimeZoneInfo.Local) : null;
        return new(showLibrary ? repository.GetInstallations() : null, current, today,
            showStatistics ? usage.GetHistory(date, week, TimeZoneInfo.Local) : null, week);
    }

    internal void ApplyDisplay(DisplaySnapshot snapshot)
    {
        if (disposed) return;
        if (snapshot.Library is not null)
        {
            library.Rows.Clear();
            foreach (var game in snapshot.Library)
            {
            var state = game.State switch { GameInstallationState.Installed => "Kurulu", GameInstallationState.NotInstalled => "Kurulu değil", _ => "Doğrulanamadı" };
            if (!game.IsConfirmed) state += " — oyun onayı bekliyor";
            library.Rows.Add(game.Name, PlatformText(game.Platform), state,
                game.LastPlayedUtc?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—", game.InstallDirectory, string.Join("; ", game.ExecutablePaths.Take(8)));
            }
        }
        if (snapshot.Current is null || snapshot.Today is null || snapshot.History is null) return;
        var current = snapshot.Current; var today = snapshot.Today;
        static string Foreground(IReadOnlyList<GameUsage> rows) => rows.Any(x => x.ForegroundDuration is not null)
            ? Duration(TimeSpan.FromTicks(rows.Sum(x => x.ForegroundDuration?.Ticks ?? 0))) : "Ölçülmedi";
        totals.Rows.Clear(); foreach (var row in current) totals.Rows.Add(row.Name, PlatformText(row.Platform), Duration(row.Duration), row.ForegroundDuration is null ? "Ölçülmedi" : Duration(row.ForegroundDuration.Value));
        selectedTotal.Text = $"Seçili {(snapshot.Week ? "haftanın" : "günün")} aktif oyun süresi: {Duration(TimeSpan.FromTicks(current.Sum(x => x.Duration.Ticks)))} • Ön plan: {Foreground(current)}";
        todayTotal.Text = $"Bugünkü toplam aktif oyun süresi: {Duration(TimeSpan.FromTicks(today.Sum(x => x.Duration.Ticks)))} (genel toplamın alt kümesi) • Ön plan: {Foreground(today)}";
        history.Rows.Clear();
        foreach (var row in snapshot.History) history.Rows.Add(row.Name, PlatformText(row.Platform),
            row.Activity.StartUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), row.Activity.EndUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"), Duration(row.Activity.ActiveDuration));
    }

    public static string PlatformText(GamePlatform platform) => platform switch
    { GamePlatform.Epic => "Epic Games", GamePlatform.Xbox => "Xbox / Microsoft Store", GamePlatform.MinecraftJava => "Minecraft Java", GamePlatform.MinecraftBedrock => "Minecraft Bedrock", GamePlatform.Standalone => "Bağımsız Windows", GamePlatform.Custom => "Kullanıcı tanımlı", _ => "Steam" };
    private static string Duration(TimeSpan span) => $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    private static Button Button(string text, string name) => new() { Text = text, Name = name, AutoSize = true, Padding = new Padding(5) };
    private static DataGridView Grid(string name) => new() { Name = name, Dock = DockStyle.Fill, ReadOnly = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window };
    private static TableLayoutPanel Layout()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8), AutoScroll = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Fixed content rows grow with font/DPI; the last row gets the remaining space.
        return panel;
    }
    private static void SizeRows(TableLayoutPanel panel, int contentRows)
    {
        for (var i = 0; i < contentRows; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
    }
    public void Dispose() { disposed = true; timer.Stop(); timer.Dispose(); discovery.Dispose(); }
}
