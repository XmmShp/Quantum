window.quantum = window.quantum || {};

window.quantum.plugins = {
  nodes: [],

  replaceContributions(contributions) {
    for (const node of this.nodes) {
      node.remove();
    }
    this.nodes = [];

    for (const contribution of contributions) {
      this.appendFragments(document.head, contribution.head, contribution.pluginId, "head");
      this.appendFragments(document.body, contribution.postBlazor, contribution.pluginId, "post-blazor");
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
  }
};
