# Quantum SDK Repository Instructions

## Capability parity

- Treat the .NET and TypeScript SDKs as adapters over the same Quantum plugin capability model.
- When changing a public capability in either runtime, inspect and update the other SDK and the Quantum Host adapter.
- Keep names, lifecycle behavior, validation, payloads, errors, cancellation, and cleanup aligned where possible.
- Update contract tests and developer documentation for both runtimes. Document intentional runtime differences.
- TypeScript declarations alone are not a complete Web capability; the matching iframe/Host transport must exist in Quantum.
