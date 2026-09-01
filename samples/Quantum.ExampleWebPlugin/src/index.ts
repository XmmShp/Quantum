import { definePlugin } from "@quantum/plugin-sdk";

interface ExamplePluginHandshake {
  message: string;
  sequence: number;
  dotNetStartedAt: string;
  integrationActive: boolean;
}

export default definePlugin({
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
        <p>当前视图：${escapeHtml(context.route.view)}</p>
        <p data-environment-status>正在读取 Quantum 插件环境…</p>
        <p data-host-status>正在通过 FQN 查询 .NET 宿主…</p>
        <div class="interop">
          <strong>TypeScript → .NET 插件 FQN 调用</strong>
          <p data-handshake-status>等待环境查询完成…</p>
          <small data-handshake-meta></small>
        </div>
        <div class="actions">
          <button type="button" data-action="handshake">再次握手</button>
          <button type="button" class="secondary" data-action="dotnet">打开 .NET 示例</button>
          <button type="button" class="secondary" data-action="home">返回概览</button>
        </div>
      </section>
    `;

    const status = context.element.querySelector<HTMLElement>("[data-handshake-status]");
    const metadata = context.element.querySelector<HTMLElement>("[data-handshake-meta]");
    const environmentStatus = context.element.querySelector<HTMLElement>("[data-environment-status]");
    const hostStatus = context.element.querySelector<HTMLElement>("[data-host-status]");
    const handshakeButton = context.element.querySelector<HTMLButtonElement>("[data-action=handshake]");
    let dotNetIntegrationActive: boolean | null = null;

    const requestHandshake = async () => {
      if (dotNetIntegrationActive === false) {
        if (status) {
          status.textContent = ".NET 示例未安装或版本不兼容，Web 插件继续独立运行。";
        }
        return;
      }

      if (handshakeButton) {
        handshakeButton.disabled = true;
      }
      try {
        if (status) {
          status.textContent = "正在连接 quantum.plugin.example…";
        }
        if (metadata) {
          metadata.textContent = "";
        }
        const handshake = await context.dotnet.invoke<ExamplePluginHandshake>({
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
          metadata.textContent = `调用序号 ${handshake.sequence} · .NET runtime 启动于 ${startedAt} · integration ${handshake.integrationActive ? "ACTIVE" : "STANDALONE"}`;
        }
      } catch (error) {
        if (!context.signal.aborted && status) {
          status.textContent = `握手失败：${error instanceof Error ? error.message : String(error)}`;
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
          integration => integration.pluginId === "quantum.plugin.example");
        dotNetIntegrationActive = dotNetIntegration?.active ?? false;
        if (!context.signal.aborted && environmentStatus) {
          environmentStatus.textContent = `已加载 ${environment.loadedPlugins.length} 个插件；.NET 示例 integration：${dotNetIntegrationActive ? "ACTIVE" : "INACTIVE"}。`;
        }
      } catch (error) {
        if (!context.signal.aborted && environmentStatus) {
          environmentStatus.textContent = `环境查询失败：${errorMessage(error)}`;
        }
      }

      try {
        const recognizesItself = await context.dotnet.invoke<boolean>({
          target: "host",
          service: "Quantum.Plugin.Abstraction.IQuantumPluginEnvironment",
          method: "IsPluginLoaded",
          arguments: [context.plugin.id],
          parameterTypes: ["System.String"]
        }, { signal: context.signal });
        if (!context.signal.aborted && hostStatus) {
          hostStatus.textContent = `.NET 宿主识别当前插件：${recognizesItself ? "是" : "否"}。`;
        }
      } catch (error) {
        if (!context.signal.aborted && hostStatus) {
          hostStatus.textContent = `宿主 FQN 调用失败：${errorMessage(error)}`;
        }
      }

      await requestHandshake();
    };

    handshakeButton?.addEventListener("click", () => void requestHandshake(), { signal: context.signal });
    context.element.querySelector("[data-action=dotnet]")?.addEventListener(
      "click",
      () => void context.navigation.navigate("/plugins/example"),
      { signal: context.signal });
    context.element.querySelector("[data-action=home]")?.addEventListener(
      "click",
      () => void context.navigation.navigate("/"),
      { signal: context.signal });

    // Mount must not be held open by cross-runtime work. Rendering succeeds immediately,
    // while every interop stage reports its own outcome and is cancelled on unmount.
    void initializeInterop();

    return () => context.element.replaceChildren();
  }
});

function escapeHtml(value: string): string {
  const element = document.createElement("span");
  element.textContent = value;
  return element.innerHTML;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
