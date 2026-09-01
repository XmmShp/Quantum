export type MaybePromise<T> = T | PromiseLike<T>;

export type QuantumCleanup =
  | void
  | (() => MaybePromise<void>)
  | { dispose(): MaybePromise<void> };

export interface QuantumRpcOptions {
  signal?: AbortSignal;
}

export interface QuantumPluginIdentity {
  readonly id: string;
  readonly version: string;
  readonly runtimeId: string;
  readonly permissions: readonly string[];
}

export interface QuantumPluginInfo {
  readonly id: string;
  readonly version: string;
}

export interface QuantumPluginIntegration {
  readonly pluginId: string;
  readonly minimumVersion: string;
  readonly active: boolean;
}

export interface QuantumEnvironmentSnapshot {
  readonly plugin: QuantumPluginIdentity;
  readonly loadedPlugins: readonly QuantumPluginInfo[];
  readonly integrations: readonly QuantumPluginIntegration[];
}

export interface QuantumDotNetInvocation {
  /** "host" or the id of an active .NET integration target. */
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
