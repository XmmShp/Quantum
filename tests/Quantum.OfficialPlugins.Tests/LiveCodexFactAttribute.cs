using Xunit;

namespace Quantum.Tests;

public sealed class LiveCodexFactAttribute : FactAttribute
{
    public LiveCodexFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QUANTUM_CODEX_AUDIO_FIXTURE")))
        {
            Skip = "Set QUANTUM_CODEX_AUDIO_FIXTURE to a synthetic speech file to exercise the installed desktop speech recognizer.";
        }
    }
}
