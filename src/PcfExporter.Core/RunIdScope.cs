namespace PcfExporter.Core;

public sealed class RunIdContext
{
    public List<string> ParameterHistory { get; set; } = [];
    public string LastParameter { get; set; } = "";
    public string LastValue { get; set; } = "";
    public string ParameterSource { get; set; } = "Instance";

    public void Remember(string parameter, string value, string source)
    {
        RunIdScope.Validate(parameter, source);
        ParameterHistory.RemoveAll(p => string.Equals(p, parameter, StringComparison.Ordinal));
        ParameterHistory.Insert(0, parameter);
        LastParameter = parameter;
        LastValue = value;
        ParameterSource = source;
    }
}

public static class RunIdScope
{
    public static void Validate(string parameter, string source)
    {
        if (string.IsNullOrWhiteSpace(parameter)) throw new ArgumentException("Enter the Run ID parameter name.");
        if (source is not ("Instance" or "Type")) throw new ArgumentException("Choose instance or type parameters for Run ID.");
    }

    // Filter before network discovery: no host, neighbor, or support may widen this scope.
    // Parameter identifiers and values are ordinal, case-sensitive, and never trimmed.
    public static IReadOnlyList<ModelElement> Filter(IEnumerable<ModelElement> elements, string parameter, string value, string source = "Instance")
    {
        Validate(parameter, source);
        return elements.Where(e => !e.Boundary &&
            (source == "Type" ? e.Type : e.Instance).TryGetValue(parameter, out var actual) &&
            string.Equals(actual, value, StringComparison.Ordinal)).ToList();
    }
}
