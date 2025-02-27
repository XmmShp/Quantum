using Quantum.Sdk.Utilities;

namespace Quantum.Sdk.Services;
public interface IModuleManager
{
    public Result<IModule> GetModule(string moduleId, Version? minVersion = null, Version? maxVersion = null);
}
