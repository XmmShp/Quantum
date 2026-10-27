using Quantum.OfficialPlugins.Codex;
using Xunit;

namespace Quantum.Tests;

public sealed class CodexLiveAudioTests
{
    [LiveCodexFact]
    public async Task SystemSpeechTranscribesSyntheticSpeechWithoutUsingTheConversationModel()
    {
        var file = Environment.GetEnvironmentVariable("QUANTUM_CODEX_AUDIO_FIXTURE")!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var recognizer = new SystemSpeechRecognizer();
        var transcript = await recognizer.TranscribeAsync(file, "en-US", timeout.Token);
        Assert.Contains("orange", transcript.ToLowerInvariant());
        Assert.Contains("bicycle", transcript.ToLowerInvariant());
        Assert.True(transcript.Contains("seventeen", StringComparison.OrdinalIgnoreCase) || transcript.Contains("17"));
    }
}
