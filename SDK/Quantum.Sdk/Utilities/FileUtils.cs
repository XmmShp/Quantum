namespace Quantum.Sdk.Utilities;

public static class FileUtils
{
    public static readonly string DataFileFolder = Path.Combine(".", "data");
    public static string GetDataFilePath(string moduleName, string filename) => Path.Combine(DataFileFolder, moduleName, filename);
}
