// ../../sdk/typescript/dist/index.js
var PluginId = Object.freeze({
  maximumLength: 128,
  of(value) {
    if (typeof value !== "string" || value.trim().length === 0) {
      throw new TypeError("A plugin id is required.");
    }
    const normalized = value.trim().toLowerCase();
    if (!/^[a-z0-9](?:[a-z0-9._-]{0,126}[a-z0-9])?$/.test(normalized)) {
      throw new TypeError("A plugin id must contain between 1 and 128 lowercase ASCII letters, numbers, dots, underscores, or hyphens, and must start and end with a letter or number.");
    }
    if (normalized === "disabled") {
      throw new TypeError("Plugin id 'disabled' is reserved by the host.");
    }
    return normalized;
  },
  tryParse(value) {
    try {
      return this.of(value);
    } catch (error) {
      if (error instanceof TypeError) {
        return null;
      }
      throw error;
    }
  }
});
var SemanticVersion = Object.freeze({
  parse(value) {
    if (parseSemanticVersion(value) === null) {
      throw new TypeError(`'${String(value)}' is not a valid Semantic Versioning 2.0.0 version.`);
    }
    return value;
  },
  tryParse(value) {
    return parseSemanticVersion(value) === null ? null : value;
  },
  components(value) {
    const components = parseSemanticVersion(value);
    if (components === null) {
      throw new TypeError(`'${String(value)}' is not a valid Semantic Versioning 2.0.0 version.`);
    }
    return components;
  },
  compare(left, right) {
    return compareSemanticVersions(this.components(left), this.components(right));
  }
});
function parseSemanticVersion(value) {
  if (typeof value !== "string" || value.length === 0) {
    return null;
  }
  const buildSeparator = value.indexOf("+");
  if (buildSeparator >= 0 && (value.indexOf("+", buildSeparator + 1) >= 0 || buildSeparator === value.length - 1)) {
    return null;
  }
  const versionWithoutBuild = buildSeparator < 0 ? value : value.slice(0, buildSeparator);
  const buildValue = buildSeparator < 0 ? null : value.slice(buildSeparator + 1);
  const preReleaseSeparator = versionWithoutBuild.indexOf("-");
  if (preReleaseSeparator >= 0 && preReleaseSeparator === versionWithoutBuild.length - 1) {
    return null;
  }
  const coreValue = preReleaseSeparator < 0 ? versionWithoutBuild : versionWithoutBuild.slice(0, preReleaseSeparator);
  const preReleaseValue = preReleaseSeparator < 0 ? null : versionWithoutBuild.slice(preReleaseSeparator + 1);
  const coreIdentifiers = coreValue.split(".");
  if (coreIdentifiers.length !== 3 || coreIdentifiers.some((identifier) => !/^(?:0|[1-9][0-9]*)$/.test(identifier))) {
    return null;
  }
  const preReleaseIdentifiers = preReleaseValue === null ? [] : preReleaseValue.split(".");
  const buildMetadataIdentifiers = buildValue === null ? [] : buildValue.split(".");
  const validIdentifier = (identifier) => /^[0-9A-Za-z-]+$/.test(identifier);
  if (preReleaseIdentifiers.some((identifier) => !validIdentifier(identifier) || /^[0-9]+$/.test(identifier) && identifier.length > 1 && identifier.startsWith("0")) || buildMetadataIdentifiers.some((identifier) => !validIdentifier(identifier))) {
    return null;
  }
  return Object.freeze({
    major: BigInt(coreIdentifiers[0]),
    minor: BigInt(coreIdentifiers[1]),
    patch: BigInt(coreIdentifiers[2]),
    preReleaseIdentifiers: Object.freeze(preReleaseIdentifiers),
    buildMetadataIdentifiers: Object.freeze(buildMetadataIdentifiers),
    isPreRelease: preReleaseIdentifiers.length > 0,
    preRelease: preReleaseValue,
    buildMetadata: buildValue
  });
}
function compareSemanticVersions(left, right) {
  for (const [leftNumber, rightNumber] of [
    [left.major, right.major],
    [left.minor, right.minor],
    [left.patch, right.patch]
  ]) {
    if (leftNumber !== rightNumber) {
      return leftNumber < rightNumber ? -1 : 1;
    }
  }
  if (left.preReleaseIdentifiers.length === 0 || right.preReleaseIdentifiers.length === 0) {
    if (left.preReleaseIdentifiers.length === right.preReleaseIdentifiers.length) {
      return 0;
    }
    return left.preReleaseIdentifiers.length === 0 ? 1 : -1;
  }
  const sharedLength = Math.min(left.preReleaseIdentifiers.length, right.preReleaseIdentifiers.length);
  for (let index = 0; index < sharedLength; index++) {
    const leftIdentifier = left.preReleaseIdentifiers[index];
    const rightIdentifier = right.preReleaseIdentifiers[index];
    const leftNumeric = /^[0-9]+$/.test(leftIdentifier);
    const rightNumeric = /^[0-9]+$/.test(rightIdentifier);
    if (leftNumeric !== rightNumeric) {
      return leftNumeric ? -1 : 1;
    }
    if (leftIdentifier !== rightIdentifier) {
      if (leftNumeric) {
        return leftIdentifier.length === rightIdentifier.length ? leftIdentifier < rightIdentifier ? -1 : 1 : leftIdentifier.length < rightIdentifier.length ? -1 : 1;
      }
      return leftIdentifier < rightIdentifier ? -1 : 1;
    }
  }
  return Math.sign(left.preReleaseIdentifiers.length - right.preReleaseIdentifiers.length);
}
var QuantumTopic = Object.freeze({
  of(value) {
    if (typeof value !== "string" || value.length === 0 || value.length > 255) {
      throw new TypeError("A topic must contain between 1 and 255 characters.");
    }
    const match = /^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$/.exec(value);
    if (match?.[0] !== value) {
      throw new TypeError("A topic must match ^[A-Za-z][A-Za-z0-9_-]*(\\.[A-Za-z0-9][A-Za-z0-9_-]*)*$/.");
    }
    return value;
  }
});
function definePlugin(definition) {
  return definition;
}

