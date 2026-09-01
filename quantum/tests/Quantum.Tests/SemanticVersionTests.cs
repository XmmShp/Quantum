using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1", "1.0.0", 0)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("1.0.0+build.1", "1.0.0+build.2", 0)]
    public void CompareTo_UsesSemanticVersionPrecedence(string left, string right, int expectedSign)
    {
        var comparison = SemanticVersion.Parse(left).CompareTo(SemanticVersion.Parse(right));

        Assert.Equal(expectedSign, Math.Sign(comparison));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.0.0-alpha..1")]
    public void TryParse_RejectsInvalidVersions(string value)
    {
        Assert.False(SemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void Equality_UsesSemanticPrecedenceAndIgnoresBuildMetadata()
    {
        var first = SemanticVersion.Parse("1.2.3-rc.1+build.1");
        var second = SemanticVersion.Parse("1.2.3-rc.1+build.2");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
