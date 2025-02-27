using ElectronNET.API;
using Quantum.Infrastructure.Models;
using Quantum.Sdk.Services;

namespace Quantum.Sdk;

/// <summary>
/// Quantum应用程序的主接口
/// </summary>
public interface IQuantum
{
    /// <summary>
    /// 获取主窗口实例
    /// </summary>
    BrowserWindow? Window { get; }

    /// <summary>
    /// 获取服务管理器实例
    /// </summary>
    IServiceManager ServiceManager { get; }

    /// <summary>
    /// 获取模块管理器实例
    /// </summary>
    IModuleManager ModuleManager { get; }

    /// <summary>
    /// 获取注入代码管理器实例
    /// </summary>
    IInjectedCodeManager InjectedCodeManager { get; }
}
