# Quantum TypeScript Plugin SDK

`@quantum/plugin-sdk` provides the typed lifecycle and host-capability contract for Quantum Web plugins.
Web plugins execute in a dedicated sandboxed iframe. The iframe is destroyed on unload, so timers, DOM state,
module globals, and event handlers cannot leak into the next runtime generation.

```ts
import { definePlugin, QuantumTopic } from "@quantum/plugin-sdk";

export default definePlugin({
  async activate(context) {
    await context.log.info("Plugin activated");
    const topic = QuantumTopic.of("devices.status");
    const subscription = await context.eventBus.subscribe(topic, event => {
      const payload = event.payload as { state?: string };
      return context.log.info(`${event.publisher.id}: ${payload.state ?? "unknown"}`);
    });
    const publisher = context.eventBus.createPublisher<{ state: string }>(topic);
    await publisher.publish({ state: "ready" });
    return () => subscription.dispose();
  },

  mount({ element, route, signal }) {
    element.textContent = route.path;
    signal.addEventListener("abort", () => element.replaceChildren(), { once: true });
  }
});
```

The emitted plugin entry must be a single self-contained ESM file. Bundle dependencies into the entry with
esbuild, Rollup, Vite, or another ESM-capable bundler; runtime imports are intentionally unavailable inside the
opaque-origin frame.

Database schema migrations are package metadata rather than an iframe API. A Web plugin may use Prisma, Drizzle,
or another ORM during development, but its release declares `database.migrations` in `plugin.json` and ships an
append-only SQLite SQL history. The Host applies pending scripts before `activate`; the iframe receives no direct
database connection. See the repository Web plugin guide for the artifact and upgrade rules.

Installed plugins run as trusted, controlled code. Host navigation and .NET invocation are directly available.
`target` is `host` or the id of any loaded .NET plugin; an `integration` declaration is not required. Each invocation owns a DI scope, injects
`CancellationToken` parameters, awaits `Task`/`ValueTask`, serializes the result, and then disposes the scope.
Entries in `environment.snapshot().integrations` are informational declarations used for soft ordering; their `active`
field reports target availability and version compatibility but does not authorize or block interaction.

## Plugin identity and version value objects

`QuantumPluginInfo` and `QuantumPluginIdentity` use the branded `PluginId` and `SemanticVersion` string types.
Construct external values at the boundary instead of casting unchecked strings:

```ts
import { PluginId, SemanticVersion, VersionRange } from "@quantum/plugin-sdk";

const pluginId = PluginId.of("Quantum.Plugin.Theme");
const current = SemanticVersion.parse("2.1.0-rc.2+linux.arm64");
const minimum = SemanticVersion.parse("2.1.0-beta.1");
const parts = SemanticVersion.components(current);
const range = VersionRange.parse("{1.2.3} | [2.0.0-alpha,3.0.0)");

if (SemanticVersion.compare(current, minimum) >= 0) {
  console.log(pluginId, parts.major, parts.preReleaseIdentifiers, VersionRange.contains(range, current));
}
```

`PluginId.of()` applies the same normalization, 128-character limit, character rules, and reserved `disabled`
check as the .NET SDK. `SemanticVersion.parse()` strictly requires SemVer 2.0.0 `major.minor.patch`; its numeric
components are `bigint`, and `SemanticVersion.compare()` ignores build metadata when determining precedence.
Both branded values remain strings on the JSON wire, preserving the existing iframe/Host payload shape.

Manifest `dependencies` and `integrations` use `versionRange` rather than the legacy `minVersion`. `VersionRange`
supports bounded or unbounded mathematical intervals, finite sets in braces, and unions separated by `|`; `(,)`
and `*` both mean all versions. Prereleases participate directly in SemVer precedence, while build metadata is
ignored, so `{1.2.3}` contains `1.2.3+linux-x64`. The branded range also remains a string on the JSON wire.

## Topic EventBus

TypeScript and .NET plugins share the same Host EventBus. `QuantumTopic.of()` applies the same dot-delimited,
255-character validation as the .NET `QuantumTopic` value object. Topics must match
`^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$`; the branded TypeScript type prevents passing unchecked
strings to the SDK API.

`QuantumEvent.payload` is the original JSON value. JavaScript already has a deserialized value, so the SDK does
not add CLR-style deserialization helpers; validate or narrow the `unknown` payload in the handler. Publishing
waits for all current subscribers, including asynchronous iframe handlers, and subscriber failures are returned
to the publisher. Disposing a subscription unregisters it immediately; runtime deactivation and iframe disposal
also remove all remaining subscriptions as a Host-side fallback. Messages are in-memory only and are not retained
or replayed.
