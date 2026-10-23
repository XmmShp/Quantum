using System.ComponentModel.DataAnnotations;

namespace Quantum.Platform;

public sealed class AgentFnAutomatedReviewOptions
{
    public const string SectionName = "QuantumPlatform:AutomatedReview";

    public bool Enabled { get; set; }

    public bool DevelopmentAutoApprove { get; set; }

    public bool IsWorkerEnabled => Enabled || DevelopmentAutoApprove;

    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    public string ClientId { get; set; } = "quantum-platform-review";

    public string PrivateJwks { get; set; } = string.Empty;

    public string PrivateJwksPath { get; set; } = string.Empty;

    public string Skill { get; set; } = "quantum-plugin-release-review@1.0.1";

    public string Model { get; set; } = "deepseek/deepseek-v4-pro";

    public int PollIntervalSeconds { get; set; } = 10;

    public int TaskTimeoutMinutes { get; set; } = 20;

    public int MaxAttempts { get; set; } = 3;

    public long MaxReviewTextBytes { get; set; } = 8 * 1024 * 1024;

    public long MaxReviewFileBytes { get; set; } = 512 * 1024;

    public bool IsValid()
        => !Enabled ||
            Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(ClientId) &&
            (!string.IsNullOrWhiteSpace(PrivateJwks) || !string.IsNullOrWhiteSpace(PrivateJwksPath)) &&
            !string.IsNullOrWhiteSpace(Skill) &&
            !string.IsNullOrWhiteSpace(Model) &&
            PollIntervalSeconds is >= 2 and <= 300 &&
            TaskTimeoutMinutes is >= 1 and <= 120 &&
            MaxAttempts is >= 1 and <= 10 &&
            MaxReviewTextBytes is > 0 and <= 64 * 1024 * 1024 &&
            MaxReviewFileBytes is > 0 && MaxReviewFileBytes <= MaxReviewTextBytes;
}
