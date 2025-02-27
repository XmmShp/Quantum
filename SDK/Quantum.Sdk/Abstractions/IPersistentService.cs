namespace Quantum.Infrastructure.Abstractions;

/// <summary>
/// 持久化服务接口
/// </summary>
public interface IPersistentService
{
    /// <summary>
    /// 异步加载状态
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    Task LoadStateAsync();

    /// <summary>
    /// 异步保存状态
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    Task SaveStateAsync();
}
