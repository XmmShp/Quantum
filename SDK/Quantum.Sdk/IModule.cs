using Quantum.Sdk.Utilities;

namespace Quantum.Sdk;

/// <summary>
/// 表示一个模块接口
/// </summary>
public interface IModule
{
    /// <summary>
    /// 模块的唯一标识符
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// 模块的版本号
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// 模块的作者
    /// </summary>
    string Author { get; }

    /// <summary>
    /// 模块的介绍
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 当所有模块加载完成时执行的异步逻辑
    /// </summary>
    /// <returns>表示异步操作结果的任务，包含一个Result，表示模块是否已就绪及对应的消息</returns>
    Task<Result> OnAllLoadedAsync();
}
