using FamilyMonitor.Agent;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Browsers;
using FamilyMonitor.Core.Security;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Browsers;
using FamilyMonitor.Infrastructure.Windows;
using FamilyMonitor.Tests;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FamilyMonitor.UiTests;

public sealed class BrowserUiTests
{
    [Fact]
    public void MissingExtensionExplainsEmptySitesWhileApplicationTimeIsVisible()=>MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();f.Tracker.Start();f.Use(4);f.Show("browserActivity");
        Assert.Empty(f.Control<DataGridView>("browserSites").Rows.Cast<DataGridViewRow>());
        Assert.Contains("uygulama: 00:00:04",f.Control<Label>("browserTotal").Text);
        Assert.Contains("web sitesi süresi: 00:00:00",f.Control<Label>("browserTotal").Text);
        Assert.Contains("birbirine eklenmez",f.Control<Label>("browserTotal").Text);
        Assert.True(f.Control<Label>("browserEmptyExplanation").Visible);
        Assert.Contains("Eklentiler kurulup bağlanmadan",f.Control<Label>("browserEmptyExplanation").Text);
        foreach(var kind in new[]{"Chrome","Edge","Firefox"})Assert.Contains(kind+": Bağlantı yok",f.Control<Label>("browserConnectionSummary").Text);
        f.Render("browser-no-extension.png");
        f.Show("browserSettings");Assert.Contains("Agent site izni: Kapalı",f.Control<Label>("browserConnectionChrome").Text);
        f.Tracker.Stop();f.EnableBrowser();f.Use(2);f.Show("browserActivity");
        Assert.Contains("Chrome: Bağlı",f.Control<Label>("browserConnectionSummary").Text);
        Assert.False(f.Control<Label>("browserEmptyExplanation").Visible);
    });
    [Fact]
    public void BrowserPermissionsAreSeparateOffByDefaultAndRequirePositiveConsent() => MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();f.Show("browserSettings");
        foreach(var name in new[]{"browserDomainConsent","browserUrlConsent","browserSearchConsent","autoStartEnabled","autoTrackOnLogin"})Assert.False(f.Control<CheckBox>(name).Checked);
        f.Control<CheckBox>("browserDomainConsent").Checked=true;f.Control<CheckBox>("browserChromeEnabled").Checked=true;
        f.Control<Button>("applyBrowserOptions").PerformClick();Assert.Equal(1,f.Dialogs.BrowserPrompts);Assert.False(f.Settings.Load().Browsers.DomainTracking);
        f.Dialogs.BrowserResponse=true;f.Control<Button>("applyBrowserOptions").PerformClick();
        Assert.True(f.Settings.Load().Browsers.DomainTracking);Assert.False(f.Settings.Load().Browsers.DetailedUrls);Assert.False(f.Settings.Load().Browsers.SearchQueries);
        Assert.False(f.Tracker.IsEnabled);Assert.True(f.Tracker.HasConsent);
    });
    [Fact]
    public void BrowserGridShowsSubsetOfGeneralTimeAndHidesUnconsentedColumns() => MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();f.EnableBrowser();f.Use(3);f.Show("browserActivity");
        var row=Assert.Single(f.Control<DataGridView>("browserSites").Rows.Cast<DataGridViewRow>());
        Assert.Equal("example.com",row.Cells[1].Value);Assert.Equal("00:00:03",row.Cells[2].Value);
        Assert.Contains("00:00:03",f.Control<Label>("dailyTotal").Text);Assert.Contains("00:00:03",f.Control<Label>("browserTotal").Text);
        Assert.False(f.Control<DataGridView>("browserVisits").Columns[6].Visible);Assert.False(f.Control<DataGridView>("browserVisits").Columns[7].Visible);
        f.Render("browser-activity.png");
        f.Control<ComboBox>("browserFilter").SelectedIndex=2;Assert.Empty(f.Control<DataGridView>("browserSites").Rows.Cast<DataGridViewRow>());
    });
    [Fact]
    public void BrowserDeleteNeedsConfirmationKeepsGeneralHistoryAndRemainsDeletedAfterRestart() => MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();f.EnableBrowser();f.Use(3);f.Show("browserSettings");
        f.Control<Button>("deleteBrowserHistory").PerformClick();Assert.NotEmpty(f.Buffer.ReadBrowser(DateTimeOffset.MinValue,DateTimeOffset.MaxValue));
        f.Dialogs.DeleteResponse=true;f.Control<Button>("deleteBrowserHistory").PerformClick();
        Assert.Empty(f.Buffer.ReadBrowser(DateTimeOffset.MinValue,DateTimeOffset.MaxValue));Assert.NotEmpty(f.Buffer.Read(DateTimeOffset.MinValue,DateTimeOffset.MaxValue));
        f.Tracker.Stop();f.Buffer.Flush();_=new SqliteActivityStore(f.Database,true);
        Assert.Empty(new SqliteBrowserActivityStore(f.Database).ReadBrowser(DateTimeOffset.MinValue,DateTimeOffset.MaxValue));Assert.True(f.Settings.Load().Browsers.DomainTracking);
    });
    [Fact]
    public void PinCannotBlockBrowserOrGeneralConsentWithdrawalOrStopping() => MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();f.EnableBrowser();f.Use(2);f.Pin.Setup("263849");f.Dialogs.ParentResponse=false;
        f.Show("browserSettings");f.Control<Button>("revokeBrowserConsent").PerformClick();
        Assert.False(f.Settings.Load().Browsers.DomainTracking);Assert.Equal(0,f.Dialogs.ParentPrompts);
        f.Show("privacySettings");MainFormTests.Descendants(f.Form).OfType<Button>().Single(x=>x.Text=="İzni geri çek").PerformClick();
        Assert.False(f.Tracker.HasConsent);Assert.False(f.Tracker.IsEnabled);Assert.Equal(0,f.Dialogs.ParentPrompts);f.Form.Close();Assert.True(f.Form.IsDisposed);
    });
    [Fact]
    public void StartupSettingsAreOptInPersistAndDoNotStartNormalLaunch() => MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();f.Show("privacySettings");Assert.False(f.Settings.Load().AutoStartEnabled);Assert.Null(f.Registry.Command);
        f.Control<CheckBox>("autoStartEnabled").Checked=true;f.Control<CheckBox>("autoTrackOnLogin").Checked=true;
        Assert.True(f.Settings.Load().AutoStartEnabled);Assert.True(f.Settings.Load().AutoTrackOnLogin);Assert.EndsWith("\" --autostart",f.Registry.Command);
        Assert.False(f.Tracker.IsEnabled);Assert.Equal(0,f.Source.Reads);
        f.Control<CheckBox>("autoStartEnabled").Checked=false;Assert.Null(f.Registry.Command);Assert.False(f.Settings.Load().AutoTrackOnLogin);
    });
    [Fact]
    public void ParentPinDenialCannotEnableStartup() => MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();f.Pin.Setup("263849");f.Dialogs.ParentResponse=false;f.Show("privacySettings");
        f.Control<CheckBox>("autoStartEnabled").Checked=true;Assert.False(f.Control<CheckBox>("autoStartEnabled").Checked);
        Assert.False(f.Settings.Load().AutoStartEnabled);Assert.Null(f.Registry.Command);Assert.Equal(1,f.Dialogs.ParentPrompts);
    });
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void BrowserSettingsRemainAccessibleWithScrollingAtScaledMinimumSize(float scale) => MainFormTests.OnUiThread(()=>
    {
        using var f=new Fixture();var form=f.Form;form.Size=form.MinimumSize;
        if(scale!=1)
        {
            var fonts=MainFormTests.Descendants(form).Prepend(form).Select(c=>(Control:c,Font:c.Font)).ToArray();form.Scale(new SizeF(scale,scale));
            foreach(var item in fonts)item.Control.Font=new Font(item.Font.FontFamily,item.Font.SizeInPoints*scale,item.Font.Style);
        }
        f.Show("browserSettings");var page=f.Control<TabPage>("browserSettings");
        foreach(var name in new[]{"browserDomainConsent","browserUrlConsent","browserSearchConsent","browserRetentionDays","applyBrowserOptions","revokeBrowserConsent","deleteBrowserHistory"})
        {
            var control=f.Control<Control>(name);page.ScrollControlIntoView(control);form.PerformLayout();
            var rectangle=control.ClientRectangle;Control child=control;
            for(var parent=control.Parent;parent is not null;parent=parent.Parent)
            {rectangle.Offset(child.Left,child.Top);Assert.True(parent.ClientRectangle.Contains(rectangle),$"{name}: {rectangle} vs {parent.ClientRectangle}");child=parent;}
        }
        page.AutoScrollPosition=Point.Empty;f.Render($"browser-settings-{(int)(100*scale)}.png");
    });

    private sealed class Fixture:IDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"FamilyMonitor.BrowserUiTests",Guid.NewGuid().ToString("N"));
        public string Database=>Path.Combine(root,"test.db");
        public FakeClock Clock=new(){UtcNow=new DateTimeOffset(DateTime.Today.AddHours(9)).ToUniversalTime()};
        public FakeSource Source=new(){Application=new("Chrome","chrome",1)};
        public LocalSettingsStore Settings;
        public BufferedActivityStore Buffer;
        public ActivityTracker Tracker;
        public BrowserRuntime Runtime;
        public MainForm Form;
        public Dialogs Dialogs=new();
        public Startup Registry=new();
        public ParentalPinService Pin;
        private Guid client;
        public Fixture()
        {
            BrowserDatabaseUpgradeService.EnsureReady(Database,_=>throw new Exception());var persistent=new SqliteActivityStore(Database,true);
            Settings=new(root);var settings=new LocalSettings{ConsentGranted=true,PrivacyVersion=1};Settings.Save(settings);
            Buffer=new(persistent,Clock,startWorker:false,browserStore:new(Database));Tracker=new(Clock,Source,Buffer,"d","s");Tracker.GrantConsent();
            Runtime=new(Tracker,Clock,Buffer);Pin=new(new Pins(),Clock);
            Form=new(Tracker,Buffer,Settings,settings,Dialogs,false){ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-12000,-12000)};
            Form.ConfigureBrowsers(Runtime,Buffer);Form.ConfigureStartup(new(Registry,Path.Combine(root,"FamilyMonitor.Agent.exe")),Pin,false);
            Control<DateTimePicker>("").Value=Clock.UtcNow.LocalDateTime;
        }
        public T Control<T>(string name)where T:Control=>MainFormTests.Descendants(Form).OfType<T>().Single(c=>c.Name==name);
        public void Show(string page){Control<TabControl>("mainTabs").SelectedTab=Control<TabPage>(page);Form.Show();Form.RefreshView();}
        public void EnableBrowser()
        {
            Show("browserSettings");Dialogs.BrowserResponse=true;Control<CheckBox>("browserDomainConsent").Checked=true;Control<CheckBox>("browserChromeEnabled").Checked=true;Control<Button>("applyBrowserOptions").PerformClick();
            client=Guid.NewGuid();Runtime.Correlator.Register(client,BrowserKind.Chrome,BrowserIdentity.ChromiumExtensionId);Tracker.Start();
            Assert.True(Runtime.Receive(client,NativeMessageProtocol.Encode(new{version=1,type="snapshot",seq=1,epoch=Runtime.Correlator.Policy(client).Epoch,windowId=1,tabId=1,focused=true,@private=false,page=new BrowserPage("example.com")})));
        }
        public void Use(int seconds){for(var i=0;i<seconds;i++){Clock.Advance(1);Tracker.Tick();}Form.RefreshView();}
        public void Render(string name){var directory=Environment.GetEnvironmentVariable("FAMILYMONITOR_UI_ARTIFACTS");if(directory is null)return;Directory.CreateDirectory(directory);using var bitmap=new Bitmap(Form.Width,Form.Height);Form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(directory,name));}
        public void Dispose(){Form.Close();Form.Dispose();Runtime.Dispose();Buffer.Dispose();SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private sealed class Dialogs:IUserDialogs
    {
        public bool BrowserResponse,DeleteResponse,ParentResponse=true;public int BrowserPrompts,ParentPrompts;
        public bool RequestConsent(IWin32Window owner)=>false;public bool ConfirmHistoryDeletion(IWin32Window owner)=>DeleteResponse;
        public bool RequestBrowserConsent(IWin32Window owner,BrowserOptions options){BrowserPrompts++;return BrowserResponse;}
        public bool ConfirmBrowserHistoryDeletion(IWin32Window owner)=>DeleteResponse;
        public bool AuthorizeParentSettings(IWin32Window owner,ParentalPinService service){ParentPrompts++;return ParentResponse;}
    }
    private sealed class Startup:IStartupRegistration {public string? Command;public string? Read()=>Command;public void Write(string? value)=>Command=value;}
    private sealed class Pins:IPinStateStore {private PinState state=new();public PinState Load()=>state;public void Save(PinState value)=>state=value;}
}
