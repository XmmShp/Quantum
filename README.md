# Quantum Official Plugins

Official plugins maintained for Quantum. Each plugin is independently versioned by its `plugin.json` manifest and is submitted to Quantum Platform for automated review.

## Plugins

- `Calendar` — `quantum.plugin.calendar`
- `Codex` — `quantum.plugin.codex`
- `Ledger` — `quantum.plugin.ledger` — offline CNY income/expense tracking, monthly summaries and cross-month entry clues

## Build

Before developing a plugin, read [the NOF RPC requirements](docs/plugin-development.md). Business capabilities that Quantum or AI tools should discover and call must have a NOF RPC contract, generated server metadata, registered handlers, and host discovery/invocation tests; a working UI and application-service DI registration alone are insufficient.

Plugin pages use generated RPC clients and share the same handlers as external callers. Each plugin's README documents its public operations.

```sh
dotnet build Quantum.OfficialPlugins.slnx -c Release
```

## Install

Find official plugins in the Quantum plugin marketplace and select **Download and install**.
The client checks compatibility and validates the package before installing it.

For local development, use each plugin's `plugin.json` manifest and the
[official plugin development guide](docs/plugin-development.md) to prepare a
package for testing in Quantum. Host-side packaging details are documented in the
[Quantum plugin development guide](https://github.com/XmmShp/Quantum/blob/main/docs/plugin-development.md).

## Mobile preview

Calendar, Ledger and Codex now declare mobile companion pages on compatible desktop hosts. See [setup and current limitations](docs/mobile.md). These changes require building the adapted plugin packages; existing marketplace versions do not gain mobile pages automatically.
