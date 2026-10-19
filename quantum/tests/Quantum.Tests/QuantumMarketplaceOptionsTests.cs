using Quantum.Marketplace;

namespace Quantum.Tests;

[Collection("Marketplace environment")]
public sealed class QuantumMarketplaceOptionsTests
{
    [Fact]
    public void DefaultVersionMatchesRunningHostAndOmitsBuildMetadata()
    {
        var previous = Environment.GetEnvironmentVariable("QUANTUM_VERSION");
        try
        {
            Environment.SetEnvironmentVariable("QUANTUM_VERSION", null);

            var options = QuantumMarketplaceOptions.FromEnvironment();
            var version = typeof(QuantumMarketplaceOptions).Assembly.GetName().Version!;

            Assert.Equal(version.ToString(3), options.QuantumVersion);
            Assert.DoesNotContain('+', options.QuantumVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUANTUM_VERSION", previous);
        }
    }

    [Fact]
    public void ExplicitDevelopmentVersionOverrideStillWorks()
    {
        var previous = Environment.GetEnvironmentVariable("QUANTUM_VERSION");
        try
        {
            Environment.SetEnvironmentVariable("QUANTUM_VERSION", " 9.8.7-beta.1 ");

            var options = QuantumMarketplaceOptions.FromEnvironment();

            Assert.Equal("9.8.7-beta.1", options.QuantumVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUANTUM_VERSION", previous);
        }
    }
}
