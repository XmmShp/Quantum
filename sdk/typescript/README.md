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

Host navigation requires `ui.navigation`. Generic .NET invocation requires one of these manifest permissions:

- `dotnet.invoke:<target>:<service-fqn>`
- `dotnet.invoke:<target>:*`
- `dotnet.invoke:*`

`target` is `host` or the id of an active .NET plugin integration. Each invocation owns a DI scope, injects
`CancellationToken` parameters, awaits `Task`/`ValueTask`, serializes the result, and then disposes the scope.

## Topic EventBus

TypeScript and .NET plugins share the same Host EventBus. `QuantumTopic.of()` applies the same dot-delimited,
255-character validation as the .NET `QuantumTopic` value object. Topics must match
`^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$`; the branded TypeScript type prevents passing unchecked
strings to the SDK API. EventBus access does not require a manifest permission.

`QuantumEvent.payload` is the original JSON value. JavaScript already has a deserialized value, so the SDK does
not add CLR-style deserialization helpers; validate or narrow the `unknown` payload in the handler. Publishing
waits for all current subscribers, including asynchronous iframe handlers, and subscriber failures are returned
to the publisher. Disposing a subscription unregisters it immediately; runtime deactivation and iframe disposal
also remove all remaining subscriptions as a Host-side fallback. Messages are in-memory only and are not retained
or replayed.
