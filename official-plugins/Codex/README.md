# Quantum Codex Plugin

Official Quantum plugin that embeds the locally installed Codex through `codex app-server` and exposes the current
Quantum RPC catalog as Codex dynamic tools.

## Runtime flow

1. The user connects from `/plugins/codex`; the plugin starts `codex app-server` over its default stdio JSONL transport.
2. The plugin initializes the App Server with `experimentalApi`, reads `quantum.rpc.catalog`, and creates one namespaced
   dynamic tool for every available RPC except the catalog itself.
3. An `item/tool/call` request is shown in Quantum for explicit user approval.
4. Approved calls execute through `IRpcInvoker`; the serialized result is returned to Codex as tool content.
5. Disconnecting, disabling, upgrading, or unloading the plugin terminates the child App Server process and declines
   outstanding calls.

The plugin reuses the local Codex authentication state and does not store an OpenAI API key. Set
`QUANTUM_CODEX_COMMAND` to the Codex executable path when `codex` is not available on `PATH`.

The App Server dynamic tool surface is experimental and therefore tied to the locally installed Codex version.
