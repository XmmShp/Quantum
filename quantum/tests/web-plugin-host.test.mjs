import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

globalThis.window = {};
await import("../src/Quantum/wwwroot/pluginHost.js");

const hostDefinition = window.quantum.plugins;
const dropTargetDefinition = window.quantum.pluginInstallerDropTarget;

function createHost(overrides = {}) {
  return Object.assign({}, hostDefinition, {
    nodes: [],
    records: new Map(),
    bindings: new Map(),
    framesByWindow: new Map(),
    dotNetReference: null,
    parkingElement: null,
    messageListenerInstalled: false,
    reconcileOperation: null
  }, overrides);
}

test("global plugin installer covers the window for file drags and removes listeners", () => {
  const listeners = new Map();
  const removed = [];
  const originalWindow = globalThis.window;
  globalThis.window = {
    innerWidth: 1280,
    innerHeight: 720,
    addEventListener(name, listener) { listeners.set(name, listener); },
    removeEventListener(name, listener) { removed.push([name, listener]); }
  };
  const classes = new Set();
  const attributes = new Map();
  const element = {
    classList: {
      add(value) { classes.add(value); },
      remove(value) { classes.delete(value); }
    },
    setAttribute(name, value) { attributes.set(name, value); },
    contains(target) { return target === this; }
  };
  const dropTarget = Object.assign({}, dropTargetDefinition, { registrations: new Map() });

  try {
    dropTarget.initialize("installer", element);
    let prevented = false;
    listeners.get("dragenter")({
      dataTransfer: { types: ["Files"] },
      preventDefault() { prevented = true; }
    });

    assert.equal(prevented, true);
    assert.equal(classes.has("visible"), true);
    assert.equal(attributes.get("aria-hidden"), "false");

    listeners.get("drop")({
      dataTransfer: { types: ["Files"] },
      target: element,
      preventDefault() { throw new Error("the file input drop must keep its native default"); }
    });
    assert.equal(classes.has("visible"), false);
    assert.equal(attributes.get("aria-hidden"), "true");

    dropTarget.dispose("installer");
    assert.equal(dropTarget.registrations.size, 0);
    assert.equal(removed.length, 5);
  } finally {
    globalThis.window = originalWindow;
  }
});

test("concurrent Web plugin reconciliations are serialized and converge to the latest runtime", async () => {
  const pluginId = "quantum.plugin.web";
  let releaseFirst;
  let firstStarted;
  const firstStartedPromise = new Promise(resolve => { firstStarted = resolve; });
  const events = [];
  const host = createHost({
    ensureMessageListener() {},
    async createRecord(descriptor) {
      events.push(`create:${descriptor.runtimeId}`);
      if (descriptor.runtimeId === "first") {
        firstStarted();
        await new Promise(resolve => { releaseFirst = resolve; });
      }
      return {
        pluginId,
        runtimeId: descriptor.runtimeId,
        mounted: false,
        disposed: false
      };
    },
    async disposeRecord(record) {
      events.push(`dispose:${record.runtimeId}`);
      record.disposed = true;
    }
  });

  const first = host.reconcileWebPlugins([{ pluginId, runtimeId: "first" }], {});
  await firstStartedPromise;
  const second = host.reconcileWebPlugins([{ pluginId, runtimeId: "second" }], {});
  await Promise.resolve();

  assert.deepEqual(events, ["create:first"]);
  releaseFirst();
  await Promise.all([first, second]);

  assert.deepEqual(events, ["create:first", "create:second", "dispose:first"]);
  assert.equal(host.records.get(pluginId).runtimeId, "second");
});

test("failed replacement disposes the candidate and restores the previous mounted runtime", async () => {
  const current = { pluginId: "quantum.plugin.web", runtimeId: "old", mounted: true, disposed: false };
  const candidate = { pluginId: "quantum.plugin.web", runtimeId: "new", mounted: false, disposed: false };
  const binding = { hostElement: {}, route: { path: "/plugins/web", view: "main" } };
  const host = createHost({
    ensureMessageListener() {},
    async createRecord() { return candidate; },
    async unmountRecord(record) { record.mounted = false; },
    async mountRecord(record) {
      if (record === candidate) {
        throw new Error("mount failed");
      }
      record.mounted = true;
    },
    async disposeRecord(record) { record.disposed = true; }
  });
  host.records.set(current.pluginId, current);
  host.bindings.set(current.pluginId, binding);

  await assert.rejects(
    host.reconcileWebPlugins([
      { pluginId: current.pluginId, runtimeId: candidate.runtimeId }
    ], {}),
    /mount failed/);

  assert.equal(candidate.disposed, true);
  assert.equal(current.mounted, true);
  assert.equal(host.records.get(current.pluginId), current);
});

