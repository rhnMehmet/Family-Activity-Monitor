using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FamilyMonitor.Core.Browsers;

namespace FamilyMonitor.Infrastructure.Browsers;

[SupportedOSPlatform("windows")]
public sealed class BrowserPipeServer : IDisposable
{
    private sealed record Client(NamedPipeServerStream Pipe,Channel<byte[]> Outbox);
    private readonly ConcurrentDictionary<Guid,Client> clients=new();
    private readonly CancellationTokenSource cancellation=new();
    private readonly SemaphoreSlim slots=new(16,16);
    private readonly BrowserRuntime runtime;
    private readonly string directory;
    private readonly string name;
    private readonly Func<NamedPipeServerStream,BrowserKind,bool> validateClient;
    private readonly Task listener;
    private bool disposed;
    public static string PipeName => NativeHostIdentity.PipeName(WindowsIdentity.GetCurrent().User!.Value,System.Diagnostics.Process.GetCurrentProcess().SessionId);
    public BrowserPipeServer(BrowserRuntime runtime,string directory)
        :this(runtime,directory,PipeName,null){ }
    internal BrowserPipeServer(BrowserRuntime runtime,string directory,string pipeName,Func<NamedPipeServerStream,BrowserKind,bool>? validation)
    {this.runtime=runtime;this.directory=Path.GetFullPath(directory);name=pipeName;validateClient=validation??TrustedClient;runtime.Correlator.PolicyChanged+=Broadcast;listener=Task.Run(Listen);}
    private async Task Listen()
    {
        while(!cancellation.IsCancellationRequested)
        {
            try{await slots.WaitAsync(cancellation.Token);}catch(OperationCanceledException){break;}
            NamedPipeServerStream? pipe=null;
            try
            {
                pipe=new NamedPipeServerStream(name,PipeDirection.InOut,16,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,8192,8192);
                await pipe.WaitForConnectionAsync(cancellation.Token);_ = Handle(pipe);
            }
            catch(OperationCanceledException){pipe?.Dispose();slots.Release();break;}
            catch(IOException){pipe?.Dispose();slots.Release();if(!cancellation.IsCancellationRequested)await Task.Delay(1000,cancellation.Token);}
        }
    }
    private async Task Handle(NamedPipeServerStream pipe)
    {
        var id=Guid.NewGuid();Task? writer=null;var outbox=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(8){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true,SingleWriter=false});
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        try
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var bytes=await NativeMessageProtocol.ReadAsync(pipe,timeout.Token);if(bytes is null)return;
            using var hello=JsonDocument.Parse(bytes);var root=hello.RootElement;
            if(!NativeMessageProtocol.Object(root,"version","type","browser","extensionId")||root.EnumerateObject().Count()!=4||root.GetProperty("version").GetInt32()!=1||root.GetProperty("type").GetString()!="hello"||
                !Enum.TryParse<BrowserKind>(root.GetProperty("browser").GetString(),out var browser)||!Enum.IsDefined(browser)||!validateClient(pipe,browser))return;
            if(!runtime.Correlator.Register(id,browser,root.GetProperty("extensionId").GetString()??""))return;
            var client=new Client(pipe,outbox);if(!clients.TryAdd(id,client))return;
            timeout.CancelAfter(TimeSpan.FromSeconds(35));writer=Write(client,timeout.Token);Send(id,client);
            while(!timeout.IsCancellationRequested)
            {
                bytes=await NativeMessageProtocol.ReadAsync(pipe,timeout.Token);if(bytes is null)break;
                if(!runtime.Receive(id,bytes))break;timeout.CancelAfter(TimeSpan.FromSeconds(35));
            }
        }
        catch(Exception error)when(error is IOException or OperationCanceledException or JsonException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException or FormatException){ }
        finally
        {
            clients.TryRemove(id,out _);runtime.Correlator.Disconnect(id);outbox.Writer.TryComplete();timeout.Cancel();pipe.Dispose();
            if(writer is not null)try{await writer;}catch(Exception error)when(error is IOException or OperationCanceledException or ObjectDisposedException){ }
            slots.Release();
        }
    }
    private static async Task Write(Client client,CancellationToken token)
    {await foreach(var bytes in client.Outbox.Reader.ReadAllAsync(token))await NativeMessageProtocol.WriteAsync(client.Pipe,bytes,token);}
    private void Send(Guid id,Client client)=>client.Outbox.Writer.TryWrite(NativeMessageProtocol.Encode(new{version=1,type="policy",policy=runtime.Correlator.Policy(id)}));
    private void Broadcast(){foreach(var pair in clients)Send(pair.Key,pair.Value);}
    private bool TrustedClient(NamedPipeServerStream pipe,BrowserKind browser)
    {
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out var processId))return false;
        var actual=new Windows.NativeWindowsActivityApi().GetExecutablePath(processId);
        return actual is not null&&Path.GetFullPath(actual).Equals(Path.Combine(directory,$"FamilyMonitor.NativeHost.{browser}.exe"),StringComparison.OrdinalIgnoreCase);
    }
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(nint pipe,out uint clientProcessId);
    public void Dispose()
    {
        if(disposed)return;disposed=true;runtime.Correlator.PolicyChanged-=Broadcast;cancellation.Cancel();
        foreach(var client in clients.Values)client.Pipe.Dispose();try{listener.GetAwaiter().GetResult();}catch(OperationCanceledException){ }
        cancellation.Dispose();
    }
}
