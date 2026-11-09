using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FamilyMonitor.Core.Browsers;

namespace FamilyMonitor.NativeHost;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var browser=NativeHostIdentity.FromExecutable(Environment.ProcessPath??"");
        if(browser is null)return 2;
        var manifest=Path.Combine(AppContext.BaseDirectory,"native-host-manifests",browser.Value.ToString().ToLowerInvariant()+".json");
        if(!NativeHostIdentity.ArgumentsAllowed(browser.Value,args,manifest)||!ParentMatches(browser.Value))return 3;
        using var cancellation=new CancellationTokenSource();
        try
        {
            using var pipe=new NamedPipeClientStream(".",NativeHostIdentity.PipeName(WindowsIdentity.GetCurrent().User!.Value,Process.GetCurrentProcess().SessionId),PipeDirection.InOut,PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1500,cancellation.Token);
            if(!TrustedServer(pipe))return 5;
            await NativeMessageProtocol.WriteAsync(pipe,NativeMessageProtocol.Encode(new{version=1,type="hello",browser=browser.Value.ToString(),extensionId=BrowserIdentity.ExtensionId(browser.Value)}),cancellation.Token);
            using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
            var incoming=Forward(input,pipe,cancellation.Token);var outgoing=Forward(pipe,output,cancellation.Token);
            var completed=await Task.WhenAny(incoming,outgoing);cancellation.Cancel();await completed;
            // Do not await a blocking stdin read after the Agent disconnects: close the native port immediately.
            return 0;
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or UnauthorizedAccessException or System.Text.Json.JsonException){return 4;}
        // Never write diagnostics to stdout (native protocol) or log payloads/URLs.
    }
    private static async Task Forward(Stream input,Stream output,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {var bytes=await NativeMessageProtocol.ReadAsync(input,token);if(bytes is null)return;await NativeMessageProtocol.WriteAsync(output,bytes,token);}
    }
    private static bool ParentMatches(BrowserKind browser)
    {
        var snapshot=CreateToolhelp32Snapshot(2,0);if(snapshot==new nint(-1))return false;
        try
        {
            var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>(),Exe=""};var own=(uint)Environment.ProcessId;
            if(!Process32First(snapshot,ref entry))return false;
            do
            {
                if(entry.ProcessId!=own)continue;
                try
                {
                    using var parent=Process.GetProcessById((int)entry.ParentId);
                    return BrowserIdentity.FromProcess(parent.ProcessName)==browser;
                }
                catch(Exception error)when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
            }while(Process32Next(snapshot,ref entry));
            return false;
        }
        finally{CloseHandle(snapshot);}
    }
    private static bool TrustedServer(NamedPipeClientStream pipe)
    {
        if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out var id))return false;
        var process=OpenProcess(0x1000,false,id);if(process==0)return false;
        try
        {
            var text=new System.Text.StringBuilder(32768);var count=text.Capacity;
            return QueryFullProcessImageName(process,0,text,ref count)&&Path.GetFullPath(text.ToString())
                .Equals(Path.Combine(AppContext.BaseDirectory,"FamilyMonitor.Agent.exe"),StringComparison.OrdinalIgnoreCase);
        }
        finally{CloseHandle(process);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct ProcessEntry
    {public uint Size,Usage,ProcessId;public nuint Heap;public uint Module,Threads,ParentId;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Exe;}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern nint CreateToolhelp32Snapshot(uint flags,uint processId);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="Process32FirstW")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool Process32First(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="Process32NextW")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool Process32Next(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetNamedPipeServerProcessId(nint pipe,out uint process);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern nint OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="QueryFullProcessImageNameW",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool QueryFullProcessImageName(nint process,uint flags,System.Text.StringBuilder path,ref int count);
}
