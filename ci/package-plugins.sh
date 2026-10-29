#!/usr/bin/env bash
set -euo pipefail
root="$(pwd)"
for plugin in Calendar Codex Ledger; do
    output="$root/artifacts/plugins/$plugin"
    dotnet publish "Quantum.OfficialPlugins/$plugin/Quantum.${plugin}Plugin.csproj" -c Release --output "$output"
    python3 Quantum.OfficialPlugins/scripts/verify-plugin-assets.py "$output"
    # Host manifests contain machine paths and build timestamps, not plugin assets.
    find "$output" -maxdepth 1 -name '*.staticwebassets.*.json' -delete
done
