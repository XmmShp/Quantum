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

declare const versionRangeBrand: unique symbol;

/** A validated union of Semantic Versioning 2.0.0 intervals and finite sets. */
export type VersionRange = string & {
  readonly [versionRangeBrand]: "VersionRange";
};

/** Parser and membership test for VersionRange values. */
export const VersionRange = Object.freeze({
  parse(value: string): VersionRange {
    const range = parseVersionRange(value);
    if (range === null) {
      throw new TypeError(`'${String(value)}' is not a valid version range.`);
    }
    return range.normalized as VersionRange;
  },

  tryParse(value: string): VersionRange | null {
    const range = parseVersionRange(value);
    return range === null ? null : range.normalized as VersionRange;
  },

  contains(range: VersionRange, version: SemanticVersion): boolean {
    const parsedRange = parseVersionRange(range);
    const parsedVersion = parseSemanticVersion(version);
    if (parsedRange === null || parsedVersion === null) {
      throw new TypeError("VersionRange.contains requires valid range and version values.");
    }
    return parsedRange.terms.some(term => versionRangeTermContains(term, parsedVersion));
  }
});

type ParsedVersionRangeTerm = ParsedVersionInterval | ParsedVersionSet;

interface ParsedVersionInterval {
  readonly kind: "interval";
  readonly lowerBound: SemanticVersionComponents | null;
  readonly includeLowerBound: boolean;
  readonly upperBound: SemanticVersionComponents | null;
  readonly includeUpperBound: boolean;
}

interface ParsedVersionSet {
  readonly kind: "set";
  readonly versions: readonly SemanticVersionComponents[];
}

interface ParsedVersionRange {
  readonly normalized: string;
  readonly terms: readonly ParsedVersionRangeTerm[];
}

function parseVersionRange(value: string): ParsedVersionRange | null {
  if (typeof value !== "string" || value.trim().length === 0) {
    return null;
  }

  const rawTerms = value.trim().split("|");
  if (rawTerms.some(term => term.trim().length === 0)) {
    return null;
  }

  const terms: ParsedVersionRangeTerm[] = [];
  const normalizedTerms: string[] = [];
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

function parseVersionRangeTerm(
  term: string
): { readonly normalized: string; readonly term: ParsedVersionRangeTerm } | null {
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
    const values = term.slice(1, -1).split(",").map(value => value.trim());
    if (values.length === 0 || values.some(value => value.length === 0)) {
      return null;
    }
    const versions = values.map(parseSemanticVersion);
    if (versions.some(version => version === null)) {
      return null;
    }
    return {
      normalized: `{${values.join(",")}}`,
      term: { kind: "set", versions: versions as SemanticVersionComponents[] }
    };
  }

  if (term.length < 3
      || !(term.startsWith("[") || term.startsWith("("))
      || !(term.endsWith("]") || term.endsWith(")"))) {
    return null;
  }

  const bounds = term.slice(1, -1).split(",").map(value => value.trim());
  if (bounds.length !== 2) {
    return null;
  }
  const lowerValue = bounds[0]!;
  const upperValue = bounds[1]!;
  const includeLowerBound = term.startsWith("[");
  const includeUpperBound = term.endsWith("]");
  if ((lowerValue.length === 0 && includeLowerBound)
      || (upperValue.length === 0 && includeUpperBound)) {
    return null;
  }

  const lowerBound = lowerValue.length === 0 ? null : parseSemanticVersion(lowerValue);
  const upperBound = upperValue.length === 0 ? null : parseSemanticVersion(upperValue);
  if ((lowerValue.length > 0 && lowerBound === null)
      || (upperValue.length > 0 && upperBound === null)) {
    return null;
  }
  if (lowerBound !== null && upperBound !== null) {
    const comparison = compareSemanticVersions(lowerBound, upperBound);
    if (comparison > 0 || (comparison === 0 && (!includeLowerBound || !includeUpperBound))) {
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

function versionRangeTermContains(
  term: ParsedVersionRangeTerm,
  version: SemanticVersionComponents
): boolean {
  if (term.kind === "set") {
    return term.versions.some(candidate => compareSemanticVersions(candidate, version) === 0);
  }

  if (term.lowerBound !== null) {
    const comparison = compareSemanticVersions(version, term.lowerBound);
    if (comparison < 0 || (comparison === 0 && !term.includeLowerBound)) {
      return false;
    }
  }
  if (term.upperBound !== null) {
    const comparison = compareSemanticVersions(version, term.upperBound);
    if (comparison > 0 || (comparison === 0 && !term.includeUpperBound)) {
      return false;
    }
  }
  return true;
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
  readonly versionRange: VersionRange;
  /** Informational availability of the declared target; this does not gate plugin interaction. */
  readonly active: boolean;
}

export interface QuantumEnvironmentSnapshot {
  readonly plugin: QuantumPluginIdentity;
  readonly loadedPlugins: readonly QuantumPluginInfo[];
  readonly integrations: readonly QuantumPluginIntegration[];
}

export type QuantumRpcContext = Readonly<Record<string, unknown>>;

export type QuantumResult<TResponse = undefined> =
  | {
      readonly isSuccess: true;
      readonly value: TResponse;
      readonly errorCode: string;
      readonly message: string;
      readonly extra?: Readonly<Record<string, string>> | null;
    }
  | {
      readonly isSuccess: false;
      readonly errorCode: string;
      readonly message: string;
      readonly extra?: Readonly<Record<string, string>> | null;
    };

export interface QuantumRpcInvoker {
  invoke<TResponse = undefined>(
    rpcName: string,
    payload: unknown,
    context?: QuantumRpcContext,
    options?: QuantumRpcOptions
  ): Promise<QuantumResult<TResponse>>;
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
  readonly rpc: QuantumRpcInvoker;
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
