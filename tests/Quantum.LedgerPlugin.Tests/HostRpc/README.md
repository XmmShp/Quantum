# Host RPC integration fixture

These five C# files are unmodified snapshots of Quantum's actual RPC discovery, catalog, serialization and dispatch implementation. They compile only into the test assembly and are never packaged with a plugin. `provenance.json` records the upstream repository, commit and SHA-256 of each source file.

This keeps the official plugins repository independently buildable while testing the host's JSON boundary against the built Ledger DLL. When host RPC/ABI behavior changes, update these files from one pinned upstream commit, update the hashes, and rerun catalog/invocation tests. Do not customize the fixture to make a plugin pass.