test("failed attach removes its binding and parks the frame", async () => {
  const record = {
    pluginId: "quantum.plugin.web",
    runtimeId: "runtime",
    frame: {},
    mounted: false,
    disposed: false
  };
  let parked = false;
  const host = createHost({
    async mountRecord() { throw new Error("mount failed"); },
    parkFrame(frame) { parked = frame === record.frame; }
  });
  host.records.set(record.pluginId, record);

  await assert.rejects(
    host.attachWebPlugin(record.pluginId, {}, { path: "/plugins/web", view: "main" }),
    /mount failed/);

  assert.equal(host.bindings.has(record.pluginId), false);
  assert.equal(parked, true);
});

test("a stale detach cannot park a newer binding", async () => {
  const pluginId = "quantum.plugin.web";
  const oldHost = {};
  const newHost = {};
  const record = { pluginId, frame: {} };
  let releaseUnmount;
  let parked = false;
  const host = createHost({
    unmountRecord() {
      return new Promise(resolve => { releaseUnmount = resolve; });
    },
    parkRecord() { parked = true; }
  });
  host.records.set(pluginId, record);
  host.bindings.set(pluginId, { hostElement: oldHost, route: {} });

  const detaching = host.detachWebPlugin(pluginId, oldHost);
  host.bindings.set(pluginId, { hostElement: newHost, route: {} });
  releaseUnmount();
  await detaching;

  assert.equal(parked, false);
  assert.equal(host.bindings.get(pluginId).hostElement, newHost);
});

test("concurrent mounts for one runtime are serialized", async () => {
  const frame = {};
  const record = {
    pluginId: "quantum.plugin.web",
    runtimeId: "runtime",
    frame,
    mounted: false,
    disposed: false,
    lifecycleOperation: null
  };
  const replacements = [];
  const signals = [];
  let waiting = false;
  const host = createHost({
    placeRecord(_record, hostElement) {
      hostElement.replaceChildren(frame);
    },
    waitFor(_record, type) {
      assert.equal(waiting, false, `overlapping '${type}' lifecycle signal`);
      waiting = true;
      signals.push(type);
      return new Promise(resolve => setTimeout(() => {
        waiting = false;
        resolve();
      }, 1));
    },
    post() {}
  });
  const firstBinding = {
    hostElement: { replaceChildren(value) { replacements.push(["first", value]); } },
    route: { path: "/plugins/web", view: "main" }
  };
  const secondBinding = {
    hostElement: { replaceChildren(value) { replacements.push(["second", value]); } },
    route: { path: "/plugins/web", view: "main" }
  };

  await Promise.all([
    host.mountRecord(record, firstBinding),
    host.mountRecord(record, secondBinding)
  ]);

  assert.deepEqual(signals, ["mounted", "unmounted", "mounted"]);
  assert.deepEqual(replacements, [["first", frame], ["second", frame]]);
  assert.equal(record.mounted, true);
});

test("mounting keeps the live iframe attached to the document portal", async () => {
  const listeners = [];
  const frame = {
    hidden: true,
    isConnected: true,
    style: {}
  };
  const record = {
    frame,
    placementCleanup: null
  };
  const hostElement = {
    getBoundingClientRect() {
      return { left: 320, top: 80, width: 900, height: 640 };
    }
  };
  const originalDocument = globalThis.document;
  const originalWindow = globalThis.window;
  globalThis.document = {
    addEventListener(...args) { listeners.push(["document-add", ...args]); },
    removeEventListener(...args) { listeners.push(["document-remove", ...args]); },
    body: { appendChild() { throw new Error("the connected iframe must not be reparented"); } }
  };
  globalThis.window = {
    addEventListener(...args) { listeners.push(["window-add", ...args]); },
    removeEventListener(...args) { listeners.push(["window-remove", ...args]); }
  };

  try {
    const host = createHost();
    host.placeRecord(record, hostElement);

    assert.equal(frame.hidden, false);
    assert.equal(frame.style.position, "fixed");
    assert.equal(frame.style.left, "320px");
    assert.equal(frame.style.top, "80px");
    assert.equal(frame.style.width, "900px");
    assert.equal(frame.style.height, "640px");

    host.parkRecord(record);
    assert.equal(frame.hidden, true);
    assert.equal(frame.style.display, "none");
    assert.deepEqual(listeners.map(([name]) => name), [
      "window-add",
      "document-add",
      "window-remove",
      "document-remove"
    ]);
  } finally {
    globalThis.document = originalDocument;
    globalThis.window = originalWindow;
  }
});

