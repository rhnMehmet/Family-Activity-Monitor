namespace FamilyMonitor.Core.Browsers;

public static class NativeHostIdentity
{
    public static string PipeName(string sid,int session) => "FamilyActivityMonitor.browser.v1."+
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sid+":"+session)))[..24];
    public static BrowserKind? FromExecutable(string path)
    {
        var name=Path.GetFileName(path);
        return Enum.GetValues<BrowserKind>().Cast<BrowserKind?>().FirstOrDefault(kind=>string.Equals(name,$"FamilyMonitor.NativeHost.{kind}.exe",StringComparison.OrdinalIgnoreCase));
    }
    public static bool ArgumentsAllowed(BrowserKind browser,string[] arguments,string manifestPath)
    {
        if(browser==BrowserKind.Firefox)
        {
            if(arguments.Length!=2||arguments[1]!=BrowserIdentity.FirefoxExtensionId)return false;
            try{return Path.GetFullPath(arguments[0]).Equals(Path.GetFullPath(manifestPath),StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}
        }
        if(arguments.Length is < 1 or > 2||arguments[0].TrimEnd('/')!=$"chrome-extension://{BrowserIdentity.ChromiumExtensionId}")return false;
        return arguments.Length==1||arguments[1].StartsWith("--parent-window=",StringComparison.Ordinal)&&long.TryParse(arguments[1][16..],out var handle)&&handle>=0;
    }
}
