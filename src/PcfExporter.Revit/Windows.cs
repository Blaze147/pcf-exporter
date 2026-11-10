using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using PcfExporter.Core;

namespace PcfExporter.Revit;

static class Ui
{
    public static Button Button(string label, Action action) { var b = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(12, 6, 12, 6) }; b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "PCF Exporter", MessageBoxButton.OK, MessageBoxImage.Error); } }; return b; }
    public static TextBlock Label(string s) => new() { Text = s, Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
}
public sealed class ExportWindow : Window
{
    Snapshot snapshot; readonly ConfigurationStore store; readonly ComboBox scope = new(); readonly ListBox networks = new() { SelectionMode = SelectionMode.Multiple, MinHeight = 170 }; readonly TextBox directory = new(); readonly CheckBox separate = new() { Content = "One file per connected network", Margin = new Thickness(4) };
    readonly ComboBox runParameter = new() { IsEditable = true, Margin = new Thickness(4) };
    readonly ComboBox runSource = new() { ItemsSource = new[] { "Instance", "Type" }, Margin = new Thickness(4) };
    readonly TextBox runValue = new() { Margin = new Thickness(4) };
    public ExportWindow(Snapshot snapshot, ConfigurationStore store)
    {
        this.snapshot = snapshot; this.store = store; Title = "PCF Exporter — Export"; Width = 720; Height = 780; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Ui.Label("What do you want to export?"));
        scope.ItemsSource = new[] { "Selection", "Connected systems", "Everything", "Run ID" }; scope.SelectedIndex = snapshot.Selection.Count > 0 ? 0 : 1; panel.Children.Add(scope);
        panel.Children.Add(Ui.Label($"{snapshot.Selection.Count} selected elements captured when opened. To change selection, close this window, select elements in Revit, and reopen."));
        var discovered = NetworkDiscovery.Discover(snapshot.Elements); networks.ItemsSource = discovered; networks.DisplayMemberPath = "Label"; panel.Children.Add(networks);
        scope.SelectionChanged += (_, _) => networks.IsEnabled = scope.SelectedIndex == 1; networks.IsEnabled = scope.SelectedIndex == 1;
        var remembered = store.Load().RunId;
        runParameter.ItemsSource = remembered.ParameterHistory.ToList(); runParameter.Text = remembered.LastParameter;
        runValue.Text = remembered.LastValue; runSource.SelectedItem = remembered.ParameterSource;
        var runFields = new StackPanel { Visibility = Visibility.Collapsed };
        runFields.Children.Add(Ui.Label("Run ID parameter — choose a previous name or type one"));
        runFields.Children.Add(runParameter);
        runFields.Children.Add(Ui.Label("Read parameter from")); runFields.Children.Add(runSource);
        runFields.Children.Add(Ui.Label("Exact value (case and spaces must match)")); runFields.Children.Add(runValue);
        runFields.Children.Add(Ui.Label("Only matching elements are included, including supports. Connected or hosted elements with a different or missing value are excluded."));
        var matchCount = Ui.Label(""); runFields.Children.Add(matchCount);
        void UpdateMatchCount()
        {
            try { matchCount.Text = $"{RunIdScope.Filter(this.snapshot.Elements, runParameter.Text, runValue.Text, runSource.SelectedItem as string ?? "Instance").Count} matching exportable elements"; }
            catch (ArgumentException ex) { matchCount.Text = ex.Message; }
        }
        runParameter.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateMatchCount()));
        runValue.TextChanged += (_, _) => UpdateMatchCount(); runSource.SelectionChanged += (_, _) => UpdateMatchCount();
        scope.SelectionChanged += (_, _) => { runFields.Visibility = scope.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed; UpdateMatchCount(); };
        Closing += (_, _) =>
        {
            if (scope.SelectedIndex != 3 || string.IsNullOrWhiteSpace(runParameter.Text)) return;
            try
            {
                var current = store.Load();
                var source = runSource.SelectedItem as string ?? "Instance";
                if (current.RunId.LastParameter == runParameter.Text && current.RunId.LastValue == runValue.Text && current.RunId.ParameterSource == source) return;
                current.RunId.Remember(runParameter.Text, runValue.Text, source); store.Save(current);
            }
            catch (Exception ex) { MessageBox.Show("Could not remember Run ID settings: " + ex.Message, "PCF Exporter"); }
        };
        panel.Children.Add(runFields);
        panel.Children.Add(Ui.Label("Output folder")); panel.Children.Add(directory);
        panel.Children.Add(Ui.Button("Browse…", () => { var dialog = new Microsoft.Win32.OpenFolderDialog(); if (dialog.ShowDialog() == true) directory.Text = dialog.FolderName; }));
        panel.Children.Add(separate); panel.Children.Add(Ui.Label("Output uses millimeters for coordinates and bores. Existing output files require confirmation."));
        panel.Children.Add(Ui.Button("Configure mappings…", () => ShowMappingEditor()));
        panel.Children.Add(Ui.Button("Validate and export", Export));
    }
    void ShowMappingEditor()
    {
        new MappingWindow(snapshot, store) { Owner = this }.ShowDialog();
        if (snapshot.Reload is not null)
        {
            var selected = networks.SelectedItems.Cast<Network>().Select(n => n.Id).ToHashSet();
            snapshot = snapshot.Reload();
            var updated = NetworkDiscovery.Discover(snapshot.Elements);
            networks.ItemsSource = updated;
            foreach (var network in updated.Where(n => selected.Contains(n.Id))) networks.SelectedItems.Add(network);
        }
    }
    void Export()
    {
        var config = store.Load();
        IReadOnlyList<Network> chosen;
        if (scope.SelectedIndex == 3)
        {
            var source = runSource.SelectedItem as string ?? "Instance";
            var matches = RunIdScope.Filter(snapshot.Elements, runParameter.Text, runValue.Text, source);
            config.RunId.Remember(runParameter.Text, runValue.Text, source);
            store.Save(config);
            runParameter.ItemsSource = config.RunId.ParameterHistory.ToList();
            runParameter.Text = config.RunId.LastParameter;
            if (matches.Count == 0) throw new InvalidOperationException("No exportable elements have that exact Run ID parameter and value. Nothing was exported.");
            chosen = NetworkDiscovery.Discover(matches);
        }
        else chosen = scope.SelectedIndex switch { 0 => NetworkDiscovery.Discover(snapshot.Elements.Where(e => snapshot.Selection.Contains(e.Id))), 1 => networks.SelectedItems.Cast<Network>().ToList(), _ => NetworkDiscovery.Discover(snapshot.Elements) };
        var errors = new List<string>();
        foreach (var e in chosen.SelectMany(n => n.Elements)) try { PcfElementFactory.Write(MappingResolver.Resolve(e, config), 1); } catch (Exception ex) { errors.Add(ex.Message); }
        if (errors.Count > 0) throw new InvalidOperationException($"Export blocked: {errors.Count} issue(s).\n\n" + string.Join("\n", errors.Take(30)));
        if (!Path.IsPathFullyQualified(directory.Text) || !Directory.Exists(directory.Text)) throw new InvalidOperationException("Choose an existing absolute output folder.");
        var files = PcfWriter.Plan(chosen, config, separate.IsChecked == true);
        var existing = files.Keys.Where(n => File.Exists(Path.Combine(directory.Text, n))).ToList();
        if (existing.Count > 0 && MessageBox.Show("Replace these files?\n" + string.Join("\n", existing), "Confirm replacement", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var (name, text) in files) { string path = Path.Combine(directory.Text, name), temp = path + ".tmp"; try { File.WriteAllText(temp, text, new UTF8Encoding(false)); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
        MessageBox.Show($"Exported {chosen.Sum(n => n.Elements.Count)} elements to {files.Count} file(s).", "PCF Exporter");
    }
}
public sealed class MappingWindow : Window
{
    readonly Snapshot snapshot; readonly ConfigurationStore store; readonly Configuration config; readonly ListBox groups = new(); readonly StackPanel detail = new(); readonly List<Action> commits = [];
    public MappingWindow(Snapshot snapshot, ConfigurationStore store)
    {
        this.snapshot = snapshot; this.store = store; config = store.Load(); Title = "PCF Exporter — Family mappings"; Width = 1050; Height = 780; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var outer = new DockPanel { Margin = new Thickness(16) }; Content = outer;
        var bottom = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(bottom, Dock.Bottom); outer.Children.Add(bottom);
        bottom.Children.Add(Ui.Button("Add group", () => { var g = new FamilyGroup(); config.Groups.Add(g); Refresh(); groups.SelectedItem = g; }));
        bottom.Children.Add(Ui.Button("Delete group", () => { if (groups.SelectedItem is FamilyGroup g) { config.Groups.Remove(g); Refresh(); } }));
        bottom.Children.Add(Ui.Button("Save configuration", () => { foreach (var commit in commits) commit(); store.Save(config); MessageBox.Show("Saved JSON beside the add-in."); }));
        groups.Width = 230; groups.DisplayMemberPath = "Name"; DockPanel.SetDock(groups, Dock.Left); outer.Children.Add(groups); outer.Children.Add(new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        groups.SelectionChanged += (_, _) => { try { ShowGroup(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Check mapping values"); } }; Refresh();
    }
    void Refresh() { groups.ItemsSource = null; groups.ItemsSource = config.Groups; }
    void ShowGroup()
    {
        foreach (var commit in commits) commit(); commits.Clear(); detail.Children.Clear(); if (groups.SelectedItem is not FamilyGroup g) return;
        detail.Children.Add(Ui.Label("Group label")); var name = new TextBox { Text = g.Name, Margin = new Thickness(4) }; name.TextChanged += (_, _) => { g.Name = name.Text; groups.Items.Refresh(); }; detail.Children.Add(name);
        detail.Children.Add(Ui.Label("Families — applies to every type. Pipes represents all pipe types."));
        foreach (var f in snapshot.Elements.Where(e => !e.Boundary).Select(e => e.Family).Concat(snapshot.AvailableFamilies).Concat(g.Families).Distinct().Order())
        {
            bool elsewhere = config.Groups.Any(other => other != g && other.Families.Contains(f));
            var check = new CheckBox { Content = f + (elsewhere ? " (assigned to another group)" : ""), IsChecked = g.Families.Contains(f), IsEnabled = !elsewhere, Margin = new Thickness(4) };
            check.Checked += (_, _) => { if (!g.Families.Contains(f)) g.Families.Add(f); }; check.Unchecked += (_, _) => g.Families.Remove(f); detail.Children.Add(check);
        }
        detail.Children.Add(Ui.Label("Map PCF fields. ComponentType and Description are required. Parameters can be selected by name, guid:<GUID>, or builtin:<Revit enum>."));
        foreach (var field in Fields.All)
        {
            if (!g.Mappings.TryGetValue(field, out var m)) { m = new FieldMapping(); g.Mappings.Add(field, m); }
            var box = new GroupBox { Header = field, Margin = new Thickness(4) }; var p = new StackPanel { Margin = new Thickness(6) }; box.Content = p; detail.Children.Add(box);
            var source = new ComboBox { ItemsSource = new[] { "Constant", "Instance", "Type" }, SelectedItem = m.Source }; p.Children.Add(source);
            var parameter = new ComboBox { IsEditable = true, Text = m.Parameter, Margin = new Thickness(0, 4, 0, 4) }; p.Children.Add(parameter);
            void Choices() { parameter.ItemsSource = snapshot.FamilyCatalog.Where(e => g.Families.Contains(e.Family)).SelectMany(e => m.Source == "Type" ? e.Type.Keys : e.Instance.Keys).Distinct().Order().ToList(); parameter.IsEnabled = m.Source != "Constant"; }
            source.SelectionChanged += (_, _) => { m.Source = (string)source.SelectedItem; Choices(); }; parameter.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => m.Parameter = parameter.Text)); Choices();
            var constant = new TextBox { Text = m.Constant, ToolTip = "Fixed value (used when source is Constant)", Margin = new Thickness(0, 4, 0, 4) }; constant.TextChanged += (_, _) => m.Constant = constant.Text; p.Children.Add(Ui.Label("Fixed value")); p.Children.Add(constant);
            if (field == "ComponentType") p.Children.Add(Ui.Label("Supported: " + string.Join(", ", PcfElementFactory.Supported)));
            var strict = new CheckBox { Content = "Require every source value to have a translation", IsChecked = m.RequireTranslation }; strict.Checked += (_, _) => m.RequireTranslation = true; strict.Unchecked += (_, _) => m.RequireTranslation = false; p.Children.Add(strict);
            p.Children.Add(Ui.Label("Value translations (source value → PCF value)"));
            var rows = m.Translations.Select(k => new TranslationRow { Source = k.Key, Target = k.Value }).ToList();
            var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, Height = 115 }; grid.Columns.Add(new DataGridTextColumn { Header = "Source value", Binding = new System.Windows.Data.Binding("Source"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); grid.Columns.Add(new DataGridTextColumn { Header = "PCF value", Binding = new System.Windows.Data.Binding("Target"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); p.Children.Add(grid);
            void Sync() { grid.CommitEdit(); grid.CommitEdit(); m.Translations = rows.Where(r => r.Source.Length > 0).ToDictionary(r => r.Source, r => r.Target, StringComparer.OrdinalIgnoreCase); }
            commits.Add(Sync);
            p.Children.Add(Ui.Button("Add translation", () => { rows.Add(new()); grid.Items.Refresh(); }));
            p.Children.Add(Ui.Button("Remove selected translation", () => { if (grid.SelectedItem is TranslationRow r) { rows.Remove(r); grid.Items.Refresh(); Sync(); } }));
            p.Children.Add(Ui.Button("Load observed parameter values", () => { foreach (var value in snapshot.FamilyCatalog.Where(e => g.Families.Contains(e.Family)).Select(e => (m.Source == "Type" ? e.Type : e.Instance).GetValueOrDefault(m.Parameter)).Where(v => !string.IsNullOrEmpty(v)).Distinct()) if (!rows.Any(r => r.Source == value)) rows.Add(new() { Source = value! }); grid.Items.Refresh(); Sync(); }));
        }
    }
    public sealed class TranslationRow { public string Source { get; set; } = ""; public string Target { get; set; } = ""; }
}
