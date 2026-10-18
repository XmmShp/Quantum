# Quantum Official Plugins

Official plugins maintained for Quantum. Each plugin is independently versioned by its `plugin.json` manifest and is submitted to Quantum Platform for automated review.

## Plugins

- `Calendar` — `quantum.plugin.calendar`
- `Codex` — `quantum.plugin.codex`
- `Ledger` — `quantum.plugin.ledger` — offline CNY income/expense tracking, monthly summaries and CSV import/export

## Build

Before developing a plugin, read [the NOF RPC requirements](docs/plugin-development.md). Business capabilities that Quantum or AI tools should discover and call must have a NOF RPC contract, generated server metadata, registered handlers, and host discovery/invocation tests; a working UI and application-service DI registration alone are insufficient.

Plugin pages use generated RPC clients and share the same handlers as external callers. Each plugin's README documents its public operations.

```sh
dotnet build Quantum.OfficialPlugins.slnx -c Release
```

## Release

Update the selected plugin's `plugin.json` version, commit the change, then create a matching tag:

```sh
git tag calendar-v1.1.1
git push origin calendar-v1.1.1
```

Supported tag prefixes are `calendar-v`, `codex-v` and `ledger-v`. GitHub Actions verifies that the tag and manifest versions match, builds the selected plugin, and submits it to Quantum Platform. The platform then performs automated review.

Calendar uses Actions secrets `QUANTUM_CLIENT_ID` and `QUANTUM_CLIENT_PRIVATE_JWKS`. Codex uses `QUANTUM_CODEX_CLIENT_ID` and `QUANTUM_CODEX_CLIENT_PRIVATE_JWKS`; Ledger uses `QUANTUM_LEDGER_CLIENT_ID` and `QUANTUM_LEDGER_CLIENT_PRIVATE_JWKS`. Restrict each Credential Client to its plugin ID.

Before the first release, create the plugin listing in the platform workspace, then create a Credential Client for that listing. Store its private JWKS directly in GitHub Actions secrets; never commit it. The platform must have automated review enabled and a working review provider.

Publishing uses `Quantum.Platform.Cli` 0.1.1 and waits up to 15 minutes for `Published`. Rejected, failed, manual-review and timeout outcomes fail the job. A successful run includes the plugin version and package SHA-256 in its summary and `publish-result` artifact. Re-running the same tag reuses an existing release only when its package hash and compatibility range match. If the payload changes, bump the manifest version and create a new tag.

To resume an already submitted package after a review interruption, run **Build and publish** manually on `main`, select the plugin and supply the original submission's full SHA-256 as `resume_sha256`. Actions authenticates with the scoped client, downloads that exact package, verifies its hash and manifest, and uses CLI 0.1.1 to await publication. It never substitutes a newly rebuilt package for that submission. New submissions omit Razor Host static-web-assets manifests containing timestamps and absolute build paths; Quantum loads the packaged `plugin.json` and `wwwroot` assets directly.
