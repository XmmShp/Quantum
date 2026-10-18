# Official plugin development

- A feature that Quantum, another plugin, or an AI tool should discover or invoke must be exposed through NOF RPC. A Razor page, public C# method, or DI registration alone does not expose an RPC API.
- Define an `IRpcService` contract with `[TransportOverQuantum]`, stable `[RpcInvocationName]` names, one JSON-serializable request DTO per method, and `Result` / `Result<T>` return types. Add descriptions to services, methods, and request properties for catalog/schema consumers.
- Keep NOF source generators enabled in plugin projects. Declare `partial class ... : RpcServer<I...>` and register every generated handler base type with its concrete implementation in the plugin DI container.
- Test discovery through `quantum.rpc.catalog` and invocation by fully qualified RPC name through the host JSON boundary. Test actual reads/writes, validation, missing records, and cancellation; direct application-service tests alone are insufficient.
- Use RPC handlers as application use cases: put orchestration and repository/DbContext access in handlers, keeping domain invariants in domain objects. Do not add parallel ApplicationService/Session forwarding layers.
- Plugin pages must constructor-inject generated `I…RpcServiceClient` interfaces, registered with generated `Local…RpcServiceClient` implementations through the shared local RPC bootstrap. Handle failed Results before reporting success. Cross-plugin calls use the host `IRpcInvoker`.
- Keep stateful infrastructure (processes, connections, streaming state) in runtime adapters when required; pages may subscribe to change signals but must read and mutate business state through RPC clients.
- Test both generated local clients and host JSON dispatch, including request-scope isolation and recovery after failures. Keep NOF generators and native dispatch pipelines enabled.
- Read [docs/plugin-development.md](docs/plugin-development.md) before adding a plugin or public capability; update the plugin's RPC documentation and version when changing its published API.
- On this Windows development machine, every command invoking `dotnet` must run in a Windows administrator process.

# Code organization and style

- Keep one production type per file by default. Put handlers under `Handlers`, RPC request DTOs under `Requests`, and shared DTOs/enums under `Models`; keep a request and its dedicated response together when that improves navigation. Preserve existing RPC names and JSON contracts when reorganizing files.
- Keep Razor markup in `.razor` and substantial component behavior in a matching `.razor.cs` partial. Retain small expressions inline; inline thin one-off wrappers instead of creating extra helper types. Keep shared rules/helpers only when they provide useful reuse.
- Follow `.editorconfig`: LF, UTF-8, four-space C# indentation, file-scoped namespaces, and braces for all control flow. Put block braces on separate lines and expand compact try/catch/finally blocks.
- Verify affected solutions in order: `dotnet restore`, `dotnet format`, `dotnet build`, `dotnet test`. Exclude pinned `tests/Quantum.LedgerPlugin.Tests/HostRpc` snapshots from formatting to preserve provenance hashes. Format linked `Shared` sources through one plugin project to avoid Roslyn linked-file merging failures.
- Commit durable documentation: development guides, public API references, and required test provenance. Keep dated task audits, work logs, temporary verification reports, and local workspace references out of repository documentation.
