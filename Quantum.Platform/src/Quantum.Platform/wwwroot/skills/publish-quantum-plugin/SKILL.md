---
name: publish-quantum-plugin
description: Publish a Quantum plugin package from a local folder or ZIP through Quantum.Platform.Cli and a private-key OIDC Credential Client. Use when setting up unattended CI release workflows, packaging plugin.json-based plugins, submitting a plugin version for automated review, troubleshooting Quantum publication authentication, or updating this skill from its signed-by-hash platform manifest. Do not use API keys or user passwords in CI.
metadata:
  manifest_url: https://quantum.io-vii.com/skills/publish-quantum-plugin/manifest.json
---

# Publish Quantum Plugin

Use `quantum-cli` with a Credential Client. The platform stores only public JWKS; the private JWKS stays in the CI secret store.

## Publish

1. Confirm the package root contains `plugin.json` with matching `id` and `version`.
2. Read `references/ci.md` when creating or changing a pipeline.
3. Install the CLI:

   ```sh
   dotnet tool install --global Quantum.Platform.Cli
   ```

4. Read `QUANTUM_CLIENT_ID` and `QUANTUM_CLIENT_PRIVATE_JWKS` from protected CI secrets. Never print them or pass private JWKS as a literal command argument.
5. Submit the folder or existing ZIP:

   ```sh
   quantum-cli plugins publish ./dist/plugin \
     --quantum-version-support ">=0.1.0" \
     --release-notes "${RELEASE_NOTES:-CI release}" \
     --output subprocess
   ```

The command exchanges a signed `private_key_jwt` assertion at `/oauth2/token`, receives a short-lived `plugin:publish` token, validates and packages the plugin, then submits it for automated review. A successful upload is not an immediate publication.

## Update this skill

Check without modifying files:

```sh
python scripts/update.py --check
```

Apply a hash-verified update:

```sh
python scripts/update.py --apply
```
