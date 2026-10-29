namespace Quantum.OfficialPlugins.Codex;

public interface ICodexSpeechRecognizer
{
    Task<string> TranscribeAsync(string path, string? language, CancellationToken cancellationToken);
}
