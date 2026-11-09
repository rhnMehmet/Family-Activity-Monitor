using System.Globalization;
using FamilyMonitor.Core;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Windows;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure.Games;
using FamilyMonitor.Core.Performance;
using FamilyMonitor.Core.Input;
using FamilyMonitor.Core.Security;
using FamilyMonitor.Infrastructure.Security;
using FamilyMonitor.Infrastructure.Browsers;

namespace FamilyMonitor.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
        ApplicationConfiguration.Initialize();
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FamilyActivityMonitor");
            Directory.CreateDirectory(directory);
            // File locking also prevents two interactive sessions of the same local user sharing the DB.
            using var instanceLock = OpenInstanceLock(directory);
            if (instanceLock is null) return;
            var settingsStore = new LocalSettingsStore(directory);
            var settings = settingsStore.Load();
            var database = Path.Combine(directory, "activity.db");
            if (!BrowserDatabaseUpgradeService.EnsureReady(database, plan => MessageBox.Show(
                "2.0.0-prototype, ayrı izinli tarayıcı kayıtları için veritabanını güncelleyecek. Yeni izinler ve otomatik başlatma kapalı başlar.\n" +
                "Önce SQLite yedeği oluşturulup bütünlüğü doğrulanacak; geçmiş ve katalog korunacak.\n" +
                $"Yedek: {plan.BackupPath}\n\n" +
                "Eski 1.2.1 sürümüne dönüş için bu yedek gerekir. Yedekler geçmiş silme düğmesiyle silinmez.\n" +
                "Yedeklemeye ve güncellemeye izin veriyor musunuz? Hayır seçilirse uygulama kapanır.",
                "Family Activity Monitor — Veritabanı güncelleme onayı", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)) return;
            settingsStore.Save(settings);
            var persistent = new SqliteActivityStore(database, optimized: true);
            var catalog = new SqliteGameCatalogRepository(database, optimized: true);
            var clock = new SystemClock();
            var policy = new GameModePolicy(clock) { Enabled = settings.AutomaticGameMode };
            using var store = new BufferedActivityStore(persistent, clock, new SqliteGameForegroundStore(database, optimized: true), () => policy.MayRunMaintenance,browserStore:new SqliteBrowserActivityStore(database));
            var files = new CachingGameFileSystem(new GameFileSystem(), clock);
            var packages = new WindowsPackageInventory();
            // All location queries are lazy and are reached only after separate discovery consent.
            var discovery = new GameDiscoveryService([
                new SteamLibraryProvider(files, WindowsGameLocations.SteamRoots),
                new EpicLibraryProvider(files, WindowsGameLocations.EpicManifestRoots),
                new XboxLibraryProvider(packages),
                new MinecraftLibraryProvider(files, () => WindowsGameLocations.JavaRoots().Concat(settingsStore.Load().GameFolders), packages),
                new StandaloneLibraryProvider(files, WindowsGameLocations.StandaloneRoots),
                new FolderLibraryProvider(files, () => settingsStore.Load().GameFolders)
            ], catalog, clock, TimeSpan.FromHours(1));
            discovery.SetConsent(settings.GameDiscoveryConsentGranted);
            var nativeSource = new WindowActivitySource(new NativeWindowsActivityApi(), () => discovery.HasConsent, clock);
            var source = new GamepadActivitySource(new GameActivityResolver(nativeSource, discovery, new GameDetector()),
                new XInputActivityProvider(new NativeXInputApi(), clock), clock);
            var sessionId = $"{settings.LocalUserId}:{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
            var tracker = new ActivityTracker(clock, source, store, settings.DeviceId, sessionId,
                new TrackerOptions { IdleThreshold = TimeSpan.FromSeconds(settings.IdleThresholdSeconds) }, store);
            if (settings.ConsentGranted) tracker.GrantConsent();
            using var browserRuntime=new BrowserRuntime(tracker,clock,store);
            using var browserPipe=new BrowserPipeServer(browserRuntime,AppContext.BaseDirectory);
            using (discovery)
            using (var form = new MainForm(tracker, store, settingsStore, settings, gameDiscovery: discovery, gameRepository: catalog))
            {
                var monitor = new GameLibraryChangeMonitor(() => WindowsGameLocations.SteamRoots().Select(p => Path.Combine(p, "steamapps"))
                    .Concat(WindowsGameLocations.SteamRoots().Select(p => Path.Combine(p, "config")))
                    .Concat(WindowsGameLocations.EpicManifestRoots()).Concat(WindowsGameLocations.JavaRoots().Select(p => Path.Combine(p, "versions")))
                    .Concat(settingsStore.Load().GameFolders), discovery.NotifyLibraryChange);
                form.ConfigurePerformance(policy, monitor);
                form.ConfigureBrowsers(browserRuntime,store);
                form.ConfigureStartup(new AutoStartService(new WindowsStartupRegistration(),Environment.ProcessPath??""),
                    new ParentalPinService(new LocalPinStateStore(directory),clock),args.Length==1&&args[0]=="--autostart");
                Application.Run(form);
                // Background reads finish without waiting for their UI continuation; then Dispose flushes the writer.
                form.PendingDatabaseRead?.GetAwaiter().GetResult();
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            MessageBox.Show($"Uygulama başlatılamadı. Yerel veri klasörünün erişilebilir olduğunu kontrol edin.\nHata türü: {error.GetType().Name}",
                "Family Activity Monitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static FileStream? OpenInstanceLock(string directory)
    {
        try { return new FileStream(Path.Combine(directory, "agent.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException)
        {
            MessageBox.Show("Uygulama bu kullanıcı için zaten açık olabilir. Sistem tepsisini kontrol edin.", "Family Activity Monitor");
            return null;
        }
    }
}
