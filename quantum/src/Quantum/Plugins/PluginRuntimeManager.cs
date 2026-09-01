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
    private PendingPluginInstall? _pendingInstall;
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

    public async Task<PluginInstallPreview> PrepareInstallAsync(
        Stream archiveStream,
        string archiveFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveFileName);
        var displayFileName = Path.GetFileName(archiveFileName.Trim());
        if (!displayFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return RejectedInstallPreview(displayFileName, "仅支持 ZIP 格式的插件包。");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            ClearPendingInstall();
            var stagingRoot = Path.Combine(SessionShadowRoot, $"install-preview-{Guid.NewGuid():N}");
            try
            {
                _logger.LogInformation("Preparing plugin package {ArchiveFileName} for installation.", displayFileName);
                var contentsRoot = await PluginPackageArchive.ExtractAsync(
                        archiveStream,
                        stagingRoot,
                        cancellationToken)
                    .ConfigureAwait(false);
                var packageCandidates = PluginPackageArchive.DiscoverPlugins(contentsRoot, _manifestReader);
                var evaluation = EvaluateInstall(packageCandidates);
                if (evaluation.Issues.Count == 0)
                {
                    var staged = await StageAsync(
                            evaluation.Plan,
                            tolerateFailures: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                    try
                    {
                        if (staged.Failures.Count > 0)
                        {
                            evaluation = evaluation with
                            {
                                Issues = staged.Failures.Select(static failure =>
                                    new PluginInstallIssue(failure.PluginId?.Value, failure.Reason)).ToArray()
                            };
                        }
                    }
                    finally
                    {
                        await DisposeRuntimesAsync(staged.Runtimes).ConfigureAwait(false);
                    }
                }

                var previewId = evaluation.Issues.Count == 0
                    && evaluation.Items.Any(static item =>
                        item.Action is PluginInstallAction.Install or PluginInstallAction.Upgrade)
                        ? Guid.NewGuid().ToString("N")
                        : null;
                var preview = new PluginInstallPreview(
                    previewId,
                    displayFileName,
                    evaluation.Items,
                    evaluation.Issues);
                if (previewId is null)
                {
                    PluginShadowCopy.TryDelete(stagingRoot);
                }
                else
                {
                    _pendingInstall = new PendingPluginInstall(
                        preview,
                        stagingRoot,
                        packageCandidates,
                        _catalog.Revision);
                }

                _logger.LogInformation(
                    "Plugin package {ArchiveFileName} preview completed: {PackagePluginCount} plugin(s), "
                    + "{InstallPluginCount} selected, {IssueCount} issue(s).",
                    displayFileName,
                    evaluation.Items.Count,
                    preview.InstallCount,
                    preview.Issues.Count);
                return preview;
            }
            catch (Exception exception) when (exception is IOException
                or InvalidDataException
                or JsonException
                or ArgumentException
                or FormatException
                or UnauthorizedAccessException)
            {
                PluginShadowCopy.TryDelete(stagingRoot);
                _logger.LogWarning(
                    exception,
                    "Plugin package {ArchiveFileName} could not be prepared.",
                    displayFileName);
                return RejectedInstallPreview(displayFileName, exception.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginOperationResult> InstallAsync(
        string previewId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            var pending = _pendingInstall;
            if (pending is null || !string.Equals(pending.Preview.PreviewId, previewId, StringComparison.Ordinal))
            {
                return PluginOperationResult.Failure("安装预览已失效，请重新拖入 ZIP 包。");
            }

            if (pending.CatalogRevision != _catalog.Revision)
            {
                ClearPendingInstall();
                return PluginOperationResult.Failure("插件清单在确认前已变化，请重新拖入 ZIP 包并确认最新清单。");
            }

            var evaluation = EvaluateInstall(pending.PackageCandidates);
            if (evaluation.Issues.Count > 0
                || !evaluation.Items.SequenceEqual(pending.Preview.Plugins))
            {
                ClearPendingInstall();
                return PluginOperationResult.Failure("插件文件或依赖状态在确认前已变化，请重新拖入 ZIP 包。");
            }

            var selectedPackages = evaluation.Items
                .Where(static item => item.Action is PluginInstallAction.Install or PluginInstallAction.Upgrade)
                .Select(item => evaluation.PackageCandidates[new PluginId(item.PluginId)])
                .ToArray();
            var transactionRoot = Path.Combine(
                Path.GetFullPath(_options.ModulesRootPath),
                $".quantum-install-{Guid.NewGuid():N}");
            var operations = new List<PluginFileInstallOperation>();
            var filesCommitted = false;
            var preserveTransaction = false;
            try
            {
                var incomingRoot = Path.Combine(transactionRoot, "incoming");
                var backupRoot = Path.Combine(transactionRoot, "backup");
                Directory.CreateDirectory(incomingRoot);
                Directory.CreateDirectory(backupRoot);

                foreach (var package in selectedPackages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pluginId = package.Manifest.Id;
                    var destinationPath = evaluation.InstalledCandidates.TryGetValue(pluginId, out var installed)
                        ? installed.RootPath
                        : Path.Combine(_options.ModulesRootPath, pluginId.Value);
                    EnsureDirectModuleChild(destinationPath);
                    var incomingPath = Path.Combine(incomingRoot, pluginId.Value);
                    var backupPath = Path.Combine(backupRoot, pluginId.Value);
                    PluginShadowCopy.Copy(package.RootPath, incomingPath);
                    operations.Add(new PluginFileInstallOperation(
                        pluginId,
                        destinationPath,
                        incomingPath,
                        backupPath,
                        Directory.Exists(destinationPath)));
                }

                filesCommitted = operations.Count > 0;
                foreach (var operation in operations)
                {
                    if (operation.HadExistingDirectory)
                    {
                        Directory.Move(operation.DestinationPath, operation.BackupPath);
                    }

                    Directory.Move(operation.IncomingPath, operation.DestinationPath);
                }

                var discovery = Discover(excludedPluginIds: null);
                var compatibilityFailure = discovery.Failures.FirstOrDefault();
                if (compatibilityFailure is not null)
                {
                    await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                    filesCommitted = false;
                    ClearPendingInstall();
                    return PluginOperationResult.Failure(
                        $"安装后的插件清单不兼容，未安装任何插件：{compatibilityFailure.Reason}");
                }

                foreach (var package in selectedPackages)
                {
                    var planned = discovery.Plan.OrderedCandidates.FirstOrDefault(candidate =>
                        candidate.Manifest.Id == package.Manifest.Id);
                    if (planned is null || !planned.Manifest.Version.Equals(package.Manifest.Version))
                    {
                        await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                        filesCommitted = false;
                        ClearPendingInstall();
                        return PluginOperationResult.Failure(
                            $"插件 '{package.Manifest.Id}' 的安装版本清单发生变化，未安装任何插件。");
                    }
                }

                var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, excludedPluginIds: null);
                if (missingCurrent.Length > 0)
                {
                    await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                    filesCommitted = false;
                    ClearPendingInstall();
                    return PluginOperationResult.Failure(
                        $"安装会使已加载插件失效，未安装任何插件：{string.Join(", ", missingCurrent)}。");
                }

                var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                    filesCommitted = false;
                    ClearPendingInstall();
                    return PluginOperationResult.Failure($"{result.Message} Modules 文件已恢复。");
                }

                filesCommitted = false;
                ClearPendingInstall();
                return PluginOperationResult.Success(
                    $"已安装 {selectedPackages.Length} 个插件："
                    + $"{string.Join(", ", selectedPackages.Select(static plugin => $"{plugin.Manifest.Id} {plugin.Manifest.Version}"))}。");
            }
            catch (OperationCanceledException exception)
            {
                if (filesCommitted)
                {
                    try
                    {
                        await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                    }
                    catch (Exception rollbackException)
                    {
                        preserveTransaction = true;
                        throw new InvalidOperationException(
                            "插件安装已取消，但 Modules 文件恢复未完成。",
                            new AggregateException(exception, rollbackException));
                    }
                }

                ClearPendingInstall();
                throw;
            }
            catch (Exception exception)
            {
                if (filesCommitted)
                {
                    try
                    {
                        await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                    }
                    catch (Exception rollbackException)
                    {
                        preserveTransaction = true;
                        _logger.LogCritical(
                            rollbackException,
                            "Plugin package file rollback failed for preview {PreviewId}.",
                            previewId);
                        return PluginOperationResult.Failure(
                            $"插件安装失败且文件恢复未完成，请检查 Modules：{exception.Message}；"
                            + $"恢复错误：{rollbackException.Message}");
                    }
                }

                _logger.LogError(exception, "Plugin package installation failed for preview {PreviewId}.", previewId);
                ClearPendingInstall();
                return PluginOperationResult.Failure($"插件安装失败，未安装任何插件：{exception.Message}");
            }
            finally
            {
                if (!preserveTransaction)
                {
                    PluginShadowCopy.TryDelete(transactionRoot);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CancelInstallAsync(
        string previewId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pendingInstall is not null
                && string.Equals(_pendingInstall.Preview.PreviewId, previewId, StringComparison.Ordinal))
            {
                ClearPendingInstall();
            }
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
            ClearPendingInstall();
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

    private PluginInstallEvaluation EvaluateInstall(IReadOnlyList<PluginCandidate> packageCandidates)
    {
        var moduleScan = ReadModuleCandidates();
        var issues = moduleScan.Failures
            .Select(static failure => new PluginInstallIssue(failure.PluginId?.Value, failure.Reason))
            .ToList();

        var installedCandidates = new Dictionary<PluginId, PluginCandidate>();
        foreach (var group in moduleScan.Candidates.GroupBy(static candidate => candidate.Manifest.Id))
        {
            var ordered = OrderByNewest(group);
            installedCandidates[group.Key] = ordered[0];
            if (ordered.Length > 1)
            {
                issues.Add(new PluginInstallIssue(
                    group.Key.Value,
                    "Modules 中有多个目录声明了同一个插件 id，无法生成唯一版本清单。"));
            }
        }

        var selectedPackages = new Dictionary<PluginId, PluginCandidate>();
        foreach (var group in packageCandidates.GroupBy(static candidate => candidate.Manifest.Id))
        {
            var ordered = OrderByNewest(group);
            var newest = ordered[0];
            selectedPackages[group.Key] = newest;
            if (ordered.Skip(1).Any(candidate => candidate.Manifest.Version.Equals(newest.Manifest.Version)))
            {
                issues.Add(new PluginInstallIssue(
                    group.Key.Value,
                    $"ZIP 中有多个 {newest.Manifest.Version} 版本，无法确定应安装的目录。"));
            }
        }

        var mergedCandidates = installedCandidates.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        var items = new List<PluginInstallPreviewItem>();
        foreach (var package in selectedPackages.Values
                     .OrderBy(static candidate => candidate.Manifest.Id.Value, StringComparer.Ordinal))
        {
            installedCandidates.TryGetValue(package.Manifest.Id, out var installed);
            var comparison = installed is null
                ? 1
                : package.Manifest.Version.CompareTo(installed.Manifest.Version);
            var action = installed is null
                ? PluginInstallAction.Install
                : comparison > 0
                    ? PluginInstallAction.Upgrade
                    : PluginInstallAction.KeepInstalled;
            if (action is PluginInstallAction.Install or PluginInstallAction.Upgrade)
            {
                mergedCandidates[package.Manifest.Id] = package;
            }

            items.Add(new PluginInstallPreviewItem(
                package.Manifest.Id.Value,
                package.Manifest.Version.ToString(),
                installed?.Manifest.Version.ToString(),
                action));

            if (action == PluginInstallAction.Install)
            {
                var canonicalDestination = Path.Combine(_options.ModulesRootPath, package.Manifest.Id.Value);
                if (Directory.Exists(canonicalDestination))
                {
                    issues.Add(new PluginInstallIssue(
                        package.Manifest.Id.Value,
                        $"目标目录 '{canonicalDestination}' 已存在但不是有效的已安装插件。"));
                }
            }
        }

        var plan = _dependencyPlanner.CreatePlan(mergedCandidates.Values);
        issues.AddRange(plan.Failures.Select(static failure =>
            new PluginInstallIssue(failure.PluginId?.Value, failure.Reason)));
        var plannedIds = plan.OrderedCandidates
            .Select(static candidate => candidate.Manifest.Id)
            .ToHashSet();
        foreach (var current in _runtimes.Where(runtime =>
                     !plannedIds.Contains(runtime.Candidate.Manifest.Id)))
        {
            issues.Add(new PluginInstallIssue(
                current.Candidate.Manifest.Id.Value,
                "新的插件版本清单会使当前已加载插件失效。"));
        }

        if (!items.Any(static item =>
                item.Action is PluginInstallAction.Install or PluginInstallAction.Upgrade))
        {
            issues.Add(new PluginInstallIssue(
                null,
                "ZIP 中没有比当前已安装版本更新的插件。"));
        }

        return new PluginInstallEvaluation(items, issues, selectedPackages, installedCandidates, plan);
    }

    private ModuleCandidateScan ReadModuleCandidates()
    {
        var candidates = new List<PluginCandidate>();
        var failures = new List<PluginLoadFailure>();
        if (!Directory.Exists(_options.ModulesRootPath))
        {
            return new ModuleCandidateScan(candidates, failures);
        }

        foreach (var directory in EnumerateModuleDirectories())
        {
            try
            {
                candidates.Add(_manifestReader.Read(directory));
            }
            catch (Exception exception) when (exception is IOException
                or JsonException
                or ArgumentException
                or FormatException)
            {
                failures.Add(new PluginLoadFailure(
                    null,
                    $"Could not read plugin from '{directory}': {exception.Message}"));
            }
        }

        return new ModuleCandidateScan(candidates, failures);
    }

    private IEnumerable<string> EnumerateModuleDirectories()
        => Directory
            .EnumerateDirectories(_options.ModulesRootPath)
            .Where(static directory => !Path.GetFileName(directory)
                .StartsWith(".quantum-install-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static PluginCandidate[] OrderByNewest(IEnumerable<PluginCandidate> candidates)
        => candidates
            .OrderByDescending(
                static candidate => candidate.Manifest.Version,
                Comparer<SemanticVersion>.Create(static (left, right) => left.CompareTo(right)))
            .ThenBy(static candidate => candidate.RootPath, StringComparer.Ordinal)
            .ToArray();

    private static PluginInstallPreview RejectedInstallPreview(string archiveFileName, string reason)
        => new(
            PreviewId: null,
            archiveFileName,
            Plugins: [],
            Issues: [new PluginInstallIssue(null, reason)]);

    private void ClearPendingInstall()
    {
        var pending = _pendingInstall;
        _pendingInstall = null;
        if (pending is not null && !PluginShadowCopy.TryDelete(pending.StagingRoot))
        {
            _logger.LogWarning(
                "Could not delete plugin install preview directory {StagingRoot}.",
                pending.StagingRoot);
        }
    }

    private void EnsureDirectModuleChild(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        var modulesRoot = Path.GetFullPath(_options.ModulesRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (parent is null || !string.Equals(
                parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                modulesRoot,
                comparison))
        {
            throw new InvalidOperationException($"插件安装目标 '{fullPath}' 不在 Modules 根目录中。");
        }
    }

    private static Task RestoreInstalledFilesAsync(IReadOnlyList<PluginFileInstallOperation> operations)
    {
        foreach (var operation in operations.Reverse())
        {
            if (Directory.Exists(operation.BackupPath))
            {
                if (Directory.Exists(operation.DestinationPath))
                {
                    Directory.Delete(operation.DestinationPath, recursive: true);
                }

                Directory.Move(operation.BackupPath, operation.DestinationPath);
            }
            else if (!operation.HadExistingDirectory
                && Directory.Exists(operation.DestinationPath)
                && !Directory.Exists(operation.IncomingPath))
            {
                Directory.Delete(operation.DestinationPath, recursive: true);
            }
        }

        return Task.CompletedTask;
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

        foreach (var directory in EnumerateModuleDirectories())
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

    private sealed record ModuleCandidateScan(
        IReadOnlyList<PluginCandidate> Candidates,
        IReadOnlyList<PluginLoadFailure> Failures);

    private sealed record PluginInstallEvaluation(
        IReadOnlyList<PluginInstallPreviewItem> Items,
        IReadOnlyList<PluginInstallIssue> Issues,
        IReadOnlyDictionary<PluginId, PluginCandidate> PackageCandidates,
        IReadOnlyDictionary<PluginId, PluginCandidate> InstalledCandidates,
        PluginLoadPlan Plan);

    private sealed record PendingPluginInstall(
        PluginInstallPreview Preview,
        string StagingRoot,
        IReadOnlyList<PluginCandidate> PackageCandidates,
        long CatalogRevision);

    private sealed record PluginFileInstallOperation(
        PluginId PluginId,
        string DestinationPath,
        string IncomingPath,
        string BackupPath,
        bool HadExistingDirectory);
}
