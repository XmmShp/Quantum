# Quantum TypeScript Plugin SDK

`@quantum/plugin-sdk` provides the typed lifecycle and host-capability contract for Quantum Web plugins.
Web plugins execute in a dedicated sandboxed iframe. The iframe is destroyed on unload, so timers, DOM state,
module globals, and event handlers cannot leak into the next runtime generation.

```ts
import { definePlugin } from "@quantum/plugin-sdk";

export default definePlugin({
  async activate(context) {
    await context.log.info("Plugin activated");
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
