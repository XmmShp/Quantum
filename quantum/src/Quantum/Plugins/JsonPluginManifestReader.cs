using System.Text.Json;
using System.Text.Json.Serialization;
using Quantum.Plugins.Persistence;
namespace Quantum.Plugins;

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

        var runtime = CreateRuntime(document);

        var manifest = new PluginManifest(
            new PluginId(document.Id),
            SemanticVersion.Parse(document.Version),
            runtime,
            document.Dependencies.Select(static dependency => new PluginDependency(
                new PluginId(dependency.Id),
                SemanticVersion.Parse(dependency.MinimumVersion))),
            document.Integrations.Select(static integration => new PluginIntegration(
                new PluginId(integration.Id),
                SemanticVersion.Parse(integration.MinimumVersion))),
            document.Ui.Routes.Select(route => runtime.Kind == PluginRuntimeKind.Web
                ? PluginRouteDefinition.Web(
                    route.Path,
                    route.View,
                    route.Title,
                    route.Icon,
                    route.Order,
                    route.ShowInNavigation)
                : new PluginRouteDefinition(
                    route.Path,
                    route.Component,
                    route.Title,
                    route.Icon,
                    route.Order,
                    route.ShowInNavigation)),
            new PluginWebContributions(document.Web.Head, document.Web.PostBlazor),
            document.Database is null
                ? null
                : new PluginDatabaseDefinition(document.Database.Migrations));

        var entryPath = ResolveEntryPath(fullRootPath, runtime);
        if (!File.Exists(entryPath))
        {
            throw new FileNotFoundException(
                $"Runtime entry '{runtime.Entry}' was not found for plugin '{manifest.Id}'.",
                entryPath);
        }

        if (manifest.Database is not null)
        {
            PluginMigrationArtifact.Discover(fullRootPath, manifest.Database);
        }

        return new PluginCandidate(manifest, fullRootPath);
    }

    private static PluginRuntimeDefinition CreateRuntime(PluginManifestDocument document)
    {
        var hasLegacyEntry = !string.IsNullOrWhiteSpace(document.EntryAssembly);
        if (document.Runtime is null)
        {
            return hasLegacyEntry
                ? PluginRuntimeDefinition.DotNet(document.EntryAssembly!)
                : throw new InvalidDataException("plugin.json must declare entryAssembly or runtime.");
        }

        if (hasLegacyEntry)
        {
            throw new InvalidDataException("plugin.json cannot declare both entryAssembly and runtime.");
        }

        return (document.Runtime.Kind ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dotnet" => PluginRuntimeDefinition.DotNet(document.Runtime.Entry!),
            "web" => PluginRuntimeDefinition.Web(document.Runtime.Entry!),
            _ => throw new InvalidDataException($"Unknown plugin runtime kind '{document.Runtime.Kind}'.")
        };
    }

    private static string ResolveEntryPath(string pluginRootPath, PluginRuntimeDefinition runtime)
    {
        var relativeEntry = runtime.Entry.Replace('/', Path.DirectorySeparatorChar);
        return runtime.Kind == PluginRuntimeKind.Web
            ? Path.Combine(pluginRootPath, "wwwroot", relativeEntry)
            : Path.Combine(pluginRootPath, relativeEntry);
    }

    private sealed class PluginManifestDocument
    {
        public string Id { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string? EntryAssembly { get; init; }

        public PluginRuntimeDocument? Runtime { get; init; }

        public IReadOnlyList<PluginDependencyDocument> Dependencies { get; init; } = [];

        public IReadOnlyList<PluginIntegrationDocument> Integrations { get; init; } = [];

        public PluginUiDocument Ui { get; init; } = new();

        public PluginWebDocument Web { get; init; } = new();

        public PluginDatabaseDocument? Database { get; init; }
    }

    private sealed class PluginRuntimeDocument
    {
        public string? Kind { get; init; }

        public string? Entry { get; init; }
    }

    private sealed class PluginDependencyDocument
    {
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("minVersion")]
        public string MinimumVersion { get; init; } = string.Empty;
    }

    private sealed class PluginIntegrationDocument
    {
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("minVersion")]
        public string MinimumVersion { get; init; } = string.Empty;
    }

    private sealed class PluginUiDocument
    {
        public IReadOnlyList<PluginRouteDocument> Routes { get; init; } = [];
    }

    private sealed class PluginRouteDocument
    {
        public string Path { get; init; } = string.Empty;

        public string Component { get; init; } = string.Empty;

        public string View { get; init; } = string.Empty;

        public string? Title { get; init; }

        public string? Icon { get; init; }

        public int Order { get; init; }

        public bool ShowInNavigation { get; init; } = true;
    }

    private sealed class PluginWebDocument
    {
        public IReadOnlyList<string> Head { get; init; } = [];

        public IReadOnlyList<string> PostBlazor { get; init; } = [];
    }

    private sealed class PluginDatabaseDocument
    {
        public string Migrations { get; init; } = string.Empty;
    }
}
