using Quantum.Application.Plugins;
using Quantum.Domain.Plugins;

namespace Quantum.Tests;

public sealed class PluginDependencyPlannerTests
{
    private readonly PluginDependencyPlanner _planner = new();

    [Fact]
    public void CreatePlan_SortsDependenciesBeforeDependents()
    {
        var core = Candidate("core", "1.0.0");
        var feature = Candidate("feature", "1.0.0", new PluginDependency(core.Manifest.Id, SemanticVersion.Parse("1.0.0")));

        var plan = _planner.CreatePlan([feature, core]);

        Assert.Equal([core.Manifest.Id, feature.Manifest.Id], plan.OrderedCandidates.Select(static item => item.Manifest.Id));
        Assert.Empty(plan.Failures);
    }

    [Fact]
    public void CreatePlan_RejectsMissingDependencyAndItsDependents()
    {
        var feature = Candidate(
            "feature",
            "1.0.0",
            new PluginDependency(new PluginId("missing"), SemanticVersion.Parse("1.0.0")));
        var child = Candidate(
            "child",
            "1.0.0",
            new PluginDependency(feature.Manifest.Id, SemanticVersion.Parse("1.0.0")));

        var plan = _planner.CreatePlan([child, feature]);

        Assert.Empty(plan.OrderedCandidates);
        Assert.Equal(2, plan.Failures.Count);
    }

    [Fact]
    public void CreatePlan_RejectsDependencyCycles()
    {
        var firstId = new PluginId("first");
        var secondId = new PluginId("second");
        var first = Candidate("first", "1.0.0", new PluginDependency(secondId, SemanticVersion.Parse("1.0.0")));
        var second = Candidate("second", "1.0.0", new PluginDependency(firstId, SemanticVersion.Parse("1.0.0")));

        var plan = _planner.CreatePlan([first, second]);

        Assert.Empty(plan.OrderedCandidates);
        Assert.All(plan.Failures, static failure => Assert.Contains("cycle", failure.Reason, StringComparison.OrdinalIgnoreCase));
    }

    private static PluginCandidate Candidate(string id, string version, params PluginDependency[] dependencies)
        => new(
            new PluginManifest(
                new PluginId(id),
                SemanticVersion.Parse(version),
                $"{id}.dll",
                dependencies),
            Path.Combine("plugins", id));
}
