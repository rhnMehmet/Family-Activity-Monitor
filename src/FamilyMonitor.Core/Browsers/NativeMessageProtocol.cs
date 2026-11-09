using System.Buffers.Binary;
using System.Text.Json;

namespace FamilyMonitor.Core.Browsers;

public static class NativeMessageProtocol
{
    public const int MaximumBytes = 8192;
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellation = default)
    {
        var prefix = new byte[4];
        var first = await stream.ReadAsync(prefix.AsMemory(0, 1), cancellation);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1), cancellation);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (count is 0 or > MaximumBytes) throw new InvalidDataException("Invalid message length.");
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellation);
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Object required.");
        return bytes;
    }
    public static async Task WriteAsync(Stream stream, byte[] bytes, CancellationToken cancellation = default)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw new InvalidDataException("Invalid message length.");
        var prefix = new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(prefix,(uint)bytes.Length);
        await stream.WriteAsync(prefix,cancellation);await stream.WriteAsync(bytes,cancellation);await stream.FlushAsync(cancellation);
    }
    public static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static bool Object(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var names = element.EnumerateObject().Select(p => p.Name).ToArray();
        return names.Length == names.Distinct(StringComparer.Ordinal).Count() && names.All(fields.Contains);
    }
    public static bool TrySnapshot(byte[] bytes, out BrowserSnapshot? snapshot, out string? type, out long sequence)
    {
        snapshot=null;type=null;sequence=0;
        try
        {
            using var document=JsonDocument.Parse(bytes,new(){MaxDepth=8});var root=document.RootElement;
            if (!Object(root,"version","type","seq","epoch","windowId","tabId","focused","private","page") ||
                root.GetProperty("version").GetInt32()!=1) return false;
            type=root.GetProperty("type").GetString();sequence=root.GetProperty("seq").GetInt64();
            if(sequence is < 1 or > 9007199254740991 || type is not ("snapshot" or "heartbeat" or "ready"))return false;
            if(type!="snapshot")return root.EnumerateObject().Count()==3;
            if(root.EnumerateObject().Count()!=9)return false;
            var epoch=root.GetProperty("epoch").GetString();var window=root.GetProperty("windowId").GetInt32();var tab=root.GetProperty("tabId").GetInt32();
            if(!Guid.TryParseExact(epoch,"N",out _)||window<0||tab<0)return false;
            var focused=root.GetProperty("focused").GetBoolean();var privateWindow=root.GetProperty("private").GetBoolean();var page=root.GetProperty("page");BrowserPage? parsed=null;
            if(page.ValueKind!=JsonValueKind.Null)
            {
                if(privateWindow||!focused||!Object(page,"domain","detailUrl","searchQuery","searchEngine"))return false;
                var domain=page.GetProperty("domain").GetString();if(domain is null||domain.Length>253)return false;
                string? Text(string key,int max){if(!page.TryGetProperty(key,out var p)||p.ValueKind==JsonValueKind.Null)return null;var value=p.GetString();if(value?.Length>max)throw new InvalidDataException();return value;}
                parsed=new(domain,Text("detailUrl",1024),Text("searchQuery",240),Text("searchEngine",32));
            }
            snapshot=new(sequence,epoch!,window,tab,focused,privateWindow,parsed);return true;
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or InvalidDataException){return false;}
    }
}
