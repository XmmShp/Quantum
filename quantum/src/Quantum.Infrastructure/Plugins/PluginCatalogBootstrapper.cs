using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quantum.Application.Plugins;
using Quantum.Domain.Plugins;
using System.Text.Json;

namespace Quantum.Infrastructure.Plugins;

public sealed class PluginCatalogBootstrapper
{
    private readonly JsonPluginManifestReader _manifestReader;
    private readonly PluginDependencyPlanner _dependencyPlanner;
    private readonly ILogger<PluginCatalogBootstrapper> _logger;

    public PluginCatalogBootstrapper(
        JsonPluginManifestReader? manifestReader = null,
        PluginDependencyPlanner? dependencyPlanner = null,
        ILogger<PluginCatalogBootstrapper>? logger = null)
    {
        _manifestReader = manifestReader ?? new JsonPluginManifestReader();
        _dependencyPlanner = dependencyPlanner ?? new PluginDependencyPlanner();
        _logger = logger ?? NullLogger<PluginCatalogBootstrapper>.Instance;
    }

    public PluginCatalog Bootstrap(string modulesRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulesRootPath);

        var candidates = new List<PluginCandidate>();
        var failures = new List<PluginLoadFailure>();
        if (!Directory.Exists(modulesRootPath))
        {
            _logger.LogInformation("Plugin directory {PluginDirectory} does not exist; starting with an empty catalog.", modulesRootPath);
            return new PluginCatalog([], failures);
        }

        foreach (var pluginDirectory in Directory.EnumerateDirectories(modulesRootPath).Order(StringComparer.Ordinal))
        {
            try
            {
                candidates.Add(_manifestReader.Read(pluginDirectory));
            }
            catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or FormatException)
            {
                failures.Add(new PluginLoadFailure(null, $"Could not read plugin from '{pluginDirectory}'.", exception));
                _logger.LogError(exception, "Could not read plugin manifest from {PluginDirectory}.", pluginDirectory);
            }
        }

        var plan = _dependencyPlanner.CreatePlan(candidates);
        failures.AddRange(plan.Failures);

        var loaded = new List<LoadedPlugin>();
        var loadedIds = new HashSet<PluginId>();
        foreach (var candidate in plan.OrderedCandidates)
        {
            var unavailableDependency = candidate.Manifest.Dependencies
                .FirstOrDefault(dependency => !loadedIds.Contains(dependency.Id));
            if (unavailableDependency is not null)
            {
                failures.Add(new PluginLoadFailure(
                    candidate.Manifest.Id,
                    $"Dependency '{unavailableDependency.Id}' did not load successfully."));
                continue;
            }

            try
            {
                var entryAssemblyPath = Path.Combine(candidate.RootPath, candidate.Manifest.EntryAssembly);
                var loadContext = new PluginLoadContext(entryAssemblyPath);
                var assembly = loadContext.LoadFromAssemblyPath(entryAssemblyPath);
                var routes = candidate.Manifest.Routes
                    .Select(route => PluginRouteRegistration.Create(candidate.Manifest.Id, route, assembly))
                    .ToArray();

                loaded.Add(new LoadedPlugin(candidate.Manifest, candidate.RootPath, assembly, routes));
                loadedIds.Add(candidate.Manifest.Id);
                _logger.LogInformation(
                    "Loaded plugin {PluginId} {PluginVersion} into {LoadContext}.",
                    candidate.Manifest.Id,
                    candidate.Manifest.Version,
                    loadContext.Name);
            }
            catch (Exception exception) when (exception is IOException
                or BadImageFormatException
                or FileLoadException
                or PlatformNotSupportedException
                or TypeLoadException
                or InvalidOperationException)
            {
                failures.Add(new PluginLoadFailure(candidate.Manifest.Id, "Plugin assembly could not be loaded.", exception));
                _logger.LogError(exception, "Could not load plugin {PluginId}.", candidate.Manifest.Id);
            }
        }

        return new PluginCatalog(loaded, failures);
    }
}
