using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

[Description("读取和控制 Quantum 官方 Codex 集成。连接和发送会启动本机 Codex 进程或模型任务；调用前应取得用户对相应操作的授权。")]
[TransportOverQuantum]
[RpcInvocationName("codex")]
public interface ICodexRpcService : IRpcService
{
    [Description("确保每个插件运行时只尝试一次自动连接。页面首次交互渲染后调用；与手动断开共享抑制状态，重复调用不会重复启动进程。失败原因保存在返回快照中。")]
    [RpcInvocationName("ensure-auto-connected")]
    Result<CodexIntegrationSnapshot> EnsureAutoConnected(CodexAutoConnectRequest request);

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

    [Description("切换当前会话后续轮次的模型；保留会话 ID 和消息，不启动推理。当前轮次运行时拒绝操作。")]
    [RpcInvocationName("change-model")]
    Result<CodexIntegrationSnapshot> ChangeModel(CodexChangeModelRequest request);

    [Description("向当前会话发送提示词并启动一轮模型任务；返回后可轮询 snapshot 查看进度与结果。当前轮次未完成时拒绝发送。")]
    [RpcInvocationName("send")]
    Result<CodexIntegrationSnapshot> Send(CodexSendRequest request);

    [Description("断开当前连接并停止本插件拥有的 App Server 进程；可能中断正在执行的模型任务。未连接时视为成功。")]
    [RpcInvocationName("disconnect")]
    Result<CodexIntegrationSnapshot> Disconnect(CodexDisconnectRequest request);
}
