# Quantum Codex Plugin

Quantum-maintained integration that connects to the locally installed Codex through `codex app-server` and exposes the current
Quantum RPC catalog as Codex dynamic tools.

## Requirements and attribution

Install and sign in to Codex CLI separately before connecting this plugin. The plugin package does not include,
download, or install Codex CLI, and does not read or copy its authentication files. The CLI uses its own authentication
state when started by the plugin.

Codex CLI is an OpenAI project licensed under [Apache-2.0](https://github.com/openai/codex/blob/main/LICENSE).
See [third-party notices](THIRD-PARTY-NOTICES.md) for upstream attribution. This is an official Quantum plugin,
not an OpenAI product or an OpenAI-endorsed integration. Access to hosted models is subject to the user's own
account, service terms, and usage limits; the CLI's software license does not grant model-service access.

## Tool access

Connecting exposes the available Quantum RPC methods as tools. A model may invoke those methods to read or change
data managed by installed plugins, including creating or deleting calendar items. Tool calls run with the user's
Quantum permissions without an additional per-call confirmation in this plugin. Connect only when you intend to
grant this access, and disconnect to stop the App Server session. Codex's file sandbox does not restrict Quantum RPC
tool effects.

## Runtime flow

1. The user selects a model and connects from `/plugins/codex`; the plugin starts `codex app-server` over its default
   stdio JSONL transport. GPT-5.6 Luna is selected by default.
2. The plugin initializes the App Server with `experimentalApi`, reads `quantum.rpc.catalog`, and creates one namespaced
   dynamic tool for every available RPC except the catalog itself.
3. An `item/tool/call` request executes directly through `IRpcInvoker`; the serialized result is returned to Codex as
   tool content.
4. Disconnecting, disabling, upgrading, or unloading the plugin terminates the child App Server process.

The plugin reuses the local Codex authentication state and does not store an OpenAI API key. Set
`QUANTUM_CODEX_COMMAND` to the Codex executable path when `codex` is not available on `PATH`.

The App Server dynamic tool surface is experimental and therefore tied to the locally installed Codex version.
Available models are refreshed from App Server's `model/list` response after connecting. Changing the model starts a
new thread and passes the selected model explicitly to `thread/start`.
If a selected model requires a newer App Server, update the CLI with
`npm install -g @openai/codex@latest`, then disconnect and reconnect the plugin.
