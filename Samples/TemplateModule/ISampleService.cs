using Quantum.Sdk.Abstractions;

namespace TemplateModule;
public interface ISampleService : IInitializableService // 可以继承 Quantum.Sdk 中的接口
{
    string SayHello();
}
