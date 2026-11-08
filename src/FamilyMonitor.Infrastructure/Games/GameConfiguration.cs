using System.Xml;
using System.Xml.Linq;
using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Infrastructure.Games;

public sealed record GameConfiguration(string ExternalId, string Name, string[] Executables)
{
    public static GameConfiguration Read(string xml, string directory)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "Game") throw new FormatException("Not a Microsoft game configuration.");
        var identity = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
        var visuals = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals");
        var id = (string?)identity?.Attribute("Name") ?? throw new FormatException("Missing game identity.");
        var name = (string?)visuals?.Attribute("DefaultDisplayName") ?? id;
        var executables = document.Descendants().Where(e => e.Name.LocalName == "Executable")
            .Select(e => (string?)e.Attribute("Name")).OfType<string>().Select(p => GameIdentity.Child(directory, p)).ToArray();
        if (executables.Length == 0) throw new FormatException("Missing game executables.");
        return new(id, name, executables);
    }
}
