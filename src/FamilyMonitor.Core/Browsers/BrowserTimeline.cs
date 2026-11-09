namespace FamilyMonitor.Core.Browsers;

/// <summary>Only partitions intervals already confirmed by the application tracker. Never adds to general totals.</summary>
public sealed class BrowserTimeline(IBrowserActivityStore store)
{
    private sealed record Context(DateTimeOffset Start,DateTimeOffset Expires,BrowserBinding? Binding);
    private readonly List<Context> contexts=[];
    private readonly Dictionary<Guid,DateTimeOffset> confirmed=[];
    private BrowserVisit? current;
    private BrowserBinding? currentBinding;
    public int ContextCount=>contexts.Count;

    public void SetContext(BrowserBinding? binding,DateTimeOffset at,DateTimeOffset expires)
    {
        if(contexts.Count>0&&contexts[^1].Binding==binding&&contexts[^1].Expires>=at)
        {contexts[^1]=contexts[^1] with{Expires=expires};return;}
        if(current is not null){store.SaveBrowser(current with{Status=ActivityStatus.Completed});current=null;}
        contexts.Add(new(at,expires,binding));while(contexts.Count>512)contexts.RemoveAt(0);
    }
    public void Confirm(ConfirmedActivity observed)
    {
        var record=observed.Record;
        var from=confirmed.GetValueOrDefault(record.Id,record.StartUtc);
        if(record.EndUtc<=from)return;
        confirmed[record.Id]=record.EndUtc;
        while(confirmed.Count>1024)confirmed.Remove(confirmed.Keys.First());
        var browser=BrowserIdentity.FromProcess(record.ProcessName);
        if(browser is null)return;
        for(var i=0;i<contexts.Count;i++)
        {
            var context=contexts[i];var binding=context.Binding;
            if(binding is null||binding.Browser!=browser||binding.NativeWindow!=observed.NativeWindow)continue;
            var start=from>context.Start?from:context.Start;
            var end=record.EndUtc<context.Expires?record.EndUtc:context.Expires;
            if(i+1<contexts.Count&&contexts[i+1].Start<end)end=contexts[i+1].Start;
            if(end<=start)continue;
            var page=binding.Page;
            if(current is null||currentBinding!=binding||current.ActivityId!=record.Id||current.Browser!=browser||current.Domain!=page.Domain||
                current.DetailUrl!=page.DetailUrl||current.SearchQuery!=page.SearchQuery||current.EndUtc!=start)
            {
                if(current is not null)store.SaveBrowser(current with{Status=ActivityStatus.Completed});
                current=new(Guid.NewGuid(),record.Id,browser.Value,page.Domain,page.DetailUrl,page.SearchQuery,page.SearchEngine,start,start,ActivityStatus.Active);
                currentBinding=binding;
            }
            current=current with{EndUtc=end,Status=record.Status};store.SaveBrowser(current);
        }
    }
    public void Reset(){contexts.Clear();confirmed.Clear();current=null;currentBinding=null;}
}
