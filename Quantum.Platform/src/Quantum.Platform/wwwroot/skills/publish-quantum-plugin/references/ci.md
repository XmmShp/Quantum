# CI setup

## One-time platform setup

Create the plugin listing in the Quantum developer portal. Then create one Credential Client per repository and limit it to the plugin IDs that repository owns. Run this interactively; the CLI prompts for the account password without echoing it:

```sh
quantum-cli clients create \
  --email developer@example.com \
  --client-id example-plugin-ci \
  --display-name "Example plugin CI" \
  --plugins quantum.plugin.example \
  --private-jwks-output ./quantum-ci.private.jwks.json
```

The private JWKS is returned once and the command refuses to overwrite an existing output file. Store it immediately in the CI secret manager, then remove the local file. `QUANTUM_USER_TOKEN` can replace interactive login for an already authenticated setup session. Delete and recreate the client if the private key is lost or exposed.

The client receives only `plugin:publish` and per-plugin permission claims. It cannot manage users, listings, reviews, or other plugins.

## GitHub Actions

Create repository secrets `QUANTUM_CLIENT_ID` and `QUANTUM_CLIENT_PRIVATE_JWKS`.

```yaml
name: Publish Quantum plugin

on:
  push:
    tags: ["v*"]

permissions:
  contents: read

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: "10.0.x"
      - run: dotnet tool install --global Quantum.Platform.Cli
      - name: Submit for automated review
        env:
          QUANTUM_CLIENT_ID: ${{ secrets.QUANTUM_CLIENT_ID }}
          QUANTUM_CLIENT_PRIVATE_JWKS: ${{ secrets.QUANTUM_CLIENT_PRIVATE_JWKS }}
        run: >-
          quantum-cli plugins publish ./dist/plugin
          --quantum-version-support ">=0.1.0"
          --release-notes "Release $GITHUB_REF_NAME"
          --output subprocess
```

Do not enable `set -x`, dump the environment, upload the private JWKS as an artifact, or interpolate it into command arguments.

## GitLab CI

Store both variables as masked, protected CI/CD variables. Use a file-type variable for `QUANTUM_CLIENT_PRIVATE_JWKS` if desired and pass its path with `--client-private-jwks "$QUANTUM_CLIENT_PRIVATE_JWKS_FILE"`.

## Output and recovery

`--output subprocess` writes exactly one JSON object on stdout. Diagnostics go to stderr and never include tokens or private key material. Re-running an already accepted `id@version` fails with a release conflict; increment the version in `plugin.json` before retrying.
