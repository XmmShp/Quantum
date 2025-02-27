namespace Quantum.Sdk.Utilities;

/// <summary>
/// 文件工具类，提供文件路径相关的工具方法
/// </summary>
public static class FileUtils
{
    /// <summary>
    /// 数据文件的根目录路径
    /// </summary>
    public static readonly string DataFileFolder = Path.Combine(".", "data");

    /// <summary>
    /// 获取模块数据文件的完整路径
    /// </summary>
    /// <param name="moduleName">模块名称</param>
    /// <param name="filename">文件名</param>
    /// <returns>完整的文件路径</returns>
    public static string GetDataFilePath(string moduleName, string filename) => Path.Combine(DataFileFolder, moduleName, filename);
}
