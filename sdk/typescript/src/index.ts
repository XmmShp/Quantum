export type MaybePromise<T> = T | PromiseLike<T>;

export type QuantumCleanup =
  | void
  | (() => MaybePromise<void>)
  | { dispose(): MaybePromise<void> };

export interface QuantumRpcOptions {
  signal?: AbortSignal;
}

export interface QuantumPluginIdentity {
  readonly id: PluginId;
  readonly version: SemanticVersion;
  readonly runtimeId: string;
}

export interface QuantumPluginInfo {
  readonly id: PluginId;
  readonly version: SemanticVersion;
}

declare const pluginIdBrand: unique symbol;

/** A normalized Quantum plugin identifier. */
export type PluginId = string & {
  readonly [pluginIdBrand]: "PluginId";
};

/** Runtime factory and validator for PluginId values. */
export const PluginId = Object.freeze({
  maximumLength: 128,

  of(value: string): PluginId {
    if (typeof value !== "string" || value.trim().length === 0) {
      throw new TypeError("A plugin id is required.");
    }

    const trimmed = value.trim();
    if (!/^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,126}[A-Za-z0-9])?$/.test(trimmed)) {
      throw new TypeError(
        "A plugin id must contain between 1 and 128 ASCII letters, numbers, dots, underscores, or hyphens, and must start and end with a letter or number."
      );
    }
    const normalized = trimmed.toLowerCase();
    if (normalized === "disabled") {
      throw new TypeError("Plugin id 'disabled' is reserved by the host.");
    }

    return normalized as PluginId;
  },

  tryParse(value: string): PluginId | null {
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

declare const semanticVersionBrand: unique symbol;

/** A validated Semantic Versioning 2.0.0 string. */
export type SemanticVersion = string & {
  readonly [semanticVersionBrand]: "SemanticVersion";
};

export interface SemanticVersionComponents {
  readonly major: bigint;
  readonly minor: bigint;
  readonly patch: bigint;
  readonly preReleaseIdentifiers: readonly string[];
  readonly buildMetadataIdentifiers: readonly string[];
  readonly isPreRelease: boolean;
  readonly preRelease: string | null;
  readonly buildMetadata: string | null;
}

/** Parser, component accessor, and precedence comparator for SemanticVersion values. */
export const SemanticVersion = Object.freeze({
  parse(value: string): SemanticVersion {
    if (parseSemanticVersion(value) === null) {
      throw new TypeError(`'${String(value)}' is not a valid Semantic Versioning 2.0.0 version.`);
    }
    return value as SemanticVersion;
  },

  tryParse(value: string): SemanticVersion | null {
    return parseSemanticVersion(value) === null ? null : value as SemanticVersion;
  },

  components(value: SemanticVersion): SemanticVersionComponents {
    const components = parseSemanticVersion(value);
    if (components === null) {
      throw new TypeError(`'${String(value)}' is not a valid Semantic Versioning 2.0.0 version.`);
    }
    return components;
  },

  compare(left: SemanticVersion, right: SemanticVersion): number {
    const leftComponents = parseSemanticVersion(left);
    const rightComponents = parseSemanticVersion(right);
    if (leftComponents === null || rightComponents === null) {
      throw new TypeError("SemanticVersion.compare requires valid Semantic Versioning 2.0.0 values.");
    }
    return compareSemanticVersions(leftComponents, rightComponents);
  }
});

function parseSemanticVersion(value: string): SemanticVersionComponents | null {
  if (typeof value !== "string" || value.length === 0) {
    return null;
  }

  const buildSeparator = value.indexOf("+");
  if (buildSeparator >= 0 && (value.indexOf("+", buildSeparator + 1) >= 0
      || buildSeparator === value.length - 1)) {
    return null;
  }
  const versionWithoutBuild = buildSeparator < 0 ? value : value.slice(0, buildSeparator);
  const buildValue = buildSeparator < 0 ? null : value.slice(buildSeparator + 1);

  const preReleaseSeparator = versionWithoutBuild.indexOf("-");
  if (preReleaseSeparator >= 0 && preReleaseSeparator === versionWithoutBuild.length - 1) {
    return null;
  }
  const coreValue = preReleaseSeparator < 0
    ? versionWithoutBuild
    : versionWithoutBuild.slice(0, preReleaseSeparator);
  const preReleaseValue = preReleaseSeparator < 0
    ? null
    : versionWithoutBuild.slice(preReleaseSeparator + 1);

  const coreIdentifiers = coreValue.split(".");
  if (coreIdentifiers.length !== 3
      || coreIdentifiers.some(identifier => !/^(?:0|[1-9][0-9]*)$/.test(identifier))) {
    return null;
  }

  const preReleaseIdentifiers = preReleaseValue === null ? [] : preReleaseValue.split(".");
  const buildMetadataIdentifiers = buildValue === null ? [] : buildValue.split(".");
  const validIdentifier = (identifier: string): boolean => /^[0-9A-Za-z-]+$/.test(identifier);
  if (preReleaseIdentifiers.some(identifier =>
        !validIdentifier(identifier) || (/^[0-9]+$/.test(identifier)
          && identifier.length > 1 && identifier.startsWith("0")))
      || buildMetadataIdentifiers.some(identifier => !validIdentifier(identifier))) {
    return null;
  }

  return Object.freeze({
    major: BigInt(coreIdentifiers[0]!),
    minor: BigInt(coreIdentifiers[1]!),
    patch: BigInt(coreIdentifiers[2]!),
    preReleaseIdentifiers: Object.freeze(preReleaseIdentifiers),
    buildMetadataIdentifiers: Object.freeze(buildMetadataIdentifiers),
    isPreRelease: preReleaseIdentifiers.length > 0,
    preRelease: preReleaseValue,
    buildMetadata: buildValue
  });
}

function compareSemanticVersions(
  left: SemanticVersionComponents,
  right: SemanticVersionComponents
): number {
  for (const [leftNumber, rightNumber] of [
    [left.major, right.major],
    [left.minor, right.minor],
    [left.patch, right.patch]
  ] as const) {
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

  const sharedLength = Math.min(
    left.preReleaseIdentifiers.length,
    right.preReleaseIdentifiers.length
  );
  for (let index = 0; index < sharedLength; index++) {
    const leftIdentifier = left.preReleaseIdentifiers[index]!;
    const rightIdentifier = right.preReleaseIdentifiers[index]!;
    const leftNumeric = /^[0-9]+$/.test(leftIdentifier);
    const rightNumeric = /^[0-9]+$/.test(rightIdentifier);
    if (leftNumeric !== rightNumeric) {
      return leftNumeric ? -1 : 1;
    }
    if (leftIdentifier !== rightIdentifier) {
      if (leftNumeric) {
        return leftIdentifier.length === rightIdentifier.length
          ? leftIdentifier < rightIdentifier ? -1 : 1
          : leftIdentifier.length < rightIdentifier.length ? -1 : 1;
      }
      return leftIdentifier < rightIdentifier ? -1 : 1;
    }
  }

  return Math.sign(left.preReleaseIdentifiers.length - right.preReleaseIdentifiers.length);
}

declare const quantumTopicBrand: unique symbol;

/** A validated, dot-delimited EventBus topic. */
export type QuantumTopic = string & {
  readonly [quantumTopicBrand]: "QuantumTopic";
};

/** Runtime factory and validator for QuantumTopic values. */
export const QuantumTopic = Object.freeze({
  of(value: string): QuantumTopic {
    if (typeof value !== "string" || value.length === 0 || value.length > 255) {
      throw new TypeError("A topic must contain between 1 and 255 characters.");
    }
    const match = /^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$/.exec(value);
    if (match?.[0] !== value) {
      throw new TypeError(
        "A topic must match ^[A-Za-z][A-Za-z0-9_-]*(\\.[A-Za-z0-9][A-Za-z0-9_-]*)*$/."
      );
    }
    return value as QuantumTopic;
  }
});

export interface QuantumEvent {
  readonly id: string;
  readonly topic: QuantumTopic;
  readonly payload: unknown;
  readonly publisher: QuantumPluginInfo;
  /** ISO-8601 timestamp assigned by the Host. */
  readonly publishedAt: string;
}

export interface QuantumEventPublisher<TMessage> {
  readonly topic: QuantumTopic;
  publish(message: TMessage, options?: QuantumRpcOptions): Promise<void>;
}

export interface QuantumEventSubscription {
  readonly topic: QuantumTopic;
  dispose(): Promise<void>;
}

export type QuantumEventHandler = (event: QuantumEvent) => MaybePromise<void>;

export interface QuantumEventBus {
  createPublisher<TMessage>(topic: QuantumTopic): QuantumEventPublisher<TMessage>;
  subscribe(
    topic: QuantumTopic,
    handler: QuantumEventHandler,
    options?: QuantumRpcOptions
  ): Promise<QuantumEventSubscription>;
}

export interface QuantumPluginIntegration {
  readonly pluginId: PluginId;
  readonly minimumVersion: SemanticVersion;
  /** Informational availability of the declared target; this does not gate plugin interaction. */
  readonly active: boolean;
}

export interface QuantumEnvironmentSnapshot {
  readonly plugin: QuantumPluginIdentity;
  readonly loadedPlugins: readonly QuantumPluginInfo[];
  readonly integrations: readonly QuantumPluginIntegration[];
}

export interface QuantumDotNetInvocation {
  /** "host" or the id of a loaded .NET plugin. */
  target?: string;
  /** Exact CLR Type.FullName without an assembly name or global:: prefix. */
  service: string;
  /** Public instance method name. */
  method: string;
  /** JSON-serializable arguments. CancellationToken parameters are supplied by Quantum. */
  arguments?: readonly unknown[];
  /** CLR Type.FullName values used to disambiguate overloads. */
  parameterTypes?: readonly string[];
}

export interface QuantumPluginContext {
  readonly plugin: QuantumPluginIdentity;
  /** Aborted before plugin cleanup runs. */
  readonly signal: AbortSignal;
  readonly log: {
    trace(message: string): Promise<void>;
    debug(message: string): Promise<void>;
    info(message: string): Promise<void>;
    warn(message: string): Promise<void>;
    error(message: string): Promise<void>;
  };
  readonly navigation: {
    navigate(href: string): Promise<void>;
  };
  readonly environment: {
    snapshot(): Promise<QuantumEnvironmentSnapshot>;
  };
  readonly eventBus: QuantumEventBus;
  readonly assets: {
    url(path: string): string;
    readText(path: string, options?: QuantumRpcOptions): Promise<string>;
  };
  readonly dotnet: {
    invoke<TResult = unknown>(
      invocation: QuantumDotNetInvocation,
      options?: QuantumRpcOptions
    ): Promise<TResult>;
  };
  rpc<TResult = unknown>(
    capability: string,
    method: string,
    payload?: unknown,
    options?: QuantumRpcOptions
  ): Promise<TResult>;
}

export interface QuantumRoute {
  readonly path: string;
  readonly view: string;
  readonly title?: string | null;
}

export interface QuantumPluginViewContext extends Omit<QuantumPluginContext, "signal"> {
  /** The root element inside the plugin's isolated document. */
  readonly element: HTMLElement;
  readonly route: QuantumRoute;
  /** Aborted whenever the view is unmounted. */
  readonly signal: AbortSignal;
}

export interface QuantumPluginDefinition {
  activate?(context: QuantumPluginContext): MaybePromise<QuantumCleanup>;
  deactivate?(context: QuantumPluginContext): MaybePromise<void>;
  mount?(context: QuantumPluginViewContext): MaybePromise<QuantumCleanup>;
  unmount?(context: QuantumPluginContext): MaybePromise<void>;
}

/**
 * Adds compile-time checking while leaving the plugin definition unchanged for bundlers.
 */
export function definePlugin<TDefinition extends QuantumPluginDefinition>(
  definition: TDefinition
): TDefinition {
  return definition;
}
