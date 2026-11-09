using FamilyMonitor.Core.Security;
using FamilyMonitor.Infrastructure;
using FamilyMonitor.Infrastructure.Windows;
using Xunit;

namespace FamilyMonitor.Tests.Browsers;

public sealed class PinAndStartupTests
{
    [Fact]
    public void PinAndRecoveryAreSaltedDerivedAndNeverStoredAsPlainText()
    {
        var store=new PinStore();var service=new ParentalPinService(store,new FakeClock());var recovery=service.Setup("263849");
        Assert.True(service.Enabled);Assert.NotEqual("263849",store.State.Hash);Assert.NotEqual(recovery,store.State.RecoveryHash);
        Assert.Equal(32,Convert.FromBase64String(store.State.Salt).Length);Assert.Equal(600000,store.State.Iterations);
        Assert.True(service.Verify("263849").Success);Assert.False(service.Verify("111111").Success);
        var first=store.State.Hash;var second=new PinStore();new ParentalPinService(second,new FakeClock()).Setup("263849");
        Assert.NotEqual(first,second.State.Hash);
    }
    [Fact]
    public void FiveFailuresPersistCooldownAcrossServiceRestartAndBackwardClock()
    {
        var clock=new FakeClock();var store=new PinStore();var service=new ParentalPinService(store,clock);service.Setup("263849");
        for(var i=0;i<5;i++)Assert.False(service.Verify("111111").Success);
        service=new(store,clock);Assert.True(service.Verify("263849").Locked);
        clock.UtcNow=clock.UtcNow.AddDays(-1);Assert.True(service.Verify("263849").Locked);
        clock.UtcNow=store.State.LastSeenUtc.AddMinutes(15);Assert.True(service.Verify("263849").Success);
    }
    [Fact]
    public void RecoveryRequiresActualOneTimeCodeAndOldCodeCannotResetNewPin()
    {
        var store=new PinStore();var service=new ParentalPinService(store,new FakeClock());var recovery=service.Setup("263849");
        Assert.False(service.Recover("invented").Success);Assert.True(service.Recover(recovery).Success);Assert.False(service.Enabled);
        service.Setup("487251");Assert.False(service.Recover(recovery).Success);Assert.True(service.Enabled);
    }
    [Fact]
    public void PinChangeAndDisableRequireCurrentPin()
    {
        var service=new ParentalPinService(new PinStore(),new FakeClock());service.Setup("263849");
        Assert.Throws<InvalidOperationException>(()=>service.Setup("123456"));Assert.Null(service.Change("000000","123456"));
        Assert.NotNull(service.Change("263849","487251"));Assert.False(service.Disable("263849"));Assert.True(service.Disable("487251"));Assert.False(service.Enabled);
    }
    [Theory]
    [InlineData("1234")]
    [InlineData("abcdef")]
    [InlineData("1234567890123")]
    public void WeakOrInvalidPinIsRejected(string pin)=>Assert.Throws<ArgumentException>(()=>new ParentalPinService(new PinStore(),new FakeClock()).Setup(pin));
    [Fact]
    public void CorruptHashAndExcessiveKdfWorkFailClosed()
    {
        var store=new PinStore{State=new(){Enabled=true,Hash="malformed",Salt="malformed",Iterations=int.MaxValue}};
        Assert.False(new ParentalPinService(store,new FakeClock()).Verify("263849").Success);
    }
    [Fact]
    public void StartupQuotesPathAndRollsBackRegistrationIfSettingsSaveFails()
    {
        var registry=new Startup();var path=Path.GetFullPath(Path.Combine("test app","FamilyMonitor.Agent.exe"));var service=new AutoStartService(registry,path);
        service.Apply(true,()=>{});Assert.Equal('"'+path+"\" --autostart",registry.Command);Assert.True(service.IsRegistered);
        Assert.Throws<IOException>(()=>service.Apply(false,()=>throw new IOException()));Assert.True(service.IsRegistered);
        service.Apply(false,()=>{});Assert.Null(registry.Command);
    }
    [Fact]
    public void StartupRejectsShellInjectionAndNonExecutableTargets()
    {
        Assert.Throws<ArgumentException>(()=>new AutoStartService(new Startup(),"relative.exe").Command);
        Assert.Throws<ArgumentException>(()=>new AutoStartService(new Startup(),Path.GetFullPath("agent.dll")).Command);
    }
    [Theory]
    [InlineData(false,true,true,1,true,false)]
    [InlineData(true,false,true,1,true,false)]
    [InlineData(true,true,false,1,true,false)]
    [InlineData(true,true,true,0,true,false)]
    [InlineData(true,true,true,1,false,false)]
    [InlineData(true,true,true,1,true,true)]
    public void AutoTrackingRequiresOptInValidConsentInteractiveSessionAndWts(bool autoLaunch,bool consent,bool interactive,int session,bool notifications,bool expected)
    {
        var settings=new LocalSettings{ConsentGranted=consent,PrivacyVersion=1,AutoStartEnabled=true,AutoTrackOnLogin=true};
        Assert.Equal(expected,AutoStartService.MayTrack(autoLaunch,settings,interactive,session,notifications));
        Assert.False(AutoStartService.MayTrack(autoLaunch,settings with{AutoTrackOnLogin=false},interactive,session,notifications));
        Assert.False(AutoStartService.MayTrack(autoLaunch,settings with{PrivacyVersion=0},interactive,session,notifications));
    }
    private sealed class PinStore:IPinStateStore {public PinState State=new();public PinState Load()=>State;public void Save(PinState value)=>State=value;}
    private sealed class Startup:IStartupRegistration {public string? Command;public string? Read()=>Command;public void Write(string? command)=>Command=command;}
}
