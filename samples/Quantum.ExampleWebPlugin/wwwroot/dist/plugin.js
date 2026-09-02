// ../../sdk/typescript/dist/index.js
var PluginId = Object.freeze({
  maximumLength: 128,
  of(value) {
    if (typeof value !== "string" || value.trim().length === 0) {
      throw new TypeError("A plugin id is required.");
    }
    const trimmed = value.trim();
    if (!/^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,126}[A-Za-z0-9])?$/.test(trimmed)) {
      throw new TypeError("A plugin id must contain between 1 and 128 ASCII letters, numbers, dots, underscores, or hyphens, and must start and end with a letter or number.");
    }
    const normalized = trimmed.toLowerCase();
    if (normalized === "disabled") {
      throw new TypeError("Plugin id 'disabled' is reserved by the host.");
    }
    return normalized;
  },
  tryParse(value) {
    try {
      return PluginId.of(value);
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
    const leftComponents = parseSemanticVersion(left);
    const rightComponents = parseSemanticVersion(right);
    if (leftComponents === null || rightComponents === null) {
      throw new TypeError("SemanticVersion.compare requires valid Semantic Versioning 2.0.0 values.");
    }
    return compareSemanticVersions(leftComponents, rightComponents);
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
var VersionRange = Object.freeze({
  parse(value) {
    const range = parseVersionRange(value);
    if (range === null) {
      throw new TypeError(`'${String(value)}' is not a valid version range.`);
    }
    return range.normalized;
  },
  tryParse(value) {
    const range = parseVersionRange(value);
    return range === null ? null : range.normalized;
  },
  contains(range, version) {
    const parsedRange = parseVersionRange(range);
    const parsedVersion = parseSemanticVersion(version);
    if (parsedRange === null || parsedVersion === null) {
      throw new TypeError("VersionRange.contains requires valid range and version values.");
    }
    return parsedRange.terms.some((term) => versionRangeTermContains(term, parsedVersion));
  }
});
function parseVersionRange(value) {
  if (typeof value !== "string" || value.trim().length === 0) {
    return null;
  }
  const rawTerms = value.trim().split("|");
  if (rawTerms.some((term) => term.trim().length === 0)) {
    return null;
  }
  const terms = [];
  const normalizedTerms = [];
  for (const rawTerm of rawTerms) {
    const parsed = parseVersionRangeTerm(rawTerm.trim());
    if (parsed === null) {
      return null;
    }
    terms.push(parsed.term);
    normalizedTerms.push(parsed.normalized);
  }
  return Object.freeze({
    normalized: normalizedTerms.join("|"),
    terms: Object.freeze(terms)
  });
}
function parseVersionRangeTerm(term) {
  if (term === "*") {
    return {
      normalized: "(,)",
      term: {
        kind: "interval",
        lowerBound: null,
        includeLowerBound: false,
        upperBound: null,
        includeUpperBound: false
      }
    };
  }
  if (term.startsWith("{") && term.endsWith("}")) {
    const values = term.slice(1, -1).split(",").map((value) => value.trim());
    if (values.length === 0 || values.some((value) => value.length === 0)) {
      return null;
    }
    const versions = values.map(parseSemanticVersion);
    if (versions.some((version) => version === null)) {
      return null;
    }
    return {
      normalized: `{${values.join(",")}}`,
      term: { kind: "set", versions }
    };
  }
  if (term.length < 3 || !(term.startsWith("[") || term.startsWith("(")) || !(term.endsWith("]") || term.endsWith(")"))) {
    return null;
  }
  const bounds = term.slice(1, -1).split(",").map((value) => value.trim());
  if (bounds.length !== 2) {
    return null;
  }
  const lowerValue = bounds[0];
  const upperValue = bounds[1];
  const includeLowerBound = term.startsWith("[");
  const includeUpperBound = term.endsWith("]");
  if (lowerValue.length === 0 && includeLowerBound || upperValue.length === 0 && includeUpperBound) {
    return null;
  }
  const lowerBound = lowerValue.length === 0 ? null : parseSemanticVersion(lowerValue);
  const upperBound = upperValue.length === 0 ? null : parseSemanticVersion(upperValue);
  if (lowerValue.length > 0 && lowerBound === null || upperValue.length > 0 && upperBound === null) {
    return null;
  }
  if (lowerBound !== null && upperBound !== null) {
    const comparison = compareSemanticVersions(lowerBound, upperBound);
    if (comparison > 0 || comparison === 0 && (!includeLowerBound || !includeUpperBound)) {
      return null;
    }
  }
  return {
    normalized: `${term[0]}${lowerValue},${upperValue}${term.at(-1)}`,
    term: {
      kind: "interval",
      lowerBound,
      includeLowerBound,
      upperBound,
      includeUpperBound
    }
  };
}
function versionRangeTermContains(term, version) {
  if (term.kind === "set") {
    return term.versions.some((candidate) => compareSemanticVersions(candidate, version) === 0);
  }
  if (term.lowerBound !== null) {
    const comparison = compareSemanticVersions(version, term.lowerBound);
    if (comparison < 0 || comparison === 0 && !term.includeLowerBound) {
      return false;
    }
  }
  if (term.upperBound !== null) {
    const comparison = compareSemanticVersions(version, term.upperBound);
    if (comparison > 0 || comparison === 0 && !term.includeUpperBound) {
      return false;
    }
  }
  return true;
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
        <p data-host-status>${text.rpcDescription}</p>
        <div class="interop">
          <strong>${text.interopHeading}</strong>
          <p data-handshake-status>${text.waitingForEnvironment}</p>
          <small data-handshake-meta></small>
        </div>
        <div class="actions">
          <button type="button" data-action="handshake">${text.handshakeAgain}</button>
          <button type="button" class="secondary" data-action="dotnet">${text.openDotNet}</button>
          <button type="button" class="secondary" data-action="home">${text.backHome}</button>
        </div>
      </section>
    `;
    const status = context.element.querySelector("[data-handshake-status]");
    const metadata = context.element.querySelector("[data-handshake-meta]");
    const environmentStatus = context.element.querySelector("[data-environment-status]");
    const handshakeButton = context.element.querySelector("[data-action=handshake]");
    let dotNetPluginAvailable = null;
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
        if (metadata) {
          metadata.textContent = "";
        }
        const result = await context.rpc.invoke(
          "sample.handshake",
          {},
          {},
          { signal: context.signal }
        );
        if (!result.isSuccess) {
          throw new Error(`${result.errorCode}: ${result.message}`);
        }
        const handshake = result.value;
        if (status) {
          status.textContent = handshake.message;
        }
        if (metadata) {
          const startedAt = new Date(handshake.dotNetStartedAt).toLocaleTimeString(context.locale.cultureName);
          metadata.textContent = text.handshakeMetadata(
            handshake.sequence,
            startedAt,
            handshake.webPluginAvailable
          );
        }
      } catch (error) {
        if (!context.signal.aborted && status) {
          status.textContent = text.handshakeFailed(
            error instanceof Error ? error.message : String(error)
          );
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
          environmentStatus.textContent = text.environmentLoaded(
            environment.loadedPlugins.length,
            dotNetPluginAvailable
          );
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
function createMessages(languageName) {
  if (languageName.toLowerCase() === "zh") {
    return {
      tag: "ISOLATED TYPESCRIPT PLUGIN",
      heading: "Quantum Web Runtime",
      interopHeading: "TypeScript \u2192 Quantum RPC \u2192 .NET Handler",
      currentView: "\u5F53\u524D\u89C6\u56FE",
      readingEnvironment: "\u6B63\u5728\u8BFB\u53D6 Quantum \u63D2\u4EF6\u73AF\u5883\u2026",
      rpcDescription: "\u8C03\u7528\u53EA\u643A\u5E26 RPC \u540D\u79F0\u4E0E JSON Payload\uFF0C\u4E0D\u4F9D\u8D56\u4EFB\u4F55 CLR \u7C7B\u578B\u3002",
      waitingForEnvironment: "\u7B49\u5F85\u73AF\u5883\u67E5\u8BE2\u5B8C\u6210\u2026",
      handshakeAgain: "\u518D\u6B21\u63E1\u624B",
      openDotNet: "\u6253\u5F00 .NET \u793A\u4F8B",
      backHome: "\u8FD4\u56DE\u6982\u89C8",
      dotNetUnavailable: ".NET \u793A\u4F8B\u672A\u5B89\u88C5\uFF0CWeb \u63D2\u4EF6\u7EE7\u7EED\u72EC\u7ACB\u8FD0\u884C\u3002",
      connecting: "\u6B63\u5728\u8FDE\u63A5 quantum.plugin.example\u2026",
      handshakeMetadata: (sequence, startedAt, available) => `\u8C03\u7528\u5E8F\u53F7 ${sequence} \xB7 .NET runtime \u542F\u52A8\u4E8E ${startedAt} \xB7 Web \u63D2\u4EF6 ${available ? "LOADED" : "STANDALONE"}`,
      handshakeFailed: (message) => `\u63E1\u624B\u5931\u8D25\uFF1A${message}`,
      environmentLoaded: (count, available) => `\u5DF2\u52A0\u8F7D ${count} \u4E2A\u63D2\u4EF6\uFF1B.NET \u793A\u4F8B\uFF1A${available ? "LOADED" : "NOT LOADED"}\u3002`,
      environmentFailed: (message) => `\u73AF\u5883\u67E5\u8BE2\u5931\u8D25\uFF1A${message}`
    };
  }
  return {
    tag: "ISOLATED TYPESCRIPT PLUGIN",
    heading: "Quantum Web Runtime",
    interopHeading: "TypeScript \u2192 Quantum RPC \u2192 .NET Handler",
    currentView: "Current view",
    readingEnvironment: "Reading the Quantum plugin environment\u2026",
    rpcDescription: "Calls carry only an RPC name and JSON payload, with no dependency on CLR types.",
    waitingForEnvironment: "Waiting for the environment query\u2026",
    handshakeAgain: "Handshake again",
    openDotNet: "Open .NET example",
    backHome: "Back to overview",
    dotNetUnavailable: "The .NET example is not installed; the Web plugin will continue standalone.",
    connecting: "Connecting to quantum.plugin.example\u2026",
    handshakeMetadata: (sequence, startedAt, available) => `Call ${sequence} \xB7 .NET runtime started at ${startedAt} \xB7 Web plugin ${available ? "LOADED" : "STANDALONE"}`,
    handshakeFailed: (message) => `Handshake failed: ${message}`,
    environmentLoaded: (count, available) => `${count} ${count === 1 ? "plugin" : "plugins"} loaded; .NET example: ${available ? "LOADED" : "NOT LOADED"}.`,
    environmentFailed: (message) => `Environment query failed: ${message}`
  };
}
export {
  index_default as default
};
