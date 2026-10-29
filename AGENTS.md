# Quantum monorepo

- On this Windows machine, run every command invoking `dotnet` with elevated permissions.
- GitLab `io7/quantum` is authoritative. GitHub `XmmShp/Quantum` is a public push mirror. GitLab Runner handles Linux verification, SDK/plugin publication and backend images; GitHub Actions builds and releases desktop/mobile clients from mirrored release tags.
- GitHub Releases distributes desktop and mobile clients built by GitHub Actions, including update feeds and checksums.
- Components live in `Quantum`, `Quantum.Sdk`, `Quantum.OfficialPlugins`, and `Quantum.Platform`. Follow their additional `AGENTS.md` instructions.
- Preserve .NET/TypeScript SDK capability parity and verify the corresponding Host transport when changing public plugin capabilities.
- Use the SDK project in this repository for development. Do not copy SDK source into plugins or publish the internal mobile protocol.
- Cross-platform verification uses `Quantum.Core.slnx` with `-p:QuantumCoreOnly=true`; native MAUI builds use the component solution on a matching OS.
