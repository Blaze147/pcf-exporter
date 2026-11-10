# PCF Exporter for Revit 2025

Standalone, read-only PCF export add-in with a dedicated ribbon. Targets only Revit
2025 (.NET 8, Windows x64). No Excel, Norsyn shell, or upstream utility assemblies.

## Build and install

With the .NET 8 SDK or newer:

```sh
dotnet build src/PcfExporter.Revit/PcfExporter.Revit.csproj -c Release
dotnet test tests/PcfExporter.Tests/PcfExporter.Tests.csproj -c Release
```

Revit reference packages restore from NuGet; Revit is not required to compile.
On Windows, close Revit and run `./install.ps1` from PowerShell. It installs for the
current user under `%APPDATA%/Autodesk/Revit/Addins/2025`. Start Revit and open the
**PCF Exporter** ribbon. Installation does not overwrite mapping JSON.

## Workflow

1. Configure mappings: create a named group and check its families. Every type in
   a family inherits that group's mappings. A family can belong to one group only.
   `Pipes` represents Revit's system pipe families collectively in this version.
2. Map `ComponentType` and `Description` (required), plus optional Material, Skey,
   Specification, ItemCode, WallThickness, endpoint types, FlatDirection, SupportName, and SupportBore.
   Choose a constant, instance parameter, or type parameter. The parameter picker
   includes names, `guid:<GUID>`, and `builtin:<Revit enum>` identifiers. Ambiguous
   display names are excluded; use GUID/built-in identifiers instead.
3. Add source-to-output translation rows. **Load observed parameter values** fills
   source rows from assigned families. Enable strict translations to block unknown
   values. Material parameters referencing Revit materials resolve to material names.
4. Save: `pcf-mappings.json` is written beside the add-in DLL. Subsequent saves
   create `.bak` and replace the JSON atomically. No hidden alternate location is used.
5. Export: choose captured selection, connected systems (multi-select), everything, or Run ID.
   Choose an existing output folder and combined or separate files. To revise the
   captured Revit selection, close the modal dialog, select elements, and reopen it.
6. Validation blocks the entire export plan on missing mappings or unsupported geometry.
   Existing files prompt before replacement. Export never writes model parameters.

## Connectivity and output

Networks follow actual round piping end-connector references. System names are
labels, not grouping keys. Mechanical equipment is a boundary and is not exported;
its inlet and outlet do not bridge networks. Selected elements form their own
induced graph: selection never expands to unselected connected components.

Coordinates and bores are millimeters; text is UTF-8 without BOM. WallThickness is
an invariant positive millimeter number, exported using upstream's
`COMPONENT-ATTRIBUTE1` convention (consumer-specific interpretation needs validation). Numeric source parameters otherwise use
Revit's formatted values, so use translations or constants where PCF needs canonical
values. Physical connector diameters are used as bores; nominal-size conversion for
specialized content remains a live-model validation item.

Combined output has a PIPELINE-REFERENCE for each network and a shared material table.
Separate output uses `network-<seed element ID>.pcf`; combined output is `piping.pcf`.
Material groups use description plus material. Output order is deterministic by
network seed and element ID. No project-specific exclusions or embedded spec tables.

Supported records: PIPE, ELBOW, TEE, REDUCER-CONCENTRIC, REDUCER-ECCENTRIC, VALVE,
FLANGE, FLANGE-BLIND, CAP, GASKET, COUPLING, UNION, FILTER, INSTRUMENT, MISC-COMPONENT, SUPPORT.
Tees identify their run by connector directions. Elbows derive angle and axis
intersection from connectors. Eccentric reducers require a FlatDirection mapping.
Special upstream features (olets, taps, generated gaskets/bolts, welds,
three-way valves, spindle orientation, LDT and CII-specific attributes) are not yet
ported and fail as unsupported rather than silently disappearing. This is a new
factory-based implementation informed by upstream, not a byte-compatible extraction
of every upstream writer.

## Layout

- `src/PcfExporter.Core`: JSON configuration, mapping resolution, graph discovery,
  component factory, PCF composition and output planning. No Revit dependencies.
- `src/PcfExporter.Revit`: ribbon, read-only model snapshot, WPF export/mapping UI.
- `tests/PcfExporter.Tests`: synthetic model and geometry tests runnable on macOS.
- `upstream-review`: ignored reference checkout; not required to build or install.

## Verification limits

The host can cross-compile on macOS. Actual WPF execution requires Windows; Revit
connector collection and downstream PCF interpretation require later user testing.
Synthetic tests prove only their stated fixtures, not compatibility with a stress
analysis application. Verify pipe/elbow/tee/reducer samples and equipment boundaries
in Revit 2025 before production use. Linked models, fabrication parts, flex pipes,
non-round connectors, and multiport equipment are outside this first version.

See THIRD-PARTY-NOTICES.md and LICENSE.md for upstream attribution and GPL-3.0 text.

## Simulated example

Run `dotnet run --project tools/PcfExporter.Simulator -- samples` to regenerate the
example JSON and two-network PCF. The example mapping is deliberately not installed
as your configuration: its family names belong to synthetic model data.

## Supports

Map a support family to ComponentType `SUPPORT`. Map SupportName for PCF `NAME`,
and optionally Skey, Specification and ItemCode. The PCF record uses `CO-ORDS`
at the family insertion point plus bore, matching upstream's support geometry.
Bore comes from a piping connector, a pipe host, or an explicit SupportBore mapping
(in millimeters). Missing location or bore blocks export.

Configured support families can be pipe accessories/fittings, Generic Models,
Specialty Equipment, Structural Framing or Mechanical Equipment. Connectorless
families are included when mapped as SUPPORT. Native fabrication hangers are not
covered. The editor offers parameters from candidate families before they are mapped.

Supports join their host's or physically connected pipe's network. They never bridge
otherwise disconnected networks; ambiguous attachments block discovery. An unattached
support appears as its own clearly labelled network; no nearest-pipe guess is made.
Selection mode exports only selected objects. Changing mappings in the export window
refreshes the snapshot and network list. This exports support records and their
mapped names, not a CAESAR/ROHR2-specific restraint solver model.

## Run ID scope

Choose **Run ID**, type a parameter name (for example `run_ID`), choose instance
or type parameters, and enter an exact value. Parameter names and values match
case-sensitively, including spaces; no trimming, substring matching, or automatic
type fallback is performed. A missing parameter never matches, even when the requested
value is empty. GUID and built-in identifiers from the mapping editor are also accepted.
For non-text parameters, equality uses the same Revit-formatted text shown in the
snapshot, including any displayed units.

The dropdown remembers previously entered names, most recently used first. Names,
last value, and instance/type choice are saved under `RunId` in `pcf-mappings.json`
beside the DLL, when exporting or closing the Run ID window. Old mapping files load
without migration. Saving context preserves existing family groups.

Filtering happens before network discovery. Only matching elements are exported:
neighbors and supports with different or missing IDs are excluded even when connected
to a matching pipe. Matching supports whose hosts are excluded remain explicit
unattached support networks. Combined/separate output choices still apply to the
networks remaining after filtering. The UI shows the matching element count; zero
matches produces a clear message and no output files.
