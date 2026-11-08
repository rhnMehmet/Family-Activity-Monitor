using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FamilyMonitor.Core;
using FamilyMonitor.Core.Input;

namespace FamilyMonitor.Infrastructure.Windows;

public readonly record struct ControllerSample(bool Connected, ushort Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY);
public interface IXInputApi { ControllerSample Read(uint index); }

public sealed class XInputActivityProvider(IXInputApi api, IClock clock) : IControllerActivityProvider
{
    private readonly TimeSpan[] retryAfter = new TimeSpan[4];
    private bool unavailable;
    public ControllerEvidence Read()
    {
        if(unavailable)return new(false,false,false);
        var connected = false; var active = false;
        try
        {
            for (uint index=0;index<4;index++)
            {
                if (clock.Elapsed < retryAfter[index]) continue;
                var state=api.Read(index);
                if (!state.Connected) { retryAfter[index]=clock.Elapsed+TimeSpan.FromSeconds(5); continue; }
                connected=true;
                active |= state.Buttons!=0 || state.LeftTrigger>30 || state.RightTrigger>30 ||
                    (long)state.LeftX*state.LeftX+(long)state.LeftY*state.LeftY > 7849L*7849 ||
                    (long)state.RightX*state.RightX+(long)state.RightY*state.RightY > 8689L*8689;
                // Raw state is discarded at this iteration; only activity/connection booleans escape.
            }
            return new(connected,active);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { unavailable=true; return new(false,false,false); }
    }
}

[SupportedOSPlatform("windows")]
public sealed class NativeXInputApi : IXInputApi
{
    public ControllerSample Read(uint index)
    {
        if (XInputGetState(index,out var state)!=0) return new(false,0,0,0,0,0,0,0);
        return new(true,state.Buttons,state.LeftTrigger,state.RightTrigger,state.LeftX,state.LeftY,state.RightX,state.RightY);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct State { public uint Packet; public ushort Buttons; public byte LeftTrigger,RightTrigger; public short LeftX,LeftY,RightX,RightY; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index,out State state);
}
