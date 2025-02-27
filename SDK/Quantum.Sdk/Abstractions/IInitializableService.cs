namespace Quantum.Infrastructure.Abstractions;

/// <summary>
/// 可初始化服务接口
/// </summary>
public interface IInitializableService
{
    /// <summary>
    /// 获取服务是否已初始化
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 获取初始化任务
    /// </summary>
    Task InitializeTask { get; }
}
