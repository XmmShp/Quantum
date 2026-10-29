# Quantum monorepo

- On this Windows machine, run every command invoking `dotnet` with elevated permissions.
- GitLab `io7/quantum` is authoritative. GitHub `XmmShp/Quantum` is a public push mirror. All CI/CD runs on GitLab Runner.
- Components live in `Quantum`, `Quantum.Sdk`, `Quantum.OfficialPlugins`, and `Quantum.Platform`. Follow their additional `AGENTS.md` instructions.
- Preserve .NET/TypeScript SDK capability parity and verify the corresponding Host transport when changing public plugin capabilities.
- Use the SDK project in this repository for development. Do not copy SDK source into plugins or publish the internal mobile protocol.
- Cross-platform verification uses `Quantum.Core.slnx` with `-p:QuantumCoreOnly=true`; native MAUI builds use the component solution on a matching OS.
