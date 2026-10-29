using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class TranscribeCodexSpeech(ICodexSpeechRecognizer recognizer) : CodexSpeechRpcService.Transcribe
{
    public override async Task<Result<string>> HandleAsync(CodexTranscribeRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Audio is null || request.Audio.ContentType is not ("audio/wav" or "audio/wave" or "audio/x-wav"))
        {
            throw new DomainException("codex_speech_format", "Use the voice recorder or select a PCM WAV recording.");
        }
        var files = await CodexAttachmentStore.StageAsync([request.Audio], cancellationToken);
        return await recognizer.TranscribeAsync(files[0].Path, request.Language, cancellationToken);
    }
}
