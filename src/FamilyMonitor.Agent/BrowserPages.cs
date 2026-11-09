using FamilyMonitor.Core;
using FamilyMonitor.Core.Browsers;
using FamilyMonitor.Core.Games;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Browsers;

namespace FamilyMonitor.Agent;

internal sealed class BrowserPages:IDisposable
{
    private readonly BrowserRuntime runtime;
    private readonly IBrowserActivityStore store;
    private readonly IActivityStore applicationStore;
    private readonly Func<LocalSettings> settings;
    private readonly Action<LocalSettings> save;
    private readonly IUserDialogs dialogs;
    private readonly IWin32Window owner;
    private readonly Action refresh;
    private readonly Label total=new(){Name="browserTotal",AutoSize=true,MaximumSize=new Size(850,0)};
    private readonly Label status=new(){Name="browserRuntimeStatus",AutoSize=true,MaximumSize=new Size(850,0)};
    private readonly Label connectionSummary=new(){Name="browserConnectionSummary",AutoSize=true,MaximumSize=new Size(850,0)};
    private readonly Label empty=new(){Name="browserEmptyExplanation",AutoSize=true,MaximumSize=new Size(850,0),ForeColor=SystemColors.GrayText};
    private readonly Dictionary<BrowserKind,Label> connections=[];
    private readonly ComboBox filter=new(){Name="browserFilter",DropDownStyle=ComboBoxStyle.DropDownList,Width=160};
    private readonly DataGridView sites=Grid("browserSites"),history=Grid("browserVisits"),browsers=Grid("browserTotals");
    private readonly CheckBox domain=Check("Alan adı takibine izin ver","browserDomainConsent");
    private readonly CheckBox details=Check("Temizlenmiş URL yolunu kaydet (query/fragment saklanmaz)","browserUrlConsent");
    private readonly CheckBox searches=Check("Bilinen arama motorlarının sonuç URL'lerinden arama sorgularını kaydet","browserSearchConsent");
    private readonly CheckBox chrome=Check("Chrome","browserChromeEnabled"),edge=Check("Edge","browserEdgeEnabled"),firefox=Check("Firefox","browserFirefoxEnabled");
    private readonly NumericUpDown retention=new(){Name="browserRetentionDays",Minimum=1,Maximum=365,Width=90};
    private readonly Label message=new(){Name="browserSettingsMessage",AutoSize=true,MaximumSize=new Size(850,0)};
    private bool disposed;
    public TabPage ActivityPage {get;}=new("TARAYICI ETKİNLİĞİ"){Name="browserActivity",AutoScroll=true};
    public TabPage SettingsPage {get;}=new("Tarayıcı Ayarları"){Name="browserSettings",AutoScroll=true};
    public BrowserKind? SelectedBrowser=>filter.SelectedIndex switch{1=>BrowserKind.Chrome,2=>BrowserKind.Edge,3=>BrowserKind.Firefox,_=>null};

