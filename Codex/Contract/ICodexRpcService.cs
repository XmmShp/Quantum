using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

[Description("Read and control the official Quantum Codex integration. Connecting and sending may start a local Codex process or model task; obtain user authorization for the corresponding operation before calling.")]
[TransportOverQuantum]
[RpcInvocationName("codex")]
public interface ICodexRpcService : IRpcService
{
    [Description("Attempt automatic connection once per plugin runtime. Called after the first interactive page render. Shares suppression state with manual disconnect and does not start duplicate processes. Failure details are retained in the returned snapshot.")]
    [RpcInvocationName("ensure-auto-connected")]
    Result<CodexIntegrationSnapshot> EnsureAutoConnected(CodexAutoConnectRequest request);

    [Description("Read connection state, thread ID, activity, models, and current messages without starting a Codex process.")]
    [RpcInvocationName("snapshot")]
    Result<CodexIntegrationSnapshot> GetSnapshot(CodexSnapshotRequest request);

    [Description("Read the cached model list: initial choices before connection and App Server choices after connection. Does not start a model request.")]
    [RpcInvocationName("models")]
    Result<CodexModelOption[]> ListModels(CodexModelsRequest request);

    [Description("Connect to the local Codex App Server, create a thread, and expose tools from other Quantum plugins. Requires a local Codex installation and sign-in.")]
    [RpcInvocationName("connect")]
    Result<CodexIntegrationSnapshot> Connect(CodexConnectRequest request);

    [Description("Start a new thread on the connected App Server with an optional model. Does not send a prompt.")]
    [RpcInvocationName("new-thread")]
    Result<CodexIntegrationSnapshot> StartNewThread(CodexNewThreadRequest request);

    [Description("Change the model for subsequent turns while retaining the thread ID and messages. Does not start inference. Rejected while a turn is running.")]
    [RpcInvocationName("change-model")]
    Result<CodexIntegrationSnapshot> ChangeModel(CodexChangeModelRequest request);

    [Description("Send a prompt to the current thread and start a model turn. Poll snapshot for progress and results. Rejected while the current turn is still running.")]
    [RpcInvocationName("send")]
    Result<CodexIntegrationSnapshot> Send(CodexSendRequest request);

    [Description("Disconnect and stop the App Server process owned by this plugin. May interrupt a running model task. A disconnected state is treated as success.")]
    [RpcInvocationName("disconnect")]
    Result<CodexIntegrationSnapshot> Disconnect(CodexDisconnectRequest request);
}
