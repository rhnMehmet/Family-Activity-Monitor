using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FamilyMonitor.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public static class WindowsSessionNotifications
{
    public const int SessionChangeMessage = 0x02B1;
    public static bool Register(nint window) => WTSRegisterSessionNotification(window, 0 /* NOTIFY_FOR_THIS_SESSION */);
    public static void Unregister(nint window) => WTSUnRegisterSessionNotification(window);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);
}
