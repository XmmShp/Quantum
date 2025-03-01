using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string projectFile;

if (args.Length == 0)
{
    // 在当前目录下查找第一个 .csproj 文件
    var currentDirectory = Directory.GetCurrentDirectory();
    var csprojFiles = Directory.GetFiles(currentDirectory, "*.csproj");

    if (csprojFiles.Length == 0)
    {
        Console.WriteLine("错误：当前目录下未找到 .csproj 文件。请提供项目文件路径作为参数。");
        return 1;
    }

    projectFile = csprojFiles[0];
    Console.WriteLine($"找到项目文件：{Path.GetFileName(projectFile)}");
}
else
{
    // 获取输入的项目文件路径（可能是相对路径）
    var inputPath = args[0];

    // 将相对路径转换为绝对路径
    projectFile = Path.GetFullPath(inputPath);
}

if (!File.Exists(projectFile))
{
    Console.WriteLine($"错误：项目文件 '{projectFile}' 不存在");
    return 1;
}

if (!projectFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"错误：文件 '{projectFile}' 不是有效的 .csproj 文件");
    return 1;
}

try
{
    // 解析 .csproj 文件获取 AssemblyName
    var projectXml = XDocument.Load(projectFile);
    var assemblyName = projectXml.Root?
        .Elements()
        .FirstOrDefault(e => e.Name.LocalName == "PropertyGroup")?
        .Elements()
        .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?
        .Value;

    if (string.IsNullOrEmpty(assemblyName))
    {
        // 如果没有明确指定 AssemblyName，使用项目文件名（不含扩展名）
        assemblyName = Path.GetFileNameWithoutExtension(projectFile);
    }

    // 创建临时发布目录
    var tempPublishPath = Path.Combine(Path.GetTempPath(), $"quantum_publish_{Guid.NewGuid()}");
    Directory.CreateDirectory(tempPublishPath);

    try
    {
        // 执行 dotnet publish 命令
        var projectDir = Path.GetDirectoryName(projectFile);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectFile}\" -c Release /p:PublishSingleFile=false -o \"{tempPublishPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = projectDir
        };

        using (var process = Process.Start(startInfo))
        {
            process?.WaitForExit();
            if (process?.ExitCode != 0)
            {
                throw new Exception("发布项目失败");
            }
        }

        // 压缩包输出到项目文件所在目录
        var zipPath = Path.Combine(projectDir!, $"{assemblyName}.zip");

        // 如果已存在同名zip文件，先删除
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        // 创建zip文件
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // 获取所有文件
            var files = Directory.GetFiles(tempPublishPath, "*.*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                // 获取相对路径
                var relativePath = Path.GetRelativePath(tempPublishPath, file);

                // 在压缩包中添加顶层文件夹
                var zipEntryPath = Path.Combine(assemblyName, relativePath);

                // 检查文件或其所在文件夹是否应该被排除
                var shouldExclude = false;
                var fileName = Path.GetFileName(file);
                var pathParts = relativePath.Split(Path.DirectorySeparatorChar);

                var excludePatterns = new[]
                {
                    "*.pdb",
                    "*.runtimeconfig.json",
                    "AntDesign.dll",
                    "ElectronNET.API.dll",
                    "Microsoft.AspNetCore.Authorization.dll",
                    "Microsoft.AspNetCore.Components.dll",
                    "Microsoft.AspNetCore.Components.Forms.dll",
                    "Microsoft.AspNetCore.Components.Web.dll",
                    "Microsoft.AspNetCore.Metadata.dll",
                    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
                    "Microsoft.Extensions.DependencyInjection.dll",
                    "Microsoft.Extensions.DependencyModel.dll",
                    "Microsoft.Extensions.Logging.Abstractions.dll",
                    "Microsoft.Extensions.Options.dll",
                    "Microsoft.Extensions.Primitives.dll",
                    "Microsoft.JSInterop.dll",
                    "Microsoft.Win32.SystemEvents.dll",
                    "Newtonsoft.Json.dll",
                    "OneOf.dll",
                    "Quantum.Sdk.*",
                    "Serilog.AspNetCore.dll",
                    "Serilog.dll",
                    "Serilog.Extensions.Hosting.dll",
                    "Serilog.Extensions.Logging.dll",
                    "Serilog.Formatting.Compact.dll",
                    "Serilog.Settings.Configuration.dll",
                    "Serilog.Sinks.Console.dll",
                    "Serilog.Sinks.Debug.dll",
                    "Serilog.Sinks.File.dll",
                    "SocketIOClient.dll",
                    "SocketIOClient.Newtonsoft.Json.dll",
                    "System.Drawing.Common.dll",
                    "AntDesign"
                };

                foreach (var pattern in excludePatterns)
                {
                    // 检查文件名是否匹配通配符模式
                    if (pattern.Contains("*") || pattern.Contains("?"))
                    {
                        if (WildcardMatch(fileName, pattern))
                        {
                            shouldExclude = true;
                            break;
                        }
                    }
                    // 检查路径中的任何部分是否完全匹配（用于文件夹）
                    else if (pathParts.Any(part => string.Equals(part, pattern, StringComparison.OrdinalIgnoreCase)))
                    {
                        shouldExclude = true;
                        break;
                    }
                }

                // 如果不在排除列表中，添加到压缩包
                if (!shouldExclude && !relativePath.EndsWith(".zip"))
                {
                    archive.CreateEntryFromFile(file, zipEntryPath);
                    Console.WriteLine($"已添加: {zipEntryPath}");
                }
                else
                {
                    Console.WriteLine($"已跳过: {zipEntryPath}");
                }
            }
        }

        Console.WriteLine($"\n处理完成！压缩包已保存到: {zipPath}");
        return 0;
    }
    finally
    {
        // 清理临时发布目录
        try
        {
            if (Directory.Exists(tempPublishPath))
            {
                Directory.Delete(tempPublishPath, true);
            }
        }
        catch
        {
            // 忽略清理临时目录时的错误
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"错误：{ex.Message}");
    return 1;
}

static bool WildcardMatch(string text, string pattern)
{
    // 将通配符转换为正则表达式模式
    pattern = pattern.Replace(".", "\\.")  // 转义点号
                    .Replace("*", ".*")    // *转换为正则的.*
                    .Replace("?", ".");     // ?转换为正则的.

    // 添加开始和结束标记
    pattern = $"^{pattern}$";

    return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
}
