// node_modules/@io7/quantum-plugin-sdk/dist/index.js
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
    const status = context.element.querySelector("[data-handshake-status]");
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
      tag: "WEB \u793A\u4F8B",
      heading: "Web \u63D2\u4EF6\u793A\u4F8B",
      interopHeading: "\u4E0E\u793A\u4F8B\u63D2\u4EF6\u8FDE\u63A5",
      currentView: "\u5F53\u524D\u89C6\u56FE",
      readingEnvironment: "\u6B63\u5728\u68C0\u67E5\u5DF2\u5B89\u88C5\u63D2\u4EF6\u2026",
      waitingForEnvironment: "\u7B49\u5F85\u8FDE\u63A5\u2026",
      handshakeAgain: "\u91CD\u65B0\u8FDE\u63A5",
      openDotNet: "\u6253\u5F00 .NET \u793A\u4F8B",
      backHome: "\u8FD4\u56DE\u6982\u89C8",
      dotNetUnavailable: "\u793A\u4F8B\u63D2\u4EF6\u672A\u5B89\u88C5\u3002",
      connecting: "\u6B63\u5728\u8FDE\u63A5\u793A\u4F8B\u63D2\u4EF6\u2026",
      handshakeFailed: (message) => `\u8FDE\u63A5\u5931\u8D25\uFF1A${message}`,
      environmentLoaded: (count, available) => `\u5DF2\u5B89\u88C5 ${count} \u4E2A\u63D2\u4EF6\uFF1B\u793A\u4F8B\u63D2\u4EF6${available ? "\u53EF\u7528" : "\u672A\u5B89\u88C5"}\u3002`,
      environmentFailed: (message) => `\u68C0\u67E5\u63D2\u4EF6\u5931\u8D25\uFF1A${message}`
    };
  }
  return {
    tag: "WEB EXAMPLE",
    heading: "Web plugin example",
    interopHeading: "Connect to the example plugin",
    currentView: "Current view",
    readingEnvironment: "Checking installed plugins\u2026",
    waitingForEnvironment: "Waiting to connect\u2026",
    handshakeAgain: "Connect again",
    openDotNet: "Open .NET example",
    backHome: "Back to overview",
    dotNetUnavailable: "The example plugin is not installed.",
    connecting: "Connecting to the example plugin\u2026",
    handshakeFailed: (message) => `Connection failed: ${message}`,
    environmentLoaded: (count, available) => `${count} ${count === 1 ? "plugin" : "plugins"} installed; example plugin ${available ? "available" : "not installed"}.`,
    environmentFailed: (message) => `Could not check plugins: ${message}`
  };
}
export {
  index_default as default
};
