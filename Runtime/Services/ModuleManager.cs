using Quantum.Sdk;
using Quantum.Sdk.Services;
using Quantum.Sdk.Utilities;
using System.Reflection;

namespace Quantum.Runtime.Services;

internal class ModuleManager(ILogger<ModuleManager> logger) : IModuleManager
{
    private readonly List<IModule> _loadedModules = [];
    private readonly List<Assembly> _loadedAssemblies = [];
    public required IServiceCollection HostServices { get; init; }
    public required IServiceProvider Activator { get; init; }
    public IReadOnlyList<Assembly> LoadedAssemblies => _loadedAssemblies.AsReadOnly();

    public async Task LoadModulesAsync()
    {
        // 扫描模块目录
        var modulesPath = Path.Combine(AppContext.BaseDirectory, "Modules");
        if (!Directory.Exists(modulesPath))
        {
            logger.LogWarning("Modules directory not found at {Path}", modulesPath);
            return;
        }

        // 加载模块程序集
        foreach (var moduleDir in Directory.GetDirectories(modulesPath))
        {
            var moduleName = Path.GetFileName(moduleDir);
            var dllPath = Path.Combine(moduleDir, $"{moduleName}.dll");

            if (!File.Exists(dllPath))
            {
                logger.LogWarning("Module DLL not found at {Path}", dllPath);
                continue;
            }

            var assembly = Assembly.LoadFrom(dllPath);
            _loadedAssemblies.Add(assembly);
        }

        _loadedAssemblies.ForEach(RegisterModule);

        // 调用所有模块的 OnAllLoaded 方法
        var initTasks = _loadedModules.Select(module =>
            module.OnAllLoadedAsync()).ToList();

        await Task.WhenAll(initTasks);

        logger.LogInformation("All modules loaded successfully. Total modules: {Count}", _loadedModules.Count);
    }

    public void LoadModule(Assembly assembly)
    {
        _loadedAssemblies.Add(assembly);
        RegisterModule(assembly);
    }

    private void RegisterModule(Assembly assembly)
    {
        var moduleTypes = assembly.GetTypes()
            .Where(t => typeof(IModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ToList();

        switch (moduleTypes.Count)
        {
            case 0:
                logger.LogWarning("No module implementation found in assembly {Assembly}", assembly.FullName);
                return;
            case > 1:
                throw new InvalidOperationException(
                    $"Multiple module implementations found in assembly {assembly.FullName}. Only one implementation of IModule is allowed per assembly.");
        }

        var moduleType = moduleTypes[0];
        // 创建模块实例并添加到已加载模块列表
        var module = (IModule)ActivatorUtilities.CreateInstance(Activator, moduleType);
        _loadedModules.Add(module);

        // 注册为 IModule
        HostServices.AddSingleton(module);

        logger.LogInformation("Registered module {ModuleId} of type {ModuleType}", module.ModuleId, moduleType.FullName);
    }

    public Result<IModule> GetModule(string moduleId, Version? minVersion = null, Version? maxVersion = null)
    {
        var module = _loadedModules.FirstOrDefault(m => m.ModuleId == moduleId);
        if (module is null)
        { return Result.Failure($"{moduleId} is not loaded"); }
        if (minVersion is not null && module.Version < minVersion)
        { return Result.Failure($"{moduleId} version is less than required version {minVersion}"); }
        if (maxVersion is not null && module.Version > maxVersion)
        { return Result.Failure($"{moduleId} version is greater than required version {maxVersion}"); }
        return Result.Success(module);
    }
}
