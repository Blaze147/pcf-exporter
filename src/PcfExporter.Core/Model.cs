namespace PcfExporter.Core;

public sealed record Point(double X, double Y, double Z)
{
    public static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
}
public sealed record Port(Point Position, double Bore, Point Direction);
public sealed class ModelElement
{
    public string Id { get; init; } = "";
    public string Family { get; init; } = "";
    public string System { get; init; } = "";
    public bool Boundary { get; init; }
    public bool IsSupport { get; init; }
    public Point? Location { get; init; }
    public string? HostId { get; init; }
    public double? HostBore { get; init; }
    public List<Port> Ports { get; init; } = [];
    public HashSet<string> Neighbors { get; init; } = [];
    public Dictionary<string, string> Instance { get; init; } = new();
    public Dictionary<string, string> Type { get; init; } = new();
}
public sealed record Network(string Id, string Label, IReadOnlyList<ModelElement> Elements);
public static class NetworkDiscovery
{
    public static IReadOnlyList<Network> Discover(IEnumerable<ModelElement> input)
    {
        var all = input.Where(e => !e.Boundary).ToList();
        var supports = all.Where(e => e.IsSupport).ToList();
        var nodes = all.Where(e => !e.IsSupport).ToDictionary(e => e.Id);
        var adjacency = nodes.Keys.ToDictionary(k => k, _ => new HashSet<string>());
        foreach (var e in nodes.Values) foreach (var id in e.Neighbors) if (nodes.ContainsKey(id)) { adjacency[e.Id].Add(id); adjacency[id].Add(e.Id); }
        var unseen = nodes.Keys.ToHashSet(); var result = new List<Network>();
        foreach (var seed in nodes.Keys.Order(StringComparer.Ordinal))
        {
            if (!unseen.Remove(seed)) continue;
            var q = new Queue<string>(); q.Enqueue(seed); var members = new List<ModelElement>();
            while (q.TryDequeue(out var id)) { members.Add(nodes[id]); foreach (var n in adjacency[id]) if (unseen.Remove(n)) q.Enqueue(n); }
            var names = string.Join(", ", members.Select(e => e.System).Where(s => s.Length > 0).Distinct().Order());
            result.Add(new(seed, $"Network {result.Count + 1} • {(names.Length > 0 ? names : "Unnamed")} • {members.Count} elements", members));
        }
        foreach (var support in supports.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            var attached = result.Where(n => n.Elements.Any(e => !e.IsSupport &&
                (e.Id == support.HostId || support.Neighbors.Contains(e.Id) || e.Neighbors.Contains(support.Id)))).ToList();
            if (attached.Count > 1) throw new MappingException($"Support {support.Id} touches multiple disconnected networks. Correct its host/connections before exporting.");
            if (attached.Count == 1)
            {
                var network = attached[0];
                var members = network.Elements.Append(support).ToList();
                result[result.IndexOf(network)] = network with { Elements = members, Label = network.Label + $" + support {support.Id}" };
            }
            else result.Add(new(support.Id, $"Unattached support {support.Id} • {support.Family}", [support]));
        }
        return result;
    }
}
public sealed record ResolvedElement(ModelElement Element, IReadOnlyDictionary<string, string> Values)
{
    public string this[string key] => Values.GetValueOrDefault(key, "");
}
public sealed class MappingException(string message) : Exception(message);
public static class MappingResolver
{
    public static bool IsSupport(ModelElement e, Configuration c)
    {
        var group = c.Groups.SingleOrDefault(g => g.Families.Contains(e.Family, StringComparer.Ordinal));
        if (group is null || !group.Mappings.TryGetValue("ComponentType", out var mapping)) return false;
        var value = mapping.Source switch { "Constant" => mapping.Constant, "Instance" => e.Instance.GetValueOrDefault(mapping.Parameter), "Type" => e.Type.GetValueOrDefault(mapping.Parameter), _ => null };
        if (value is null) return false;
        var translated = mapping.Translations.FirstOrDefault(p => string.Equals(p.Key, value, StringComparison.OrdinalIgnoreCase));
        if (translated.Key is not null) value = translated.Value;
        return string.Equals(value, "SUPPORT", StringComparison.OrdinalIgnoreCase);
    }
    public static ResolvedElement Resolve(ModelElement e, Configuration c)
    {
        var groups = c.Groups.Where(g => g.Families.Contains(e.Family, StringComparer.Ordinal)).ToList();
        if (groups.Count != 1) throw new MappingException($"Element {e.Id}, family '{e.Family}': expected one mapping group, found {groups.Count}.");
        var values = new Dictionary<string, string>();
        foreach (var (field, m) in groups[0].Mappings)
        {
            string? raw = m.Source switch { "Constant" => m.Constant, "Instance" => e.Instance.GetValueOrDefault(m.Parameter), "Type" => e.Type.GetValueOrDefault(m.Parameter), _ => throw new MappingException($"Unknown source '{m.Source}'.") };
            if (raw is null) throw new MappingException($"Element {e.Id}: missing {m.Source} parameter '{m.Parameter}' for {field}.");
            var match = m.Translations.FirstOrDefault(p => string.Equals(p.Key, raw, StringComparison.OrdinalIgnoreCase));
            if (match.Key is not null) raw = match.Value;
            else if (m.RequireTranslation) throw new MappingException($"Element {e.Id}: unmapped value '{raw}' for {field}.");
            if (raw.Contains('\n') || raw.Contains('\r')) throw new MappingException($"Element {e.Id}: {field} contains a line break.");
            values[field] = raw;
        }
        foreach (var required in new[] { "ComponentType", "Description" }) if (string.IsNullOrWhiteSpace(values.GetValueOrDefault(required))) throw new MappingException($"Element {e.Id}: {required} is required.");
        return new(e, values);
    }
}
