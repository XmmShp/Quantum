using Quantum.Platform.Authentication;
using Xunit;

namespace Quantum.Platform.Tests;

public sealed class OidcConfigurationTests
{
    [Fact]
    public void ProductionRejectsTheDevelopmentIssuerDefault()
        => Assert.Throws<InvalidOperationException>(() => new QuantumPlatformOidcOptions().Validate(false));

    [Theory]
    [InlineData("http://quantum.example/oauth2")]
    [InlineData("https://localhost/oauth2")]
    [InlineData("https://user:password@quantum.example/oauth2")]
    [InlineData("https://quantum.example/oauth2?query=1")]
    public void ProductionRejectsInvalidPublicIssuers(string issuer)
        => Assert.Throws<InvalidOperationException>(() => new QuantumPlatformOidcOptions { Issuer = issuer }.Validate(false));

    [Fact]
    public void ProductionAcceptsAnExplicitPublicHttpsIssuer()
        => new QuantumPlatformOidcOptions { Issuer = "https://quantum.example/oauth2" }.Validate(false);

    [Fact]
    public void DevelopmentAllowsLoopbackHttp()
        => new QuantumPlatformOidcOptions().Validate(true);
}
