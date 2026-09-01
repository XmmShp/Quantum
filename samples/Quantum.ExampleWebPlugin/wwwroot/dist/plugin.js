// ../../sdk/typescript/dist/index.js
function definePlugin(definition) {
  return definition;
}

// src/index.ts
var index_default = definePlugin({
  async activate(context) {
    await context.log.info("TypeScript example activated");
    return () => context.log.info("TypeScript example deactivated");
  },
  async mount(context) {
    context.element.innerHTML = `
      <style>
        body { color: #29243d; background: #fff; font-family: Inter, system-ui, sans-serif; }
        .page { padding: 42px; }
        .tag { color: #6653e8; font-size: 12px; font-weight: 800; letter-spacing: .12em; }
        h1 { margin: 10px 0; font-size: 38px; }
        p { color: #716b7e; line-height: 1.7; }
        .interop { margin: 24px 0; padding: 18px; border-radius: 14px; background: #f4f1ff; }
        .interop strong { display: block; color: #342974; margin-bottom: 6px; }
        .actions { display: flex; gap: 10px; flex-wrap: wrap; }
        button { border: 0; border-radius: 10px; padding: 10px 14px; color: #fff; background: #6553e7; cursor: pointer; }
        button.secondary { color: #554a80; background: #ece8fb; }
        button:disabled { cursor: wait; opacity: .65; }
      </style>
      <section class="page">
        <div class="tag">ISOLATED TYPESCRIPT PLUGIN</div>
        <h1>Quantum Web Runtime</h1>
        <p>\u5F53\u524D\u89C6\u56FE\uFF1A${escapeHtml(context.route.view)}</p>
        <p data-environment-status>\u6B63\u5728\u8BFB\u53D6 Quantum \u63D2\u4EF6\u73AF\u5883\u2026</p>
        <p data-host-status>\u6B63\u5728\u901A\u8FC7 FQN \u67E5\u8BE2 .NET \u5BBF\u4E3B\u2026</p>
        <div class="interop">
          <strong>TypeScript \u2192 .NET \u63D2\u4EF6 FQN \u8C03\u7528</strong>
          <p data-handshake-status>\u7B49\u5F85\u73AF\u5883\u67E5\u8BE2\u5B8C\u6210\u2026</p>
          <small data-handshake-meta></small>
        </div>
        <div class="actions">
          <button type="button" data-action="handshake">\u518D\u6B21\u63E1\u624B</button>
          <button type="button" class="secondary" data-action="dotnet">\u6253\u5F00 .NET \u793A\u4F8B</button>
          <button type="button" class="secondary" data-action="home">\u8FD4\u56DE\u6982\u89C8</button>
        </div>
      </section>
    `;
    const status = context.element.querySelector("[data-handshake-status]");
    const metadata = context.element.querySelector("[data-handshake-meta]");
    const environmentStatus = context.element.querySelector("[data-environment-status]");
    const hostStatus = context.element.querySelector("[data-host-status]");
    const handshakeButton = context.element.querySelector("[data-action=handshake]");
    let dotNetIntegrationActive = null;
    const requestHandshake = async () => {
      if (dotNetIntegrationActive === false) {
        if (status) {
          status.textContent = ".NET \u793A\u4F8B\u672A\u5B89\u88C5\u6216\u7248\u672C\u4E0D\u517C\u5BB9\uFF0CWeb \u63D2\u4EF6\u7EE7\u7EED\u72EC\u7ACB\u8FD0\u884C\u3002";
        }
        return;
      }
      if (handshakeButton) {
        handshakeButton.disabled = true;
      }
      try {
        if (status) {
          status.textContent = "\u6B63\u5728\u8FDE\u63A5 quantum.plugin.example\u2026";
        }
        if (metadata) {
          metadata.textContent = "";
        }
        const handshake = await context.dotnet.invoke({
          target: "quantum.plugin.example",
          service: "Quantum.ExamplePlugin.IExamplePluginState",
          method: "CreateWebHandshakeAsync",
          arguments: [context.plugin.id],
          parameterTypes: ["System.String"]
        }, { signal: context.signal });
        if (status) {
          status.textContent = handshake.message;
        }
        if (metadata) {
          const startedAt = new Date(handshake.dotNetStartedAt).toLocaleTimeString();
          metadata.textContent = `\u8C03\u7528\u5E8F\u53F7 ${handshake.sequence} \xB7 .NET runtime \u542F\u52A8\u4E8E ${startedAt} \xB7 integration ${handshake.integrationActive ? "ACTIVE" : "STANDALONE"}`;
        }
      } catch (error) {
        if (!context.signal.aborted && status) {
          status.textContent = `\u63E1\u624B\u5931\u8D25\uFF1A${error instanceof Error ? error.message : String(error)}`;
        }
      } finally {
        if (handshakeButton && !context.signal.aborted) {
          handshakeButton.disabled = false;
        }
      }
    };
    const initializeInterop = async () => {
      try {
        const environment = await context.environment.snapshot();
        const dotNetIntegration = environment.integrations.find(
          (integration) => integration.pluginId === "quantum.plugin.example"
        );
        dotNetIntegrationActive = dotNetIntegration?.active ?? false;
        if (!context.signal.aborted && environmentStatus) {
          environmentStatus.textContent = `\u5DF2\u52A0\u8F7D ${environment.loadedPlugins.length} \u4E2A\u63D2\u4EF6\uFF1B.NET \u793A\u4F8B integration\uFF1A${dotNetIntegrationActive ? "ACTIVE" : "INACTIVE"}\u3002`;
        }
      } catch (error) {
        if (!context.signal.aborted && environmentStatus) {
          environmentStatus.textContent = `\u73AF\u5883\u67E5\u8BE2\u5931\u8D25\uFF1A${errorMessage(error)}`;
        }
      }
      try {
        const recognizesItself = await context.dotnet.invoke({
          target: "host",
          service: "Quantum.Plugin.Abstraction.IQuantumPluginEnvironment",
          method: "IsPluginLoaded",
          arguments: [context.plugin.id],
          parameterTypes: ["System.String"]
        }, { signal: context.signal });
        if (!context.signal.aborted && hostStatus) {
          hostStatus.textContent = `.NET \u5BBF\u4E3B\u8BC6\u522B\u5F53\u524D\u63D2\u4EF6\uFF1A${recognizesItself ? "\u662F" : "\u5426"}\u3002`;
        }
      } catch (error) {
        if (!context.signal.aborted && hostStatus) {
          hostStatus.textContent = `\u5BBF\u4E3B FQN \u8C03\u7528\u5931\u8D25\uFF1A${errorMessage(error)}`;
        }
      }
      await requestHandshake();
    };
    handshakeButton?.addEventListener("click", () => void requestHandshake(), { signal: context.signal });
    context.element.querySelector("[data-action=dotnet]")?.addEventListener(
      "click",
      () => void context.navigation.navigate("/plugins/example"),
      { signal: context.signal }
    );
    context.element.querySelector("[data-action=home]")?.addEventListener(
      "click",
      () => void context.navigation.navigate("/"),
      { signal: context.signal }
    );
    void initializeInterop();
    return () => context.element.replaceChildren();
  }
});
function escapeHtml(value) {
  const element = document.createElement("span");
  element.textContent = value;
  return element.innerHTML;
}
function errorMessage(error) {
  return error instanceof Error ? error.message : String(error);
}
export {
  index_default as default
};
