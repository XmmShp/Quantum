using System.Text.Json;
using System.Text.Json.Serialization;
using Quantum.Application.Plugins;
using Quantum.Domain.Plugins;

namespace Quantum.Infrastructure.Plugins;

public sealed class JsonPluginManifestReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public PluginCandidate Read(string pluginRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRootPath);

        var fullRootPath = Path.GetFullPath(pluginRootPath);
        var manifestPath = Path.Combine(fullRootPath, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Plugin manifest was not found.", manifestPath);
        }

        using var stream = File.OpenRead(manifestPath);
        var document = JsonSerializer.Deserialize<PluginManifestDocument>(stream, SerializerOptions)
            ?? throw new InvalidDataException($"Plugin manifest '{manifestPath}' is empty.");

        var manifest = new PluginManifest(
            new PluginId(document.Id),
            SemanticVersion.Parse(document.Version),
            document.EntryAssembly,
            document.Dependencies.Select(static dependency => new PluginDependency(
                new PluginId(dependency.Id),
                SemanticVersion.Parse(dependency.MinimumVersion))),
            document.Permissions.Select(static permission => new PluginPermission(permission.Name, permission.Required)),
            document.Ui.Routes.Select(static route => new PluginRouteDefinition(
                route.Path,
                route.Component,
                route.Title,
                route.Icon,
                route.Order)),
            new PluginWebContributions(document.Web.Head, document.Web.PostBlazor));

        var entryAssemblyPath = Path.Combine(fullRootPath, manifest.EntryAssembly);
        if (!File.Exists(entryAssemblyPath))
        {
            throw new FileNotFoundException(
                $"Entry assembly '{manifest.EntryAssembly}' was not found for plugin '{manifest.Id}'.",
                entryAssemblyPath);
        }

        return new PluginCandidate(manifest, fullRootPath);
    }

    private sealed class PluginManifestDocument
    {
        public string Id { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string EntryAssembly { get; init; } = string.Empty;

        public IReadOnlyList<PluginDependencyDocument> Dependencies { get; init; } = [];

        public IReadOnlyList<PluginPermissionDocument> Permissions { get; init; } = [];

        public PluginUiDocument Ui { get; init; } = new();

        public PluginWebDocument Web { get; init; } = new();
    }

    private sealed class PluginDependencyDocument
    {
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("minVersion")]
        public string MinimumVersion { get; init; } = string.Empty;
    }

    private sealed class PluginPermissionDocument
    {
        public string Name { get; init; } = string.Empty;

        public bool Required { get; init; } = true;
    }

    private sealed class PluginUiDocument
    {
        public IReadOnlyList<PluginRouteDocument> Routes { get; init; } = [];
    }

    private sealed class PluginRouteDocument
    {
        public string Path { get; init; } = string.Empty;

        public string Component { get; init; } = string.Empty;

        public string? Title { get; init; }

        public string? Icon { get; init; }

        public int Order { get; init; }
    }

    private sealed class PluginWebDocument
    {
        public IReadOnlyList<string> Head { get; init; } = [];

        public IReadOnlyList<string> PostBlazor { get; init; } = [];
    }
}
