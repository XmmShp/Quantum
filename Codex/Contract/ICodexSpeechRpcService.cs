using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

[Description("Transcribe user-selected recorded speech independently of conversation models.")]
[TransportOverQuantum]
[RpcInvocationName("speech")]
public interface ICodexSpeechRpcService : IRpcService
{
    [Description("Transcribe a user-selected PCM WAV recording using installed speech languages. Does not access a microphone, start a conversation or execute Quantum tools.")]
    [RpcInvocationName("transcribe")]
    Result<string> Transcribe(CodexTranscribeRequest request);
}
