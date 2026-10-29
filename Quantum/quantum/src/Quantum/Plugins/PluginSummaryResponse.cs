namespace Quantum.Plugins;

public sealed record PluginSummaryResponse(
    string Id,
    string Version,
    string State,
    IReadOnlyList<string> Routes);
