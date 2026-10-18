using System.ComponentModel;
using NOF.Application;
using NOF.Contract;
using Quantum.Plugin.Abstraction;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexSnapshotRequest;
public sealed record CodexModelsRequest;
public sealed record CodexDisconnectRequest;
public sealed record CodexConnectRequest(
    [property: Description("本机已存在的工作目录；空字符串使用 Quantum 当前工作目录。连接会启动本机 Codex App Server 并开放其他插件的 Quantum 工具。")] string WorkingDirectory = "",
    [property: Description("要使用的模型 ID，必须由本机 Codex 支持。默认 gpt-5.6-luna。")] string Model = "gpt-5.6-luna");
public sealed record CodexNewThreadRequest(
    [property: Description("可选模型 ID；省略时保留当前模型。此操作会清空当前显示的会话消息。")] string? Model = null);
public sealed record CodexSendRequest(
    [property: Description("发送给当前本机 Codex 会话的非空提示词。会启动模型推理，并可能产生账号用量及 Quantum 工具副作用。")] string Prompt);

[Description("读取和控制 Quantum 官方 Codex 集成。连接和发送会启动本机 Codex 进程或模型任务；调用前应取得用户对相应操作的授权。")]
[TransportOverQuantum]
[RpcInvocationName("codex")]
public interface ICodexRpcService : IRpcService
{
    [Description("读取连接状态、会话 ID、运行状态、模型和当前消息；不启动 Codex 进程。")]
    [RpcInvocationName("snapshot")]
    Result<CodexIntegrationSnapshot> GetSnapshot(CodexSnapshotRequest request);

    [Description("读取当前缓存的模型列表；未连接时为初始选项，连接后为 App Server 返回的选项。不发起模型请求。")]
    [RpcInvocationName("models")]
    Result<CodexModelOption[]> ListModels(CodexModelsRequest request);

    [Description("连接本机 Codex App Server，创建会话并开放其他 Quantum 插件的工具；需要本机已安装并登录 Codex。")]
    [RpcInvocationName("connect")]
    Result<CodexIntegrationSnapshot> Connect(CodexConnectRequest request);

    [Description("在已连接的 App Server 中开启新会话，可选择模型；不会自动发送提示词。")]
    [RpcInvocationName("new-thread")]
    Result<CodexIntegrationSnapshot> StartNewThread(CodexNewThreadRequest request);

    [Description("向当前会话发送提示词并启动一轮模型任务；返回后可轮询 snapshot 查看进度与结果。当前轮次未完成时拒绝发送。")]
    [RpcInvocationName("send")]
    Result<CodexIntegrationSnapshot> Send(CodexSendRequest request);

    [Description("断开当前连接并停止本插件拥有的 App Server 进程；可能中断正在执行的模型任务。未连接时视为成功。")]
    [RpcInvocationName("disconnect")]
    Result<CodexIntegrationSnapshot> Disconnect(CodexDisconnectRequest request);
}

public partial class CodexRpcService : RpcServer<ICodexRpcService>;
