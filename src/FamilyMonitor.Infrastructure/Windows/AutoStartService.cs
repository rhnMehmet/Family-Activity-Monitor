using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FamilyMonitor.Infrastructure.Windows;

public interface IStartupRegistration { string? Read();void Write(string? command); }

public sealed class AutoStartService(IStartupRegistration registration,string executable)
{
    public string Command
    {
        get
        {
            if(!Path.IsPathFullyQualified(executable)||executable.Contains('"')||!Path.GetExtension(executable).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("A published Windows executable is required.");
            var command='"'+Path.GetFullPath(executable)+"\" --autostart";
            if(command.Length>260)throw new ArgumentException("Startup command is too long.");return command;
        }
    }
    public bool IsRegistered=>string.Equals(registration.Read(),Command,StringComparison.OrdinalIgnoreCase);
    public void Apply(bool enabled,Action persistSettings)
    {
        var previous=registration.Read();registration.Write(enabled?Command:null);
        try{persistSettings();}catch{registration.Write(previous);throw;}
    }
    public static bool MayTrack(bool autoLaunch,LocalSettings settings,bool userInteractive,int session,bool notificationsReady) =>
        autoLaunch&&settings.AutoStartEnabled&&settings.AutoTrackOnLogin&&settings.ConsentGranted&&settings.PrivacyVersion==LocalSettingsStore.CurrentPrivacyVersion&&userInteractive&&session>0&&notificationsReady;
}

[SupportedOSPlatform("windows")]
public sealed class WindowsStartupRegistration:IStartupRegistration
{
    private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name="FamilyActivityMonitor";
    public string? Read(){using var key=Registry.CurrentUser.OpenSubKey(Key);return key?.GetValue(Name) as string;}
    public void Write(string? command)
    {
        using var key=Registry.CurrentUser.CreateSubKey(Key,true);
        if(command is null)key.DeleteValue(Name,false);else key.SetValue(Name,command,RegistryValueKind.String);
    }
}
