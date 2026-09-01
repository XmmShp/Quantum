namespace Quantum.Plugins;

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
        : this(
            id,
            version,
            PluginRuntimeDefinition.DotNet(entryAssembly),
            dependencies,
            integrations,
            permissions,
            routes,
            web)
    {
    }

    public PluginManifest(
        PluginId id,
        SemanticVersion version,
        PluginRuntimeDefinition runtime,
        IEnumerable<PluginDependency>? dependencies = null,
        IEnumerable<PluginIntegration>? integrations = null,
        IEnumerable<PluginPermission>? permissions = null,
        IEnumerable<PluginRouteDefinition>? routes = null,
        PluginWebContributions? web = null)
    {
        Id = id;
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Dependencies = (dependencies ?? []).ToArray();
        Integrations = (integrations ?? []).ToArray();
        Permissions = (permissions ?? []).ToArray();
        Routes = (routes ?? []).ToArray();
        Web = web ?? PluginWebContributions.Empty;

        foreach (var route in Routes)
        {
            if (Runtime.Kind == PluginRuntimeKind.DotNet && route.Component is null)
            {
                throw new ArgumentException($".NET route '{route.Path}' must declare a component.", nameof(routes));
            }

            if (Runtime.Kind == PluginRuntimeKind.Web && route.View is null)
            {
                throw new ArgumentException($"Web route '{route.Path}' must declare a view.", nameof(routes));
            }
        }

        if (Runtime.Kind == PluginRuntimeKind.Web
            && (Web.Head.Count > 0 || Web.PostBlazor.Count > 0))
        {
            throw new ArgumentException(
                "Web plugins cannot inject HTML into the host document; render inside the isolated plugin frame instead.",
                nameof(web));
        }

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

    public PluginRuntimeDefinition Runtime { get; }

    public string? EntryAssembly => Runtime.Kind == PluginRuntimeKind.DotNet ? Runtime.Entry : null;

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

public enum PluginRuntimeKind
{
    DotNet,
    Web
}

public sealed record PluginRuntimeDefinition
{
    private PluginRuntimeDefinition(PluginRuntimeKind kind, string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        if (!string.Equals(entry, entry.Trim(), StringComparison.Ordinal)
            || Path.IsPathRooted(entry)
            || entry.Contains('\\', StringComparison.Ordinal)
            || entry.Contains(':', StringComparison.Ordinal)
            || entry.Split('/').Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException(
                "Runtime entry must be a normalized relative path using forward slashes.",
                nameof(entry));
        }

        if (kind == PluginRuntimeKind.DotNet
            && (entry.Contains('/', StringComparison.Ordinal)
                || !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(".NET runtime entry must be a DLL file name without a path.", nameof(entry));
        }

        if (kind == PluginRuntimeKind.Web
            && !entry.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            && !entry.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Web runtime entry must be a JavaScript module.", nameof(entry));
        }

        Kind = kind;
        Entry = entry;
    }

    public PluginRuntimeKind Kind { get; }

    public string Entry { get; }

    public static PluginRuntimeDefinition DotNet(string entryAssembly)
        => new(PluginRuntimeKind.DotNet, entryAssembly);

    public static PluginRuntimeDefinition Web(string entryModule)
        => new(PluginRuntimeKind.Web, entryModule);
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
        : this(path, component, view: null, title, icon, order)
    {
    }

    private PluginRouteDefinition(
        string path,
        string? component,
        string? view,
        string? title,
        string? icon,
        int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith('/'))
        {
            throw new ArgumentException("Plugin route paths must be absolute.", nameof(path));
        }

        Path = path.Length > 1 ? path.TrimEnd('/') : path;
        Component = string.IsNullOrWhiteSpace(component) ? null : component.Trim();
        View = string.IsNullOrWhiteSpace(view) ? null : view.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        Order = order;
    }

    public string Path { get; }

    public string? Component { get; }

    public string? View { get; }

    public string? Title { get; }

    public string? Icon { get; }

    public int Order { get; }

    public static PluginRouteDefinition Web(
        string path,
        string view,
        string? title = null,
        string? icon = null,
        int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        return new PluginRouteDefinition(path, component: null, view, title, icon, order);
    }
}

public sealed record PluginWebContributions(
    IReadOnlyList<string> Head,
    IReadOnlyList<string> PostBlazor)
{
    public static PluginWebContributions Empty { get; } = new([], []);
}