test("a Web runtime frame uses a Blob document instead of navigating the app scheme", async () => {
  const originalDocument = globalThis.document;
  const originalFetch = globalThis.fetch;
  const frame = {
    contentWindow: {},
    setAttribute() {},
    remove() {}
  };
  globalThis.document = {
    createElement(name) {
      assert.equal(name, "iframe");
      return frame;
    }
  };
  globalThis.fetch = async () => ({
    ok: true,
    text: async () => "export default {};"
  });

  try {
    const posted = [];
    const host = createHost({
      async loadFrameBootstrap() {
        return '<script>const runtimeId = "__QUANTUM_RUNTIME_ID__";<\/script>';
      },
      waitFor() { return Promise.resolve(); },
      parkFrame() {},
      post(_record, type) { posted.push(type); }
    });
    const descriptor = {
      pluginId: "quantum.plugin.web",
      runtimeId: "runtime-123",
      version: "1.0.0",
      entryUrl: "app://0.0.0.1/_content/quantum.plugin.web/plugin.js",
      assetBaseUrl: "app://0.0.0.1/_content/quantum.plugin.web/"
    };

    const record = await host.createRecord(descriptor);

    assert.equal(record.frame, frame);
    assert.match(frame.src, /^blob:/);
    assert.equal("srcdoc" in frame, false);
    assert.equal(record.bootstrapUrl, null);
    assert.deepEqual(posted, ["initialize"]);
  } finally {
    globalThis.document = originalDocument;
    globalThis.fetch = originalFetch;
  }
});

test("messages must match both the iframe window and runtime id", () => {
  const frameWindow = {};
  const record = { pluginId: "quantum.plugin.web", runtimeId: "current" };
  let calls = 0;
  const host = createHost({
    handleRpc() {
      calls++;
      return Promise.resolve();
    }
  });
  host.framesByWindow.set(frameWindow, record);

  host.handleMessage({
    source: frameWindow,
    data: { channel: "quantum-web-plugin", runtimeId: "stale", type: "rpc" }
  });
  host.handleMessage({
    source: {},
    data: { channel: "quantum-web-plugin", runtimeId: "current", type: "rpc" }
  });
  host.handleMessage({
    source: frameWindow,
    data: { channel: "quantum-web-plugin", runtimeId: "current", type: "rpc" }
  });

  assert.equal(calls, 1);
});

test("a file drag entering a Web plugin iframe opens the host drop target", () => {
  const frameWindow = {};
  const record = { pluginId: "quantum.plugin.web", runtimeId: "current" };
  const host = createHost();
  host.framesByWindow.set(frameWindow, record);
  const originalDropTarget = window.quantum.pluginInstallerDropTarget;
  let calls = 0;
  window.quantum.pluginInstallerDropTarget = {
    showAll() { calls++; }
  };

  try {
    host.handleMessage({
      source: frameWindow,
      data: {
        channel: "quantum-web-plugin",
        runtimeId: "current",
        type: "host-file-drag-enter"
      }
    });

    assert.equal(calls, 1);
  } finally {
    window.quantum.pluginInstallerDropTarget = originalDropTarget;
  }
});

test("EventBus delivery reaches an activating iframe and waits for acknowledgement", async () => {
  const frameWindow = {};
  const record = {
    pluginId: "quantum.plugin.web",
    runtimeId: "runtime",
    frame: { contentWindow: frameWindow },
    disposed: false,
    eventDeliveries: new Map()
  };
  let posted;
  const host = createHost({
    post(_record, type, payload) {
      posted = { type, ...payload };
    }
  });
  host.framesByWindow.set(frameWindow, record);

  const delivery = host.dispatchEvent(
    record.pluginId,
    record.runtimeId,
    "subscription-1",
    { topic: "devices.status", payload: { state: "ready" } });
  assert.equal(posted.type, "eventbus-event");
  assert.equal(posted.subscriptionId, "subscription-1");

  host.handleMessage({
    source: frameWindow,
    data: {
      channel: "quantum-web-plugin",
      runtimeId: record.runtimeId,
      type: "eventbus-result",
      deliveryId: posted.deliveryId
    }
  });

  await delivery;
  assert.equal(record.eventDeliveries.size, 0);
});

test("disposing a Web runtime releases its Host EventBus", async () => {
  const calls = [];
  const frame = {
    contentWindow: null,
    remove() { calls.push("frame-removed"); }
  };
  const record = {
    pluginId: "quantum.plugin.web",
    runtimeId: "runtime",
    frame,
    bootstrapUrl: null,
    signals: new Map(),
    eventDeliveries: new Map(),
    placementCleanup: null,
    mounted: false,
    disposed: false
  };
  const host = createHost({
    dotNetReference: {
      async invokeMethodAsync(method, pluginId, runtimeId) {
        calls.push([method, pluginId, runtimeId]);
      }
    }
  });

  await host.disposeRecordCore(record);

  assert.deepEqual(calls, [
    ["ReleaseRuntimeAsync", record.pluginId, record.runtimeId],
    "frame-removed"
  ]);
});

test("the iframe bootstrap script parses as JavaScript", async () => {
  const html = await readFile(
    new URL("../src/Quantum/wwwroot/webPluginFrame.html", import.meta.url),
    "utf8");
  const script = html.match(/<script>([\s\S]*?)<\/script>/)?.[1];
  assert.ok(script, "bootstrap script was not found");
  assert.doesNotThrow(() => new Function(script));
});
