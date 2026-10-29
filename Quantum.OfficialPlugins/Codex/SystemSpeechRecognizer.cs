using NOF.Domain;
using System.Globalization;
using System.Runtime.Versioning;
using System.Speech.Recognition;

namespace Quantum.OfficialPlugins.Codex;

internal sealed class SystemSpeechRecognizer : ICodexSpeechRecognizer, IDisposable
{
    public void Dispose() => _gate.Dispose();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> TranscribeAsync(string path, string? language, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DomainException("codex_speech_unavailable", "Install a speech.transcribe provider for this desktop platform.");
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await RecognizeWindowsAsync(path, language, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<string> RecognizeWindowsAsync(string path, string? language, CancellationToken cancellationToken)
    {
        var requested = string.IsNullOrWhiteSpace(language) ? CultureInfo.InstalledUICulture.Name : language;
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        var info = installed.FirstOrDefault(r => r.Culture.Name.Equals(requested, StringComparison.OrdinalIgnoreCase))
            ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName.Equals(requested.Split('-')[0], StringComparison.OrdinalIgnoreCase));
        if (info is null)
        {
            throw new DomainException("codex_speech_language_unavailable", "The requested speech language is not installed on this desktop.");
        }
        using var engine = new SpeechRecognitionEngine(info);
        var parts = new List<string>();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SpeechRecognized += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Result.Text) && args.Result.Confidence >= 0.2f)
            {
                lock (parts)
                {
                    parts.Add(args.Result.Text);
                }
            }
        };
        engine.RecognizeCompleted += (_, args) =>
        {
            if (args.Cancelled)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else if (args.Error is not null)
            {
                completion.TrySetException(new DomainException("codex_speech_failed", "Speech recognition failed."));
            }
            else
            {
                lock (parts)
                {
                    completion.TrySetResult(string.Join(" ", parts));
                }
            }
        };
        try
        {
            engine.LoadGrammar(new DictationGrammar());
            engine.SetInputToWaveFile(path);
            using var stop = cancellationToken.Register(() => { try { engine.RecognizeAsyncCancel(); } catch (InvalidOperationException) { } });
            engine.RecognizeAsync(RecognizeMode.Multiple);
            var transcript = await completion.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
            if (string.IsNullOrWhiteSpace(transcript))
            {
                throw new DomainException("codex_speech_empty", "No speech was recognized. Try recording again.");
            }
            return transcript;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new DomainException("codex_speech_format", "Use the voice recorder or select a PCM WAV recording.");
        }
    }
}
