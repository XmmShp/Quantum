namespace Quantum.Domain.Plugins;

public sealed class PluginManifest
{
    public PluginManifest(
        PluginId id,
        SemanticVersion version,
        string entryAssembly,
        IEnumerable<PluginDependency>? dependencies = null,
        IEnumerable<PluginIntegration>? integrations = null,
        IEnumerable<PluginPermission>? permissions = null,
        IEnumerable<PluginRouteDefinition>? routes = null,
        PluginWebContributions? web = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssembly);
        if (!string.Equals(entryAssembly, Path.GetFileName(entryAssembly), StringComparison.Ordinal)
            || !entryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Entry assembly must be a DLL file name without a path.", nameof(entryAssembly));
        }

        Id = id;
        Version = version ?? throw new ArgumentNullException(nameof(version));
        EntryAssembly = entryAssembly;
        Dependencies = (dependencies ?? []).ToArray();
        Integrations = (integrations ?? []).ToArray();
        Permissions = (permissions ?? []).ToArray();
        Routes = (routes ?? []).ToArray();
        Web = web ?? PluginWebContributions.Empty;

        if (Dependencies.Any(dependency => dependency.Id == Id)
            || Integrations.Any(integration => integration.Id == Id))
        {
            throw new ArgumentException("A plugin cannot declare a relationship with itself.");
        }

        EnsureUnique(
            Dependencies.Select(static dependency => dependency.Id)
                .Concat(Integrations.Select(static integration => integration.Id)),
            "plugin relationship");
        EnsureUnique(Routes.Select(static route => route.Path), "route");
        EnsureUnique(Permissions.Select(static permission => permission.Name), "permission");
    }

    public PluginId Id { get; }

    public SemanticVersion Version { get; }

    public string EntryAssembly { get; }

    public IReadOnlyList<PluginDependency> Dependencies { get; }

    public IReadOnlyList<PluginIntegration> Integrations { get; }

    public IReadOnlyList<PluginPermission> Permissions { get; }

    public IReadOnlyList<PluginRouteDefinition> Routes { get; }

    public PluginWebContributions Web { get; }

    private static void EnsureUnique<T>(IEnumerable<T> values, string name)
        where T : notnull
    {
        var seen = new HashSet<T>();
        if (values.Any(value => !seen.Add(value)))
        {
            throw new ArgumentException($"Plugin manifest contains a duplicate {name}.");
        }
    }
}

public sealed record PluginDependency(PluginId Id, SemanticVersion MinimumVersion);

public sealed record PluginIntegration(PluginId Id, SemanticVersion MinimumVersion);

public sealed record PluginPermission
{
    public PluginPermission(string name, bool required = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Required = required;
    }

    public string Name { get; }

    public bool Required { get; }
}

public sealed record PluginRouteDefinition
{
    public PluginRouteDefinition(string path, string component, string? title = null, string? icon = null, int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        if (!path.StartsWith('/'))
        {
            throw new ArgumentException("Plugin route paths must be absolute.", nameof(path));
        }

        Path = path.Length > 1 ? path.TrimEnd('/') : path;
        Component = component.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        Order = order;
    }

    public string Path { get; }

    public string Component { get; }

    public string? Title { get; }

    public string? Icon { get; }

    public int Order { get; }
}

public sealed record PluginWebContributions(
    IReadOnlyList<string> Head,
    IReadOnlyList<string> PostBlazor)
{
    public static PluginWebContributions Empty { get; } = new([], []);
}
