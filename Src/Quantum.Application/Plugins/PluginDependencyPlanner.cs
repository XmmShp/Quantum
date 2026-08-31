using Quantum.Domain.Plugins;

namespace Quantum.Application.Plugins;

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

        var dependencyCount = active.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Manifest.Dependencies.Count);
        var dependents = active.Keys.ToDictionary(static id => id, static _ => new List<PluginId>());
        foreach (var candidate in active.Values)
        {
            foreach (var dependency in candidate.Manifest.Dependencies)
            {
                dependents[dependency.Id].Add(candidate.Manifest.Id);
            }
        }

        var ready = new PriorityQueue<PluginId, string>(StringComparer.Ordinal);
        foreach (var pair in dependencyCount.Where(static pair => pair.Value == 0))
        {
            ready.Enqueue(pair.Key, pair.Key.Value);
        }

        var ordered = new List<PluginCandidate>(active.Count);
        while (ready.TryDequeue(out var pluginId, out _))
        {
            ordered.Add(active[pluginId]);
            foreach (var dependent in dependents[pluginId])
            {
                dependencyCount[dependent]--;
                if (dependencyCount[dependent] == 0)
                {
                    ready.Enqueue(dependent, dependent.Value);
                }
            }
        }

        foreach (var pluginId in active.Keys.Except(ordered.Select(static candidate => candidate.Manifest.Id)))
        {
            failures.Add(new PluginLoadFailure(pluginId, "Plugin dependencies contain a cycle."));
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
}

public sealed record PluginLoadPlan(
    IReadOnlyList<PluginCandidate> OrderedCandidates,
    IReadOnlyList<PluginLoadFailure> Failures);

public sealed record PluginLoadFailure(PluginId? PluginId, string Reason, Exception? Exception = null);
