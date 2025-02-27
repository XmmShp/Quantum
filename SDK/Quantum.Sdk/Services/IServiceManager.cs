using Quantum.Infrastructure.Abstractions;

namespace Quantum.Sdk.Services;

/// <summary>
/// 服务管理器接口
/// </summary>
public interface IServiceManager
{
    /// <summary>
    /// 添加一个急切初始化的服务
    /// </summary>
    /// <param name="service">要添加的可初始化服务实例</param>
    /// <returns>服务管理器实例，用于链式调用</returns>
    IServiceManager AddEagerInitializeService(IInitializableService service);
}
