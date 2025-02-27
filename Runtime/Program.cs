using ElectronNET.API;
using ElectronNET.API.Entities;
using Quantum.Runtime.Services;
using Quantum.Sdk;


#if RELEASE
using Serilog;
using Serilog.Events;
#endif

// 处理待安装的模块
HandlePendingModules();

// 处理待卸载的模块
HandleModulesToUninstall();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseElectron(args);

#if RELEASE
// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.File(
        Path.Combine("logs", $"log_{DateTime.Now:yyyy-MM-dd.HH.mm.ss}.txt"),
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] [{FilePath}:{LineNumber}] {Message:lj}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10 * 1024 * 1024)
    .CreateLogger();

builder.Host.UseSerilog();
#endif

var preloadServices = new ServiceCollection();
preloadServices.AddLogging()
    .AddSingleton<InjectedCodeManager>()
    .AddSingleton(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<ModuleManager>>();
        var moduleManager = new ModuleManager(logger) { Activator = sp, HostServices = builder.Services };
        return moduleManager;
    })
    .AddSingleton(sp =>
    {
        var serviceManager = new ServiceManager(builder.Services);
        return serviceManager;
    })
    .AddSingleton<Quantum.Runtime.Services.Quantum>();

#pragma warning disable ASP0000 // Do not call 'IServiceCollection.BuildServiceProvider' in 'ConfigureServices'
var preloadProvider = preloadServices.BuildServiceProvider();
#pragma warning restore ASP0000 // Do not call 'IServiceCollection.BuildServiceProvider' in 'ConfigureServices'

var quantum = preloadProvider.GetRequiredService<Quantum.Runtime.Services.Quantum>();
var injectedCodeManager = preloadProvider.GetRequiredService<InjectedCodeManager>();
var moduleManager = preloadProvider.GetRequiredService<ModuleManager>();
await moduleManager.LoadModulesAsync();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddAntDesign()
    .AddSingleton<IQuantum>(quantum)
    .AddSingleton(injectedCodeManager);

#region MODULE_DEBUG
// 在这里手动加载模块，方便调试
// loader.LoadModule(typeof(IModule).Assembly);
#endregion

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.MapStaticAssets();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // 禁用所有静态文件的缓存
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "-1");
    }
})
    .UseCors("AllowAll")
    .UseAntiforgery();

app.MapRazorComponents<Quantum.Runtime.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies([.. moduleManager.LoadedAssemblies]);

if (HybridSupport.IsElectronActive)
{
    await app.StartAsync();

    var window = await Electron.WindowManager.CreateWindowAsync(new BrowserWindowOptions
    {
        Frame = false,
        Show = false,
        MinWidth = 1024,
        MinHeight = 768,
        WebPreferences = new WebPreferences
        {
            NodeIntegration = false,
            ContextIsolation = true,
            Sandbox = false,
#if DEBUG
            DevTools = true
#endif
        }
    });
    quantum.Window = window;

    window.RemoveMenu();

#if DEBUG
    window.WebContents.OpenDevTools();
#endif

    window.OnReadyToShow += () =>
    {
        window.Show();
    };

    window.OnClosed += () => app.StopAsync();

    await Task.Run(() => app.WaitForShutdown());
}
else
{
    await app.RunAsync();
}

return;

static void HandlePendingModules()
{
    var pendingPath = Path.Combine(Directory.GetCurrentDirectory(), "PendingModule");
    if (!Directory.Exists(pendingPath))
        return;

    foreach (var moduleFolder in Directory.GetDirectories(pendingPath))
    {
        try
        {
            var moduleName = Path.GetFileName(moduleFolder);
            var wwwrootSource = Path.Combine(moduleFolder, "wwwroot");
            var moduleTarget = Path.Combine(Directory.GetCurrentDirectory(), "Modules", moduleName);
            var wwwrootTarget = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

            // 处理wwwroot中的静态资源
            if (Directory.Exists(wwwrootSource))
            {
                var moduleContentTarget = Path.Combine(wwwrootTarget, "_content", moduleName);
                var contentSource = Path.Combine(wwwrootSource, "_content");

                // 如果存在 _content 目录，直接合并到运行目录的 wwwroot/_content
                if (Directory.Exists(contentSource))
                {
                    CopyDirectory(contentSource, Path.Combine(wwwrootTarget, "_content"), true);
                    Directory.Delete(contentSource, true);
                }

                // 将其他文件移动到 _content/{moduleName} 下
                Directory.CreateDirectory(moduleContentTarget);
                foreach (var item in Directory.GetFileSystemEntries(wwwrootSource))
                {
                    var itemName = Path.GetFileName(item);
                    if (itemName == "_content")
                        continue;

                    var targetPath = Path.Combine(moduleContentTarget, itemName);
                    if (File.Exists(item))
                    {
                        File.Copy(item, targetPath, true);
                        File.Delete(item);
                    }
                    else if (Directory.Exists(item))
                    {
                        CopyDirectory(item, targetPath, true);
                        Directory.Delete(item, true);
                    }
                }

                Directory.Delete(wwwrootSource, true);
            }

            // 将剩余内容复制到Modules文件夹
            Directory.CreateDirectory(Path.GetDirectoryName(moduleTarget)
                ?? throw new InvalidOperationException($"无法获取目录名称: {moduleTarget}"));
            if (Directory.Exists(moduleTarget))
                Directory.Delete(moduleTarget, true);

            CopyDirectory(moduleFolder, moduleTarget, true);
            Directory.Delete(moduleFolder, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理待安装模块失败: {ex}");
        }
    }

    try
    {
        if (Directory.Exists(pendingPath))
            Directory.Delete(pendingPath, true);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"清理PendingModule目录失败: {ex}");
    }
}

static void HandleModulesToUninstall()
{
    var modulesPath = Path.Combine(Directory.GetCurrentDirectory(), "Modules");
    if (!Directory.Exists(modulesPath))
        return;

    foreach (var moduleFolder in Directory.GetDirectories(modulesPath))
    {
        try
        {
            var uninstallMark = Path.Combine(moduleFolder, "TobeUninstalled.Quantum.MarkTag");
            if (!File.Exists(uninstallMark))
                continue;

            var moduleName = Path.GetFileName(moduleFolder);
            var moduleContentPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "_content", moduleName);

            // 删除模块的静态资源
            if (Directory.Exists(moduleContentPath))
                Directory.Delete(moduleContentPath, true);

            // 删除模块目录
            Directory.Delete(moduleFolder, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"处理待卸载模块失败: {ex}");
        }
    }
}

static void CopyDirectory(string sourceDir, string targetDir, bool overwrite)
{
    Directory.CreateDirectory(targetDir);

    foreach (var file in Directory.GetFiles(sourceDir))
    {
        var fileName = Path.GetFileName(file);
        var targetPath = Path.Combine(targetDir, fileName);
        File.Copy(file, targetPath, overwrite);
    }

    foreach (var directory in Directory.GetDirectories(sourceDir))
    {
        var dirName = Path.GetFileName(directory);
        var targetPath = Path.Combine(targetDir, dirName);
        CopyDirectory(directory, targetPath, overwrite);
    }
}
