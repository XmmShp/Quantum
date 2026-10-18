# Official plugin development

- A feature that Quantum, another plugin, or an AI tool should discover or invoke must be exposed through NOF RPC. A Razor page, public C# method, or DI registration alone does not expose an RPC API.
- Define an `IRpcService` contract with `[TransportOverQuantum]`, stable `[RpcInvocationName]` names, one JSON-serializable request DTO per method, and `Result` / `Result<T>` return types. Add descriptions to services, methods, and request properties for catalog/schema consumers.
- Keep NOF source generators enabled in plugin projects. Declare `partial class ... : RpcServer<I...>` and register every generated handler base type with its concrete implementation in the plugin DI container.
- Test discovery through `quantum.rpc.catalog` and invocation by fully qualified RPC name through the host JSON boundary. Test actual reads/writes, validation, missing records, and cancellation; direct application-service tests alone are insufficient.
- Keep UI and RPC behavior on the same application/domain layer. Do not duplicate business validation or bypass persistence rules in RPC handlers.
- Read [docs/plugin-development.md](docs/plugin-development.md) before adding a plugin or public capability; update the plugin's RPC documentation and version when changing its published API.
- On this Windows development machine, every command invoking `dotnet` must run in a Windows administrator process.
