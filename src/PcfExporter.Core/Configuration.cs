using System.Text.Json;
namespace PcfExporter.Core;

public sealed class Configuration
{
    public int Version { get; set; } = 1;
    public List<FamilyGroup> Groups { get; set; } = [];
    public RunIdContext RunId { get; set; } = new();
    public void Validate()
    {
        if (Version != 1) throw new InvalidDataException($"Unsupported configuration version {Version}.");
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Name)) throw new InvalidDataException("Every group needs a name.");
            foreach (var f in g.Families) if (!assigned.Add(f)) throw new InvalidDataException($"Family '{f}' belongs to multiple groups.");
            foreach (var m in g.Mappings) if (!Fields.All.Contains(m.Key)) throw new InvalidDataException($"Unknown PCF field '{m.Key}'.");
        }
    }
}
public sealed class FamilyGroup
{
    public string Name { get; set; } = "New group";
    public List<string> Families { get; set; } = [];
    public Dictionary<string, FieldMapping> Mappings { get; set; } = new();
}
public sealed class FieldMapping
{
    public string Source { get; set; } = "Constant";
    public string Parameter { get; set; } = "";
    public string Constant { get; set; } = "";
    public bool RequireTranslation { get; set; }
    public Dictionary<string, string> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public static class Fields
{
    public static readonly string[] All = ["ComponentType", "Description", "Material", "Skey", "Specification", "ItemCode", "WallThickness", "End1", "End2", "Branch1", "FlatDirection", "SupportName", "SupportBore"];
}
public sealed class ConfigurationStore(string path)
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public Configuration Load() { if (!File.Exists(path)) return new(); var c = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Configuration is empty."); c.Validate(); return c; }
    public void Save(Configuration c)
    {
        c.Validate(); var temp = path + "." + Guid.NewGuid() + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(c, Options)); if (File.Exists(path)) File.Copy(path, path + ".bak", true); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
