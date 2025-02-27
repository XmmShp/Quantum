using Quantum.Sdk.Utilities;

namespace Quantum.Sdk.Services;
public interface IModuleManager
{
    public Result<T> GetModule<T>(string moduleId, Version? minVersion = null, Version? maxVersion = null) where T : IModule;
}
