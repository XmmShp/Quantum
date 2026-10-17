# Quantum Official Plugins

Official plugins maintained for Quantum. Each plugin is independently versioned by its `plugin.json` manifest and is submitted to Quantum Platform for automated review.

## Plugins

- `Calendar` — `quantum.plugin.calendar`
- `Codex` — `quantum.plugin.codex`

## Build

```sh
dotnet build Quantum.OfficialPlugins.slnx -c Release
```

## Install

Find official plugins in the Quantum plugin marketplace and select **Download and install**.
The client checks compatibility and validates the package before installing it.

For local development, use each plugin's `plugin.json` manifest and the
[plugin development guide](https://github.com/XmmShp/Quantum/blob/main/docs/plugin-development.md)
to prepare a package for testing in Quantum.
