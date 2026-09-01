using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
namespace Quantum.Plugins;

public sealed class PluginRuntimeManager : IPluginRuntimeManager, IAsyncDisposable
{
    private readonly PluginCatalog _catalog;
    private readonly PluginRuntimeOptions _options;
    private readonly JsonPluginManifestReader _manifestReader;
    private readonly PluginDependencyPlanner _dependencyPlanner;
    private readonly IPluginReferenceRelease? _referenceRelease;
    private readonly ILogger<PluginRuntimeManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<string> _staleShadowRoots = [];
    private IReadOnlyList<PluginRuntime> _runtimes = [];
    private IServiceProvider? _hostServices;
    private bool _disposed;

    public PluginRuntimeManager(
        PluginCatalog catalog,
        PluginRuntimeOptions options,
        IPluginReferenceRelease? referenceRelease = null,
        JsonPluginManifestReader? manifestReader = null,
        PluginDependencyPlanner? dependencyPlanner = null,
        ILogger<PluginRuntimeManager>? logger = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Validate();
        _referenceRelease = referenceRelease;
        _manifestReader = manifestReader ?? new JsonPluginManifestReader();
        _dependencyPlanner = dependencyPlanner ?? new PluginDependencyPlanner();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PluginRuntimeManager>.Instance;
        SessionShadowRoot = Path.Combine(
            Path.GetFullPath(_options.ShadowRootPath),
            $"{Environment.ProcessId}-{Guid.NewGuid():N}");
    }

    public bool IsInitialized { get; private set; }

    internal string SessionShadowRoot { get; }

    public async Task InitializeAsync(
        IServiceProvider hostServices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsInitialized)
            {
                _logger.LogDebug("Plugin runtime initialization was skipped because it is already initialized.");
                return;
            }

