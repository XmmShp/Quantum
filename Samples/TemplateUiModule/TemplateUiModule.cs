using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quantum.Sdk;
using Quantum.Sdk.Utilities;

namespace TemplateUiModule;

public class TemplateUiModule(ILogger<TemplateUiModule> logger, IQuantum quantum) : IUiModule // 实现了IUiModule接口的类是UI模块加载的入口，一个程序集中只能有一个
{
    public string ModuleId => "TemplateUiModule"; // ModuleId是模块的唯一标识符，用于区分不同的模块
    public Version Version { get; } = new(1, 0);
    public string Author => "QuantumTeam"; // Author是模块的作者信息
    public string Description => "示例UI模块"; // Description是模块的描述信息
    public Task<Result> OnAllLoadedAsync()// OnAllLoadedAsync方法是所有模块加载完成后的回调方法
                                          // 通常用于检查依赖项是否已加载，注册服务等

    {
        quantum.InjectedCodeManager.AddPreBlazor("<script>console.log('hello, world');</script>");
        logger.LogInformation("TemplateUiModule is Loaded");
        quantum.HostServices.AddScoped<ExampleJsInterop>();
        return Task.FromResult<Result>(quantum.ModuleManager.GetModule("TemplateModule", new Version(1, 0)));
    }

    public string ModuleTitle => "示例模块"; // ModuleTitle是模块的标题，用于显示在上方导航栏上
    public string ModuleIcon => "book"; // ModuleIcon是模块的图标，用于显示在上方导航栏上，具体图标样式请参考 https://antblazor.com/zh-CN/components/icon
    public string DefaultRoute => "/example/1"; // DefaultComponent是模块的默认页面组件

    public IEnumerable<NavigationItem> GetNavigationItems() // GetNavigationItems方法用于获取模块的导航项列表，用于显示在左侧菜单栏上
        =>
        [
            new NavigationItem
            {
                Title = "示例页面1",
                Icon = "book",
                Route = "/example/1",
            },
            new NavigationItem
            {
                Title = "示例页面2",
                Icon = "book",
                Route = "/example/2",
            }
        ];
}
