namespace Quantum.Sdk;

/// <summary>
/// 表示一个UI模块接口
/// </summary>
public interface IUiModule : IModule
{
    /// <summary>
    /// 获取模块标题
    /// </summary>
    string ModuleTitle { get; }

    /// <summary>
    /// 获取模块图标
    /// </summary>
    string? ModuleIcon { get; }

    /// <summary>
    /// 获取默认页面
    /// </summary>
    string DefaultRoute { get; }

    /// <summary>
    /// 获取导航项列表
    /// </summary>
    /// <returns>有序的导航项列表</returns>
    IEnumerable<NavigationItem> GetNavigationItems();
}

/// <summary>
/// 表示一个导航项
/// </summary>
public class NavigationItem
{
    /// <summary>
    /// 菜单项的唯一标识
    /// </summary>
    public string Key { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 菜单项的显示标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 菜单项的图标
    /// </summary>
    public required string Icon { get; init; }

    /// <summary>
    /// 菜单项的路由
    /// </summary>
    public required string Route { get; init; }

    /// <summary>
    /// 子菜单项
    /// </summary>
    public IEnumerable<NavigationItem> Children { get; init; } = [];
}
