# GitLab CI/CD

GitLab `io7/quantum` is the source of truth. GitHub `XmmShp/Quantum` is a public push mirror configured in GitLab repository settings with a dedicated writable SSH deploy key. GitHub Actions builds and releases desktop/mobile clients; GitLab Runner handles the Linux pipeline below. Push changes and tags to GitLab only. The mirror keeps divergent refs so an unexpected GitHub change is reported instead of silently overwritten; resolve divergence from GitLab.

## Linux pipeline

The existing instance runners with the `ubuntu` tag run:

- All .NET SDK, Host core, official plugin and Platform tests through `Quantum.Core.slnx`.
- TypeScript SDK, example Web plugin, iframe transport, SDK version policy and Codex media tests.
- Publishing protocol tests and published static-asset validation for every official plugin.
- Platform container publication to `$CI_REGISTRY_IMAGE/platform:$CI_COMMIT_SHA` after verification on the default branch. The image build context is the monorepo root; it uses the same internal mobile protocol revision as the Host. Runtime configuration is injected externally.

The Docker executor uses the host Docker socket. It does not require Docker-in-Docker or privileged containers. Image publication does not change a running production deployment. Use the immutable image reported by `publish-platform-image` for deployment.

The optional `pages` job builds DocFX on GitLab Runner and publishes GitLab Pages. GitHub Releases is the public download and desktop update endpoint for desktop and mobile clients. GitHub Actions builds the packages from mirrored release tags and uploads them to GitHub Releases.

## Releases

Create release tags on commits in the monorepo default branch, after updating the relevant component version:

| Tag | Result | Credentials |
| --- | --- | --- |
| `sdk/vX.Y.Z` | NuGet `Quantum.Plugin.Abstraction` and npm `@io7/quantum-plugin-sdk` | `NUGET_API_KEY`, `NPM_TOKEN` |
| `cli-vX.Y.Z` | NuGet `Quantum.Platform.Cli` | `NUGET_API_KEY` |
| `calendar-vX.Y.Z` | Publish Calendar and wait for marketplace review | `QUANTUM_CLIENT_ID`, `QUANTUM_CLIENT_PRIVATE_JWKS` |
| `codex-vX.Y.Z` | Publish Codex and wait for marketplace review | `QUANTUM_CODEX_CLIENT_ID`, `QUANTUM_CODEX_CLIENT_PRIVATE_JWKS` |
| `ledger-vX.Y.Z` | Publish Ledger and wait for marketplace review | `QUANTUM_LEDGER_CLIENT_ID`, `QUANTUM_LEDGER_CLIENT_PRIVATE_JWKS` |
| `desktop-vX.Y.Z` | GitHub Actions builds Windows and optional macOS packages, then publishes GitHub Releases | GitHub signing secrets below; built-in `GITHUB_TOKEN` publishes |
| `mobile-vX.Y.Z[-preview.N]` | GitHub Actions builds Windows/Android packages and publishes a preview Release | GitHub Android signing secrets below; built-in `GITHUB_TOKEN` publishes |

GitHub Actions secrets cannot be read back or exported. Registry credentials must be configured in GitLab; GitHub-bound npm/NuGet trusted publishing is not reused by GitLab Runner. The SDK job publishes only for explicit SDK tags, validates release ancestry, version ordering and the public package boundary, and uses a temporary npm configuration file removed after publication. Re-running a partially published SDK release skips versions already present in either registry.

Protect the release tag patterns in GitLab and keep credentials protected and masked. Do not put credentials in files or mirror URLs. GitLab's built-in `CI_REGISTRY_*` variables authenticate container publication without an additional secret.

### Client builds and downloads on GitHub

Push code and release tags to GitLab only. The existing GitLab push mirror uses a dedicated writable SSH deploy key to synchronize branches and tags to GitHub. A mirrored `desktop-v*` or `mobile-v*` tag triggers the corresponding workflow in `.github/workflows/`. Ordinary branch pushes synchronize source without creating a release. Do not push several release tags together: GitHub does not emit tag push events when more than three tags are pushed at once.

GitHub Actions uses GitHub-hosted Windows, macOS and Linux runners; local native GitLab runners are not required. `workflow_dispatch` accepts a version for a build-only rehearsal and retains artifacts without publishing. Tag-triggered jobs build and verify all selected platforms, validate SHA256 manifests, create a draft release, verify uploaded digests, and publish only after the complete upload succeeds. Re-running a draft upload skips identical assets and rejects differing assets. Published releases are immutable. Desktop releases become latest; mobile releases remain previews and do not replace the desktop update channel.

GitHub's built-in `GITHUB_TOKEN` has `contents: write` only in the release job. No `GITHUB_RELEASE_TOKEN` needs to be copied to GitLab. SDK, CLI and plugin release credentials from the table above remain protected/masked GitLab variables; client signing credentials remain GitHub Actions secrets.

Windows signing uses GitHub secrets `WINDOWS_CERTIFICATE_BASE64`, `WINDOWS_CERTIFICATE_PASSWORD`, and repository variable `REQUIRE_WINDOWS_SIGNING=true` when signing must be enforced. Android requires `MOBILE_ANDROID_KEYSTORE_BASE64` and `MOBILE_ANDROID_KEYSTORE_PASSWORD`; preserve the existing key for installed clients to remain upgradable.

The repository variable `ENABLE_MACOS_RELEASE=true` enables both macOS architectures. Before enabling it, configure GitHub secrets `MACOS_APPLICATION_CERTIFICATE_BASE64`, `MACOS_INSTALLER_CERTIFICATE_BASE64`, `MACOS_CERTIFICATE_PASSWORD`, `MACOS_APPLICATION_IDENTITY`, `MACOS_INSTALLER_IDENTITY`, `APPLE_ID`, `APPLE_TEAM_ID`, and `APPLE_APP_SPECIFIC_PASSWORD`. macOS signing initialization runs in the same PowerShell process as packaging, preserving its temporary keychain environment.

## History

Each component import has the original component revision as a parent, preserving commit IDs. The initial SDK import includes the mobile bridge branch ahead of its old `main`. All old tags are namespaced under `legacy/<component>/`; original desktop, mobile, plugin and CLI release tags also retain their names. Those historical tags have their original single-repository tree, so create new release tags only on monorepo commits.

Local `.migration-backup` data is ignored and must never be published. Old unrelated workspace projects and local artifacts are also excluded.
