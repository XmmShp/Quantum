namespace Quantum.Contract.Plugins;

public sealed record PluginSummaryResponse(
    string Id,
    string Version,
    string State,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Routes);
