import { definePlugin, QuantumTopic } from "@io7/quantum-plugin-sdk";

interface ExamplePluginHandshake {
  message: string;
  sequence: number;
  dotNetStartedAt: string;
  webPluginAvailable: boolean;
}

export default definePlugin({
  async activate(context) {
    await context.log.info("TypeScript example activated");
    const topic = QuantumTopic.of("example.web.status");
    const subscription = await context.eventBus.subscribe(topic, event => {
      const payload = event.payload as { state?: string };
      return context.log.info(
        `${event.publisher.id} published ${payload.state ?? "unknown"} to ${event.topic}`);
    });
    const publisher = context.eventBus.createPublisher<{ state: string }>(topic);
    await publisher.publish({ state: "activated" });
    return async () => {
      await subscription.dispose();
      await context.log.info("TypeScript example deactivated");
    };
  },

  async mount(context) {
    const text = createMessages(context.locale.languageName);
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
        <div class="tag">${text.tag}</div>
        <h1>${text.heading}</h1>
        <p>${text.currentView}: ${escapeHtml(context.route.view)}</p>
        <p data-environment-status>${text.readingEnvironment}</p>
        <div class="interop">
          <strong>${text.interopHeading}</strong>
          <p data-handshake-status>${text.waitingForEnvironment}</p>
        </div>
        <div class="actions">
          <button type="button" data-action="handshake">${text.handshakeAgain}</button>
          <button type="button" class="secondary" data-action="dotnet">${text.openDotNet}</button>
          <button type="button" class="secondary" data-action="home">${text.backHome}</button>
        </div>
      </section>
    `;

    const status = context.element.querySelector<HTMLElement>("[data-handshake-status]");
    const environmentStatus = context.element.querySelector<HTMLElement>("[data-environment-status]");
    const handshakeButton = context.element.querySelector<HTMLButtonElement>("[data-action=handshake]");
    let dotNetPluginAvailable: boolean | null = null;

    const requestHandshake = async () => {
      if (dotNetPluginAvailable === false) {
        if (status) {
          status.textContent = text.dotNetUnavailable;
        }
        return;
      }

      if (handshakeButton) {
        handshakeButton.disabled = true;
      }
      try {
        if (status) {
          status.textContent = text.connecting;
        }
        const result = await context.rpc.invoke<ExamplePluginHandshake>(
          "sample.handshake",
          {},
          {},
          { signal: context.signal });
        if (!result.isSuccess) {
          throw new Error(`${result.errorCode}: ${result.message}`);
        }
        const handshake = result.value;
        if (status) {
          status.textContent = handshake.message;
        }
      } catch (error) {
        if (!context.signal.aborted && status) {
          status.textContent = text.handshakeFailed(
            error instanceof Error ? error.message : String(error));
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
        dotNetPluginAvailable = environment.loadedPlugins.some(
          plugin => plugin.id === "quantum.plugin.example");
        if (!context.signal.aborted && environmentStatus) {
          environmentStatus.textContent = text.environmentLoaded(
            environment.loadedPlugins.length,
            dotNetPluginAvailable);
        }
      } catch (error) {
        if (!context.signal.aborted && environmentStatus) {
          environmentStatus.textContent = text.environmentFailed(errorMessage(error));
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

interface ExampleMessages {
  readonly tag: string;
  readonly heading: string;
  readonly interopHeading: string;
  readonly currentView: string;
  readonly readingEnvironment: string;
  readonly waitingForEnvironment: string;
  readonly handshakeAgain: string;
  readonly openDotNet: string;
  readonly backHome: string;
  readonly dotNetUnavailable: string;
  readonly connecting: string;
  handshakeFailed(message: string): string;
  environmentLoaded(count: number, available: boolean): string;
  environmentFailed(message: string): string;
}

function createMessages(languageName: string): ExampleMessages {
  if (languageName.toLowerCase() === "zh") {
    return {
      tag: "WEB 示例",
      heading: "Web 插件示例",
      interopHeading: "与示例插件连接",
      currentView: "当前视图",
      readingEnvironment: "正在检查已安装插件…",
      waitingForEnvironment: "等待连接…",
      handshakeAgain: "重新连接",
      openDotNet: "打开 .NET 示例",
      backHome: "返回概览",
      dotNetUnavailable: "示例插件未安装。",
      connecting: "正在连接示例插件…",
      handshakeFailed: message => `连接失败：${message}`,
      environmentLoaded: (count, available) =>
        `已安装 ${count} 个插件；示例插件${available ? "可用" : "未安装"}。`,
      environmentFailed: message => `检查插件失败：${message}`
    };
  }

  return {
    tag: "WEB EXAMPLE",
    heading: "Web plugin example",
    interopHeading: "Connect to the example plugin",
    currentView: "Current view",
    readingEnvironment: "Checking installed plugins…",
    waitingForEnvironment: "Waiting to connect…",
    handshakeAgain: "Connect again",
    openDotNet: "Open .NET example",
    backHome: "Back to overview",
    dotNetUnavailable: "The example plugin is not installed.",
    connecting: "Connecting to the example plugin…",
    handshakeFailed: message => `Connection failed: ${message}`,
      environmentLoaded: (count, available) =>
        `${count} ${count === 1 ? "plugin" : "plugins"} installed; example plugin ${available ? "available" : "not installed"}.`,
    environmentFailed: message => `Could not check plugins: ${message}`
  };
}
