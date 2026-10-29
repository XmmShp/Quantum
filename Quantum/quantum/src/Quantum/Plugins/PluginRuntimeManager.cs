using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Quantum.Localization;

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

    private string DisabledRootPath => Path.Combine(
        Path.GetFullPath(_options.ModulesRootPath),
        "disabled");

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
                return PluginOperationResult.Failure(AppText.Get(
                    "PluginArgIsNotLoadedTryRescanning",
                    normalized));
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
                return PluginOperationResult.Failure(AppText.Get(
                    "TheOnDiskVersionOfPluginArgIs",
                    normalized));
            }

            var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, excludedPluginIds: null);
            if (missingCurrent.Length > 0)
            {
                _logger.LogWarning(
                    "Reload of plugin {PluginId} was cancelled because the new plan would remove "
                    + "currently loaded plugins: {MissingPluginIds}.",
                    normalized,
                    missingCurrent);
                return PluginOperationResult.Failure(AppText.Get(
                    "TheNewSnapshotWouldInvalidateLoadedPluginsSo",
                    string.Join(", ", missingCurrent)));
            }

            var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return result;
            }

            return PluginOperationResult.Success(
                dependentPluginIds.Count == 0
                    ? AppText.Get(
                        "PluginArgWasHotSwappedFromArgTo",
                        normalized,
                        current.Candidate.Manifest.Version,
                        candidate.Manifest.Version)
                    : AppText.Get(
                        "PluginArgWasHotSwappedFromArgTo2",
                        normalized,
                        current.Candidate.Manifest.Version,
                        candidate.Manifest.Version,
                        string.Join(", ", dependentPluginIds)));
        }
        finally
        {
            _gate.Release();
        }
    }

    public PluginOperationImpact GetDisableImpact(string pluginId)
        => GetCascadeImpact(pluginId);

    public PluginOperationImpact GetUninstallImpact(string pluginId)
        => GetCascadeImpact(pluginId);

    private PluginOperationImpact GetCascadeImpact(string pluginId)
    {
        var normalized = NormalizePluginId(pluginId);
        while (true)
        {
            var revision = _catalog.Revision;
            var plugins = _catalog.Plugins;
            var dependents = FindStrongDependentIds(
                    plugins.Select(static plugin => plugin.Manifest),
                    normalized)
                .Select(static id => (string)id)
                .ToArray();
            if (revision == _catalog.Revision)
            {
                return new PluginOperationImpact((string)normalized, dependents, revision);
            }
        }
    }

    public async Task<PluginOperationResult> DisableAsync(
        string pluginId,
        long confirmedCatalogRevision,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePluginId(pluginId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            _logger.LogInformation("Disable requested for plugin {PluginId}.", normalized);
            var current = _runtimes.FirstOrDefault(runtime => runtime.Candidate.Manifest.Id == normalized);
            if (current is null)
            {
                _logger.LogWarning("Cannot disable plugin {PluginId} because it is not loaded.", normalized);
                return PluginOperationResult.Failure(AppText.Get("PluginArgIsNotLoaded", normalized));
            }

            var dependentPluginIds = FindStrongDependentIds(
                _runtimes.Select(static runtime => runtime.Candidate.Manifest),
                normalized);
            if (confirmedCatalogRevision != _catalog.Revision)
            {
                _logger.LogWarning(
                    "Disable confirmation for plugin {PluginId} is stale at catalog revision {CatalogRevision}.",
                    normalized,
                    _catalog.Revision);
                return PluginOperationResult.Failure(AppText.Get(
                    "ThePluginCatalogChangedConfirmDisablingPluginArg",
                    normalized));
            }

            var affectedPluginIds = dependentPluginIds.Append(normalized).ToHashSet();
            var affectedRuntimes = _runtimes
                .Where(runtime => affectedPluginIds.Contains(runtime.Candidate.Manifest.Id))
                .ToArray();
            var disabledRoot = DisabledRootPath;
            var moves = new List<PluginDirectoryMove>();
            try
            {
                foreach (var runtime in affectedRuntimes)
                {
                    var sourcePath = Path.GetFullPath(runtime.Candidate.RootPath);
                    EnsureDirectModuleChild(sourcePath);
                    var destinationPath = Path.Combine(disabledRoot, Path.GetFileName(sourcePath));
                    if (Directory.Exists(destinationPath))
                    {
                        return PluginOperationResult.Failure(AppText.Get(
                            "ArgAlreadyExistsInTheDisabledDirectoryNo",
                            Path.GetFileName(sourcePath)));
                    }

                    moves.Add(new PluginDirectoryMove(sourcePath, destinationPath));
                }

                Directory.CreateDirectory(disabledRoot);
                MovePluginDirectories(moves);
                var discovery = Discover(excludedPluginIds: null);
                var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, affectedPluginIds);
                if (missingCurrent.Length > 0)
                {
                    RestorePluginDirectories(moves);
                    return PluginOperationResult.Failure(AppText.Get(
                        "TheNewSnapshotWouldInvalidateOtherLoadedPlugins",
                        string.Join(", ", missingCurrent)));
                }

                var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    RestorePluginDirectories(moves);
                    return result;
                }

                return PluginOperationResult.Success(
                    dependentPluginIds.Count == 0
                        ? AppText.Get(
                            "PluginArgWasDisabledAndMovedToModules",
                            normalized)
                        : AppText.Get(
                            "PluginArgAndItsStronglyDependentDownstreamPlugins",
                            normalized,
                            string.Join(", ", dependentPluginIds)));
            }
            catch (Exception exception)
            {
                RestorePluginDirectories(moves);
                _logger.LogError(exception, "Could not disable plugin {PluginId}; file moves were rolled back.", normalized);
                return PluginOperationResult.Failure(AppText.Get(
                    "CouldNotDisablePluginArgDirectoriesWereRolled",
                    normalized,
                    exception.Message));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<DisabledPluginInfo> GetDisabledPlugins()
        => ReadDisabledModuleCandidates()
            .Candidates
            .OrderBy(static candidate => (string)candidate.Manifest.Id, StringComparer.Ordinal)
            .Select(static candidate => new DisabledPluginInfo(
                (string)candidate.Manifest.Id,
                candidate.Manifest.Version.ToString()))
            .ToArray();

    public async Task<PluginOperationResult> EnableAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePluginId(pluginId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            var matches = ReadDisabledModuleCandidates().Candidates
                .Where(candidate => candidate.Manifest.Id == normalized)
                .ToArray();
            if (matches.Length == 0)
            {
                return PluginOperationResult.Failure(AppText.Get(
                    "PluginArgWasNotFoundInTheDisabled",
                    normalized));
            }

            if (matches.Length > 1)
            {
                return PluginOperationResult.Failure(AppText.Get(
                    "MultipleCopiesOfPluginArgExistInThe",
                    normalized));
            }

            var sourcePath = Path.GetFullPath(matches[0].RootPath);
            var destinationPath = Path.Combine(
                Path.GetFullPath(_options.ModulesRootPath),
                Path.GetFileName(sourcePath));
            EnsureDirectModuleChild(destinationPath);
            if (Directory.Exists(destinationPath))
            {
                return PluginOperationResult.Failure(AppText.Get(
                    "DirectoryArgAlreadyExistsInModulesSoPlugin",
                    Path.GetFileName(destinationPath),
                    normalized));
            }

            var moves = new[] { new PluginDirectoryMove(sourcePath, destinationPath) };
            try
            {
                MovePluginDirectories(moves);
                var discovery = Discover(excludedPluginIds: null);
                var enabled = discovery.Plan.OrderedCandidates.Any(candidate => candidate.Manifest.Id == normalized);
                var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, excludedPluginIds: null);
                if (!enabled || missingCurrent.Length > 0)
                {
                    RestorePluginDirectories(moves);
                    var reason = discovery.Failures
                        .FirstOrDefault(failure => failure.PluginId == normalized)?.Reason
                        ?? discovery.Failures.FirstOrDefault()?.Reason
                        ?? AppText.Get("DependencyOrCompatibilityChecksFailed");
                    return PluginOperationResult.Failure(
                        !enabled
                            ? AppText.Get("PluginArgCannotBeEnabledArg", normalized, reason)
                            : AppText.Get(
                                "EnablingWouldInvalidateLoadedPluginsSoTheOperation",
                                string.Join(", ", missingCurrent)));
                }

                var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    RestorePluginDirectories(moves);
                    return result;
                }

                return PluginOperationResult.Success(AppText.Get(
                    "PluginArgWasEnabledAndMovedBackTo",
                    normalized));
            }
            catch (Exception exception)
            {
                RestorePluginDirectories(moves);
                _logger.LogError(exception, "Could not enable plugin {PluginId}; file move was rolled back.", normalized);
                return PluginOperationResult.Failure(AppText.Get(
                    "CouldNotEnablePluginArgDirectoriesWereRolled",
                    normalized,
                    exception.Message));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginOperationResult> UninstallAsync(
        string pluginId,
        long confirmedCatalogRevision,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePluginId(pluginId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            if (confirmedCatalogRevision != _catalog.Revision)
            {
                return PluginOperationResult.Failure(AppText.Get(
                    "ThePluginCatalogChangedConfirmUninstallingPluginArg",
                    normalized));
            }

            var current = _runtimes.FirstOrDefault(runtime => runtime.Candidate.Manifest.Id == normalized);
            if (current is null)
            {
                var disabledMatches = ReadDisabledModuleCandidates().Candidates
                    .Where(candidate => candidate.Manifest.Id == normalized)
                    .ToArray();
                if (disabledMatches.Length == 0)
                {
                    return PluginOperationResult.Failure(AppText.Get(
                        "PluginArgIsNotInstalled",
                        normalized));
                }

                if (disabledMatches.Length > 1)
                {
                    return PluginOperationResult.Failure(AppText.Get(
                        "MultipleCopiesOfPluginArgExistInThe2",
                        normalized));
                }

                var disabledPath = Path.GetFullPath(disabledMatches[0].RootPath);
                EnsureDirectDisabledChild(disabledPath);
                if (!PluginShadowCopy.TryDelete(disabledPath))
                {
                    return PluginOperationResult.Failure(AppText.Get(
                        "CouldNotDeleteThePhysicalFilesForPlugin",
                        normalized,
                        disabledPath));
                }

                return PluginOperationResult.Success(AppText.Get(
                    "DisabledPluginArgWasPermanentlyUninstalledAndIts",
                    normalized));
            }

            var dependentPluginIds = FindStrongDependentIds(
                _runtimes.Select(static runtime => runtime.Candidate.Manifest),
                normalized);
            var affectedPluginIds = dependentPluginIds.Append(normalized).ToHashSet();
            var transactionRoot = Path.Combine(
                Path.GetFullPath(_options.ModulesRootPath),
                $".quantum-uninstall-{Guid.NewGuid():N}");
            var backupRoot = Path.Combine(transactionRoot, "backup");
            var moves = _runtimes
                .Where(runtime => affectedPluginIds.Contains(runtime.Candidate.Manifest.Id))
                .Select(runtime =>
                {
                    var sourcePath = Path.GetFullPath(runtime.Candidate.RootPath);
                    EnsureDirectModuleChild(sourcePath);
                    return new PluginDirectoryMove(
                        sourcePath,
                        Path.Combine(backupRoot, Path.GetFileName(sourcePath)));
                })
                .ToArray();

            try
            {
                Directory.CreateDirectory(backupRoot);
                MovePluginDirectories(moves);
                var discovery = Discover(excludedPluginIds: null);
                var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, affectedPluginIds);
                if (missingCurrent.Length > 0)
                {
                    RestorePluginDirectories(moves);
                    PluginShadowCopy.TryDelete(transactionRoot);
                    return PluginOperationResult.Failure(AppText.Get(
                        "TheNewSnapshotWouldInvalidateOtherLoadedPlugins2",
                        string.Join(", ", missingCurrent)));
                }

                var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    RestorePluginDirectories(moves);
                    PluginShadowCopy.TryDelete(transactionRoot);
                    return result;
                }

                if (!PluginShadowCopy.TryDelete(transactionRoot))
                {
                    _logger.LogError(
                        "Plugin {PluginId} was removed from the runtime, but uninstall transaction {TransactionRoot} could not be deleted.",
                        normalized,
                        transactionRoot);
                    return PluginOperationResult.Failure(AppText.Get(
                        "ThePluginStoppedButItsPhysicalFilesCould",
                        transactionRoot));
                }

                return PluginOperationResult.Success(
                    dependentPluginIds.Count == 0
                        ? AppText.Get(
                            "PluginArgWasPermanentlyUninstalledAndItsPhysical",
                            normalized)
                        : AppText.Get(
                            "PluginArgAndItsStronglyDependentDownstreamPlugins2",
                            normalized,
                            string.Join(", ", dependentPluginIds)));
            }
            catch (Exception exception)
            {
                RestorePluginDirectories(moves);
                PluginShadowCopy.TryDelete(transactionRoot);
                _logger.LogError(exception, "Could not uninstall plugin {PluginId}; file moves were rolled back.", normalized);
                return PluginOperationResult.Failure(AppText.Get(
                    "CouldNotUninstallPluginArgDirectoriesWereRolled",
                    normalized,
                    exception.Message));
            }
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
                ? PluginOperationResult.Success(AppText.Get("ModulesWasRescannedAndThePluginRuntimeSnapshot"))
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
            return RejectedInstallPreview(displayFileName, AppText.Get("OnlyZIPPluginPackagesAreSupported"));
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
                                    new PluginInstallIssue(failure.PluginId?.ToString(), failure.Reason)).ToArray()
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
                return PluginOperationResult.Failure(AppText.Get("TheInstallationPreviewExpiredDropTheZIPPackage"));
            }

            if (pending.CatalogRevision != _catalog.Revision)
            {
                ClearPendingInstall();
                return PluginOperationResult.Failure(AppText.Get(
                    "ThePluginCatalogChangedBeforeConfirmationDropThe"));
            }

            var evaluation = EvaluateInstall(pending.PackageCandidates);
            if (evaluation.Issues.Count > 0
                || !evaluation.Items.SequenceEqual(pending.Preview.Plugins))
            {
                ClearPendingInstall();
                return PluginOperationResult.Failure(AppText.Get(
                    "PluginFilesOrDependencyStateChangedBeforeConfirmation"));
            }

            var selectedPackages = evaluation.Items
                .Where(static item => item.Action is PluginInstallAction.Install or PluginInstallAction.Upgrade)
                .Select(item => evaluation.PackageCandidates[PluginId.Of(item.PluginId)])
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
                        : Path.Combine(_options.ModulesRootPath, (string)pluginId);
                    EnsureDirectModuleChild(destinationPath);
                    var incomingPath = Path.Combine(incomingRoot, (string)pluginId);
                    var backupPath = Path.Combine(backupRoot, (string)pluginId);
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
                    return PluginOperationResult.Failure(AppText.Get(
                        "TheResultingPluginCatalogIsIncompatibleNoPlugins",
                        compatibilityFailure.Reason));
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
                        return PluginOperationResult.Failure(AppText.Get(
                            "TheSelectedInstallationVersionForPluginArgChanged",
                            package.Manifest.Id));
                    }
                }

                var missingCurrent = FindMissingCurrentPlugins(discovery.Plan, excludedPluginIds: null);
                if (missingCurrent.Length > 0)
                {
                    await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                    filesCommitted = false;
                    ClearPendingInstall();
                    return PluginOperationResult.Failure(AppText.Get(
                        "InstallationWouldInvalidateLoadedPluginsNoPluginsWere",
                        string.Join(", ", missingCurrent)));
                }

                var result = await ReplaceAllAsync(discovery, cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    await RestoreInstalledFilesAsync(operations).ConfigureAwait(false);
                    filesCommitted = false;
                    ClearPendingInstall();
                    return PluginOperationResult.Failure(AppText.Get(
                        "ArgModulesFilesWereRestored",
                        result.Message));
                }

                filesCommitted = false;
                ClearPendingInstall();
                var installedPluginList = string.Join(", ", selectedPackages.Select(static plugin =>
                    $"{plugin.Manifest.Id} {plugin.Manifest.Version}"));
                return PluginOperationResult.Success(
                    selectedPackages.Length == 1
                        ? AppText.Get(
                            "InstalledPluginSingular",
                            selectedPackages.Length,
                            installedPluginList)
                        : AppText.Get(
                            "InstalledPluginsPlural",
                            selectedPackages.Length,
                            installedPluginList));
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
                            AppText.Get("PluginInstallationWasCancelledButModulesFilesWere"),
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
                        return PluginOperationResult.Failure(AppText.Get(
                            "PluginInstallationFailedAndFileRestorationDidNot",
                            exception.Message,
                            rollbackException.Message));
                    }
                }

                _logger.LogError(exception, "Plugin package installation failed for preview {PreviewId}.", previewId);
                ClearPendingInstall();
                return PluginOperationResult.Failure(AppText.Get(
                    "PluginInstallationFailedNoPluginsWereInstalledArg",
                    exception.Message));
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
            return PluginOperationResult.Failure(AppText.Get(
                "TheNewPluginSnapshotCouldNotBeLoaded",
                staged.Failures[0].Reason));
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
            return PluginOperationResult.Failure(AppText.Get(
                "AnExistingPluginLifecycleCouldNotStopSo",
                stopFailures[0].Message));
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
            return PluginOperationResult.Failure(AppText.Get(
                "TheNewPluginLifecycleFailedToStartAnd",
                exception.Message));
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
            _runtimes.Select(static runtime => (string)runtime.Candidate.Manifest.Id).ToArray());
        return PluginOperationResult.Success(AppText.Get("ThePluginRuntimeSnapshotWasUpdated"));
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
            .Select(runtime => (string)runtime.Candidate.Manifest.Id)
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
        var rpcRegistry = PluginRpcRegistry.Create(
            runtimes
                .Select(static runtime => runtime.LoadedPlugin.RpcRuntime)
                .OfType<PluginRpcRuntime>(),
            _logger);
        foreach (var runtime in runtimes)
        {
            runtime.UseEnvironment(environment);
            runtime.UseRpcRegistry(rpcRegistry);
        }
    }

    private PluginInstallEvaluation EvaluateInstall(IReadOnlyList<PluginCandidate> packageCandidates)
    {
        var moduleScan = ReadModuleCandidates();
        var issues = moduleScan.Failures
            .Select(static failure => new PluginInstallIssue(failure.PluginId?.ToString(), failure.Reason))
            .ToList();

        var installedCandidates = new Dictionary<PluginId, PluginCandidate>();
        foreach (var group in moduleScan.Candidates.GroupBy(static candidate => candidate.Manifest.Id))
        {
            var ordered = OrderByNewest(group);
            installedCandidates[group.Key] = ordered[0];
            if (ordered.Length > 1)
            {
                issues.Add(new PluginInstallIssue(
                    (string)group.Key,
                    AppText.Get("MultipleModulesDirectoriesDeclareTheSamePluginId")));
            }
        }

        var selectedPackages = new Dictionary<PluginId, PluginCandidate>();
        foreach (var group in packageCandidates.GroupBy(static candidate => candidate.Manifest.Id))
        {
            var ordered = OrderByNewest(group);
            var newest = ordered[0];
            selectedPackages[group.Key] = newest;
            if (ordered.Skip(1).Any(candidate =>
                candidate.Manifest.Version.CompareTo(newest.Manifest.Version) == 0))
            {
                issues.Add(new PluginInstallIssue(
                    (string)group.Key,
                    AppText.Get(
                        "TheZIPContainsMultipleCopiesOfVersionArg",
                        newest.Manifest.Version)));
            }
        }

        var disabledPluginIds = ReadDisabledModuleCandidates().Candidates
            .Select(static candidate => candidate.Manifest.Id)
            .ToHashSet();
        foreach (var disabledPluginId in selectedPackages.Keys.Where(disabledPluginIds.Contains))
        {
            issues.Add(new PluginInstallIssue(
                (string)disabledPluginId,
                AppText.Get("ThisPluginIsDisabledEnableItAgainOr")));
        }

        var mergedCandidates = installedCandidates.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        var items = new List<PluginInstallPreviewItem>();
        foreach (var package in selectedPackages.Values
                     .OrderBy(static candidate => (string)candidate.Manifest.Id, StringComparer.Ordinal))
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
                (string)package.Manifest.Id,
                package.Manifest.Version.ToString(),
                installed?.Manifest.Version.ToString(),
                action));

            if (action == PluginInstallAction.Install)
            {
                var canonicalDestination = Path.Combine(_options.ModulesRootPath, (string)package.Manifest.Id);
                if (Directory.Exists(canonicalDestination))
                {
                    issues.Add(new PluginInstallIssue(
                        (string)package.Manifest.Id,
                        AppText.Get(
                            "TargetDirectoryArgExistsButIsNotA",
                            canonicalDestination)));
                }
            }
        }

        var plan = _dependencyPlanner.CreatePlan(mergedCandidates.Values);
        issues.AddRange(plan.Failures.Select(static failure =>
            new PluginInstallIssue(failure.PluginId?.ToString(), failure.Reason)));
        var plannedIds = plan.OrderedCandidates
            .Select(static candidate => candidate.Manifest.Id)
            .ToHashSet();
        foreach (var current in _runtimes.Where(runtime =>
                     !plannedIds.Contains(runtime.Candidate.Manifest.Id)))
        {
            issues.Add(new PluginInstallIssue(
                (string)current.Candidate.Manifest.Id,
                AppText.Get("TheNewPluginVersionCatalogWouldInvalidateA")));
        }

        if (!items.Any(static item =>
                item.Action is PluginInstallAction.Install or PluginInstallAction.Upgrade))
        {
            issues.Add(new PluginInstallIssue(
                null,
                AppText.Get("TheZIPContainsNoPluginNewerThanThe")));
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

    private ModuleCandidateScan ReadDisabledModuleCandidates()
    {
        var candidates = new List<PluginCandidate>();
        var failures = new List<PluginLoadFailure>();
        if (!Directory.Exists(DisabledRootPath))
        {
            return new ModuleCandidateScan(candidates, failures);
        }

        foreach (var directory in Directory.EnumerateDirectories(DisabledRootPath).Order(StringComparer.Ordinal))
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
                    $"Could not read disabled plugin from '{directory}': {exception.Message}"));
            }
        }

        return new ModuleCandidateScan(candidates, failures);
    }

    private IEnumerable<string> EnumerateModuleDirectories()
        => Directory
            .EnumerateDirectories(_options.ModulesRootPath)
            .Where(static directory =>
            {
                var name = Path.GetFileName(directory);
                return !string.Equals(name, "disabled", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith(".quantum-", StringComparison.Ordinal);
            })
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
            throw new InvalidOperationException(AppText.Get(
                "PluginInstallationTargetArgIsOutsideTheModules",
                fullPath));
        }
    }

    private void EnsureDirectDisabledChild(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath);
        var disabledRoot = DisabledRootPath
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (parent is null || !string.Equals(
                parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                disabledRoot,
                comparison))
        {
            throw new InvalidOperationException(AppText.Get(
                "DisabledPluginDirectoryArgIsOutsideModulesDisabled",
                fullPath));
        }
    }

    private static void MovePluginDirectories(IReadOnlyList<PluginDirectoryMove> moves)
    {
        foreach (var move in moves)
        {
            if (!Directory.Exists(move.SourcePath))
            {
                throw new DirectoryNotFoundException(AppText.Get(
                    "PluginDirectoryDoesNotExistArg",
                    move.SourcePath));
            }

            if (Directory.Exists(move.DestinationPath))
            {
                throw new IOException(AppText.Get(
                    "TheTargetPluginDirectoryAlreadyExistsArg",
                    move.DestinationPath));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(move.DestinationPath)!);
            Directory.Move(move.SourcePath, move.DestinationPath);
        }
    }

    private static void RestorePluginDirectories(IReadOnlyList<PluginDirectoryMove> moves)
    {
        foreach (var move in moves.Reverse())
        {
            if (!Directory.Exists(move.DestinationPath) || Directory.Exists(move.SourcePath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(move.SourcePath)!);
            Directory.Move(move.DestinationPath, move.SourcePath);
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
                failure.PluginId?.ToString() ?? "<unknown>",
                failure.Reason);
        }

        _logger.LogInformation(
            "Plugin scan completed: {CandidateCount} candidate(s), {PlannedPluginCount} loadable, "
            + "{FailureCount} failure(s); load order: {PluginLoadOrder}.",
            candidates.Count,
            plan.OrderedCandidates.Count,
            failures.Count,
            plan.OrderedCandidates.Select(static candidate => (string)candidate.Manifest.Id).ToArray());
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
        return PluginId.Of(pluginId);
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

    private sealed record PluginDirectoryMove(string SourcePath, string DestinationPath);
}
