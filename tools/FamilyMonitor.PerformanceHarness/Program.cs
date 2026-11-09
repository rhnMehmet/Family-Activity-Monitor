using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Games;
using FamilyMonitor.Core.Performance;
using FamilyMonitor.Core.Browsers;
using FamilyMonitor.Infrastructure.Browsers;
using Microsoft.Data.Sqlite;
using ApplicationIdentity = FamilyMonitor.Core.ApplicationIdentity;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var label = args.ElementAtOrDefault(0) ?? "baseline";
        var output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/performance/baseline.json");
        var browserPrototype=label.StartsWith("2.0",StringComparison.Ordinal);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        var measurements = new List<object>();
        var phases=browserPrototype?new[]{("system-only",20),("stopped-visible",20),("game-visible",25),("browser-visible",30),("browser-hidden",30),("longrun-hidden",90)}:
            [("system-only",20),("stopped-visible",20),("active-visible",25),("scan-visible",25),("game-visible",25),("active-hidden",25),("longrun-hidden",90)];
        foreach (var (mode, seconds) in phases)
        {
            if(args.Length>2&&mode!=args[2])continue;
            Console.WriteLine($"Starting {label}/{mode} ({seconds}s), synthetic data only.");
            measurements.Add(Measure(mode, seconds,browserPrototype));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { Label=label, TimestampUtc=DateTimeOffset.UtcNow,
                OS=RuntimeInformation.OSDescription, Runtime=RuntimeInformation.FrameworkDescription,
                LogicalProcessors=Environment.ProcessorCount, CatalogSeed=1000, HistorySeed=5000,
                Method=browserPrototype?"Production MainForm/Tracker/buffered SQLite/browser runtime plus isolated named-pipe listener; three synthetic extension connections with in-process sanitized messages. No actual browser/native-host subprocess/native input/user DB. Process logical I/O is NOT physical disk. FPS/GPU/browser-engine/bridge-subprocess resources unmeasured.":
                    "Same MainForm/Tracker/SQLite code; synthetic foreground and Steam library; no user database/native input. Process I/O is logical OS I/O, not physical disk bytes. No FPS/GPU measurement.", Measurements=measurements }, new JsonSerializerOptions { WriteIndented=true }));
        }
        Console.WriteLine(output);
    }

    private static object Measure(string mode, int seconds,bool browserPrototype)
    {
        var root = Path.Combine(Path.GetTempPath(), "FamilyMonitor.Performance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Form? form = null; ActivityTracker? tracker = null; GameDiscoveryService? discovery = null;
        BufferedActivityStore? productionHost = null;
        BrowserRuntime? browserRuntime=null;BrowserPipeServer? browserPipe=null;Action<int>? browserTick=null;
        Task? scanTask = null;
        using var process = Process.GetCurrentProcess();
        var scanTimes = new List<double>(); var scans = 0;
        try
        {
            if (mode != "system-only")
            {
                var settingsStore = new LocalSettingsStore(root);
                var settings = new LocalSettings { ConsentGranted=true, PrivacyVersion=1, GameDiscoveryConsentGranted=true, GameDiscoveryPrivacyVersion=1,
                    Browsers=browserPrototype?new(){ConsentVersion=1,DomainTracking=true,ChromeEnabled=true,EdgeEnabled=true,FirefoxEnabled=true}:new() };
                settingsStore.Save(settings);
                var database=Path.Combine(root,"activity.db");
                if(browserPrototype)BrowserDatabaseUpgradeService.EnsureReady(database,_=>throw new InvalidOperationException("Only a new synthetic database is allowed."));
                else DatabaseUpgradeService.EnsureReady(database, _ => throw new InvalidOperationException("Only a new synthetic database is allowed."));
                var store = new SqliteActivityStore(database, optimized:true); var catalog=new SqliteGameCatalogRepository(database, optimized:true);
                var seed=Enumerable.Range(0,1000).Select(i=>GameIdentity.Create(i.ToString(), "Synthetic_"+i, GamePlatform.Custom,
                    "Fixture", root, Path.Combine(root,"seed",i.ToString()), [Path.Combine(root,"seed",i.ToString(),"runtime.exe")],GameInstallationState.Installed)).ToArray();
                catalog.ApplyScan(new("Fixture",seed,[]));
                using (var connection=new SqliteConnection($"Data Source={database};Pooling=False"))
                {
                    connection.Open(); using var transaction=connection.BeginTransaction(); using var command=connection.CreateCommand(); command.Transaction=transaction;
                    command.CommandText="INSERT INTO activities VALUES($id,'synthetic','synthetic','Synthetic app','synthetic',$start,$end,$duration,'Completed');";
                    command.Parameters.Add("$id",SqliteType.Text); command.Parameters.Add("$start",SqliteType.Integer);command.Parameters.Add("$end",SqliteType.Integer);command.Parameters.Add("$duration",SqliteType.Integer);
                    var start=browserPrototype?TimeZoneInfo.ConvertTimeToUtc(DateTime.Today.AddHours(1)).Ticks:DateTimeOffset.UtcNow.Date.AddHours(1).Ticks;
                    for(var i=0;i<5000;i++){command.Parameters["$id"].Value=Guid.NewGuid().ToString();command.Parameters["$start"].Value=start+i*TimeSpan.TicksPerSecond;
                        command.Parameters["$end"].Value=start+(i+1)*TimeSpan.TicksPerSecond;command.Parameters["$duration"].Value=TimeSpan.TicksPerSecond;command.ExecuteNonQuery();}
                    transaction.Commit();
                }
                var steam=Path.Combine(root,"steam");
                for(var i=0;i<200;i++)
                {
                    var game=Path.Combine(steam,"steamapps","common",i.ToString());Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,"runtime.exe"),"synthetic, never executed");
                    File.WriteAllText(Path.Combine(steam,"steamapps",$"appmanifest_{i}.acf"),$"\"AppState\" {{ \"appid\" \"{i}\" \"name\" \"SyntheticSteam_{i}\" \"installdir\" \"{i}\" \"StateFlags\" \"4\" }}");
                }
                var clock=new SystemClock();
                var policy=new GameModePolicy(clock);
                productionHost=new(store,clock,new SqliteGameForegroundStore(database,optimized:true),()=>policy.MayRunMaintenance,browserStore:browserPrototype?new(database):null);
                discovery=new([new SteamLibraryProvider(new CachingGameFileSystem(new GameFileSystem(),clock),()=>[steam])],catalog,clock,TimeSpan.FromHours(1));discovery.SetConsent(true);
                var source=new SyntheticSource(mode=="game-visible" ? new("Synthetic game","synthetic",1,seed[0].ExecutablePaths[0],GameInstallationId:seed[0].Id,GameName:seed[0].Name) :
                    mode.StartsWith("browser-",StringComparison.Ordinal)?new("Chrome","chrome",1):new("Synthetic app","synthetic",1));
                tracker=new(clock,source,productionHost,"synthetic","synthetic",foregroundStore:productionHost);tracker.GrantConsent();
                var type=typeof(LocalSettings).Assembly; _=type;
                var agent=Assembly.Load("FamilyMonitor.Agent"); var main=agent.GetType("FamilyMonitor.Agent.MainForm",true)!;
                form=(Form)Activator.CreateInstance(main,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                    [tracker,productionHost,settingsStore,settings,null,false,discovery,catalog],null)!;
                main.GetProperty("UseAsyncQueries",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(form,true);
                main.GetMethod("ConfigurePerformance",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,[policy,null]);
                if(browserPrototype)
                {
                    browserRuntime=new(tracker,clock,productionHost);
                    main.GetMethod("ConfigureBrowsers",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,[browserRuntime,productionHost]);
                    browserPipe=new(browserRuntime,root,"FamilyMonitor.Performance."+Guid.NewGuid().ToString("N"),null);
                    var clients=Enum.GetValues<BrowserKind>().ToDictionary(k=>k,k=>Guid.NewGuid());var sequences=clients.Keys.ToDictionary(k=>k,k=>0L);var tab=1;
                    foreach(var client in clients)browserRuntime.Correlator.Register(client.Value,client.Key,BrowserIdentity.ExtensionId(client.Key));
                    var runtime=browserRuntime;
                    void Respond()
                    {
                        foreach(var client in clients)
                        {
                            var current=runtime.Correlator.Policy(client.Value);if(!current.Enabled)continue;
                            runtime.Receive(client.Value,NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq=++sequences[client.Key],epoch=current.Epoch,windowId=(int)source.Application.WindowId,tabId=tab,
                                focused=true,@private=false,page=new BrowserPage(tab%2==0?"docs.example.test":"example.test")}));
                        }
                    }
                    runtime.Correlator.PolicyChanged+=Respond;
                    browserTick=tick=>
                    {
                        if(mode.StartsWith("browser-",StringComparison.Ordinal)&&tick%5==0){tab++;Respond();}
                        if(mode.StartsWith("browser-",StringComparison.Ordinal)&&tick%15==0)source.Application=source.Application with{WindowId=source.Application.WindowId==1?2:1};
                        if(tick%10==0)foreach(var client in clients)runtime.Receive(client.Value,NativeMessageProtocol.Encode(new{version=1,type="heartbeat",seq=++sequences[client.Key]}));
                    };
                    if(mode.StartsWith("browser-",StringComparison.Ordinal))
                    {
                        var tabs=(TabControl)form.Controls.Find("mainTabs",true).Single();tabs.SelectedTab=tabs.TabPages.Cast<TabPage>().Single(p=>p.Name=="browserActivity");
                    }
                }
                form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-12000,-12000);form.Show();
                if(mode.Contains("hidden",StringComparison.Ordinal))form.Hide();
                if(mode!="stopped-visible")tracker.Start();
                for(var i=0;i<3;i++){main.GetMethod("SampleAndRefresh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,null);Application.DoEvents();}
                var warmup=Stopwatch.StartNew();
                while((bool)main.GetProperty("IsReading",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)! && warmup.Elapsed<TimeSpan.FromSeconds(5)){Application.DoEvents();Thread.Yield();}
            }
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            var samples=new List<(double Working,double Private,long Heap)>();
            process.Refresh();var cpuStart=process.TotalProcessorTime; Native.GetProcessIoCounters(process.Handle,out var ioStart);
            Native.GetSystemTimes(out var idleStart,out var kernelStart,out var userStart);
            var watch=Stopwatch.StartNew(); var tick=0;
            using var timer=new System.Windows.Forms.Timer{Interval=1000};
            var refresh=form?.GetType().GetMethod("SampleAndRefresh",BindingFlags.Instance|BindingFlags.NonPublic);
            timer.Tick+=(_,_)=>
            {
                tick++;
                browserTick?.Invoke(tick);
                if(tracker is not null)refresh!.Invoke(form,null);
                if(mode=="scan-visible" && watch.Elapsed.TotalSeconds>=(scans+1)*5 && scans<5 && discovery is not null)
                {
                    if(scanTask is null || scanTask.IsCompleted)
                        scanTask=Task.Run(()=>LowPriorityWork.Run(()=>HeavyWorkCoordinator.Run(()=>{var scan=Stopwatch.StartNew();var report=discovery.Refresh();if(report.Ran){lock(scanTimes)scanTimes.Add(scan.Elapsed.TotalMilliseconds);Interlocked.Increment(ref scans);}return true;})));
                }
                process.Refresh();samples.Add((process.WorkingSet64/1048576d,process.PrivateMemorySize64/1048576d,GC.GetTotalMemory(false)));
                // Finish the final scheduled scan before ExitThread closes the form and revokes discovery.
                // This wait is only the measurement boundary, never the production sampling/UI path.
                if(watch.Elapsed.TotalSeconds>=seconds){timer.Stop();scanTask?.GetAwaiter().GetResult();Application.ExitThread();}
            };
            timer.Start();Application.Run();scanTask?.GetAwaiter().GetResult();watch.Stop();
            process.Refresh();Native.GetProcessIoCounters(process.Handle,out var ioEnd);Native.GetSystemTimes(out var idleEnd,out var kernelEnd,out var userEnd);
            var cpuSeconds=(process.TotalProcessorTime-cpuStart).TotalSeconds;
            var totalSystem=(kernelEnd-kernelStart)+(userEnd-userStart); var idle=idleEnd-idleStart;
            return new {Mode=mode,ElapsedSeconds=watch.Elapsed.TotalSeconds,Samples=samples.Count,
                ProcessCpuTotalPercent=mode=="system-only"?(double?)null:cpuSeconds/watch.Elapsed.TotalSeconds/Environment.ProcessorCount*100,
                ProcessCpuSingleCorePercent=mode=="system-only"?(double?)null:cpuSeconds/watch.Elapsed.TotalSeconds*100,
                SystemCpuPercent=totalSystem==0?0:(1-(double)idle/totalSystem)*100,
                WorkingSetAverageMiB=mode=="system-only"?(double?)null:samples.Average(x=>x.Working),WorkingSetLastMiB=mode=="system-only"?(double?)null:samples.Last().Working,
                WorkingSetMaxMiB=mode=="system-only"?(double?)null:samples.Max(x=>x.Working),PrivateAverageMiB=mode=="system-only"?(double?)null:samples.Average(x=>x.Private),
                ManagedHeapFirstBytes=samples.First().Heap,ManagedHeapLastBytes=samples.Last().Heap,
                LogicalReadBytes=ioEnd.ReadTransferCount-ioStart.ReadTransferCount,LogicalWriteBytes=ioEnd.WriteTransferCount-ioStart.WriteTransferCount,
                ReadOperations=ioEnd.ReadOperationCount-ioStart.ReadOperationCount,WriteOperations=ioEnd.WriteOperationCount-ioStart.WriteOperationCount,
                ScanCount=scans,ScanMilliseconds=scanTimes, Fps=(double?)null,FrameTimeMs=(double?)null,GpuPercent=(double?)null};
        }
        finally
        {
            tracker?.Stop();form?.Close();
            (form?.GetType().GetProperty("PendingDatabaseRead",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(form) as Task)?.GetAwaiter().GetResult();
            form?.Dispose();browserPipe?.Dispose();browserRuntime?.Dispose();productionHost?.Dispose();discovery?.Dispose();SqliteConnection.ClearAllPools();
            var full=Path.GetFullPath(root);var parent=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FamilyMonitor.Performance"))+Path.DirectorySeparatorChar;
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe cleanup target.");
            Directory.Delete(full,true);
        }
    }
    private sealed class SyntheticSource(ApplicationIdentity app):IActivitySource { public ApplicationIdentity Application{get;set;}=app;public ActivitySample Read()=>new(Application,TimeSpan.Zero); }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Io {public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
        [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool GetProcessIoCounters(nint handle,out Io counters);
        [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    }
}
