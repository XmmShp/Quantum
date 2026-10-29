---
name: quantum-plugin-release-review
description: Review a Quantum plugin release package for security, manifest consistency, unsafe migrations, undeclared behavior, and publication risk. Use only for Quantum Platform release-review tasks with an attached expanded package and review-request.json.
---

# Quantum Plugin Release Review

Review the inspectable plugin files in `package/` against `review-request.json` and the complete `package-inventory.json`. Produce only the structured result required by the output schema.

The Quantum Platform has already checked archive paths, size limits, duplicate entries, manifest presence, runtime entry shape, migration filenames, and package SHA-256. Treat those checks as necessary but not sufficient.

## Review

1. Read `review-request.json` and `package/plugin.json` first. Confirm the plugin ID, version, runtime declaration, compatibility range, and declared database migration directory agree.
2. Use `package-inventory.json` as the authoritative inventory. Inspect the included source, scripts, configuration, web assets, manifests, and SQL. Treat omitted files according to their recorded reason. Do not execute packaged binaries or scripts, install dependencies, start services, or use credentials.
3. Look for credential theft, hidden network destinations, command execution, persistence, privilege escalation, unsafe dynamic loading, obfuscation intended to conceal behavior, destructive or privilege-changing SQL, undeclared telemetry, and behavior unrelated to the plugin description.
4. Distinguish evidence from inference. Every rejecting finding must name a concrete package path and concise evidence. Never include secrets or reproduce long file contents.
5. Return `approve` only when all relevant content is inspectable, behavior matches the declared purpose, and there are no critical or high findings.
6. Return `reject` for concrete malicious behavior, destructive migrations, credential handling violations, or a material mismatch between declared and observed behavior.
7. Return `manual_review` when important binaries or generated/minified artifacts cannot be meaningfully assessed, evidence is ambiguous, required context is absent, or any review step cannot be completed. Fail closed; uncertainty is not approval.

## Severity

- `critical`: direct compromise, credential exfiltration, destructive data loss, or deliberate security-boundary bypass.
- `high`: exploitable unsafe behavior, undeclared privileged capability, or materially deceptive package behavior.
- `medium`: meaningful weakness that should be fixed but does not alone prove compromise.
- `low`: limited hardening or maintainability concern.
- `info`: relevant observation without a required change.

Keep the summary suitable for a release audit record. Set `reviewedFiles` to the number of package files actually inspected and list short stable check identifiers in `checksPerformed`.
