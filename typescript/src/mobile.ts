export interface MobileNode {
  readonly type: string;
  readonly id: string;
  readonly text?: string | null;
  readonly placeholder?: string | null;
  readonly style?: string;
  readonly maxLength?: number;
  readonly enabled?: boolean;
  readonly action?: MobileRpcAction | null;
  readonly children?: readonly MobileNode[];
}
export interface MobileRpcAction {
  readonly rpcName: string;
  readonly constants: Readonly<Record<string, unknown>>;
  readonly inputs: Readonly<Record<string, string>>;
}
export interface MobilePage {
  readonly schemaVersion: 1;
  readonly id: string;
  readonly title: string;
  readonly root: MobileNode;
}
export interface QuantumMobilePages {
  publish(page: MobilePage): Promise<void>;
  remove(pageId: string): Promise<void>;
}

class NodeBuilder implements MobileNode {
  readonly type: string;
  readonly id: string;
  readonly text?: string | null;
  readonly placeholder?: string | null;
  readonly style?: string;
  readonly maxLength?: number;
  readonly enabled?: boolean;
  readonly action?: MobileRpcAction | null;
  readonly children?: readonly MobileNode[];
  constructor(node: MobileNode) { Object.assign(this, node); this.type = node.type; this.id = node.id; }
  label(text: string): NodeBuilder { return new NodeBuilder({ ...this, text }); }
  hint(placeholder: string): NodeBuilder { return new NodeBuilder({ ...this, placeholder }); }
  limit(maxLength: number): NodeBuilder { return new NodeBuilder({ ...this, maxLength }); }
  appearance(style: string): NodeBuilder { return new NodeBuilder({ ...this, style }); }
  onClick(action: MobileRpcAction): NodeBuilder { return new NodeBuilder({ ...this, action }); }
}
class ActionBuilder implements MobileRpcAction {
  constructor(readonly rpcName: string, readonly constants: Readonly<Record<string, unknown>> = {}, readonly inputs: Readonly<Record<string, string>> = {}) {}
  constant(field: string, value: unknown): ActionBuilder { return new ActionBuilder(this.rpcName, { ...this.constants, [field]: value }, this.inputs); }
  input(field: string, nodeId: string): ActionBuilder { return new ActionBuilder(this.rpcName, this.constants, { ...this.inputs, [field]: nodeId }); }
}

export const Mobile = Object.freeze({
  page: (id: string, title: string, root: MobileNode): MobilePage => ({ schemaVersion: 1, id, title, root }),
  vStack: (id: string, ...children: MobileNode[]) => new NodeBuilder({ type: "VStack", id, children }),
  hStack: (id: string, ...children: MobileNode[]) => new NodeBuilder({ type: "HStack", id, children }),
  scroll: (id: string, child: MobileNode) => new NodeBuilder({ type: "Scroll", id, children: [child] }),
  text: (id: string, text: string) => new NodeBuilder({ type: "Text", id, text }),
  textField: (id: string) => new NodeBuilder({ type: "TextField", id }),
  toggle: (id: string, text: string) => new NodeBuilder({ type: "Toggle", id, text }),
  button: (id: string, text: string) => new NodeBuilder({ type: "Button", id, text }),
  divider: (id: string) => new NodeBuilder({ type: "Divider", id }),
  spacer: (id: string) => new NodeBuilder({ type: "Spacer", id }),
  progress: (id: string) => new NodeBuilder({ type: "Progress", id }),
  rpc: (name: string) => new ActionBuilder(name)
});

export function validateMobilePage(page: MobilePage): void {
  const validId = (value: unknown) => typeof value === "string" && /^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$/.test(value);
  if (!page || page.schemaVersion !== 1 || !validId(page.id) || typeof page.title !== "string" || !page.title.trim() || page.title.length > 200)
    throw new TypeError("Invalid mobile page.");
  const nodes = new Map<string, MobileNode>();
  const types = ["VStack", "HStack", "Scroll", "Text", "TextField", "Toggle", "Button", "Divider", "Spacer", "Progress"];
  const visit = (node: MobileNode, depth: number) => {
    if (!node || depth > 16 || nodes.size >= 500 || !validId(node.id) || nodes.has(node.id) || !types.includes(node.type)) throw new TypeError("Invalid mobile tree.");
    if ((node.text != null && (typeof node.text !== "string" || node.text.length > 2000)) ||
        (node.placeholder != null && (typeof node.placeholder !== "string" || node.placeholder.length > 2000)) ||
        (node.enabled !== undefined && typeof node.enabled !== "boolean") ||
        !Number.isInteger(node.maxLength ?? 500) || (node.maxLength ?? 500) < 1 || (node.maxLength ?? 500) > 2000 ||
        !["body", "title", "caption", "error"].includes(node.style ?? "body")) throw new TypeError("Invalid mobile properties.");
    const children = node.children ?? [];
    if (!Array.isArray(children) || (node.type === "Scroll" && children.length !== 1) ||
        (!["VStack", "HStack", "Scroll"].includes(node.type) && children.length !== 0)) throw new TypeError("Invalid mobile children.");
    nodes.set(node.id, node);
    children.forEach(child => visit(child, depth + 1));
  };
  visit(page.root, 1);
  for (const node of nodes.values()) {
    if (!node.action) continue;
    const action = node.action;
    if (node.type !== "Button" || typeof action.rpcName !== "string" || action.rpcName.length > 256 ||
        action.rpcName.split(".").length < 3 || !/^[A-Za-z0-9_.-]+$/.test(action.rpcName) || !action.constants || !action.inputs)
      throw new TypeError("Invalid mobile action.");
    const fields = [...Object.keys(action.constants), ...Object.keys(action.inputs)];
    if (fields.length > 32 || !fields.every(validId)) throw new TypeError("Invalid mobile action fields.");
    for (const [field, source] of Object.entries(action.inputs)) {
      if (Object.hasOwn(action.constants, field) || !["TextField", "Toggle"].includes(nodes.get(source)?.type ?? ""))
        throw new TypeError("Invalid mobile input binding.");
    }
  }
  if (new TextEncoder().encode(JSON.stringify(page)).length > 128 * 1024) throw new TypeError("Mobile page is too large.");
}
