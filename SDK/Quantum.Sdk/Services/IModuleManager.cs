using Quantum.Sdk.Utilities;

namespace Quantum.Sdk.Services;

/// <summary>
/// 模块管理器接口
/// </summary>
public interface IModuleManager
{
    /// <summary>
    /// 获取指定ID和版本范围的模块
    /// </summary>
    /// <param name="moduleId">模块ID</param>
    /// <param name="minVersion">最小版本要求，可选</param>
    /// <param name="maxVersion">最大版本要求，可选</param>
    /// <returns>包含模块实例的结果对象</returns>
    public Result<IModule> GetModule(string moduleId, Version? minVersion = null, Version? maxVersion = null);
}
