namespace Quantum.Plugins;

public sealed class PluginDependencyPlanner
{
    public PluginLoadPlan CreatePlan(IEnumerable<PluginCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var candidateList = candidates.ToArray();
        var failures = new List<PluginLoadFailure>();
        var duplicateIds = candidateList
            .GroupBy(static candidate => candidate.Manifest.Id)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet();

        foreach (var duplicateId in duplicateIds)
        {
            failures.Add(new PluginLoadFailure(duplicateId, "Multiple plugin directories declare the same id."));
        }

        var active = candidateList
            .Where(candidate => !duplicateIds.Contains(candidate.Manifest.Id))
            .ToDictionary(static candidate => candidate.Manifest.Id);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var candidate in active.Values.ToArray())
            {
                var failure = FindDependencyFailure(candidate, active);
                if (failure is null)
                {
                    continue;
                }

                active.Remove(candidate.Manifest.Id);
                failures.Add(new PluginLoadFailure(candidate.Manifest.Id, failure));
                changed = true;
            }
        }

        var requiredDependencyCount = active.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Manifest.Dependencies.Count);
        var requiredDependents = active.Keys.ToDictionary(static id => id, static _ => new List<PluginId>());
        foreach (var candidate in active.Values)
        {
            foreach (var dependency in candidate.Manifest.Dependencies)
            {
                requiredDependents[dependency.Id].Add(candidate.Manifest.Id);
            }
        }

        var integrationPrerequisites = active.ToDictionary(
            static pair => pair.Key,
            pair => pair.Value.Manifest.Integrations
                .Where(integration => IsCompatibleIntegration(integration, active))
                .Select(static integration => integration.Id)
                .ToHashSet());

        var ordered = new List<PluginCandidate>(active.Count);
        var remaining = active.Keys.ToHashSet();
        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(pluginId => requiredDependencyCount[pluginId] == 0)
                .OrderBy(static pluginId => (string)pluginId, StringComparer.Ordinal)
                .ToArray();
            if (ready.Length == 0)
            {
                break;
            }

            var preferred = ready
                .Where(pluginId => integrationPrerequisites[pluginId].All(
                    integrationId => !remaining.Contains(integrationId)))
                .ToArray();
            var pluginId = preferred.Length > 0 ? preferred[0] : ready[0];

            ordered.Add(active[pluginId]);
            remaining.Remove(pluginId);
            foreach (var dependent in requiredDependents[pluginId])
            {
                requiredDependencyCount[dependent]--;
            }
        }

        foreach (var pluginId in remaining.OrderBy(static pluginId => (string)pluginId, StringComparer.Ordinal))
        {
            failures.Add(new PluginLoadFailure(
                pluginId,
                "Required plugin dependencies contain a cycle or depend on one."));
        }

        return new PluginLoadPlan(ordered, failures);
    }

    private static string? FindDependencyFailure(
        PluginCandidate candidate,
        IReadOnlyDictionary<PluginId, PluginCandidate> active)
    {
        foreach (var dependency in candidate.Manifest.Dependencies)
        {
            if (!active.TryGetValue(dependency.Id, out var dependencyCandidate))
            {
                return $"Required plugin '{dependency.Id}' is missing or invalid.";
            }

            if (dependencyCandidate.Manifest.Version.CompareTo(dependency.MinimumVersion) < 0)
            {
                return $"Plugin '{dependency.Id}' must be at least version {dependency.MinimumVersion}.";
            }
        }

        return null;
    }

    private static bool IsCompatibleIntegration(
        PluginIntegration integration,
        IReadOnlyDictionary<PluginId, PluginCandidate> active)
        => active.TryGetValue(integration.Id, out var candidate)
            && candidate.Manifest.Version.CompareTo(integration.MinimumVersion) >= 0;
}

public sealed record PluginLoadPlan(
    IReadOnlyList<PluginCandidate> OrderedCandidates,
    IReadOnlyList<PluginLoadFailure> Failures);

public sealed record PluginLoadFailure(PluginId? PluginId, string Reason, Exception? Exception = null);
