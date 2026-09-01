# Quantum Repository Instructions

## Plugin SDK capability parity

- Treat the .NET and TypeScript plugin SDKs as two adapters over the same Quantum plugin capability model.
- When adding, removing, or changing a public capability in either `sdk/dotnet` or `sdk/typescript`, inspect the other SDK and its Host adapter in the same change.
- Keep names, lifecycle behavior, validation rules, payload shape, error propagation, cancellation, and cleanup semantics aligned wherever the runtimes allow it.
- Add or update contract tests and developer documentation for both SDKs. If exact parity is not technically possible, document the intentional difference and its runtime reason.
- A capability is not complete for Web plugins when only TypeScript declarations exist; implement and verify the corresponding iframe/Host transport.
