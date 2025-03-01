using Microsoft.Extensions.Logging;
using Quantum.Sdk;
using Quantum.Sdk.Extensions;
using Quantum.Sdk.Utilities;

namespace TemplateModule;

public class TemplateModule(ILogger<TemplateModule> logger, IQuantum quantum) : IModule // 实现了IModule接口的类是模块加载的入口，一个程序集中只能有一个
{
    public string ModuleId => "TemplateModule"; // ModuleId是模块的唯一标识符，用于区分不同的模块
    public Version Version { get; } = new(1, 0);
    public string Author => "QuantumTeam"; // Author是模块的作者信息
    public string Description => "示例模块";
    public Task<Result> OnAllLoadedAsync()
    {
        logger.LogInformation("TemplateModule is loaded.");
        quantum.HostServices.AddEagerInitializeService<ISampleService, SampleService>();
        return Task.FromResult(Result.Success());
    }
}
