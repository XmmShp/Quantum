window.quantum = window.quantum || {};

window.quantum.plugins = {
  nodes: [],
  records: new Map(),
  bindings: new Map(),
  framesByWindow: new Map(),
  dotNetReference: null,
  parkingElement: null,
  frameBootstrapPromise: null,

  replaceContributions(contributions) {
    const items = Array.isArray(contributions)
      ? contributions
      : contributions
        ? [contributions]
        : [];

    for (const node of this.nodes) {
      node.remove();
    }
    this.nodes = [];

    for (const contribution of items) {
      this.appendFragments(document.head, contribution.head ?? [], contribution.pluginId, "head");
      this.appendFragments(document.body, contribution.postBlazor ?? [], contribution.pluginId, "post-blazor");
    }
  },

  appendFragments(target, fragments, pluginId, location) {
    for (const fragment of fragments) {
      const template = document.createElement("template");
      template.innerHTML = fragment.trim();

      for (const sourceNode of [...template.content.childNodes]) {
        const node = sourceNode.nodeName === "SCRIPT"
          ? this.cloneScript(sourceNode)
          : sourceNode.cloneNode(true);
        if (node.nodeType === Node.ELEMENT_NODE) {
          node.dataset.quantumPlugin = pluginId;
          node.dataset.quantumLocation = location;
        }
        target.appendChild(node);
        this.nodes.push(node);
      }
    }
  },

  cloneScript(source) {
    const script = document.createElement("script");
    for (const attribute of source.attributes) {
      script.setAttribute(attribute.name, attribute.value);
    }
    script.textContent = source.textContent;
    return script;
  },

  async reconcileWebPlugins(descriptors, dotNetReference) {
    this.dotNetReference = dotNetReference;
    this.ensureMessageListener();
    const desired = new Map((descriptors ?? []).map(descriptor => [descriptor.pluginId, descriptor]));
    const errors = [];

    for (const [pluginId, record] of [...this.records].reverse()) {
      if (!desired.has(pluginId)) {
        this.records.delete(pluginId);
        await this.disposeRecord(record);
      }
    }

    for (const descriptor of desired.values()) {
      const current = this.records.get(descriptor.pluginId);
      if (current?.runtimeId === descriptor.runtimeId) {
        continue;
      }

      let candidate = null;
      try {
        candidate = await this.createRecord(descriptor);
        const binding = this.bindings.get(descriptor.pluginId);
        if (binding) {
          if (current) {
            await this.unmountRecord(current);
          }
          await this.mountRecord(candidate, binding);
        }

        this.records.set(descriptor.pluginId, candidate);
        if (current) {
          await this.disposeRecord(current);
        }
      } catch (error) {
        if (candidate) {
          await this.disposeRecord(candidate);
        }

        let fallbackError = null;
        const fallbackBinding = this.bindings.get(descriptor.pluginId);
        if (current && fallbackBinding && !current.disposed && !current.mounted) {
          try {
            await this.mountRecord(current, fallbackBinding);
          } catch (fallbackFailure) {
            fallbackError = this.errorMessage(fallbackFailure);
          }
        }

        const suffix = fallbackError ? `; previous runtime restore failed: ${fallbackError}` : "";
        errors.push(`${descriptor.pluginId}: ${this.errorMessage(error)}${suffix}`);
      }
    }

    if (errors.length > 0) {
      throw new Error(errors.join("; "));
    }
  },

  async attachWebPlugin(pluginId, hostElement, route) {
    const binding = { hostElement, route };
    this.bindings.set(pluginId, binding);
    const record = this.records.get(pluginId);
    if (record) {
      try {
        await this.mountRecord(record, binding);
        return true;
      } catch (error) {
        if (this.bindings.get(pluginId) === binding) {
          this.bindings.delete(pluginId);
        }
        if (!this.bindings.has(pluginId)) {
          this.parkRecord(record);
        }
        throw error;
      }
    }

    return false;
  },

  async detachWebPlugin(pluginId, hostElement) {
    const binding = this.bindings.get(pluginId);
    if (!binding || binding.hostElement !== hostElement) {
      return;
    }

    this.bindings.delete(pluginId);
    const record = this.records.get(pluginId);
    if (record) {
      await this.unmountRecord(record);
      if (!this.bindings.has(pluginId)) {
        this.parkRecord(record);
      }
    }
  },

  async shutdownWebPlugins() {
    this.bindings.clear();
    for (const record of [...this.records.values()].reverse()) {
      await this.disposeRecord(record);
    }
    this.records.clear();
    this.dotNetReference = null;
  },

  async createRecord(descriptor) {
    const bootstrap = await this.loadFrameBootstrap();
    const runtimeMarker = "__QUANTUM_RUNTIME_ID__";
    if (!bootstrap.includes(JSON.stringify(runtimeMarker))) {
      throw new Error("Web plugin frame bootstrap does not contain the runtime marker.");
    }

    const frame = document.createElement("iframe");
    frame.className = "quantum-web-plugin-frame";
    frame.title = descriptor.pluginId;
    frame.setAttribute("sandbox", "allow-scripts");
    frame.setAttribute("referrerpolicy", "no-referrer");
    const frameDocument = bootstrap.replace(
      JSON.stringify(runtimeMarker),
      JSON.stringify(descriptor.runtimeId));
    const bootstrapUrl = URL.createObjectURL(new Blob([frameDocument], { type: "text/html" }));
    frame.src = bootstrapUrl;

    const record = {
      pluginId: descriptor.pluginId,
      runtimeId: descriptor.runtimeId,
      descriptor,
      frame,
      bootstrapUrl,
      signals: new Map(),
      lifecycleOperation: null,
      placementCleanup: null,
      mounted: false,
      disposed: false
    };
    const ready = this.waitFor(record, "ready", 10000);
    this.parkRecord(record);
    this.framesByWindow.set(frame.contentWindow, record);

    try {
      await ready;
      URL.revokeObjectURL(record.bootstrapUrl);
      record.bootstrapUrl = null;
      const response = await fetch(descriptor.entryUrl, { cache: "no-store" });
      if (!response.ok) {
        throw new Error(`could not load entry module (${response.status})`);
      }

      const source = await response.text();
      const activated = this.waitFor(record, "activated", 30000);
      this.post(record, "initialize", {
        source,
        metadata: {
          pluginId: descriptor.pluginId,
          version: descriptor.version,
          runtimeId: descriptor.runtimeId,
          assetBaseUrl: descriptor.assetBaseUrl,
          permissions: descriptor.permissions ?? []
        }
      });
      await activated;
      return record;
    } catch (error) {
      await this.disposeRecord(record);
      throw error;
    }
  },

  async loadFrameBootstrap() {
    this.frameBootstrapPromise ??= fetch(
      new URL("webPluginFrame.html", document.baseURI).href,
      { cache: "no-store" })
      .then(async response => {
        if (!response.ok) {
          throw new Error(`could not load Web plugin frame bootstrap (${response.status})`);
        }
        return response.text();
      });
    return this.frameBootstrapPromise;
  },

  async mountRecord(record, binding) {
    return this.runLifecycleOperation(record, () => this.mountRecordCore(record, binding));
  },

  async mountRecordCore(record, binding) {
    if (record.disposed) {
      throw new Error(`Web plugin '${record.pluginId}' has been disposed.`);
    }

    if (record.mounted) {
      await this.unmountRecordCore(record);
    }

    this.placeRecord(record, binding.hostElement);
    const mounted = this.waitFor(record, "mounted", 30000);
    this.post(record, "mount", { route: binding.route });
    await mounted;
    record.mounted = true;
  },

  async unmountRecord(record) {
    return this.runLifecycleOperation(record, () => this.unmountRecordCore(record));
  },

  async unmountRecordCore(record) {
    if (!record.mounted || record.disposed) {
      return;
    }

    try {
      const unmounted = this.waitFor(record, "unmounted", 10000);
      this.post(record, "unmount");
      await unmounted;
    } finally {
      record.mounted = false;
    }
  },

  async disposeRecord(record) {
    return this.runLifecycleOperation(record, () => this.disposeRecordCore(record));
  },

  async disposeRecordCore(record) {
    if (record.disposed) {
      return;
    }

    record.disposed = true;
    try {
      if (record.mounted) {
        record.disposed = false;
        await this.unmountRecordCore(record);
        record.disposed = true;
      }

      if (record.frame.contentWindow) {
        const deactivated = this.waitFor(record, "deactivated", 10000);
        this.post(record, "deactivate");
        await deactivated;
      }
    } catch (error) {
      console.warn(`Could not gracefully dispose Web plugin '${record.pluginId}'.`, error);
    } finally {
      for (const signal of record.signals.values()) {
        clearTimeout(signal.timer);
        signal.reject(new Error(`Web plugin '${record.pluginId}' was disposed.`));
      }
      record.signals.clear();
      this.framesByWindow.delete(record.frame.contentWindow);
      if (record.bootstrapUrl) {
        URL.revokeObjectURL(record.bootstrapUrl);
        record.bootstrapUrl = null;
      }
      record.placementCleanup?.();
      record.placementCleanup = null;
      record.frame.remove();
    }
  },

  async runLifecycleOperation(record, callback) {
    const previous = record.lifecycleOperation ?? Promise.resolve();
    const operation = previous
      .catch(() => undefined)
      .then(callback);
    record.lifecycleOperation = operation;
    try {
      return await operation;
    } finally {
      if (record.lifecycleOperation === operation) {
        record.lifecycleOperation = null;
      }
    }
  },

  waitFor(record, type, timeoutMilliseconds) {
    const previous = record.signals.get(type);
    if (previous) {
      clearTimeout(previous.timer);
      previous.reject(new Error(`Signal '${type}' was superseded.`));
    }

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        record.signals.delete(type);
        reject(new Error(`Timed out waiting for '${type}'.`));
      }, timeoutMilliseconds);
      record.signals.set(type, { resolve, reject, timer });
    });
  },

  post(record, type, payload = {}) {
    record.frame.contentWindow?.postMessage({
      channel: "quantum-web-plugin",
      runtimeId: record.runtimeId,
      type,
      ...payload
    }, "*");
  },

  ensureMessageListener() {
    if (this.messageListenerInstalled) {
      return;
    }

    this.messageListenerInstalled = true;
    window.addEventListener("message", event => this.handleMessage(event));
  },

  handleMessage(event) {
    const record = this.framesByWindow.get(event.source);
    const message = event.data;
    if (!record
      || !message
      || message.channel !== "quantum-web-plugin"
      || message.runtimeId !== record.runtimeId) {
      return;
    }

    if (message.type === "rpc") {
      void this.handleRpc(record, message);
      return;
    }

    if (message.type === "rpc-cancel") {
      void this.dotNetReference?.invokeMethodAsync(
        "CancelAsync",
        record.pluginId,
        record.runtimeId,
        message.requestId);
      return;
    }

    if (message.type === "error") {
      const error = new Error(`${message.phase ?? "runtime"}: ${message.message ?? "unknown error"}`);
      for (const signal of record.signals.values()) {
        clearTimeout(signal.timer);
        signal.reject(error);
      }
      record.signals.clear();
      return;
    }

    const signal = record.signals.get(message.type);
    if (signal) {
      clearTimeout(signal.timer);
      record.signals.delete(message.type);
      signal.resolve(message.result);
    }
  },

  async handleRpc(record, message) {
    try {
      if (!this.dotNetReference) {
        throw new Error("The .NET interop bridge is not connected.");
      }

      const result = await this.dotNetReference.invokeMethodAsync(
        "InvokeAsync",
        record.pluginId,
        record.runtimeId,
        message.requestId,
        message.capability,
        message.method,
        message.arguments ?? {});
      this.post(record, "rpc-result", { requestId: message.requestId, result });
    } catch (error) {
      this.post(record, "rpc-error", {
        requestId: message.requestId,
        message: this.errorMessage(error)
      });
    }
  },

  parkFrame(frame) {
    frame.hidden = true;
    frame.style.display = "none";
    if (!frame.isConnected) {
      document.body.appendChild(frame);
    }
  },

  parkRecord(record) {
    record.placementCleanup?.();
    record.placementCleanup = null;
    this.parkFrame(record.frame);
  },

  placeRecord(record, hostElement) {
    record.placementCleanup?.();
    const frame = record.frame;
    const updatePosition = () => {
      const bounds = hostElement.getBoundingClientRect();
      frame.style.left = `${bounds.left}px`;
      frame.style.top = `${bounds.top}px`;
      frame.style.width = `${Math.max(0, bounds.width)}px`;
      frame.style.height = `${Math.max(0, bounds.height)}px`;
    };
    const resizeObserver = typeof ResizeObserver === "function"
      ? new ResizeObserver(updatePosition)
      : null;

    frame.hidden = false;
    frame.style.display = "block";
    frame.style.position = "fixed";
    frame.style.zIndex = "10";
    updatePosition();
    resizeObserver?.observe(hostElement);
    window.addEventListener("resize", updatePosition);
    document.addEventListener("scroll", updatePosition, true);
    record.placementCleanup = () => {
      resizeObserver?.disconnect();
      window.removeEventListener("resize", updatePosition);
      document.removeEventListener("scroll", updatePosition, true);
    };
  },

  errorMessage(error) {
    return error instanceof Error ? error.message : String(error);
  }
};
