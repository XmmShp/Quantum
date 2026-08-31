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
        var feature = Candidate(
            "feature",
            "1.0.0",
            dependencies: [new PluginDependency(core.Manifest.Id, SemanticVersion.Parse("1.0.0"))]);

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
            dependencies: [new PluginDependency(new PluginId("missing"), SemanticVersion.Parse("1.0.0"))]);
        var child = Candidate(
            "child",
            "1.0.0",
            dependencies: [new PluginDependency(feature.Manifest.Id, SemanticVersion.Parse("1.0.0"))]);

        var plan = _planner.CreatePlan([child, feature]);

        Assert.Empty(plan.OrderedCandidates);
        Assert.Equal(2, plan.Failures.Count);
    }

    [Fact]
    public void CreatePlan_RejectsIncompatibleRequiredDependency()
    {
        var core = Candidate("core", "1.0.0");
        var feature = Candidate(
            "feature",
            "1.0.0",
            dependencies: [new PluginDependency(core.Manifest.Id, SemanticVersion.Parse("2.0.0"))]);

        var plan = _planner.CreatePlan([core, feature]);

        Assert.Equal(core.Manifest.Id, Assert.Single(plan.OrderedCandidates).Manifest.Id);
        Assert.Contains(plan.Failures, failure => failure.PluginId == feature.Manifest.Id);
    }

    [Fact]
    public void CreatePlan_RejectsDependencyCycles()
    {
        var firstId = new PluginId("first");
        var secondId = new PluginId("second");
        var first = Candidate(
            "first",
            "1.0.0",
            dependencies: [new PluginDependency(secondId, SemanticVersion.Parse("1.0.0"))]);
        var second = Candidate(
            "second",
            "1.0.0",
            dependencies: [new PluginDependency(firstId, SemanticVersion.Parse("1.0.0"))]);

        var plan = _planner.CreatePlan([first, second]);

        Assert.Empty(plan.OrderedCandidates);
        Assert.All(plan.Failures, static failure => Assert.Contains("cycle", failure.Reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreatePlan_AllowsMissingIntegration()
    {
        var plugin = Candidate(
            "standalone",
            "1.0.0",
            integrations: [new PluginIntegration(new PluginId("optional-addon"), SemanticVersion.Parse("1.0.0"))]);

        var plan = _planner.CreatePlan([plugin]);

        Assert.Equal(plugin.Manifest.Id, Assert.Single(plan.OrderedCandidates).Manifest.Id);
        Assert.Empty(plan.Failures);
    }

    [Fact]
    public void CreatePlan_PrefersCompatibleIntegrationBeforeOwner()
    {
        var target = Candidate("z-target", "1.2.0");
        var owner = Candidate(
            "a-owner",
            "1.0.0",
            integrations: [new PluginIntegration(target.Manifest.Id, SemanticVersion.Parse("1.1.0"))]);

        var plan = _planner.CreatePlan([owner, target]);

        Assert.Equal([target.Manifest.Id, owner.Manifest.Id], plan.OrderedCandidates.Select(static item => item.Manifest.Id));
        Assert.Empty(plan.Failures);
    }

    [Fact]
    public void CreatePlan_IgnoresIncompatibleIntegration()
    {
        var target = Candidate("z-target", "1.0.0");
        var owner = Candidate(
            "a-owner",
            "1.0.0",
            integrations: [new PluginIntegration(target.Manifest.Id, SemanticVersion.Parse("2.0.0"))]);

        var plan = _planner.CreatePlan([target, owner]);

        Assert.Equal([owner.Manifest.Id, target.Manifest.Id], plan.OrderedCandidates.Select(static item => item.Manifest.Id));
        Assert.Empty(plan.Failures);
    }

    [Fact]
    public void CreatePlan_BreaksIntegrationCyclesWithoutRejectingPlugins()
    {
        var firstId = new PluginId("first");
        var secondId = new PluginId("second");
        var first = Candidate(
            "first",
            "1.0.0",
            integrations: [new PluginIntegration(secondId, SemanticVersion.Parse("1.0.0"))]);
        var second = Candidate(
            "second",
            "1.0.0",
            integrations: [new PluginIntegration(firstId, SemanticVersion.Parse("1.0.0"))]);

        var plan = _planner.CreatePlan([second, first]);

        Assert.Equal([firstId, secondId], plan.OrderedCandidates.Select(static item => item.Manifest.Id));
        Assert.Empty(plan.Failures);
    }

    [Fact]
    public void Manifest_RejectsDuplicateStrongAndWeakRelationship()
    {
        var targetId = new PluginId("target");

        Assert.Throws<ArgumentException>(() => Candidate(
            "owner",
            "1.0.0",
            dependencies: [new PluginDependency(targetId, SemanticVersion.Parse("1.0.0"))],
            integrations: [new PluginIntegration(targetId, SemanticVersion.Parse("1.0.0"))]));
    }

    private static PluginCandidate Candidate(
        string id,
        string version,
        IReadOnlyList<PluginDependency>? dependencies = null,
        IReadOnlyList<PluginIntegration>? integrations = null)
        => new(
            new PluginManifest(
                new PluginId(id),
                SemanticVersion.Parse(version),
                $"{id}.dll",
                dependencies,
                integrations),
            Path.Combine("plugins", id));
}
