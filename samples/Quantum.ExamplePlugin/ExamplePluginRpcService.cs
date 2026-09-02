using NOF.Application;
using NOF.Contract;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExamplePlugin;

[TransportOverQuantum]
[RpcInvocationName("example")]
public interface IExamplePluginRpcService : IRpcService
{
    [RpcInvocationName("greet")]
    [RpcInvocationAlias("sample.greet")]
    Result<string> CreateDependencyGreeting(Empty request);

    [RpcInvocationName("handshake")]
    [RpcInvocationAlias("sample.handshake")]
    Result<ExamplePluginHandshake> CreateWebHandshake(Empty request);
}

public partial class ExamplePluginRpcService : RpcServer<IExamplePluginRpcService>;
