namespace FamilyMonitor.Infrastructure.Games;

/// <summary>Bounded parser for the text subset of Valve KeyValues. No account/userdata files are read.</summary>
public sealed record ValveNode(string Key, string? Value, IReadOnlyList<ValveNode> Children)
{
    public string? Get(string name) => Children.FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    public ValveNode? Child(string name) => Children.FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public static class ValveKeyValues
{
    public static ValveNode Parse(string text)
    {
        if (text.Length > 2 * 1024 * 1024) throw new FormatException("Valve document too large.");
        var index = 0;
        var count = 0;
        string? Token()
        {
            while (index < text.Length)
            {
                if (char.IsWhiteSpace(text[index]) || text[index] == '\uFEFF') { index++; continue; }
                if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '/')
                { while (index < text.Length && text[index] != '\n') index++; continue; }
                break;
            }
            if (index >= text.Length) return null;
            if (text[index] is '{' or '}') return text[index++].ToString();
            if (text[index] != '"')
            {
                var start = index;
                while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] is not '{' and not '}') index++;
                return text[start..index];
            }
            index++;
            var value = new System.Text.StringBuilder();
            while (index < text.Length)
            {
                var c = text[index++];
                if (c == '"') return value.ToString();
                if (c == '\\' && index < text.Length && text[index] is '\\' or '"') c = text[index++];
                value.Append(c);
            }
            throw new FormatException("Unterminated Valve string.");
        }
        List<ValveNode> Block(int depth)
        {
            if (depth > 32) throw new FormatException("Valve nesting too deep.");
            var nodes = new List<ValveNode>();
            while (Token() is { } key)
            {
                if (key == "}") { if (depth == 0) throw new FormatException("Unexpected brace."); return nodes; }
                if (++count > 100000 || key == "{") throw new FormatException("Invalid Valve structure.");
                var value = Token() ?? throw new FormatException("Missing Valve value.");
                if (value == "}") throw new FormatException("Missing Valve value.");
                nodes.Add(value == "{" ? new(key, null, Block(depth + 1)) : new(key, value, []));
            }
            if (depth != 0) throw new FormatException("Unclosed Valve block.");
            return nodes;
        }
        return new("root", null, Block(0));
    }
}