    public BrowserPages(BrowserRuntime runtime,IBrowserActivityStore store,IActivityStore applicationStore,Func<LocalSettings> settings,
        Action<LocalSettings> save,IUserDialogs dialogs,IWin32Window owner,Action refresh)
    {
        this.runtime=runtime;this.store=store;this.applicationStore=applicationStore;this.settings=settings;this.save=save;this.dialogs=dialogs;this.owner=owner;this.refresh=refresh;
        var activity=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=1,RowCount=8,Padding=new Padding(8),Height=600};
        ActivityPage.SizeChanged+=(_,_)=>activity.Height=Math.Max((int)(600*ActivityPage.DeviceDpi/96f),ActivityPage.ClientSize.Height);
        activity.ColumnStyles.Add(new(SizeType.Percent,100));
        for(var i=0;i<5;i++)activity.RowStyles.Add(new(SizeType.AutoSize));
        activity.RowStyles.Add(new(SizeType.Absolute,92));activity.RowStyles.Add(new(SizeType.Percent,40));activity.RowStyles.Add(new(SizeType.Percent,60));
        activity.Controls.Add(total,0,0);activity.Controls.Add(connectionSummary,0,1);activity.Controls.Add(status,0,2);activity.Controls.Add(empty,0,3);
        filter.Items.AddRange(["Tümü","Chrome","Edge","Firefox"]);filter.SelectedIndex=0;filter.SelectedIndexChanged+=(_,_)=>refresh();activity.Controls.Add(filter,0,4);
        foreach(var name in new[]{"Tarayıcı","Uygulama olarak aktif süre"})browsers.Columns.Add(name,name);
        foreach(var name in new[]{"Tarayıcı","Web sitesi","Doğrulanmış web sitesi süresi","Ziyaret dönemi"})sites.Columns.Add(name,name);
        foreach(var name in new[]{"Tarayıcı","Web sitesi","Başlangıç (yerel)","Bitiş (yerel)","Aktif süre","Atıf durumu","URL yolu","Arama sorgusu"})history.Columns.Add(name,name);
        activity.Controls.Add(browsers,0,5);activity.Controls.Add(sites,0,6);activity.Controls.Add(history,0,7);ActivityPage.Controls.Add(activity);
        var config=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12)};
        foreach(var kind in Enum.GetValues<BrowserKind>()){var label=new Label{Name="browserConnection"+kind,AutoSize=true,MaximumSize=new Size(830,0)};connections[kind]=label;config.Controls.Add(label);}
        config.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(830,0),Text="Eklentileri kendiniz kurup etkinleştirin. Native Messaging host kaydı yalnızca kullanıcı komutuyla yapılır; ağ portu açılmaz. Eklenti izni ve buradaki izin birlikte gerekir."});
        var instructions=Button("Eklenti ve Native Messaging kurulum yönergeleri","browserInstallInstructions");
        instructions.Click+=(_,_)=>MessageBox.Show(owner,
            "Yayın klasöründeki TARAYICI-KURULUM.md belgesini izleyin.\n"+
            "Chrome/Edge: ilgili extensions klasörünü geliştirici modunda Paketlenmemiş yükle ile seçin.\nFirefox: geçici test için about:debugging kullanın; kalıcı kurulum imzalama gerektirir.\n"+
            "Install-NativeHosts.ps1 betiğini yalnızca seçtiğiniz tarayıcı için kendiniz çalıştırın. Yönetici gerekmez.\n"+
            "Eklentide açık izin verin, burada da tarayıcıyı ve alan adı iznini seçin. Ardından genel izlemeyi başlatın.",
            "Kurulum yönergeleri");config.Controls.Add(instructions);
        config.Controls.Add(domain);
        var selection=new FlowLayoutPanel{AutoSize=true};selection.Controls.AddRange([chrome,edge,firefox]);config.Controls.Add(selection);
        config.Controls.Add(details);config.Controls.Add(searches);
        var retentionRow=new FlowLayoutPanel{AutoSize=true};retentionRow.Controls.Add(new Label{Text="Tarayıcı kayıtlarını saklama süresi (gün):",AutoSize=true});retentionRow.Controls.Add(retention);config.Controls.Add(retentionRow);
        config.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(830,0),Text="Özel gezinme, şifre/form/çerez/sayfa içeriği toplanmaz. Belirsiz eşleşmede site kaydı oluşmaz. Site süreleri genel tarayıcı süresinin alt kümesidir ve ikinci kez eklenmez. Listeler en fazla 500 satır gösterir."});
        var actions=new FlowLayoutPanel{AutoSize=true};
        var apply=Button("İzinleri ve saklama süresini uygula","applyBrowserOptions");
        var revoke=Button("Tarayıcı iznini geri çek","revokeBrowserConsent");
        var delete=Button("Tarayıcı geçmişini temizle","deleteBrowserHistory");
        actions.Controls.AddRange([apply,revoke,delete]);config.Controls.Add(actions);config.Controls.Add(message);SettingsPage.Controls.Add(config);
        apply.Click+=(_,_)=>Apply();revoke.Click+=(_,_)=>Revoke();
        delete.Click+=(_,_)=>{if(!dialogs.ConfirmBrowserHistoryDeletion(owner))return;try{runtime.DeleteHistory();message.Text="Tarayıcı geçmişi temizlendi; genel süreler ve izinler korundu.";refresh();}catch(Exception e)when(e is not OutOfMemoryException){message.Text="Silme tamamlanamadı ("+e.GetType().Name+").";}};
        LoadOptions();
    }
    private void LoadOptions()
    {
        var value=settings().Browsers;domain.Checked=value.DomainTracking;details.Checked=value.DetailedUrls;searches.Checked=value.SearchQueries;
        chrome.Checked=value.ChromeEnabled;edge.Checked=value.EdgeEnabled;firefox.Checked=value.FirefoxEnabled;retention.Value=value.RetentionDays;
    }
    private void Apply()
    {
        var previous=settings().Browsers;
        var value=new BrowserOptions{ConsentVersion=BrowserIdentity.PrivacyVersion,DomainTracking=domain.Checked,DetailedUrls=details.Checked,SearchQueries=searches.Checked,
            ChromeEnabled=chrome.Checked,EdgeEnabled=edge.Checked,FirefoxEnabled=firefox.Checked,RetentionDays=(int)retention.Value}.Normalize();
        var expands=value.DomainTracking&&(!previous.DomainTracking||value.DetailedUrls&&!previous.DetailedUrls||value.SearchQueries&&!previous.SearchQueries||
            value.ChromeEnabled&&!previous.ChromeEnabled||value.EdgeEnabled&&!previous.EdgeEnabled||value.FirefoxEnabled&&!previous.FirefoxEnabled);
        var narrows=previous.DomainTracking&&!value.DomainTracking||previous.DetailedUrls&&!value.DetailedUrls||previous.SearchQueries&&!value.SearchQueries||
            previous.ChromeEnabled&&!value.ChromeEnabled||previous.EdgeEnabled&&!value.EdgeEnabled||previous.FirefoxEnabled&&!value.FirefoxEnabled;
        if(expands&&!dialogs.RequestBrowserConsent(owner,value)){message.Text="İzin verilmedi; seçimler uygulanmadı.";return;}
        try
        {
            if(narrows)runtime.Configure(value with{DomainTracking=false,DetailedUrls=false,SearchQueries=false});
            save(settings() with{Browsers=value});runtime.Configure(value);message.Text="Tarayıcı seçimleri uygulandı. Eklentide de izin verip genel izlemeyi başlatın.";refresh();
        }
        catch(Exception e)when(e is not OutOfMemoryException){message.Text="Seçimler kaydedilemedi ("+e.GetType().Name+").";}
    }
    public void Revoke()
    {
        var value=settings().Browsers with{DomainTracking=false,DetailedUrls=false,SearchQueries=false};
        runtime.Configure(value);
        try{save(settings() with{Browsers=value});LoadOptions();message.Text="Tarayıcı izni geri çekildi. Eski kayıtlar ayrı temizleme seçimine kadar kalır.";}
        catch(Exception error)when(error is not OutOfMemoryException){LoadOptions();message.Text="Bu oturumda toplama durdu; tercih diske yazılamadı ("+error.GetType().Name+"). Windows başlangıcını ve sonraki açılıştaki izinleri kontrol edin.";}
        finally{refresh();}
    }
    public void UpdateRuntime()
    {
        if(disposed)return;
        foreach(var pair in connections)
        {
            var count=runtime.Correlator.Connections(pair.Key);
            pair.Value.Text=$"{BrowserIdentity.Text(pair.Key)}: {(count==0?"Bağlantı yok — eklenti kurulmamış, kapalı veya ulaşılamıyor":$"Bağlı ({count} eklenti bağlantısı)")}\n"+
                $"Agent site izni: {(settings().Browsers.Allows(pair.Key)?"Açık":"Kapalı — yeni site kaydı alınmaz")}";
        }
        connectionSummary.Text=string.Join("  •  ",Enum.GetValues<BrowserKind>().Select(kind=>$"{BrowserIdentity.Text(kind)}: {(runtime.Correlator.Connections(kind)>0?"Bağlı":"Bağlantı yok")}"));
        status.Text="Site kaydı durumu: "+(runtime.LastError??runtime.Correlator.State)+". Bağlı olması tek başına kayıt alındığı anlamına gelmez.";
    }
    internal sealed record Snapshot(IReadOnlyList<ApplicationUsage> General,IReadOnlyList<ApplicationUsage> Today,IReadOnlyList<BrowserSiteUsage> Sites,IReadOnlyList<BrowserVisit> Visits);
    public Snapshot? LoadDisplay(DateOnly day,BrowserKind? filter,bool visible,IReadOnlyList<ApplicationUsage>? general=null)
    {
        if(!visible||disposed)return null;
        var from=GameUsageService.LocalDayBoundary(day,TimeZoneInfo.Local);var to=GameUsageService.LocalDayBoundary(day.AddDays(1),TimeZoneInfo.Local);
        static bool IsBrowser(ApplicationUsage v)=>BrowserIdentity.FromProcess(v.ProcessName)is not null;
        var selected=(general??applicationStore.GetDailyUsage(day,TimeZoneInfo.Local)).Where(IsBrowser).ToArray();
        return new(selected,
            day==DateOnly.FromDateTime(DateTime.Today)?selected:applicationStore.GetDailyUsage(DateOnly.FromDateTime(DateTime.Today),TimeZoneInfo.Local).Where(IsBrowser).ToArray(),
            store.GetBrowserUsage(from,to,filter),store.ReadBrowser(from,to,filter));
    }
    public void ApplyDisplay(Snapshot snapshot,BrowserKind? filter)
    {
        if(disposed)return;
        var selected=snapshot.General.Where(v=>filter is null||BrowserIdentity.FromProcess(v.ProcessName)==filter).ToArray();
        total.Text=$"Bugünkü tarayıcı uygulama süresi: {Duration(TimeSpan.FromTicks(snapshot.Today.Sum(x=>x.Duration.Ticks)))}\n"+
            $"Seçili gün/filtre — uygulama: {Duration(TimeSpan.FromTicks(selected.Sum(x=>x.Duration.Ticks)))} • Listelenen doğrulanmış web sitesi süresi: {Duration(TimeSpan.FromTicks(snapshot.Sites.Sum(x=>x.Duration.Ticks)))}\n"+
            "Web sitesi süresi uygulama süresinin içindedir; iki süre birbirine eklenmez.";
        empty.Visible=snapshot.Sites.Count==0;
        empty.Text="Seçili gün ve filtrede web sitesi kaydı yok. Eklentiler kurulup bağlanmadan site listesi oluşmaz; tarayıcı uygulama süresi yine ölçülebilir. Tarayıcı Ayarları'ndaki kurulum yönergelerini izleyin, iki tarafta izin verin ve izlemeyi başlatın. Bağlantı varsa tarih/filtreyi ve site kaydı durumunu kontrol edin.";
        browsers.Rows.Clear();foreach(var group in selected.GroupBy(x=>BrowserIdentity.FromProcess(x.ProcessName)!.Value))browsers.Rows.Add(BrowserIdentity.Text(group.Key),Duration(TimeSpan.FromTicks(group.Sum(x=>x.Duration.Ticks))));
        sites.Rows.Clear();foreach(var site in snapshot.Sites)sites.Rows.Add(BrowserIdentity.Text(site.Browser),site.Domain,Duration(site.Duration),site.Visits);
        var options=settings().Browsers;
        history.Columns[6].Visible=options.DetailedUrls;history.Columns[7].Visible=options.SearchQueries;
        history.Rows.Clear();foreach(var row in snapshot.Visits)history.Rows.Add(BrowserIdentity.Text(row.Browser),row.Domain,row.StartUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            row.EndUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),Duration(row.Duration),"Ön plan doğrulandı",options.DetailedUrls?row.DetailUrl??"":"",options.SearchQueries?row.SearchQuery??"":"");
    }
    private static string Duration(TimeSpan v)=>$"{(int)v.TotalHours:00}:{v.Minutes:00}:{v.Seconds:00}";
    private static CheckBox Check(string text,string name)=>new(){Text=text,Name=name,AutoSize=true,MaximumSize=new Size(830,0)};
    private static Button Button(string text,string name)=>new(){Text=text,Name=name,AutoSize=true,Padding=new Padding(5)};
    private static DataGridView Grid(string name)=>new(){Name=name,Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,
        AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize,BackgroundColor=SystemColors.Window};
    public void Dispose()=>disposed=true;
}
