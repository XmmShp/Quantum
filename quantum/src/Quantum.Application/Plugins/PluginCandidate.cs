using Quantum.Domain.Plugins;

namespace Quantum.Application.Plugins;

public sealed record PluginCandidate(PluginManifest Manifest, string RootPath);
