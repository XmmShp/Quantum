# Host RPC integration fixture

These C# files are unmodified copies of Quantum's actual RPC implementation. They compile only into the test assembly and are never packaged with a plugin. `provenance.json` records the repository, base commit, source state and SHA-256 hashes. The snapshot is pinned to Quantum commit [af08cd0](https://github.com/XmmShp/Quantum/commit/af08cd08dc29a007cccae3c4f853ee5f2b40fc18); all files match that committed source byte for byte.

Copy framework files verbatim when host behavior changes; never customize this fixture to make a plugin pass. Tests execute the NOF generated assembly initializers and the host's inbound pipeline, including JSON defaults, failure handling and cancellation.