            _hostServices = hostServices;
            _logger.LogInformation(
                "Initializing plugin runtime from {ModulesRootPath}; session shadow root: {SessionShadowRoot}.",
                _options.ModulesRootPath,
                SessionShadowRoot);
            Directory.CreateDirectory(SessionShadowRoot);
            var discovery = Discover(excludedPluginIds: null);
            var staged = await StageAsync(discovery.Plan, tolerateFailures: true, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Initial plugin discovery and staging completed: {PlannedPluginCount} planned, "
                + "{StagedPluginCount} staged, {FailureCount} failure(s).",
                discovery.Plan.OrderedCandidates.Count,
                staged.Runtimes.Count,
                discovery.Failures.Count + staged.Failures.Count);
            if (cancellationToken.IsCancellationRequested)
            {
                await DisposeRuntimesAsync(staged.Runtimes).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var failures = _catalog.Failures
                .Concat(discovery.Failures)
                .Concat(staged.Failures)
                .ToList();

            SetRuntimeEnvironment(staged.Runtimes, failures);
            var activeRuntimes = new List<PluginRuntime>();
            var activeIds = new HashSet<PluginId>();
            foreach (var runtime in staged.Runtimes)
            {
                var unavailableDependency = runtime.Candidate.Manifest.Dependencies
                    .FirstOrDefault(dependency => !activeIds.Contains(dependency.Id));
                if (unavailableDependency is not null)
                {
                    failures.Add(new PluginLoadFailure(
                        runtime.Candidate.Manifest.Id,
                        $"Dependency '{unavailableDependency.Id}' did not start successfully."));
                    _logger.LogWarning(
                        "Skipping plugin {PluginId} because dependency {DependencyId} did not start successfully.",
                        runtime.Candidate.Manifest.Id,
                        unavailableDependency.Id);
                    continue;
                }

                try
                {
                    // Publishing the initial snapshot starts a commit phase. Finish it even if
                    // the caller cancels so the catalog and owned runtimes stay consistent.
                    await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    activeRuntimes.Add(runtime);
                    activeIds.Add(runtime.Candidate.Manifest.Id);
                    _logger.LogInformation(
                        "Loaded plugin {PluginId} {PluginVersion} as {RuntimeKind} runtime {RuntimeId}.",
                        runtime.Candidate.Manifest.Id,
                        runtime.Candidate.Manifest.Version,
                        runtime.Candidate.Manifest.Runtime.Kind,
                        runtime.LoadedPlugin.RuntimeId);
                }
                catch (Exception exception)
                {
                    failures.Add(new PluginLoadFailure(
                        runtime.Candidate.Manifest.Id,
                        $"Plugin lifecycle failed to start: {exception.Message}"));
                    _logger.LogError(
                        exception,
                        "Plugin {PluginId} lifecycle failed during initial startup.",
                        runtime.Candidate.Manifest.Id);
                }
            }

            var rejectedRuntimes = staged.Runtimes
                .Where(runtime => !activeIds.Contains(runtime.Candidate.Manifest.Id))
                .ToArray();
            _runtimes = activeRuntimes;
            SetRuntimeEnvironment(_runtimes, failures);
            _catalog.Replace(_runtimes.Select(static runtime => runtime.LoadedPlugin), failures);
            await DisposeRuntimesAsync(rejectedRuntimes).ConfigureAwait(false);
            IsInitialized = true;
            _logger.LogInformation(
                "Plugin runtime initialization finished with {ActivePluginCount} active plugin(s) "
                + "and {FailureCount} failure(s).",
                _runtimes.Count,
                failures.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginOperationResult> ReloadAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePluginId(pluginId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            _logger.LogInformation("Reload requested for plugin {PluginId}.", normalized);
            var current = _runtimes.FirstOrDefault(runtime => runtime.Candidate.Manifest.Id == normalized);
            if (current is null)
            {
                _logger.LogWarning("Cannot reload plugin {PluginId} because it is not loaded.", normalized);
                return PluginOperationResult.Failure($"插件 '{normalized}' 当前未加载，请使用重新扫描。");
            }

            var dependentPluginIds = FindStrongDependentIds(
                _runtimes.Select(static runtime => runtime.Candidate.Manifest),
                normalized);
            var discovery = Discover(excludedPluginIds: null);
            var candidate = discovery.Plan.OrderedCandidates.FirstOrDefault(item => item.Manifest.Id == normalized);
            if (candidate is null)
            {
                _logger.LogWarning(
                    "Cannot reload plugin {PluginId} because no valid on-disk candidate was discovered.",
                    normalized);
                return PluginOperationResult.Failure(
                    $"插件 '{normalized}' 的磁盘版本无效或已不存在，当前版本继续运行。");
            }

            var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, excludedPluginIds: null);
            if (missingCurrent.Length > 0)
            {
                _logger.LogWarning(
                    "Reload of plugin {PluginId} was cancelled because the new plan would remove "
                    + "currently loaded plugins: {MissingPluginIds}.",
                    normalized,
                    missingCurrent);
                return PluginOperationResult.Failure(
                    $"新快照会使已加载插件失效，热升级已取消：{string.Join(", ", missingCurrent)}。");
            }

            var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return result;
            }

            return PluginOperationResult.Success(
                dependentPluginIds.Count == 0
                    ? $"插件 '{normalized}' 已从 {current.Candidate.Manifest.Version} 热切换到 {candidate.Manifest.Version}。"
                    : $"插件 '{normalized}' 已从 {current.Candidate.Manifest.Version} 热切换到 {candidate.Manifest.Version}；"
                        + $"下游强依赖插件已按顺序停用并恢复：{string.Join(", ", dependentPluginIds)}。");
        }
        finally
        {
            _gate.Release();
        }
    }

    public PluginUnloadImpact GetUnloadImpact(string pluginId)
    {
        var normalized = NormalizePluginId(pluginId);
        while (true)
        {
            var revision = _catalog.Revision;
            var plugins = _catalog.Plugins;
            var dependents = FindStrongDependentIds(
                    plugins.Select(static plugin => plugin.Manifest),
                    normalized)
                .Select(static id => id.Value)
                .ToArray();
            if (revision == _catalog.Revision)
            {
                return new PluginUnloadImpact(normalized.Value, dependents, revision);
            }
        }
    }

    public Task<PluginOperationResult> UnloadAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
        => UnloadCoreAsync(pluginId, confirmedCatalogRevision: null, cancellationToken);

    public Task<PluginOperationResult> UnloadAsync(
        string pluginId,
        long confirmedCatalogRevision,
        CancellationToken cancellationToken = default)
        => UnloadCoreAsync(pluginId, confirmedCatalogRevision, cancellationToken);

    private async Task<PluginOperationResult> UnloadCoreAsync(
        string pluginId,
        long? confirmedCatalogRevision,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizePluginId(pluginId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            _logger.LogInformation("Unload requested for plugin {PluginId}.", normalized);
            var current = _runtimes.FirstOrDefault(runtime => runtime.Candidate.Manifest.Id == normalized);
            if (current is null)
            {
                _logger.LogWarning("Cannot unload plugin {PluginId} because it is not loaded.", normalized);
                return PluginOperationResult.Failure($"插件 '{normalized}' 当前未加载。");
            }

            var dependentPluginIds = FindStrongDependentIds(
                _runtimes.Select(static runtime => runtime.Candidate.Manifest),
                normalized);
            if (dependentPluginIds.Count > 0
                && confirmedCatalogRevision != _catalog.Revision)
            {
                _logger.LogWarning(
                    "Unload of plugin {PluginId} requires confirmation for dependent plugins "
                    + "{DependentPluginIds} at catalog revision {CatalogRevision}.",
                    normalized,
                    dependentPluginIds,
                    _catalog.Revision);
                return PluginOperationResult.Failure(
                    $"卸载插件 '{normalized}' 会同时卸载以下下游强依赖插件，请按最新清单确认后重试："
                    + $"{string.Join(", ", dependentPluginIds)}。");
            }

            var excludedPluginIds = dependentPluginIds.Append(normalized).ToHashSet();
            var discovery = Discover(excludedPluginIds);
            var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, excludedPluginIds);
            if (missingCurrent.Length > 0)
            {
                _logger.LogWarning(
                    "Unload of plugin {PluginId} was cancelled because the new plan would remove "
                    + "other currently loaded plugins: {MissingPluginIds}.",
                    normalized,
                    missingCurrent);
                return PluginOperationResult.Failure(
                    $"新快照会使其他已加载插件失效，卸载已取消：{string.Join(", ", missingCurrent)}。");
            }

            var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                ? PluginOperationResult.Success(
                    dependentPluginIds.Count == 0
                        ? $"插件 '{normalized}' 已从运行时卸载；Modules 中的文件保留，可重新扫描恢复。"
                        : $"插件 '{normalized}' 及其下游强依赖插件已从运行时卸载："
                            + $"{string.Join(", ", dependentPluginIds)}；Modules 中的文件均保留，可重新扫描恢复。")
                : result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginOperationResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            _logger.LogInformation("A full plugin directory refresh was requested.");
            var discovery = Discover(excludedPluginIds: null);
            var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
            return result.Succeeded
                ? PluginOperationResult.Success("Modules 已重新扫描，插件运行时快照已热切换。")
                : result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IServiceProvider? GetPluginServices(Assembly assembly)
        => _catalog.FindPlugin(assembly)?.Services;

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var runtimes = _runtimes;
            _logger.LogInformation(
                "Disposing plugin runtime manager with {PluginCount} active plugin(s).",
                runtimes.Count);
            _runtimes = [];
            _catalog.Replace([]);
            await ReleaseReferencesAsync(CancellationToken.None).ConfigureAwait(false);
            await DisposeRuntimesAsync(runtimes).ConfigureAwait(false);
            CleanupStaleShadowRoots();
            if (!PluginShadowCopy.TryDelete(SessionShadowRoot))
            {
                _logger.LogWarning(
                    "Could not delete plugin session shadow root {SessionShadowRoot} during shutdown.",
                    SessionShadowRoot);
            }
            else
            {
                _logger.LogDebug("Deleted plugin session shadow root {SessionShadowRoot}.", SessionShadowRoot);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PluginOperationResult> ReplaceAllAsync(
        PluginDiscovery discovery,
        CancellationToken cancellationToken)
    {
        CleanupStaleShadowRoots();
        _logger.LogInformation(
            "Preparing plugin snapshot replacement: {CurrentPluginCount} current, "
            + "{PlannedPluginCount} planned, {DiscoveryFailureCount} discovery failure(s).",
            _runtimes.Count,
            discovery.Plan.OrderedCandidates.Count,
            discovery.Failures.Count);
        var staged = await StageAsync(discovery.Plan, tolerateFailures: false, cancellationToken)
            .ConfigureAwait(false);
        if (staged.Failures.Count > 0)
        {
            _logger.LogWarning(
                "Plugin snapshot replacement was rejected during staging: {FailureReason}",
                staged.Failures[0].Reason);
            await DisposeRuntimesAsync(staged.Runtimes).ConfigureAwait(false);
            return PluginOperationResult.Failure(
                $"新插件快照加载失败，已保留当前版本：{staged.Failures[0].Reason}");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await DisposeRuntimesAsync(staged.Runtimes).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var oldRuntimes = _runtimes;
        SetRuntimeEnvironment(staged.Runtimes, discovery.Failures);
        // Once commit begins it must finish or roll back even if the caller cancels.
        _logger.LogDebug(
            "Stopping {PluginCount} runtime(s) before committing the new plugin snapshot.",
            oldRuntimes.Count);
        var stopFailures = await StopRuntimesAsync(oldRuntimes, CancellationToken.None).ConfigureAwait(false);
        if (stopFailures.Count > 0)
        {
            _logger.LogError(
                stopFailures[0],
                "Plugin snapshot replacement was cancelled because an existing runtime failed to stop.");
            await RestartRuntimesAsync(oldRuntimes, CancellationToken.None).ConfigureAwait(false);
            await DisposeRuntimesAsync(staged.Runtimes).ConfigureAwait(false);
            return PluginOperationResult.Failure(
                $"旧插件生命周期停止失败，热切换已取消：{stopFailures[0].Message}");
        }

        try
        {
            foreach (var runtime in staged.Runtimes)
            {
                _logger.LogDebug(
                    "Starting staged plugin {PluginId} runtime {RuntimeId} for snapshot replacement.",
                    runtime.Candidate.Manifest.Id,
                    runtime.LoadedPlugin.RuntimeId);
                await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "New plugin snapshot failed to start; rolling back.");
            await StopRuntimesAsync(staged.Runtimes, CancellationToken.None).ConfigureAwait(false);
            await RestartRuntimesAsync(oldRuntimes, CancellationToken.None).ConfigureAwait(false);
            await DisposeRuntimesAsync(staged.Runtimes).ConfigureAwait(false);
            return PluginOperationResult.Failure(
                $"新插件生命周期启动失败，已回滚到当前版本：{exception.Message}");
        }

        _runtimes = staged.Runtimes;
        _catalog.Replace(
            _runtimes.Select(static runtime => runtime.LoadedPlugin),
            discovery.Failures);
        await ReleaseReferencesAsync(CancellationToken.None).ConfigureAwait(false);
        await DisposeRuntimesAsync(oldRuntimes).ConfigureAwait(false);
        _logger.LogInformation(
            "Plugin runtime snapshot switched successfully: revision {CatalogRevision}, "
            + "{PluginCount} active plugin(s): {PluginIds}.",
            _catalog.Revision,
            _runtimes.Count,
            _runtimes.Select(static runtime => runtime.Candidate.Manifest.Id.Value).ToArray());
        return PluginOperationResult.Success("插件运行时快照已更新。");
    }

    private async Task<PluginStageResult> StageAsync(
        PluginLoadPlan plan,
        bool tolerateFailures,
        CancellationToken cancellationToken)
    {
        var runtimes = new List<PluginRuntime>();
        var failures = new List<PluginLoadFailure>();
        var loadedIds = new HashSet<PluginId>();
        _logger.LogDebug(
            "Staging {PluginCount} plugin candidate(s); tolerate failures: {TolerateFailures}.",
            plan.OrderedCandidates.Count,
            tolerateFailures);
        try
        {
            foreach (var candidate in plan.OrderedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unavailableDependency = candidate.Manifest.Dependencies
                    .FirstOrDefault(dependency => !loadedIds.Contains(dependency.Id));
                if (unavailableDependency is not null)
                {
                    failures.Add(new PluginLoadFailure(
                        candidate.Manifest.Id,
                        $"Dependency '{unavailableDependency.Id}' could not be staged."));
                    _logger.LogWarning(
                        "Plugin {PluginId} cannot be staged because dependency {DependencyId} "
                        + "was not staged.",
                        candidate.Manifest.Id,
                        unavailableDependency.Id);
                    if (!tolerateFailures)
                    {
                        break;
                    }

                    continue;
                }

                try
                {
                    _logger.LogDebug(
                        "Staging plugin {PluginId} {PluginVersion} from {PluginDirectory}.",
                        candidate.Manifest.Id,
                        candidate.Manifest.Version,
                        candidate.RootPath);
                    var runtime = await PluginRuntime.CreateAsync(
                            candidate,
                            SessionShadowRoot,
                            _options.DatabasePath,
                            _catalog,
                            _hostServices!,
                            _logger)
                        .ConfigureAwait(false);
                    runtimes.Add(runtime);
                    loadedIds.Add(candidate.Manifest.Id);
                    _logger.LogInformation(
                        "Staged plugin {PluginId} {PluginVersion} as {RuntimeKind} runtime {RuntimeId}.",
                        candidate.Manifest.Id,
                        candidate.Manifest.Version,
                        candidate.Manifest.Runtime.Kind,
                        runtime.LoadedPlugin.RuntimeId);
                }
                catch (Exception exception)
                {
                    failures.Add(new PluginLoadFailure(
                        candidate.Manifest.Id,
                        $"Plugin could not be staged: {exception.Message}"));
                    _logger.LogError(exception, "Could not stage plugin {PluginId}.", candidate.Manifest.Id);
                    if (!tolerateFailures)
                    {
                        break;
                    }
                }
            }
        }
        catch
        {
            await DisposeRuntimesAsync(runtimes).ConfigureAwait(false);
            throw;
        }

        _logger.LogDebug(
            "Plugin staging finished: {StagedPluginCount} staged, {FailureCount} failure(s).",
            runtimes.Count,
            failures.Count);
        return new PluginStageResult(runtimes, failures);
    }

    private string[] FindMissingCurrentPlugins(
        PluginLoadPlan plan,
        IReadOnlySet<PluginId>? excludedPluginIds)
    {
        var plannedIds = plan.OrderedCandidates
            .Select(static candidate => candidate.Manifest.Id)
            .ToHashSet();
        return _runtimes
            .Where(runtime => excludedPluginIds is null
                || !excludedPluginIds.Contains(runtime.Candidate.Manifest.Id))
            .Where(runtime => !plannedIds.Contains(runtime.Candidate.Manifest.Id))
            .Select(runtime => runtime.Candidate.Manifest.Id.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<PluginId> FindStrongDependentIds(
        IEnumerable<PluginManifest> manifests,
        PluginId targetPluginId)
    {
        var orderedManifests = manifests.ToArray();
        var affectedIds = new HashSet<PluginId> { targetPluginId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var manifest in orderedManifests)
            {
                if (!affectedIds.Contains(manifest.Id)
                    && manifest.Dependencies.Any(dependency => affectedIds.Contains(dependency.Id)))
                {
                    changed |= affectedIds.Add(manifest.Id);
                }
            }
        }

        return orderedManifests
            .Reverse()
            .Where(manifest => manifest.Id != targetPluginId && affectedIds.Contains(manifest.Id))
            .Select(static manifest => manifest.Id)
            .ToArray();
    }

    private void SetRuntimeEnvironment(
        IReadOnlyList<PluginRuntime> runtimes,
        IEnumerable<PluginLoadFailure> failures)
    {
        var environment = new PluginCatalog(
            runtimes.Select(static runtime => runtime.LoadedPlugin),
            failures,
            _logger);
        foreach (var runtime in runtimes)
        {
            runtime.UseEnvironment(environment);
        }
    }

    private PluginDiscovery Discover(IReadOnlySet<PluginId>? excludedPluginIds)
    {
        var candidates = new List<PluginCandidate>();
        var failures = new List<PluginLoadFailure>();
        _logger.LogInformation("Scanning plugin directory {ModulesRootPath}.", _options.ModulesRootPath);
        if (!Directory.Exists(_options.ModulesRootPath))
        {
            _logger.LogWarning(
                "Plugin directory {ModulesRootPath} does not exist; no plugins will be loaded.",
                _options.ModulesRootPath);
            return new PluginDiscovery(_dependencyPlanner.CreatePlan([]), failures);
        }

        foreach (var directory in Directory
                     .EnumerateDirectories(_options.ModulesRootPath)
                     .Order(StringComparer.Ordinal))
        {
            try
            {
                var candidate = _manifestReader.Read(directory);
                if (excludedPluginIds is null || !excludedPluginIds.Contains(candidate.Manifest.Id))
                {
                    candidates.Add(candidate);
                    _logger.LogInformation(
                        "Discovered plugin {PluginId} {PluginVersion} ({RuntimeKind}) in {PluginDirectory}.",
                        candidate.Manifest.Id,
                        candidate.Manifest.Version,
                        candidate.Manifest.Runtime.Kind,
                        directory);
                }
                else
                {
                    _logger.LogDebug(
                        "Excluded plugin {PluginId} from the new runtime plan.",
                        candidate.Manifest.Id);
                }
            }
            catch (Exception exception) when (exception is IOException
                or JsonException
                or ArgumentException
                or FormatException)
            {
                failures.Add(new PluginLoadFailure(
                    null,
                    $"Could not read plugin from '{directory}': {exception.Message}"));
                _logger.LogError(exception, "Could not read plugin manifest from {PluginDirectory}.", directory);
            }
        }

        var plan = _dependencyPlanner.CreatePlan(candidates);
        failures.AddRange(plan.Failures);
        foreach (var failure in plan.Failures)
        {
            _logger.LogWarning(
                "Plugin dependency planning rejected {PluginId}: {FailureReason}",
                failure.PluginId?.Value ?? "<unknown>",
                failure.Reason);
        }

        _logger.LogInformation(
            "Plugin scan completed: {CandidateCount} candidate(s), {PlannedPluginCount} loadable, "
            + "{FailureCount} failure(s); load order: {PluginLoadOrder}.",
            candidates.Count,
            plan.OrderedCandidates.Count,
            failures.Count,
            plan.OrderedCandidates.Select(static candidate => candidate.Manifest.Id.Value).ToArray());
        return new PluginDiscovery(plan, failures);
    }

    private async Task<IReadOnlyList<Exception>> StopRuntimesAsync(
        IReadOnlyList<PluginRuntime> runtimes,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        for (var index = runtimes.Count - 1; index >= 0; index--)
        {
            failures.AddRange(await runtimes[index].StopAsync(cancellationToken).ConfigureAwait(false));
        }

        return failures;
    }

    private async Task RestartRuntimesAsync(
        IReadOnlyList<PluginRuntime> runtimes,
        CancellationToken cancellationToken)
    {
        foreach (var runtime in runtimes)
        {
            try
            {
                await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogCritical(
                    exception,
                    "Plugin {PluginId} failed to restart during rollback.",
                    runtime.Candidate.Manifest.Id);
            }
        }
    }

    private async Task DisposeRuntimesAsync(IReadOnlyList<PluginRuntime> runtimes)
    {
        foreach (var runtime in runtimes.Reverse())
        {
            try
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogWarning(
                    exception,
                    "Plugin {PluginId} reported an error while its runtime was being disposed.",
                    runtime.Candidate.Manifest.Id);
            }

            if (!PluginShadowCopy.TryDelete(runtime.ShadowRootPath))
            {
                _staleShadowRoots.Add(runtime.ShadowRootPath);
                _logger.LogWarning(
                    "Plugin {PluginId} shadow directory {ShadowRootPath} could not be deleted; "
                    + "cleanup will be retried.",
                    runtime.Candidate.Manifest.Id,
                    runtime.ShadowRootPath);
            }
            else
            {
                _logger.LogDebug(
                    "Disposed plugin {PluginId} runtime {RuntimeId} and deleted shadow directory {ShadowRootPath}.",
                    runtime.Candidate.Manifest.Id,
                    runtime.LoadedPlugin.RuntimeId,
                    runtime.ShadowRootPath);
            }
        }
    }

    private async Task ReleaseReferencesAsync(CancellationToken cancellationToken)
    {
        if (_referenceRelease is null)
        {
            return;
        }

        try
        {
            await _referenceRelease.ReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Reference release is host-specific and best effort. A failure here must not leave
            // a committed catalog pointing at runtimes that the manager did not adopt.
            _logger.LogWarning(
                exception,
                "The host could not release all references to the previous plugin snapshot.");
        }
    }

    private void CleanupStaleShadowRoots()
    {
        for (var index = _staleShadowRoots.Count - 1; index >= 0; index--)
        {
            if (PluginShadowCopy.TryDelete(_staleShadowRoots[index]))
            {
                _logger.LogDebug(
                    "Deleted stale plugin shadow directory {ShadowRootPath}.",
                    _staleShadowRoots[index]);
                _staleShadowRoots.RemoveAt(index);
            }
        }
    }

    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsInitialized || _hostServices is null)
        {
            throw new InvalidOperationException("Plugin runtime manager has not been initialized.");
        }
    }

    private static PluginId NormalizePluginId(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new PluginId(pluginId.Trim().ToLowerInvariant());
    }

    private sealed record PluginDiscovery(
        PluginLoadPlan Plan,
        IReadOnlyList<PluginLoadFailure> Failures);

    private sealed record PluginStageResult(
        IReadOnlyList<PluginRuntime> Runtimes,
        IReadOnlyList<PluginLoadFailure> Failures);
}
