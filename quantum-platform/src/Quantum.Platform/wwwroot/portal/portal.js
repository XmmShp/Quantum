(() => {
  "use strict";

  const $ = id => document.getElementById(id);
  const state = { token: sessionStorage.getItem("quantum.platform.token"), user: null, plugins: [], selected: null, releases: [], authMode: "login", busy: false };
  const roles = { reviewer: 4, admin: 8 };
  let toastTimer;

  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  async function rpc(method, params = {}, requireAuth = false) {
    if (requireAuth && !state.token) throw new Error("请先登录。");
    const headers = { "Content-Type": "application/json" };
    if (state.token) headers.Authorization = `Bearer ${state.token}`;
    const response = await fetch("/rpc", {
      method: "POST",
      headers,
      body: JSON.stringify({ jsonrpc: "2.0", id: crypto.randomUUID(), method, params })
    });
    if (!response.ok) throw new Error(`平台请求失败（HTTP ${response.status}）。`);
    const envelope = await response.json();
    if (envelope.error) throw new Error(envelope.error.message || "平台拒绝了请求。");
    const result = envelope.result;
    if (!result) throw new Error("平台返回了无效响应。");
    if (result.isSuccess === false) throw new Error(result.message || result.errors?.[0]?.message || "平台拒绝了请求。");
    return Object.prototype.hasOwnProperty.call(result, "value") ? result.value : result;
  }

  function notify(message, error = false) {
    clearTimeout(toastTimer);
    const toast = $("toast");
    toast.textContent = message;
    toast.className = `toast${error ? " error" : ""}`;
    toast.hidden = false;
    toastTimer = setTimeout(() => { toast.hidden = true; }, 4200);
  }

  async function run(action) {
    if (state.busy) return;
    state.busy = true;
    document.querySelectorAll("button").forEach(button => { button.disabled = true; });
    try { await action(); }
    catch (error) { notify(error instanceof Error ? error.message : String(error), true); }
    finally {
      state.busy = false;
      document.querySelectorAll("button").forEach(button => { button.disabled = false; });
    }
  }

  function setAuthMode(mode) {
    state.authMode = mode;
    const registering = mode === "register";
    $("loginTab").classList.toggle("active", !registering);
    $("registerTab").classList.toggle("active", registering);
    $("loginTab").setAttribute("aria-selected", String(!registering));
    $("registerTab").setAttribute("aria-selected", String(registering));
    $("usernameField").hidden = !registering;
    $("usernameInput").required = registering;
    $("passwordHint").hidden = !registering;
    $("passwordInput").autocomplete = registering ? "new-password" : "current-password";
    $("authTitle").textContent = registering ? "注册开发者账号" : "登录开发者账号";
    $("authSubmit").textContent = registering ? "注册并登录" : "登录";
  }

  function showWorkspace() {
    $("authView").hidden = true;
    $("workspaceView").hidden = false;
    $("accountArea").hidden = false;
    $("accountName").textContent = `${state.user.username} · ${state.user.email}`;
    const canReview = (Number(state.user.roles) & (roles.reviewer | roles.admin)) !== 0;
    $("reviewNav").hidden = !canReview;
  }

  function showAuth() {
    $("authView").hidden = false;
    $("workspaceView").hidden = true;
    $("accountArea").hidden = true;
  }

  async function authenticate(event) {
    event.preventDefault();
    await run(async () => {
      const email = $("emailInput").value.trim();
      const password = $("passwordInput").value;
      if (state.authMode === "register") {
        await rpc("RegisterUser", { username: $("usernameInput").value.trim(), email, password });
      }
      const login = await rpc("Login", { email, password });
      state.token = login.accessToken;
      state.user = login.user;
      sessionStorage.setItem("quantum.platform.token", state.token);
      $("passwordInput").value = "";
      showWorkspace();
      await loadPlugins();
      notify("登录成功。");
    });
  }

  function logout() {
    sessionStorage.removeItem("quantum.platform.token");
    Object.assign(state, { token: null, user: null, plugins: [], selected: null, releases: [] });
    showAuth();
  }

  async function loadPlugins() {
    state.plugins = await rpc("ListManagedPlugins", {}, true);
    if (state.selected) state.selected = state.plugins.find(plugin => plugin.pluginId === state.selected.pluginId) || null;
    renderPlugins();
  }

  function renderPlugins() {
    const list = $("pluginList");
    list.replaceChildren();
    if (!state.plugins.length) {
      list.append(element("div", "empty-mini", "还没有插件。点击“新建插件”开始。"));
      return;
    }
    for (const plugin of state.plugins) {
      const button = element("button", `plugin-item${state.selected?.pluginId === plugin.pluginId ? " selected" : ""}`);
      button.type = "button";
      const mark = element("span", "plugin-mark", plugin.name.slice(0, 1).toUpperCase());
      const text = element("span");
      text.append(element("strong", "", plugin.name), element("small", "", plugin.pluginId));
      button.append(mark, text);
      button.addEventListener("click", () => run(() => selectPlugin(plugin)));
      list.append(button);
    }
  }

  function beginPlugin(plugin = null) {
    state.selected = plugin;
    $("emptyPlugin").hidden = true;
    $("pluginEditor").hidden = false;
    $("releaseManager").hidden = !plugin;
    $("pluginEditorTitle").textContent = plugin ? "编辑插件资料" : "新建插件";
    $("pluginIdInput").value = plugin?.pluginId || "";
    $("pluginIdInput").disabled = Boolean(plugin);
    $("pluginNameInput").value = plugin?.name || "";
    $("pluginDescriptionInput").value = plugin?.description || "";
    $("pluginTagsInput").value = plugin?.tags?.join(", ") || "";
    $("deletePluginButton").hidden = !plugin;
    if (!plugin) { state.releases = []; renderPlugins(); }
  }

  async function selectPlugin(plugin) {
    beginPlugin(plugin);
    state.releases = await rpc("ListPluginReleases", { pluginId: plugin.pluginId }, true);
    renderPlugins();
    renderReleases();
  }

  function parseTags(value) {
    return [...new Set(value.split(",").map(tag => tag.trim().toLowerCase()).filter(Boolean))];
  }

  async function savePlugin(event) {
    event.preventDefault();
    await run(async () => {
      const params = {
        pluginId: $("pluginIdInput").value.trim(),
        name: $("pluginNameInput").value.trim(),
        description: $("pluginDescriptionInput").value.trim(),
        tags: parseTags($("pluginTagsInput").value)
      };
      const method = state.selected ? "UpdatePlugin" : "CreatePlugin";
      const saved = await rpc(method, params, true);
      await loadPlugins();
      const plugin = state.plugins.find(item => item.pluginId === saved.pluginId) || saved;
      await selectPlugin(plugin);
      notify("插件资料已保存。");
    });
  }

  async function deletePlugin() {
    if (!state.selected || !confirm(`永久删除 ${state.selected.pluginId} 及其全部版本？`)) return;
    await run(async () => {
      await rpc("DeletePlugin", { pluginId: state.selected.pluginId }, true);
      state.selected = null; state.releases = [];
      $("pluginEditor").hidden = true; $("releaseManager").hidden = true; $("emptyPlugin").hidden = false;
      await loadPlugins();
      notify("插件已删除。");
    });
  }

  function statusInfo(status) {
    if (Number(status) === 1) return ["pending", "待审核"];
    if (Number(status) === 2) return ["published", "已发布"];
    return ["rejected", "已拒绝"];
  }

  function formatSize(bytes) {
    return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, bytes / 1024).toFixed(1)} KB`;
  }

  function renderReleases() {
    $("releaseCount").textContent = `${state.releases.length} 个版本`;
    const list = $("releaseList");
    list.replaceChildren();
    if (!state.releases.length) { list.append(element("div", "empty-mini", "还没有版本。上传第一个 ZIP 提交审核。")); return; }
    for (const release of state.releases) {
      const article = element("article");
      const top = element("div", "release-top");
      const summary = element("div");
      summary.append(element("strong", "", `v${release.version}`), element("small", "", `Quantum ${release.quantumVersionSupport} · ${formatSize(release.packageSizeBytes)}`));
      const [className, label] = statusInfo(release.status);
      top.append(summary, element("span", `status ${className}`, label));
      article.append(top);
      if (release.releaseNotes) article.append(element("p", "", release.releaseNotes));
      if (release.reviewNotes) article.append(element("p", "", `审核意见：${release.reviewNotes}`));
      list.append(article);
    }
  }

  function bytesToBase64(bytes) {
    const chunkSize = 0x8000;
    let binary = "";
    for (let offset = 0; offset < bytes.length; offset += chunkSize) {
      binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
    }
    return btoa(binary);
  }

  async function uploadRelease(event) {
    event.preventDefault();
    if (!state.selected) return;
    const file = $("archiveInput").files[0];
    if (!file) return notify("请选择插件 ZIP。", true);
    if (file.size > 256 * 1024 * 1024) return notify("插件 ZIP 不能超过 256 MB。", true);
    await run(async () => {
      $("checksumText").textContent = "正在计算 SHA-256…";
      const bytes = new Uint8Array(await file.arrayBuffer());
      const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
      const sha256 = [...digest].map(value => value.toString(16).padStart(2, "0")).join("");
      $("checksumText").textContent = `SHA-256 ${sha256.slice(0, 16)}…`;
      await rpc("UploadPluginRelease", {
        pluginId: state.selected.pluginId,
        version: $("versionInput").value.trim(),
        quantumVersionSupport: $("supportInput").value.trim(),
        releaseNotes: $("releaseNotesInput").value.trim(),
        packageArchiveBase64: bytesToBase64(bytes),
        expectedSha256: sha256
      }, true);
      $("releaseForm").reset();
      $("supportInput").value = ">=0.1.0";
      $("fileName").textContent = "选择不超过 256 MB 的 ZIP";
      $("checksumText").textContent = "";
      state.releases = await rpc("ListPluginReleases", { pluginId: state.selected.pluginId }, true);
      renderReleases();
      notify("版本已提交审核；你现在可以在 Quantum 客户端登录并测试安装。");
    });
  }

  async function loadReviews() {
    const releases = await rpc("ListAllPluginReleases", { status: 1 }, true);
    const list = $("reviewList");
    list.replaceChildren();
    if (!releases.length) { list.append(element("div", "empty-mini", "当前没有待审核版本。")); return; }
    for (const release of releases) {
      const article = element("article");
      const head = element("div", "review-head");
      head.append(element("span", "plugin-mark", (release.pluginId || "P").slice(0, 1).toUpperCase()));
      const text = element("div");
      text.append(element("h3", "", `${release.pluginId || "未知插件"} · v${release.version}`), element("p", "", `Quantum ${release.quantumVersionSupport} · ${formatSize(release.packageSizeBytes)} · SHA-256 ${release.packageSha256.slice(0, 12)}…`));
      head.append(text); article.append(head);
      if (release.releaseNotes) article.append(element("p", "muted", release.releaseNotes));
      const actions = element("div", "review-actions");
      const notes = element("textarea"); notes.placeholder = "审核意见（拒绝时建议填写）"; notes.maxLength = 2000; notes.setAttribute("aria-label", "审核意见");
      const reject = element("button", "button secondary", "拒绝"); reject.type = "button";
      const approve = element("button", "button primary", "发布"); approve.type = "button";
      reject.addEventListener("click", () => review(release, 3, notes.value));
      approve.addEventListener("click", () => review(release, 2, notes.value));
      actions.append(notes, reject, approve); article.append(actions); list.append(article);
    }
  }

  function review(release, status, notes) {
    run(async () => {
      await rpc("ReviewPluginRelease", { releaseId: release.releaseId, status, notes: notes.trim() || null }, true);
      await loadReviews();
      notify(status === 2 ? "版本已发布。" : "版本已拒绝。");
    });
  }

  function switchView(name) {
    $("publishingView").hidden = name !== "publishing";
    $("reviewView").hidden = name !== "review";
    document.querySelectorAll(".nav-button").forEach(button => button.classList.toggle("active", button.dataset.view === name));
    if (name === "review") run(loadReviews);
  }

  async function initialize() {
    $("loginTab").addEventListener("click", () => setAuthMode("login"));
    $("registerTab").addEventListener("click", () => setAuthMode("register"));
    $("authForm").addEventListener("submit", authenticate);
    $("logoutButton").addEventListener("click", logout);
    $("newPluginButton").addEventListener("click", () => beginPlugin());
    $("refreshPluginsButton").addEventListener("click", () => run(loadPlugins));
    $("pluginForm").addEventListener("submit", savePlugin);
    $("deletePluginButton").addEventListener("click", deletePlugin);
    $("releaseForm").addEventListener("submit", uploadRelease);
    $("archiveInput").addEventListener("change", event => { $("fileName").textContent = event.target.files[0]?.name || "选择不超过 256 MB 的 ZIP"; });
    $("refreshReviewsButton").addEventListener("click", () => run(loadReviews));
    document.querySelectorAll(".nav-button").forEach(button => button.addEventListener("click", () => switchView(button.dataset.view)));

    if (!state.token) return showAuth();
    try {
      state.user = await rpc("GetCurrentUser", {}, true);
      showWorkspace();
      await loadPlugins();
    } catch {
      logout();
    }
  }

  initialize();
})();