// src/index.ts
var index_default = definePlugin({
  async activate(context) {
    await context.log.info("TypeScript example activated");
    const topic = QuantumTopic.of("example.web.status");
    const subscription = await context.eventBus.subscribe(topic, (event) => {
      const payload = event.payload;
      return context.log.info(
        `${event.publisher.id} published ${payload.state ?? "unknown"} to ${event.topic}`
      );
    });
    const publisher = context.eventBus.createPublisher(topic);
    await publisher.publish({ state: "activated" });
    return async () => {
      await subscription.dispose();
      await context.log.info("TypeScript example deactivated");
    };
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
    let dotNetPluginAvailable = null;
    const requestHandshake = async () => {
      if (dotNetPluginAvailable === false) {
        if (status) {
          status.textContent = ".NET \u793A\u4F8B\u672A\u5B89\u88C5\uFF0CWeb \u63D2\u4EF6\u7EE7\u7EED\u72EC\u7ACB\u8FD0\u884C\u3002";
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
          metadata.textContent = `\u8C03\u7528\u5E8F\u53F7 ${handshake.sequence} \xB7 .NET runtime \u542F\u52A8\u4E8E ${startedAt} \xB7 Web \u63D2\u4EF6 ${handshake.webPluginAvailable ? "LOADED" : "STANDALONE"}`;
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
        dotNetPluginAvailable = environment.loadedPlugins.some(
          (plugin) => plugin.id === "quantum.plugin.example"
        );
        if (!context.signal.aborted && environmentStatus) {
          environmentStatus.textContent = `\u5DF2\u52A0\u8F7D ${environment.loadedPlugins.length} \u4E2A\u63D2\u4EF6\uFF1B.NET \u793A\u4F8B\uFF1A${dotNetPluginAvailable ? "LOADED" : "NOT LOADED"}\u3002`;
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
