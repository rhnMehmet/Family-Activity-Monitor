using System.Text.Json;
using FamilyMonitor.Core.Security;

namespace FamilyMonitor.Infrastructure.Security;

public sealed class LocalPinStateStore(string directory):IPinStateStore
{
    private string FilePath=>Path.Combine(directory,"parental-security.json");
    public PinState Load()
    {
        if(!File.Exists(FilePath))return new();
        try{return JsonSerializer.Deserialize<PinState>(File.ReadAllText(FilePath))??new(){Enabled=true};}
        catch(JsonException){return new(){Enabled=true};}
    }
    public void Save(PinState state)
    {
        Directory.CreateDirectory(directory);var temporary=FilePath+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(state));File.Move(temporary,FilePath,true);
    }
}
