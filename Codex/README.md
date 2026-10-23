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

1. The first interactive render of the global drawer or `/plugins/codex` automatically attempts a connection
   using the current working directory and selected model (GPT-5.6 Luna initially). Both views share one attempt per
   plugin runtime, so navigation does not launch another process or replace the conversation. Waiting until interactive
   rendering ensures the host RPC catalog is available and avoids blocking plugin startup.
2. The plugin initializes the App Server with `experimentalApi`, reads `quantum.rpc.catalog`, and creates one namespaced
   dynamic tool for each available RPC except the catalog and this plugin's own control RPCs. This prevents a model
   from recursively sending prompts to, replacing, or disconnecting the session executing its current turn.
3. An `item/tool/call` request executes directly through `IRpcInvoker`; the serialized result is returned to Codex as
   tool content.
4. Disconnecting, disabling, upgrading, or unloading the plugin terminates the child App Server process.

Automatic connection has a 30-second deadline. Connection failures and timeout details appear in the page and drawer,
with a **Retry connection** button and editable directory/model fields. It does not continually retry in the background.
After a deliberate disconnect, opening another view does not reconnect; the user can connect explicitly. Unloading the
plugin cancels an in-flight automatic attempt before disposing its process. Automatic connection creates a session and
exposes the other Quantum plugins' tools, but sends no prompt and starts no model turn until the user sends a message.

The plugin reuses the local Codex authentication state and does not store an OpenAI API key. Set
`QUANTUM_CODEX_COMMAND` to the Codex executable path when `codex` is not available on `PATH`.

The App Server dynamic tool surface is experimental and therefore tied to the locally installed Codex version.
Available models are refreshed from App Server's `model/list` response after connecting. Changing the model preserves the thread ID, messages and tool context. Each `turn/start` explicitly
passes the selected `model` for the existing `threadId`; changing models sends no prompt. Only **New conversation**
creates a fresh thread and clears history. Model changes are rejected while a turn is running. See the
[official App Server turn documentation](https://developers.openai.com/codex/app-server#turns).
If a selected model requires a newer App Server, update the CLI with
`npm install -g @openai/codex@latest`, then disconnect and reconnect the plugin.

## Quantum RPC

The `ICodexRpcService` NOF contract exposes these operations through `quantum.rpc.catalog`. Prefix each name below with
`quantum.plugin.codex.` for a fully qualified name. External Quantum callers can discover and invoke them; they are
intentionally excluded from the dynamic tools injected into this same Codex session.

| Method | Request | Result |
| --- | --- | --- |
| `codex.ensure-auto-connected` | `{}` | Once per runtime automatic connection attempt; respects manual disconnect suppression; returns snapshot including any connection failure |
| `codex.snapshot` | `{}` | Connection, current thread, running state, models and messages; no process starts |
| `codex.models` | `{}` | Cached model options; initial options before connection, App Server options after connection |
| `codex.connect` | `{ "workingDirectory": "C:/work", "model": "gpt-5.6-luna" }` | Starts the local App Server and creates a session; returns its snapshot |
| `codex.new-thread` | `{ "model": null }` | Starts a fresh thread, optionally changes model, clears displayed messages |
| `codex.change-model` | `{ "model": "gpt-5.6-sol" }` | Selects the model for later turns; preserves the current thread and messages; no inference |
| `codex.send` | `{ "prompt": "..." }` | Starts a model turn; poll `snapshot` for progress and completion |
| `codex.disconnect` | `{}` | Stops this plugin's App Server process; may interrupt an active turn |

Requests and responses cross the host JSON boundary using NOF `Result<T>`. Business failures use
`codex_invalid_request`, `codex_invalid_state` or `codex_connection_failed`; cancellation propagates.
Connection failures that the UI records in its snapshot are explicitly returned as failed RPC results.

Connect and send only with the user's authorization: these operations may expose other Quantum plugin tools to Codex,
consume model usage and cause tool effects. Snapshot includes local session messages. No authentication files or tokens
are returned. Tests cover the actual production snapshot/disconnect/error paths without launching Codex, plus dispatch
of all commands through a controlled runtime substitute so automated tests never consume model usage.

## Internal architecture

Both the page and global drawer constructor-inject generated `ICodexRpcServiceClient`, backed by
`LocalCodexRpcServiceClient`. Business calls, including snapshot reads and automatic connection, enter the same
handlers as host RPC. `CodexRuntime` remains a singleton process/connection/streaming adapter;
it is not a parallel application-service facade. The UI subscribes only to `ICodexRuntimeEvents.Changed` and
refreshes snapshots through RPC. Local dispatch creates a fresh server scope while preserving the shared runtime.
See [the official plugin development guide](../docs/plugin-development.md).
