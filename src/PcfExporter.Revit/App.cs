using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PcfExporter.Core;
using System.Reflection;
namespace PcfExporter.Revit;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication a)
    {
        try { a.CreateRibbonTab("PCF Exporter"); } catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        var p = a.CreateRibbonPanel("PCF Exporter", "PCF"); string dll = Assembly.GetExecutingAssembly().Location;
        p.AddItem(new PushButtonData("ExportPcf", "Export PCF", dll, typeof(ExportCommand).FullName));
        p.AddItem(new PushButtonData("ConfigurePcf", "Configure mappings", dll, typeof(ConfigureCommand).FullName)); return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication a) => Result.Succeeded;
    internal static ConfigurationStore Store => new(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(App).Assembly.Location)!, "pcf-mappings.json"));
}
[Transaction(TransactionMode.ReadOnly)]
public class ExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) { try { var snapshot = Snapshot.Read(data.Application.ActiveUIDocument); new ExportWindow(snapshot, App.Store).ShowDialog(); return Result.Succeeded; } catch (Exception ex) { message = ex.Message; return Result.Failed; } }
}
[Transaction(TransactionMode.ReadOnly)]
public class ConfigureCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) { try { new MappingWindow(Snapshot.Read(data.Application.ActiveUIDocument), App.Store).ShowDialog(); return Result.Succeeded; } catch (Exception ex) { message = ex.Message; return Result.Failed; } }
}
