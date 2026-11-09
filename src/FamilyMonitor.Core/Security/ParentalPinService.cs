using System.Security.Cryptography;

namespace FamilyMonitor.Core.Security;

public sealed record PinState
{
    public bool Enabled { get; init; }
    public string Salt { get; init; }="";
    public string Hash { get; init; }="";
    public string RecoverySalt { get; init; }="";
    public string RecoveryHash { get; init; }="";
    public int Iterations { get; init; }=600000;
    public int FailedAttempts { get; init; }
    public DateTimeOffset? LockedUntil { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}
public interface IPinStateStore { PinState Load();void Save(PinState state); }
public sealed record PinResult(bool Success,bool Locked,int AttemptsRemaining);

/// <summary>Protects settings UI only. Consent withdrawal, pause, stop and exit never require this service.</summary>
public sealed class ParentalPinService(IPinStateStore store,IClock clock)
{
    public bool Enabled=>store.Load().Enabled;
    public string Setup(string pin)
    {
        if(Enabled)throw new InvalidOperationException("Verify existing PIN before changing.");
        return SaveNew(pin);
    }
    public string? Change(string current,string next)
    {if(!Verify(current).Success)return null;return SaveNew(next);}
    private string SaveNew(string pin)
    {
        if(pin.Length is < 6 or > 12||!pin.All(char.IsAsciiDigit))throw new ArgumentException("PIN 6–12 rakam olmalıdır.");
        var salt=RandomNumberGenerator.GetBytes(32);var recoverySalt=RandomNumberGenerator.GetBytes(32);var recovery=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        store.Save(new(){Enabled=true,Salt=Convert.ToBase64String(salt),Hash=Derive(pin,salt,600000),RecoverySalt=Convert.ToBase64String(recoverySalt),RecoveryHash=Derive(recovery,recoverySalt,600000)});
        return recovery;
    }
    public PinResult Verify(string pin)=>Check(pin,false);
    public bool Disable(string pin){if(!Verify(pin).Success)return false;store.Save(new());return true;}
    public PinResult Recover(string code)
    {var result=Check(code.Trim().ToUpperInvariant(),true);if(result.Success)store.Save(new());return result;}
    private PinResult Check(string value,bool recovery)
    {
        var state=store.Load();if(!state.Enabled)return new(!recovery,false,5);
        var now=clock.UtcNow>state.LastSeenUtc?clock.UtcNow:state.LastSeenUtc;
        if(state.LockedUntil is { } until&&now<until)return new(false,true,0);
        var attempts=state.LockedUntil is not null?0:state.FailedAttempts;
        var matched=false;
        if(value.Length<=128&&state.Iterations is >= 600000 and <= 2000000)
        {
            try
            {
                var salt=Convert.FromBase64String(recovery?state.RecoverySalt:state.Salt);var expected=Convert.FromBase64String(recovery?state.RecoveryHash:state.Hash);
                if(salt.Length==32&&expected.Length==32)
                {
                    var derived=Rfc2898DeriveBytes.Pbkdf2(value,salt,state.Iterations,HashAlgorithmName.SHA256,32);
                    matched=CryptographicOperations.FixedTimeEquals(derived,expected);CryptographicOperations.ZeroMemory(derived);
                }
            }
            catch(FormatException){ }
        }
        if(matched){store.Save(state with{FailedAttempts=0,LockedUntil=null,LastSeenUtc=now});return new(true,false,5);}
        attempts++;var locked=attempts>=5;
        store.Save(state with{FailedAttempts=attempts,LockedUntil=locked?now.AddMinutes(15):null,LastSeenUtc=now});
        return new(false,locked,Math.Max(0,5-attempts));
    }
    private static string Derive(string value,byte[] salt,int iterations)=>Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(value,salt,iterations,HashAlgorithmName.SHA256,32));
}
