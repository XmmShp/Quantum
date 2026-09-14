# Quantum Official Plugins

Official plugins maintained for Quantum. Each plugin is independently versioned by its `plugin.json` manifest and is submitted to Quantum Platform for automated review.

## Plugins

- `Calendar` — `quantum.plugin.calendar`
- `Codex` — `quantum.plugin.codex`

## Build

```sh
dotnet build Quantum.OfficialPlugins.slnx -c Release
```

## Release

Update the selected plugin's `plugin.json` version, commit the change, then create a matching tag:

```sh
git tag calendar-v1.1.1
git push origin calendar-v1.1.1
```

Supported tag prefixes are `calendar-v` and `codex-v`. GitHub Actions verifies that the tag and manifest versions match, builds the selected plugin, and submits it to Quantum Platform. The platform then performs automated review.

The repository requires Actions secrets `QUANTUM_CLIENT_ID` and `QUANTUM_CLIENT_PRIVATE_JWKS`. Generate them from the platform's plugin workspace and restrict the Credential Client to the two plugin IDs above.
