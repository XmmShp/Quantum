# GitLab CI/CD

GitLab `io7/quantum` is the source of truth. GitHub `XmmShp/Quantum` is a public push mirror configured in GitLab repository settings with a dedicated writable SSH deploy key. No GitHub Actions workflow is active. Push changes and tags to GitLab only. The mirror keeps divergent refs so an unexpected GitHub change is reported instead of silently overwritten; resolve divergence from GitLab.

## Linux pipeline

The existing instance runners with the `ubuntu` tag run:

- All .NET SDK, Host core, official plugin and Platform tests through `Quantum.Core.slnx`.
- TypeScript SDK, example Web plugin, iframe transport, SDK version policy and Codex media tests.
- Publishing protocol tests and published static-asset validation for every official plugin.
- Platform container publication to `$CI_REGISTRY_IMAGE/platform:$CI_COMMIT_SHA` after verification on the default branch. The image build context is the monorepo root; it uses the same internal mobile protocol revision as the Host. Runtime configuration is injected externally.

The Docker executor uses the host Docker socket. It does not require Docker-in-Docker or privileged containers. Image publication does not change a running production deployment. Use the immutable image reported by `publish-platform-image` for deployment.

The optional `pages` job builds DocFX on GitLab Runner and publishes GitLab Pages. Existing GitHub release downloads remain available; new native builds are retained as GitLab job artifacts until platform release hosting is configured.

## Releases

Create release tags on commits in the monorepo default branch, after updating the relevant component version:

| Tag | Result | Protected masked GitLab variables |
| --- | --- | --- |
| `sdk/vX.Y.Z` | NuGet `Quantum.Plugin.Abstraction` and npm `@io7/quantum-plugin-sdk` | `NUGET_API_KEY`, `NPM_TOKEN` |
| `cli-vX.Y.Z` | NuGet `Quantum.Platform.Cli` | `NUGET_API_KEY` |
| `calendar-vX.Y.Z` | Publish Calendar and wait for marketplace review | `QUANTUM_CLIENT_ID`, `QUANTUM_CLIENT_PRIVATE_JWKS` |
| `codex-vX.Y.Z` | Publish Codex and wait for marketplace review | `QUANTUM_CODEX_CLIENT_ID`, `QUANTUM_CODEX_CLIENT_PRIVATE_JWKS` |
| `ledger-vX.Y.Z` | Publish Ledger and wait for marketplace review | `QUANTUM_LEDGER_CLIENT_ID`, `QUANTUM_LEDGER_CLIENT_PRIVATE_JWKS` |
| `desktop-vX.Y.Z` | Optional manual Windows/macOS package jobs | Platform signing variables below |
| `mobile-vX.Y.Z[-preview.N]` | Optional manual mobile package jobs | Android signing variables below |

GitHub Actions secrets cannot be read back or exported. Registry credentials must be configured in GitLab; GitHub-bound npm/NuGet trusted publishing is not reused by GitLab Runner. The SDK job publishes only for explicit SDK tags, validates release ancestry, version ordering and the public package boundary, and uses a temporary npm configuration file removed after publication. Re-running a partially published SDK release skips versions already present in either registry.

Protect the release tag patterns in GitLab and keep credentials protected and masked. Do not put credentials in files or mirror URLs. GitLab's built-in `CI_REGISTRY_*` variables authenticate container publication without an additional secret.

## Optional platform runners

Linux verification and container publication work with the current runners. Native jobs are manual and non-blocking until suitable runners are registered:

| Runner tag | Prerequisites |
| --- | --- |
| `quantum-windows` | Windows, PowerShell 7, .NET 10; run the runner service with administrator privileges for MAUI workloads |
| `quantum-macos` | macOS with compatible Xcode, .NET 10, PowerShell, Apple signing and notarization credentials |
| `quantum-android` | Linux Docker Runner with an image supplying .NET 10, Android SDK, Java 21 and PowerShell; set `QUANTUM_ANDROID_IMAGE` and `ANDROID_HOME` |

Windows signing uses `WINDOWS_CERTIFICATE_BASE64`, `WINDOWS_CERTIFICATE_PASSWORD`, and optional `REQUIRE_WINDOWS_SIGNING=true`. macOS uses `MACOS_APPLICATION_CERTIFICATE_BASE64`, `MACOS_INSTALLER_CERTIFICATE_BASE64`, `MACOS_CERTIFICATE_PASSWORD`, `MACOS_APPLICATION_IDENTITY`, `MACOS_INSTALLER_IDENTITY`, `APPLE_ID`, `APPLE_TEAM_ID`, and `APPLE_APP_SPECIFIC_PASSWORD`. Android uses `MOBILE_ANDROID_KEYSTORE_BASE64` and `MOBILE_ANDROID_KEYSTORE_PASSWORD`; preserve the existing key to keep installed clients upgradable.

## History

Each component import has the original component revision as a parent, preserving commit IDs. The initial SDK import includes the mobile bridge branch ahead of its old `main`. All old tags are namespaced under `legacy/<component>/`; original desktop, mobile, plugin and CLI release tags also retain their names. Those historical tags have their original single-repository tree, so create new release tags only on monorepo commits.

Local `.migration-backup` data is ignored and must never be published. Old unrelated workspace projects and local artifacts are also excluded.
