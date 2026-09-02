using System.Numerics;
using NOF.Domain;

namespace Quantum.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta", "1.0.0-beta.2", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("1.0.0+build.1", "1.0.0+build.2", 0)]
    [InlineData("999999999999999999999999999999.0.0", "2.0.0", 1)]
    public void CompareTo_UsesSemanticVersionPrecedence(string left, string right, int expectedSign)
    {
        var comparison = SemanticVersion.Of(left).CompareTo(SemanticVersion.Of(right));

        Assert.Equal(expectedSign, Math.Sign(comparison));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+build..1")]
    [InlineData(" 1.0.0")]
    [InlineData("1.0.0 ")]
    public void Of_RejectsInvalidVersions(string value)
        => Assert.Throws<DomainValidationException>(() => SemanticVersion.Of(value));

    [Fact]
    public void Comparison_IgnoresBuildMetadataWhileValueEqualityPreservesIt()
    {
        var first = SemanticVersion.Of("1.2.3-rc.1+build.1");
        var second = SemanticVersion.Of("1.2.3-rc.1+build.2");

        Assert.Equal(0, first.CompareTo(second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Parse_ExposesSemanticVersionComponents()
    {
        var version = SemanticVersion.Of("12.34.56-rc.1+linux.arm64");

        Assert.Equal(new BigInteger(12), version.Major);
        Assert.Equal(new BigInteger(34), version.Minor);
        Assert.Equal(new BigInteger(56), version.Patch);
        Assert.True(version.IsPreRelease);
        Assert.Equal(["rc", "1"], version.PreReleaseIdentifiers);
        Assert.Equal("rc.1", version.PreRelease);
        Assert.Equal(["linux", "arm64"], version.BuildMetadataIdentifiers);
        Assert.Equal("linux.arm64", version.BuildMetadata);
        Assert.Equal("12.34.56-rc.1+linux.arm64", version.ToString());
    }
}
