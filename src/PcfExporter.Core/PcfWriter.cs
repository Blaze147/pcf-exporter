using System.Globalization;
using System.Text;
namespace PcfExporter.Core;
// Factory-based component dispatch follows the upstream exporter architecture.
// Revit-independent records prevent model mutation during export.
public static class PcfElementFactory
{
    public static readonly string[] Supported = ["PIPE", "ELBOW", "TEE", "REDUCER-CONCENTRIC", "REDUCER-ECCENTRIC", "VALVE", "FLANGE", "FLANGE-BLIND", "CAP", "GASKET", "COUPLING", "UNION", "FILTER", "INSTRUMENT", "MISC-COMPONENT", "SUPPORT"];
    public static string Write(ResolvedElement r, int materialId)
    {
        var type = r["ComponentType"].ToUpperInvariant(); var ports = r.Element.Ports;
        if (!Supported.Contains(type)) throw new MappingException($"Element {r.Element.Id}: unsupported component type '{type}'.");
        if (type == "SUPPORT") return WriteSupport(r, materialId);
        int expected = type == "TEE" ? 3 : type is "CAP" or "FLANGE-BLIND" ? 1 : 2;
        if (ports.Count != expected) throw new MappingException($"Element {r.Element.Id}: {type} requires {expected} physical piping ports, found {ports.Count}.");
        if (ports.Any(p => p.Bore <= 0 || !double.IsFinite(p.Bore) || !Finite(p.Position) || !Finite(p.Direction))) throw new MappingException($"Element {r.Element.Id}: invalid port geometry.");
        var sb = new StringBuilder().AppendLine(type);
        IEnumerable<Port> ordered = ports;
        if (type == "TEE")
        {
            var pairs = from i in Enumerable.Range(0, 3) from j in Enumerable.Range(i + 1, 3 - i - 1) select (i, j, d: Dot(ports[i].Direction, ports[j].Direction));
            var run = pairs.MinBy(p => p.d); ordered = new[] { ports[run.i], ports[run.j], ports[3 - run.i - run.j] };
        }
        var list = ordered.ToList();
        for (int i = 0; i < list.Count; i++) sb.AppendLine($"    {(i == 2 ? "BRANCH1-POINT" : "END-POINT")} {Format(list[i].Position)} {N(list[i].Bore)}{(r[i == 2 ? "Branch1" : i == 0 ? "End1" : "End2"].Length > 0 ? " " + r[i == 2 ? "Branch1" : i == 0 ? "End1" : "End2"] : "")}");
        if (type is "ELBOW" or "TEE" or "VALVE" or "INSTRUMENT")
        {
            var center = type is "ELBOW" or "TEE" ? Intersection(list[0], list[type == "TEE" ? 2 : 1], r.Element.Id) : Point.Mid(list[0].Position, list[1].Position);
            sb.AppendLine($"    CENTRE-POINT {Format(center)}");
        }
        if (type == "ELBOW")
        {
            double cosine = -Dot(list[0].Direction, list[1].Direction) / Math.Sqrt(Dot(list[0].Direction, list[0].Direction) * Dot(list[1].Direction, list[1].Direction));
            if (!double.IsFinite(cosine)) throw new MappingException($"Element {r.Element.Id}: invalid elbow axes.");
            sb.AppendLine($"    ANGLE {Math.Round(Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI * 100).ToString(CultureInfo.InvariantCulture)}");
        }
        if (type == "REDUCER-ECCENTRIC")
        {
            if (!new[] { "NORTH", "SOUTH", "EAST", "WEST", "UP", "DOWN" }.Contains(r["FlatDirection"])) throw new MappingException($"Element {r.Element.Id}: eccentric reducer requires FlatDirection.");
            sb.AppendLine($"    FLAT-DIRECTION {r["FlatDirection"]}");
        }
        if (r["WallThickness"].Length > 0)
        {
            if (!double.TryParse(r["WallThickness"], NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness) || thickness <= 0 || !double.IsFinite(thickness)) throw new MappingException($"Element {r.Element.Id}: WallThickness must be a positive number in millimeters.");
            sb.AppendLine($"    COMPONENT-ATTRIBUTE1 {N(thickness)}");
        }
        if (r["Skey"].Length > 0) sb.AppendLine($"    SKEY {r["Skey"]}");
        if (r["Specification"].Length > 0) sb.AppendLine($"    PIPING-SPEC {r["Specification"]}");
        if (r["ItemCode"].Length > 0) sb.AppendLine($"    ITEM-CODE {r["ItemCode"]}");
        sb.AppendLine($"    MATERIAL-IDENTIFIER {materialId}"); return sb.ToString();
    }
    static string WriteSupport(ResolvedElement r, int materialId)
    {
        var point = r.Element.Location ?? throw new MappingException($"Support {r.Element.Id}: family insertion point is missing.");
        if (!Finite(point)) throw new MappingException($"Support {r.Element.Id}: invalid insertion point.");
        double bore = r.Element.Ports.FirstOrDefault()?.Bore ?? r.Element.HostBore ?? 0;
        if (r["SupportBore"].Length > 0 && !double.TryParse(r["SupportBore"], NumberStyles.Float, CultureInfo.InvariantCulture, out bore))
            throw new MappingException($"Support {r.Element.Id}: SupportBore must be a number in millimeters.");
        if (!double.IsFinite(bore) || bore <= 0) throw new MappingException($"Support {r.Element.Id}: map SupportBore in millimeters or provide a round piping connector/pipe host.");
        var sb = new StringBuilder().AppendLine("SUPPORT");
        sb.AppendLine($"    CO-ORDS {Format(point)} {N(bore)}");
        if (r["SupportName"].Length > 0) sb.AppendLine($"    NAME {r["SupportName"]}");
        if (r["Skey"].Length > 0) sb.AppendLine($"    SKEY {r["Skey"]}");
        if (r["Specification"].Length > 0) sb.AppendLine($"    PIPING-SPEC {r["Specification"]}");
        if (r["ItemCode"].Length > 0) sb.AppendLine($"    ITEM-CODE {r["ItemCode"]}");
        sb.AppendLine($"    MATERIAL-IDENTIFIER {materialId}");
        return sb.ToString();
    }
    static bool Finite(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    static Point Intersection(Port a, Port b, string id)
    {
        var u = a.Direction; var v = b.Direction; var w = new Point(a.Position.X - b.Position.X, a.Position.Y - b.Position.Y, a.Position.Z - b.Position.Z);
        double aa = Dot(u, u), bb = Dot(u, v), cc = Dot(v, v), dd = Dot(u, w), ee = Dot(v, w), den = aa * cc - bb * bb;
        if (Math.Abs(den) < 1e-10) throw new MappingException($"Element {id}: cannot derive centre from parallel connector axes.");
        double t = (bb * ee - cc * dd) / den, s = (aa * ee - bb * dd) / den;
        var p = new Point(a.Position.X + t * u.X, a.Position.Y + t * u.Y, a.Position.Z + t * u.Z);
        var q = new Point(b.Position.X + s * v.X, b.Position.Y + s * v.Y, b.Position.Z + s * v.Z);
        var d = new Point(p.X - q.X, p.Y - q.Y, p.Z - q.Z);
        if (Dot(d, d) > 0.01) throw new MappingException($"Element {id}: connector axes do not intersect."); return Point.Mid(p, q);
    }
    static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    static string Format(Point p) => $"{N(p.X)} {N(p.Y)} {N(p.Z)}";
}
public static class PcfWriter
{
    public static string Compose(IEnumerable<Network> networks, Configuration config)
    {
        config.Validate(); var resolved = networks.Select(n => (Network: n, Elements: n.Elements.Select(e => MappingResolver.Resolve(e, config)).ToList())).ToList();
        if (resolved.Count == 0 || resolved.All(n => n.Elements.Count == 0)) throw new MappingException("No piping elements selected.");
        var materials = new Dictionary<(string Description, string Material), int>();
        var sb = new StringBuilder("ISOGEN-FILES ISOGEN.FLS\nUNITS-BORE MM\nUNITS-CO-ORDS MM\nUNITS-WEIGHT KGS\nUNITS-WEIGHT-LENGTH METER\n");
        foreach (var (network, elements) in resolved)
        {
            sb.AppendLine($"PIPELINE-REFERENCE NETWORK-{network.Id}");
            foreach (var e in elements.OrderBy(e => e.Element.Id, StringComparer.Ordinal))
            {
                var key = (e["Description"], e["Material"]); if (!materials.TryGetValue(key, out int mat)) { mat = materials.Count + 1; materials.Add(key, mat); }
                sb.Append(PcfElementFactory.Write(e, mat));
            }
        }
        sb.AppendLine("MATERIALS"); foreach (var (key, id) in materials) { sb.AppendLine($"    MATERIAL-IDENTIFIER {id}"); sb.AppendLine($"        DESCRIPTION {key.Description}{(key.Material.Length > 0 ? ", " + key.Material : "")}"); }
        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
    public static IReadOnlyDictionary<string, string> Plan(IEnumerable<Network> networks, Configuration config, bool separate)
    {
        var list = networks.ToList(); if (list.Count == 0) throw new MappingException("No networks selected.");
        return separate ? list.ToDictionary(n => $"network-{n.Id}.pcf", n => Compose([n], config)) : new Dictionary<string, string> { { "piping.pcf", Compose(list, config) } };
    }
}
