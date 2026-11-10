using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using PcfExporter.Core;
namespace PcfExporter.Revit;

public sealed class Snapshot
{
    public List<ModelElement> Elements { get; } = [];
    public Func<Snapshot>? Reload { get; private set; }
    public HashSet<string> Selection { get; } = [];
    public List<ModelElement> FamilyCatalog { get; } = [];
    public HashSet<string> AvailableFamilies { get; } = [];
    public static Snapshot Read(UIDocument ui)
    {
        var s = new Snapshot { Reload = () => Read(ui) }; var doc = ui.Document;
        var config = App.Store.Load(); config.Validate();
        foreach (FamilyInstance family in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))) s.AvailableFamilies.Add(family.Symbol.Family.Name);
        foreach (var id in ui.Selection.GetElementIds()) s.Selection.Add(id.Value.ToString());
        var categories = new[] { BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_StructuralFraming };
        var filter = new ElementMulticategoryFilter(categories);
        foreach (var e in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType())
        {
            ConnectorManager? cm = e is MEPCurve curve ? curve.ConnectorManager : (e as FamilyInstance)?.MEPModel?.ConnectorManager;
            var type = doc.GetElement(e.GetTypeId());
            string family = e is FamilyInstance fi ? fi.Symbol.Family.Name : "Pipes";
            var instanceParameters = Parameters(e); var typeParameters = Parameters(type);
            s.FamilyCatalog.Add(new() { Id = e.Id.Value.ToString(), Family = family, Instance = instanceParameters, Type = typeParameters });
            bool support = MappingResolver.IsSupport(new() { Family = family, Instance = instanceParameters, Type = typeParameters }, config);
            bool pipingCategory = e.Category.Id.Value == (long)BuiltInCategory.OST_PipeCurves || e.Category.Id.Value == (long)BuiltInCategory.OST_PipeFitting || e.Category.Id.Value == (long)BuiltInCategory.OST_PipeAccessory;
            if (!support && !pipingCategory && e.Category.Id.Value != (long)BuiltInCategory.OST_MechanicalEquipment) continue;
            if (cm is null && !support) continue;
            var ports = new List<Port>(); var neighbors = new HashSet<string>();
            foreach (Connector c in cm?.Connectors ?? new ConnectorSet())
            {
                if (c.Domain != Domain.DomainPiping) continue;
                if (c.ConnectorType is ConnectorType.End or ConnectorType.Curve)
                    foreach (Connector other in c.AllRefs)
                        if (other.Owner.Id != e.Id && other.Domain == Domain.DomainPiping && other.ConnectorType is ConnectorType.End or ConnectorType.Curve) neighbors.Add(other.Owner.Id.Value.ToString());
                if (c.ConnectorType != ConnectorType.End) continue;
                if (c.Shape != ConnectorProfileType.Round) throw new InvalidOperationException($"Element {e.Id}: only round piping connectors are supported.");
                var p = c.Origin; var d = c.CoordinateSystem.BasisZ;
                ports.Add(new(new(p.X * 304.8, p.Y * 304.8, p.Z * 304.8), c.Radius * 609.6, new(d.X, d.Y, d.Z)));

            }
            var host = (e as FamilyInstance)?.Host;
            var position = (e.Location as LocationPoint)?.Point;
            s.Elements.Add(new() { Id = e.Id.Value.ToString(), Family = family, System = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "", Boundary = !support && e.Category.Id.Value == (long)BuiltInCategory.OST_MechanicalEquipment, IsSupport = support, Location = position is null ? null : new(position.X * 304.8, position.Y * 304.8, position.Z * 304.8), HostId = host?.Id.Value.ToString(), HostBore = host is Pipe hostPipe ? hostPipe.Diameter * 304.8 : null, Ports = ports, Neighbors = neighbors, Instance = instanceParameters, Type = typeParameters });
        }
        s.Selection.IntersectWith(s.Elements.Where(e => !e.Boundary).Select(e => e.Id)); return s;
    }
    static Dictionary<string, string> Parameters(Element? e)
    {
        var values = new Dictionary<string, string>(); var duplicateNames = new HashSet<string>(); if (e is null) return values;
        foreach (Parameter p in e.Parameters)
        {
            string value = p.StorageType switch { StorageType.String => p.AsString() ?? "", StorageType.ElementId => e.Document.GetElement(p.AsElementId())?.Name ?? p.AsValueString() ?? "", _ => p.AsValueString() ?? "" };
            // A duplicate display name is ambiguous; it remains available by GUID or built-in ID.
            if (!duplicateNames.Contains(p.Definition.Name) && !values.TryAdd(p.Definition.Name, value)) { values.Remove(p.Definition.Name); duplicateNames.Add(p.Definition.Name); }
            if (p.IsShared) values["guid:" + p.GUID] = value;
            if (p.Id.Value < 0) values["builtin:" + ((BuiltInParameter)p.Id.Value)] = value;
        }
        return values;
    }
}
