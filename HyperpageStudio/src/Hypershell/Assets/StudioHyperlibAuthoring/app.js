// Studio content authoring reuses Hyperlib's revision-checked writer, hierarchy, media and renderer toolset.
const $ = (s, root = document) => root.querySelector(s);
const $$ = (s, root = document) => [...root.querySelectorAll(s)];
const esc = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const icons = {
  pen: "M14 4l4 4M4 20l4-1L20 7a2.8 2.8 0 0 0-4-4L4 15z",
  grid: "M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z",
  panel: "M3 4h18v16H3zM9 4v16",
  code: "M8 7l-5 5 5 5m8-10l5 5-5 5m-3-13l-2 20",
  palette:
    "M12 3a9 9 0 1 0 0 18h1a2 2 0 0 0 1-4c-1-1 0-3 2-3h2a3 3 0 0 0 3-3 9 9 0 0 0-9-8zM7 11h.01M9 7h.01M14 7h.01M17 10h.01",
  more: "M5 12h.01M12 12h.01M19 12h.01",
  plus: "M12 5v14M5 12h14",
  undo: "M8 4L3 9l5 5M3 9h11a7 7 0 0 1 0 14",
  redo: "M16 4l5 5-5 5m5-5H10a7 7 0 0 0 0 14",
  close: "M6 6l12 12M6 18L18 6",
  check: "M5 12l4 4L19 6",
  arrow: "M5 12h14m-5-5l5 5-5 5",
  back: "M19 12H5m5-5l-5 5 5 5",
  book: "M3 4h6c2 0 3 1 3 3v14c0-2-1-3-3-3H3zm18 0h-6c-2 0-3 1-3 3v14c0-2 1-3 3-3h6z",
  folder: "M3 6h7l2 3h9v11H3z",
  page: "M6 3h8l4 4v14H6zM14 3v5h4M9 12h6M9 16h6",
  search: "M16 16l5 5M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14",
  text: "M4 5h16M12 5v15M8 20h8",
  image: "M3 3h18v18H3zM3 17l6-6 4 4 3-3 5 5M15 7h.01",
  audio: "M8 16V5l12-2v13M8 8l12-2M8 16a3 3 0 1 1-3-3h3m12 3a3 3 0 1 1-3-3h3",
  video: "M3 5h13v14H3zM16 9l5-3v12l-5-3",
  quote: "M4 5h6v8H4c0 4 3 6 5 6m5-14h6v8h-6c0 4 3 6 5 6",
  spark: "M12 2l3 7 7 3-7 3-3 7-3-7-7-3 7-3z",
  wave: "M2 12c4-15 6 15 10 0s6 15 10 0",
  up: "M6 14l6-6 6 6",
  down: "M6 10l6 6 6-6",
  trash: "M3 6h18M9 6V3h6v3M6 6l1 15h10l1-15M10 10v7M14 10v7",
  upload: "M12 16V3M7 8l5-5 5 5M4 16v5h16v-5",
  save: "M4 3h13l4 4v14H3V3zM7 3v6h10V3M7 21v-7h10v7M14 5v2",
  help: "M9 8a3 3 0 1 1 5 2c-2 1-2 2-2 4M12 18h.01",
  link: "M10 14l4-4M8 16l-2 2a4 4 0 0 1-6-6l5-5m11 1l2-2a4 4 0 0 1 6 6l-5 5",
};
function icon(name) {
  return `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${icons[name] || icons.page}"/></svg>`;
}
function hydrateIcons(root = document) {
  $$("[data-icon]", root).forEach(
    (el) => (el.innerHTML = icon(el.dataset.icon)),
  );
}
const app = {
  state: null,
  id: "library",
  edit: true,
  pipeline: null,
  recovery: null,
  invalidFields: new Map(),
  panel: "templates",
  leftPanel: "contents",
  library: { items: null, loading: null, error: null, scope: "page", search: "", category: "all", project: "all", kind: "all", selectedId: null, limit: 80, busy: false, status: "" },
  libraryMutation: null,
  catalogElement: null,
  deferredWorkspacePanel: null,
  workspaceChrome: null,
  workspacePanelFront: "right",
  libraryPreview: { sequence: 0, timer: null, itemId: null, nodeId: null, scroll: null },
  selected: null,
  template: null,
  templates: new Map(),
  native: null,
  busy: false,
  messages: [],
  draft: null,
  timer: null,
  mutation: Promise.resolve(),
  frameGeneration: null,
  frameReady: false,
  frameReadyPageId: null,
  frameReadyGenerationId: null,
  frameDirty: false,
  canvasGesture: false,
  frameFlushes: new Map(),
  frameReadyWaiters: new Set(),
  frameFlushing: null,
  baseTemplates: null,
  navigation: 0,
  navBranches: new Map(),
  navLibrary: null,
  navSelection: null,
  archives: null,
  chromeReady: false,
  editChanging: false,
  workFrameGeneration: null,
};
function hasNativeChrome() {
  return window === window.top && window.__hyperlibNativeChrome === true;
}
function publishChromeState() {
  const canEdit = app.chromeReady && !app.editChanging;
  $("#edit-toggle").disabled = !canEdit;
  $("#edit-toggle").setAttribute("aria-pressed", String(app.edit));
  if (hasNativeChrome())
    window.chrome?.webview?.postMessage({
      type: "hyperlib:chrome-state", editing: app.edit, canEdit,
    });
}
window.addEventListener("hyperlib:native-chrome", () => {
  if (!hasNativeChrome()) return;
  document.documentElement.classList.add("hyperlib-native-chrome");
  updateNavigationLayout();
  publishChromeState();
});
window.addEventListener("hyperlib:edit-mode", (event) => {
  if (hasNativeChrome() && typeof event.detail?.enabled === "boolean")
    setEditMode(event.detail.enabled);
});
const requests = new Map();
let toastTimer;
function toast(message, error = false) {
  const el = $("#toast");
  el.textContent = message;
  el.classList.toggle("error", error);
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (el.hidden = true), error ? 8500 : 3500);
}
function status(message = "Saved locally", kind = "") {
  const el = $("#save-status");
  el.textContent = message;
  el.className = `save-status ${kind}`;
  publishStudioState();
}
async function api(path, options = {}) {
  const headers = { ...options.headers };
  if (options.body && !(options.body instanceof FormData))
    headers["Content-Type"] = "application/json";
  if (options.method && options.method !== "GET")
    headers["X-HyperLib-Client"] = "editor";
  const response = await fetch(path, { ...options, headers });
  const body = await response
    .json()
    .catch(() => ({ error: response.statusText }));
  if (!response.ok) {
    const error = new Error(
      body.error || body.detail || "The request could not be completed.",
    );
    error.status = response.status;
    throw error;
  }
  return body;
}
function native(method, params = {}) {
  if (!window.chrome?.webview)
    return Promise.reject(
      new Error("Open HyperLib in Hypershell to use this feature."),
    );
  const id = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(
      () => {
        requests.delete(id);
        reject(new Error("Hypershell did not respond."));
      },
      method === "archives.sync" ? 960000 : method === "ai.send" ? 600000 : 15000,
    );
    requests.set(id, { resolve, reject, timer });
    window.chrome.webview.postMessage({
      channel: "hyperlib",
      id,
      method,
      params,
    });
  });
}
window.chrome?.webview?.addEventListener("message", (event) => {
  const m = event.data;
  if (m.channel !== "hyperlib") return;
  if (m.event === "ai.progress") {
    $("#ai-progress")?.replaceChildren(
      document.createTextNode(m.message || "Working…"),
    );
    return;
  }
  const request = requests.get(m.id);
  if (!request) return;
  clearTimeout(request.timer);
  requests.delete(m.id);
  m.error
    ? request.reject(
        new Error(typeof m.error === "string" ? m.error : m.error.message),
      )
    : request.resolve(m.result);
});
const rootNode = () =>
  app.state.nodes.find((n) => n.type === "library") || {
    id: app.state.libraryId,
    type: "library",
    title: "HyperLib",
    appearance: app.state.appearance,
  };
const node = (id = app.id) =>
  id === "library" || id === app.state.libraryId
    ? rootNode()
    : app.state.nodes.find((n) => n.id === id);
const children = (id) =>
  app.state.nodes
    .filter((n) => n.parentId === id)
    .sort((a, b) => a.order - b.order || a.title.localeCompare(b.title));
const work = () => {
  if (!app.state) return undefined;
  const n = node();
  return ancestors(n).find((item) => item.type === "work") ||
    app.state.nodes.find((x) => x.id === n?.workId && x.type === "work");
};
const immersiveWork = () => !!work() && !app.edit;
const page = () =>
  app.draft?.id === app.id ? app.draft : app.state.pages[app.id];
const label = (type) => type === "landing" ? "Landing" : app.state.labels[type] || type;
const plural = (type) =>
  label(type) === "Series" ? "Series" : `${label(type)}s`;
function ancestors(n) {
  const result = [];
  const seen = new Set();
  while (n && !seen.has(n.id)) {
    seen.add(n.id);
    result.unshift(n);
    n = n.parentId ? node(n.parentId) : null;
  }
  return result;
}
function studioWorkFor(current = node()) {
  return ancestors(current).find(item => item.type === "work")
    || app.state.nodes.find(item => item.type === "work" && item.id === current?.workId)
    || (app.pipeline?.workId ? node(app.pipeline.workId) : null);
}
function studioLandingNode(owner = studioWorkFor()) {
  if (owner?.type !== "work") return null;
  const id = owner.appearance?.workLandingPage
    || (app.pipeline?.workId === owner.id ? app.pipeline.landingPageId : null);
  const landing = id ? node(id) : null;
  return landing?.type === "page" && app.state.pages[landing.id]
    && (landing.workId === owner.id || ancestors(landing).some(item => item.id === owner.id)) ? landing : null;
}
function studioWorkDestination(current) {
  return current?.type === "work" ? studioLandingNode(current)?.id || current.id : current?.id;
}
function descendantPages(n) {
  return Object.values(app.state.pages).filter((p) =>
    ancestors(node(p.id)).some((x) => x.id === n.id),
  );
}
function descendants(n, type) {
  return app.state.nodes.filter(
    (x) => x.type === type && ancestors(x).some((a) => a.id === n.id),
  );
}
async function loadState() {
  app.state = await api("/api/state");
}
async function loadTemplate() {
  const id = work()?.id || "library";
  if (!app.templates.has(id))
    app.templates.set(
      id,
      await api(`/api/templates/${encodeURIComponent(id)}`),
    );
  app.template = app.templates.get(id);
}
function safeUrl(value) {
  if (typeof value !== "string" || !value.trim()) return "";
  try {
    const url = new URL(value, location.origin);
    return ["http:", "https:"].includes(url.protocol) ? url.href : "";
  } catch {
    return "";
  }
}
function localMedia(value) {
  const url = safeUrl(value);
  return url && new URL(url).origin === location.origin ? url : "";
}
async function navigate(id) {
  clearStudioLibraryPreview();
  if (app.libraryMutation) await app.libraryMutation;
  await flushDraft();
  if (app.draft) return;
  app.navOpen = false;
  updateNavigationLayout();
  // The old document may remain painted until the renderer replaces it. It
  // must not accept an edit intended for the next Page during that interval.
  if (app.id !== id) setPageFrameLoading(false);
  location.hash = encodeURIComponent(id);
  if (app.id === id) await render();
}
// Tag only this library's entries. Bounds survive reloads without enabling Back
// to an unrelated page, and a new branch discards the old Forward destination.
let browserHistory = null;
const browserHistoryKey = "hyperlib.browser-history";
function initializeBrowserHistory() {
  let saved;
  try { saved = JSON.parse(sessionStorage.getItem(browserHistoryKey)); } catch { /* Optional session preference. */ }
  const entry = history.state?.hyperlibHistory;
  if (saved && typeof saved.session === "string" && entry?.session === saved.session &&
      Number.isInteger(entry.index) && entry.index >= 0 && Number.isInteger(saved.length) && entry.index < saved.length)
    browserHistory = { session: saved.session, index: entry.index, length: saved.length };
  else {
    browserHistory = { session: crypto.randomUUID(), index: 0, length: 1 };
    stampBrowserHistory();
  }
  synchronizeBrowserHistory();
}
function stampBrowserHistory() {
  history.replaceState({ ...history.state, hyperlibHistory: {
    session: browserHistory.session, index: browserHistory.index,
  } }, "");
}
function synchronizeBrowserHistory() {
  if (!browserHistory) return;
  const entry = history.state?.hyperlibHistory;
  if (entry?.session === browserHistory.session && Number.isInteger(entry.index))
    browserHistory.index = entry.index;
  else {
    browserHistory.index++;
    browserHistory.length = browserHistory.index + 1;
    stampBrowserHistory();
  }
  try { sessionStorage.setItem(browserHistoryKey, JSON.stringify(browserHistory)); } catch { /* History still works without storage. */ }
  $("#history-back").disabled = browserHistory.index <= 0;
  $("#history-forward").disabled = browserHistory.index >= browserHistory.length - 1;
}
async function travelBrowserHistory(direction) {
  if (!browserHistory || (direction < 0 ? $("#history-back") : $("#history-forward")).disabled) return;
  try {
    await flushDraft();
    if (app.draft) return;
    history.go(direction);
  } catch (error) { toast(error.message, true); }
}
$("#history-back").onclick = () => travelBrowserHistory(-1);
$("#history-forward").onclick = () => travelBrowserHistory(1);
window.addEventListener("popstate", synchronizeBrowserHistory);
window.addEventListener("hashchange", async () => {
  clearStudioLibraryPreview();
  synchronizeBrowserHistory();
  try {
    await flushDraft();
    if (app.draft) {
      history.replaceState(history.state, "", `#${encodeURIComponent(app.id)}`);
      return;
    }
    app.id = decodeURIComponent(location.hash.slice(1) || "library");
    app.selected = null;
    await render();
    window.scrollTo(0, 0);
  } catch (error) {
    history.replaceState(history.state, "", `#${encodeURIComponent(app.id)}`);
    toast(error.message, true);
  }
});
function navigationStorageKey() {
  return `hyperlib.navigation.${encodeURIComponent(app.state.libraryId)}`;
}
function saveNavigationBranches() {
  try {
    localStorage.setItem(navigationStorageKey(), JSON.stringify([...app.navBranches]));
  } catch { /* Navigation still works when local storage is unavailable. */ }
}
function prepareNavigationBranches() {
  if (app.navLibrary !== app.state.libraryId) {
    app.navLibrary = app.state.libraryId;
    app.navSelection = null;
    app.navBranches = new Map();
    try {
      const saved = JSON.parse(localStorage.getItem(navigationStorageKey()) || "[]");
      if (Array.isArray(saved))
        for (const entry of saved)
          if (Array.isArray(entry) && typeof entry[0] === "string" && typeof entry[1] === "boolean")
            app.navBranches.set(entry[0], entry[1]);
    } catch { /* Ignore an old or incomplete navigation preference. */ }
  }
  const known = new Set(app.state.nodes.map((item) => item.id));
  for (const id of app.navBranches.keys()) if (!known.has(id)) app.navBranches.delete(id);
  const path = new Set(ancestors(node()).map((item) => item.id));
  if (app.navSelection !== app.id) {
    // A new destination must be visible, even if its branch was collapsed earlier.
    // A later manual collapse stays respected until navigation changes again.
    for (const id of path) if (app.navBranches.get(id) === false) app.navBranches.delete(id);
    app.navSelection = app.id;
  }
  return path;
}
function toggleNavigationBranch(id) {
  if (!node(id) || !children(id).length) return;
  const path = prepareNavigationBranches();
  app.navBranches.set(id, !(app.navBranches.get(id) ?? path.has(id)));
  saveNavigationBranches();
  renderNavigation();
  $$("[data-nav-toggle]", $("#nav-tree")).find((button) => button.dataset.navToggle === id)?.focus({ preventScroll: true });
}
function renderNavigation() {
  const root = (app.pipeline?.workId ? node(app.pipeline.workId) : null) || work() || rootNode();
  const path = prepareNavigationBranches();
  document.documentElement.style.setProperty(
    "--accent",
    app.state.appearance?.accent || "#a7d7c5",
  );
  const landing = studioLandingNode(root);
  const landingRow = landing ? `<div class="nav-branch studio-landing-branch"><div class="nav-row" style="--depth:0"><span class="nav-toggle-space" aria-hidden="true"></span><button type="button" class="nav-item studio-landing-item ${app.id === landing.id ? "active" : ""}" data-navigate="${esc(landing.id)}" data-landing-page="${esc(landing.id)}"${app.id === landing.id ? ' aria-current="page"' : ""} title="${esc(landing.title)} · Landing">${icon("page")}<span>${esc(landing.title)}</span><small class="studio-landing-label">Landing</small></button></div></div>` : "";
  $("#nav-tree").innerHTML = landingRow + renderTree(root.id, 0, path);
  $(".nav-home[data-navigate]").dataset.navigate = root.id;
  $("#studio-landing").hidden = root.type !== "work";
  $("#studio-landing").title = landing ? `Open ${landing.title}` : "Create the Work landing page";
  $$(".nav-home").forEach((b) =>
    b.classList.toggle("active", b.id === "studio-landing" ? landing?.id === app.id : b.dataset.navigate === app.id),
  );
  $("#library-count").textContent =
    `${Object.keys(app.state.pages).filter(id => id !== landing?.id).length} content Pages`;
}
function renderTree(id, depth, path, seen = new Set()) {
  if (seen.has(id)) return "";
  const branch = new Set(seen).add(id);
  return children(id)
    .filter((n) => !branch.has(n.id) && n.id !== studioLandingNode()?.id)
    .map((n) => {
      const hasChildren = n.type !== "page" && children(n.id).length > 0;
      const expanded = hasChildren && (app.navBranches.get(n.id) ?? path.has(n.id));
      const contentsId = `nav-children-${n.id}`;
      const toggle = hasChildren
        ? `<button type="button" class="nav-toggle" data-nav-toggle="${esc(n.id)}" aria-expanded="${expanded}" aria-controls="${esc(contentsId)}" aria-label="${expanded ? "Collapse" : "Expand"} ${esc(n.title)}" title="${expanded ? "Collapse" : "Expand"}">${icon("down")}</button>`
        : '<span class="nav-toggle-space" aria-hidden="true"></span>';
      return `<div class="nav-branch"><div class="nav-row" style="--depth:${Math.min(depth, 6)}">${toggle}<button type="button" class="nav-item ${depth === 0 ? "nav-group" : ""} ${app.id === n.id ? "active" : ""}" data-navigate="${esc(n.id)}"${app.id === n.id ? ' aria-current="page"' : ""} title="${esc(n.title)}">${icon(n.type === "work" ? "book" : n.type === "page" ? "page" : "folder")}<span>${esc(n.title)}</span></button></div>${hasChildren ? `<div id="${esc(contentsId)}"${expanded ? "" : " hidden"}>${expanded ? renderTree(n.id, depth + 1, path, branch) : ""}</div>` : ""}</div>`;
    })
    .join("");
}
function setRuntimeNotice(text, error = false) {
  const el = $("#runtime-notice");
  el.hidden = !text;
  el.textContent = text;
  el.classList.toggle("error", error);
}
async function render() {
  if (!app.state) return;
  if (app.libraryPreview.itemId && app.libraryPreview.nodeId !== app.id) clearStudioLibraryPreview();
  const token = ++app.navigation;
  let current = node();
  if (!current) {
    app.id = "library";
    current = rootNode();
  }
  await loadTemplate().catch(() => (app.template = null));
  if (token !== app.navigation) return;
  if (app.selected && !page()?.blocks.some((block) => block.id === app.selected))
    app.selected = null;
  document.title = `${current.title} — Hyperpage Studio`;
  document.body.classList.toggle("immersive-work", immersiveWork());
  renderNavigation();
  updateNavigationLayout();
  const path = ancestors(current);
  const workPosition = path.findIndex(item => item.type === "work");
  $("#breadcrumbs").innerHTML = (workPosition >= 0 ? path.slice(workPosition) : path.filter(item => item.type !== "library"))
    .map(
      (n, i, a) =>
        `${i ? '<span class="sep">/</span>' : ""}${i === a.length - 1 ? `<span class="current">${esc(n.type === "library" ? "Library" : n.title)}</span>` : `<a href="#${encodeURIComponent(n.id)}">${esc(n.type === "library" ? "Library" : n.title)}</a>`}`,
    )
    .join("");
  const templateContext = $("#studio-template-context");
  if (templateContext) {
    const templateId = current.appearance?.studioPageTemplateId;
    const pageTemplate = studioPageTemplates().find(item => item.id === templateId)
      || (!templateId ? studioPageTemplates().find(item => item.id === app.pipeline?.defaultPageTemplateId) || studioPageTemplates()[0] : null);
    templateContext.hidden = current.type !== "page" || !app.pipeline;
    templateContext.textContent = current.id === studioLandingNode()?.id ? "Work landing template"
      : pageTemplate ? `Template · ${pageTemplate.name}` : "Existing Page layout";
    templateContext.title = "Edit reusable designs in Page templates. Your Page content stays independent.";
  }
  $("#studio-button").hidden = true;
  $("#archives-button").hidden = true;
  if (app.archives?.draft && app.archives.draft.contentRevision !== app.state.revision) {
    app.archives.draft.stale = true;
    renderArchiveReview(app.archives);
  }
  $("#more-button").hidden = current.type === "library";
  $("#page-view").hidden = current.type !== "page";
  $("#work-view").hidden = !immersiveWork() || current.type === "page";
  $("#landing").hidden = current.type === "page" || immersiveWork();
  setRuntimeNotice("");
  if (current.type === "page") {
    clearWorkFrame();
    $("#landing").replaceChildren();
    renderPage(token);
  } else {
    const pageFrame = $("#page-frame");
    if (pageFrame.dataset.pageId) {
      replaceFrameDocument(pageFrame, "about:blank");
      pageFrame.hidden = true;
      delete pageFrame.dataset.pageId;
      delete pageFrame.dataset.routeId;
      delete pageFrame.dataset.generationId;
      app.frameGeneration = null;
      app.frameReady = false;
    }
    if (immersiveWork()) {
      $("#landing").replaceChildren();
      renderWorkSurface(current, token);
    } else {
      clearWorkFrame();
      renderLanding(current);
      renderPresentation(current, token);
    }
  }
  renderPanel();
  hydrateIcons();
  if (app.recovery) showStudioRecovery();
  publishStudioState();
}
function card(n, index) {
  const count = descendantPages(n).length;
  const works = descendants(n, "work").length;
  const art =
    n.cover ||
    n.appearance?.cover ||
    (n.id === "natural-world" || n.id === "animal-atlas"
      ? "/media/woodland.svg"
      : n.id === "science-lab" || n.id === "wave-lab"
        ? "/media/waves.svg"
        : null);
  return `<a class="collection-card" href="#${encodeURIComponent(studioWorkDestination(n))}" data-card-title="${esc(n.title.toLowerCase())}"><div class="card-art">${art && localMedia(art) ? `<img src="${esc(art)}" alt="${esc(n.title)} cover" loading="lazy">` : `<div class="card-empty-art">${icon(n.type === "work" ? "book" : "folder")}</div>`}<span class="card-number">${esc(label(n.type).toUpperCase())} / ${String(index + 1).padStart(2, "0")}</span></div><div class="card-body"><div class="card-kicker">${esc(label(n.type))}</div><h3 class="card-title">${esc(n.title)}</h3><p class="card-description">${esc(n.appearance?.description || (n.id === "natural-world" ? "Notes from the wild. A closer look at the world we share." : n.id === "science-lab" ? "Explore ideas through experiments, models, and a little curiosity." : `${count} ${count === 1 ? "page" : "pages"} to discover and make your own.`))}</p><div class="card-meta"><span>${works ? `${works} ${works === 1 ? label("work").toLowerCase() : plural("work").toLowerCase()} <span aria-hidden="true">·</span> ` : ""}${count} ${count === 1 ? "page" : "pages"}</span>${icon("arrow")}</div></div></a>`;
}
function recentRow(p) {
  const reference = p.blocks?.find((block) => block.id === "catalog-entry")?.values?.text || "";
  return `<a class="recent-row" data-card-title="${esc(`${p.title} ${reference}`.toLowerCase())}" href="#${encodeURIComponent(p.id)}"><span class="recent-icon">${icon("page")}</span><div><h3>${esc(p.title)}</h3><p>${esc(
    ancestors(node(p.id))
      .filter((n) => n.type !== "library" && n.type !== "page")
      .map((n) => n.title)
      .join(" / "),
  )}</p></div><span class="recent-tag">${esc(label("page"))}</span>${icon("arrow")}</a>`;
}
function allowedChildTypes(n) {
  const order = [
    "library",
    "collection",
    "series",
    "work",
    "volume",
    "part",
    "chapter",
    "page",
  ];
  if (n.type === "library") return ["collection"];
  if (n.type === "collection") return ["work", "series", "collection"];
  if (n.type === "series") return ["work"];
  if (n.type === "page") return [];
  return [
    "page",
    ...order.slice(order.indexOf(n.type) + 1).filter((t) => t !== "page"),
  ];
}
function renderLanding(n) {
  const contents = children(n.id);
  const allWorks = !app.edit && n.type === "collection";
  const folders = allWorks ? descendants(n, "work") : contents.filter((x) => x.type !== "page");
  const pages = descendantPages(n);
  const ownPages = (allWorks ? [] : contents)
    .filter((x) => x.type === "page")
    .map((x) => app.state.pages[x.id]);
  const title = n.type === "library" ? "A library of your own." : n.title;
  const heading =
    n.type === "library"
      ? `Your ${plural("collection").toLowerCase()}`
      : allWorks ? "Works" : folders.length
        ? "Explore this " + label(n.type).toLowerCase()
        : "Pages";
  const description =
    n.appearance?.description ||
    (n.type === "library"
      ? "Collect your interests. Build your knowledge. Make something worth returning to."
      : n.type === "work"
        ? "A shared design. A growing collection of ideas. All yours."
        : "Follow your curiosity, one page at a time.");
  $("#landing").innerHTML =
    `<section class="hero"><div class="eyebrow">${n.type === "library" ? "PERSONAL KNOWLEDGE, BEAUTIFULLY KEPT" : esc(label(n.type).toUpperCase())}</div><h1>${esc(title)}</h1><p>${esc(description)}</p><div class="hero-bottom"><div class="hero-stats"><span><strong>${descendants(n, "work").length}</strong>${plural("work").toLowerCase()}</span><span><strong>${pages.length}</strong>Pages</span><span>Made to grow</span></div><div class="hero-actions author-only"><button id="new-node" class="primary-button">${icon("plus")}Add ${esc(label(allowedChildTypes(n)[0]).toLowerCase())}</button></div></div></section><div id="presentation-slot"></div><div class="section-head"><h2>${esc(heading)}<span class="section-count">${folders.length || ownPages.length}</span></h2><div class="search-box">${icon("search")}<input id="collection-search" type="search" aria-label="Filter items" placeholder="Find in your library"></div></div>${folders.length ? `<div class="cards">${folders.map(card).join("")}</div>` : ""}${ownPages.length ? `<div class="recent-list">${ownPages.map(recentRow).join("")}</div>` : ""}${!(allWorks ? folders.length : contents.length) ? `<div class="empty-state"><h3>A new chapter starts here.</h3><p>Turn on Edit Mode to add your first ${n.type === "work" ? "page" : "item"}.</p></div>` : ""}`;
  $("#new-node")?.addEventListener("click", () => createNodeDialog(n));


  $("#collection-search").addEventListener("input", (event) => {
    $$("[data-card-title]").forEach(
      (card) =>
        (card.hidden = !card.dataset.cardTitle.includes(
          event.target.value.toLowerCase(),
        )),
    );
  });
}
async function renderPresentation(n, token) {
  try {
    const generation = await api(
      `/api/runtime/${encodeURIComponent(n.type === "work" ? n.id : "library")}`,
    );
    if (token !== app.navigation || !generation.proxyPrefix) return;
    const slot = $("#presentation-slot");
    if (!slot) return;
    const frame = document.createElement("iframe");
    frame.title = `${n.title} presentation`;
    frame.className = "presentation-frame";
    frame.dataset.routeId = n.id;
    frame.dataset.generationId = generation.generationId;
    const hero = $(".hero");
    hero
      .querySelectorAll(":scope > .eyebrow,:scope > h1,:scope > p")
      .forEach((el) => (el.hidden = true));
    hero.prepend(frame);
    replaceFrameDocument(frame, `${generation.proxyPrefix}browse/${encodeURIComponent(n.id)}?edit=${app.edit ? 1 : 0}`);
    slot.remove();
  } catch (error) {
    if (token === app.navigation)
      setRuntimeNotice(`Presentation is preparing: ${error.message}`, true);
  }
}
function clearWorkFrame() {
  const frame = $("#work-frame");
  if (!frame.dataset.routeId) return;
  replaceFrameDocument(frame, "about:blank");
  frame.hidden = true;
  delete frame.dataset.routeId;
  delete frame.dataset.generationId;
  app.workFrameGeneration = null;
}
function workAppearanceStyle() {
  const appearance = Object.assign({}, ...ancestors(node()).map((item) => item.appearance || {}));
  const fonts = { system: "system-ui,sans-serif", serif: "Georgia,serif", monospace: "Consolas,monospace", humanist: "Trebuchet MS,sans-serif", geometric: "Century Gothic,sans-serif" };
  return ["background", "ink", "accent"].map((key) => /^#[0-9a-f]{6}$/i.test(appearance[key] || "") ? `--work-${key}:${appearance[key]}` : "").filter(Boolean).join(";") +
    (fonts[appearance.font] ? `;--work-font:${fonts[appearance.font]}` : "");
}
function workFallbackNavigation(current) {
  const owner = work();
  const collection = [...ancestors(owner)].reverse().find((item) => item.type === "collection");
  const pages = descendantPages(owner);
  const links = pages.map((item) => {
    const group = ancestors(node(item.id)).filter((part) => part.id !== owner.id && !["library", "collection", "series", "page"].includes(part.type)).map((part) => part.title).join(" / ");
    return `<a href="#${encodeURIComponent(item.id)}"${item.id === current.id ? ' aria-current="page"' : ""}>${group ? `<small>${esc(group)}</small>` : ""}<span>${esc(item.title)}</span></a>`;
  }).join("");
  return `<nav class="work-fallback-navigation" aria-label="Work navigation"><a class="work-fallback-home" href="#${encodeURIComponent(studioWorkDestination(owner))}">${esc(owner.title)}</a><details><summary>Contents</summary><div class="work-fallback-contents">${links || '<p>No pages yet.</p>'}</div></details><a class="work-fallback-return" href="#${encodeURIComponent(collection?.id || "library")}">Back to library</a></nav>`;
}
function renderWorkFallback(current, message = "") {
  const owner = work();
  const pages = descendantPages(current);
  const cover = localMedia(current.cover || current.appearance?.cover || owner.cover || owner.appearance?.cover);
  $("#work-fallback").innerHTML = `<article class="work-fallback" style="${workAppearanceStyle()}">${workFallbackNavigation(current)}<div class="work-fallback-body">${message ? `<p class="work-fallback-notice" role="status">${esc(message)}</p>` : ""}${cover ? `<img class="work-fallback-cover" src="${esc(cover)}" alt="">` : ""}<header class="work-fallback-heading"><h1>${esc(current.title)}</h1>${current.appearance?.description || owner.appearance?.description ? `<p>${esc(current.appearance?.description || owner.appearance.description)}</p>` : ""}</header><div class="work-fallback-pages">${pages.map((item) => `<a href="#${encodeURIComponent(item.id)}"><span>${esc(item.title)}</span>${icon("arrow")}</a>`).join("") || '<p>No pages yet. Turn on Edit Mode to start this work.</p>'}</div></div></article>`;
}
function resetSurfaceHeight(frame) {
  const header = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--header-height")) || 0;
  frame.style.height = `${Math.max(1, window.innerHeight - header)}px`;
}
function setPageFrameLoading(resetReady = true) {
  const frame = $("#page-frame");
  frame.inert = true;
  frame.setAttribute("aria-busy", "true");
  if (resetReady) {
    app.frameReady = false;
    app.frameReadyPageId = null;
    app.frameReadyGenerationId = null;
  }
}
function currentPageFrameReady(id = app.id) {
  const frame = $("#page-frame");
  return !!app.state && app.id === id && node(id)?.type === "page" && !frame.hidden && !frame.inert && app.frameReady
    && frame.dataset.pageId === id && frame.dataset.routeId === id && app.frameReadyPageId === id
    && !!frame.dataset.generationId && frame.dataset.generationId === app.frameReadyGenerationId;
}
async function waitForStudioNavigation(id) {
  const deadline = performance.now() + 45000;
  while (performance.now() < deadline) {
    if (app.id === id && (node(id)?.type !== "page" || currentPageFrameReady(id))) return;
    await new Promise(resolve => setTimeout(resolve, 40));
  }
  throw new Error("The selected Page is still opening. Wait for its live canvas before editing it.");
}
function replaceFrameDocument(frame, url) {
  // Subframe src assignments add entries to the browser's joint history. Keep
  // browser and native Back/Forward focused on the host's library hash routes.
  const target = new URL(url, location.origin).href;
  if (frame.id === "page-frame") {
    // These observers belong to the outgoing document. In particular, keep no
    // parent-owned ResizeObserver attached after a Page becomes a Work/Part.
    frame.__studioCanvas?.destroy();
    frame.__studioCanvas = null;
    frame.__studioSharedTemplateGuard?.disconnect();
    frame.__studioSharedTemplateGuard = null;
    setPageFrameLoading();
  }
  frame.dataset.surfaceUrl = target;
  frame.contentWindow.location.replace(target);
}
async function renderWorkSurface(current, token) {
  const frame = $("#work-frame");
  const fallback = $("#work-fallback");
  if (frame.dataset.routeId !== current.id) {
    frame.hidden = true;
    fallback.hidden = false;
    renderWorkFallback(current, "Opening this work…");
  }
  try {
    const generation = await api(`/api/runtime/${encodeURIComponent(work().id)}`);
    if (token !== app.navigation) return;
    if (!generation.proxyPrefix) throw new Error(generation.error || "The work renderer is unavailable.");
    if (frame.dataset.routeId === current.id && frame.dataset.generationId === generation.generationId && !frame.hidden) return;
    app.workFrameGeneration = generation.generationId;
    frame.dataset.routeId = current.id;
    frame.dataset.generationId = generation.generationId;
    frame.title = current.title;
    resetSurfaceHeight(frame);
    replaceFrameDocument(frame, `${generation.proxyPrefix}browse/${encodeURIComponent(current.id)}?edit=0`);
    frame.hidden = false;
    fallback.hidden = true;
  } catch (error) {
    if (token !== app.navigation) return;
    frame.hidden = true;
    fallback.hidden = false;
    renderWorkFallback(current, "The work design is preparing. Your pages are available here.");
  }
}
async function renderPage(token) {
  const p = page();
  const frame = $("#page-frame");
  const samePage = frame.dataset.pageId === p.id && !frame.hidden;
  setPageFrameLoading();
  if (!samePage) {
    frame.hidden = true;
    $("#page-fallback").hidden = false;
    $("#page-fallback").innerHTML =
      '<div class="loading">Opening this work…</div>';
  }
  try {
    const generation = await api(
      `/api/runtime/${encodeURIComponent(p.workId)}`,
    );
    if (token !== app.navigation) return;
    if (!generation.proxyPrefix)
      throw new Error(generation.error || "The work renderer is unavailable.");
    if (
      samePage &&
      app.frameGeneration === generation.generationId &&
      frame.dataset.edit === String(app.edit)
    ) {
      sendFrame({ kind: "refresh", pageId: p.id });
      return;
    }
    if (samePage) app.restoreScroll = { x: window.scrollX, y: window.scrollY };
    app.frameGeneration = generation.generationId;
    app.frameReady = false;
    frame.dataset.pageId = p.id;
    frame.dataset.routeId = p.id;
    frame.dataset.generationId = generation.generationId;
    frame.dataset.edit = String(app.edit);
    if (immersiveWork()) resetSurfaceHeight(frame);
    replaceFrameDocument(frame, `${generation.proxyPrefix}page/${encodeURIComponent(p.id)}?edit=${app.edit ? 1 : 0}`);
    $("#page-frame").hidden = false;
    $("#page-fallback").hidden = true;
    if (generation.error) setRuntimeNotice(generation.error, true);
  } catch (error) {
    if (token !== app.navigation) return;
    frame.hidden = true;
    $("#page-fallback").hidden = false;
    setRuntimeNotice(
      `The interactive renderer could not open. Content remains available below. ${error.message}`,
      true,
    );
    renderFallback();
  }
}
function renderFallback() {
  const p = page();
  const appearance = Object.assign({}, ...ancestors(node()).map((item) => item.appearance || {}));
  const colors = ["background", "ink", "accent"].map((key) => /^#[0-9a-f]{6}$/i.test(appearance[key] || "") ? `--fallback-${key}:${appearance[key]}` : "").filter(Boolean).join(";");
  $("#page-fallback").innerHTML =
    `<article class="fallback-page${immersiveWork() ? " work-fallback" : ""}" style="${colors};${workAppearanceStyle()}">${immersiveWork() ? workFallbackNavigation(node()) + '<p class="work-fallback-notice" role="status">The work design is preparing. Your content is available here.</p>' : ""}<div class="fallback-grid">${p.blocks.map((b) => `<section class="fallback-block ${app.selected === b.id ? "selected" : ""}" style="${fallbackStyle(b)}" data-select-block="${esc(b.id)}" ${app.edit ? 'tabindex="0" role="button" aria-label="Select element"' : ""}>${fallbackBlock(b)}</section>`).join("")}</div></article>`;
}
function fallbackStyle(block) {
  const values = block.values || {};
  const spans = { full: 6, half: 3, third: 2, "two-thirds": 4 };
  const fonts = { system: "system-ui,sans-serif", serif: "Georgia,serif", monospace: "Consolas,monospace", humanist: "Trebuchet MS,sans-serif", geometric: "Century Gothic,sans-serif" };
  return `--block-span:${spans[values.span] || 6};${fonts[values.font] ? `font-family:${fonts[values.font]};` : ""}${["left", "center", "right", "justify"].includes(values.align) ? `text-align:${values.align};` : ""}${/^#[0-9a-f]{6}$/i.test(values.color || "") ? `color:${values.color};` : ""}`;
}
function sharedLayoutRegion(block) {
  return block?.type === "studio-header-row" ? "Header"
    : block?.type === "studio-body-row" ? "Body"
      : block?.type === "studio-footer-row" ? "Footer"
        : block?.type === "studio-navigation-layout" ? "Navigation" : null;
}
function isPrivateStudioBlockType(type) {
  return ["studio-header-row", "studio-body-row", "studio-footer-row", "studio-navigation-layout"].includes(type);
}
function fallbackBlock(b) {
  const v = b.values || {};
  const region = sharedLayoutRegion(b);
  if (region)
    return `<p><strong>Shared ${region.toLowerCase()}</strong></p><p>Edit this layout in Templates → ${region}.</p>`;
  if (["header", "banner"].includes(b.type))
    return `${v.eyebrow ? `<p class="eyebrow">${esc(v.eyebrow)}</p>` : ""}<h1>${esc(v.title)}</h1><p>${esc(v.text)}</p>${b.type === "banner" && localMedia(v.src) ? `<img src="${esc(localMedia(v.src))}" alt="${esc(v.alt)}">` : ""}`;
  if (b.type === "footer") return `<footer>${esc(v.text)} ${safeUrl(v.url) ? `<a href="${esc(safeUrl(v.url))}" target="_blank" rel="noopener noreferrer">${esc(v.label || v.url)}</a>` : ""}</footer>`;
  if (b.type === "divider") return `<hr><small>${esc(v.label)}</small>`;
  if (b.type === "image")
    return `<figure>${localMedia(v.src || v.url) ? `<img src="${esc(v.src || v.url)}" alt="${esc(v.alt)}">` : ""}<figcaption>${esc(v.caption)}</figcaption></figure>`;
  if (["audio", "video"].includes(b.type))
    return `<${b.type} controls src="${esc(localMedia(v.src || v.url))}"></${b.type}><p>${esc(v.caption)}</p>`;
  if (b.type === "callout")
    return `<div class="callout"><strong>${esc(v.title || "Note")}</strong><p>${esc(v.text || v.body)}</p></div>`;
  if (b.type === "heading") return `<h2>${esc(v.text)}</h2>`;
  if (
    ["wave", "counter", "calculator", "csharp-counter", "wave-lab"].includes(
      b.type,
    )
  )
    return `<p><strong>${esc(app.template?.blocks.find((x) => x.id === b.type)?.title || b.type)}</strong> — interactive element requires the work renderer.</p>`;
  return `<p>${esc(v.text || v.content || v.body || v.title || "").replace(/\n/g, "<br>")}</p>`;
}
function sendFrame(message) {
  const frame = $("#page-frame");
  if (frame && !frame.hidden) {
    if (message.kind === "refresh") setPageFrameLoading();
    frame.contentWindow.postMessage(
      { source: "hyperlib-editor", blockId: app.selected, ...message },
      location.origin,
    );
  }
}
async function flushFrame() {
  if (!app.edit || $("#page-frame").hidden) return;
  if (app.frameFlushing) return app.frameFlushing;
  const pending = (async () => {
    if (!app.frameReady) await new Promise((resolve, reject) => {
      const ready = () => { clearTimeout(timer); app.frameReadyWaiters.delete(ready); resolve(); };
      const timer = setTimeout(() => { app.frameReadyWaiters.delete(ready); reject(new Error("The live Page is still opening. Wait for it to finish before saving.")); }, 10000);
      app.frameReadyWaiters.add(ready);
    });
    const requestId = crypto.randomUUID();
    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        app.frameFlushes.delete(requestId);
        reject(new Error("The page is still saving its inline edit. Please try again when it is ready."));
      }, 5000);
      app.frameFlushes.set(requestId, () => { clearTimeout(timer); app.frameDirty = false; resolve(); });
      sendFrame({ kind: "flush", requestId });
    });
  })();
  app.frameFlushing = pending;
  try { await pending; } finally { if (app.frameFlushing === pending) app.frameFlushing = null; }
}
const studioGeometryFieldTypes = { studioPositioned: "boolean", studioX: "number", studioY: "number", studioWidth: "number", studioHeight: "number" };
const inspectorPanels = ["properties", "appearance", "actions", "code"];
const studioElementEditor = window.createStudioElementEditor({
  escape: esc, page, selected: () => app.selected, editing: () => app.edit,
  selectOwner: id => { app.selected = id; }, render: () => renderStudioLeftPanel(),
  refreshHandles: () => $("#page-frame").__studioCanvas?.refresh(),
  stage: (id, value) => editBlock(id, "studioElements", value), save: flushDraft, toast,
  pendingCode: key => app.invalidFields.get(key),
  pendingPath: id => [...app.invalidFields.keys()].find(key => key.startsWith(id + ":studio-code:"))?.slice((id + ":studio-code:").length),
  codeDraft: (key, value) => { beginDraft(); app.invalidFields.set(key, value); status("Unapplied code — choose Apply or Reset editor", "dirty"); publishStudioState(); },
  clearCode: key => { app.invalidFields.delete(key); if (app.draft && !app.invalidFields.size && JSON.stringify(app.draft) === JSON.stringify(app.state.pages[app.draft.id])) app.draft = null; status(app.draft ? "Unsaved changes" : "Saved locally", app.draft ? "dirty" : ""); publishStudioState(); }
});
function supportsStudioCanvasGeometry(definition) {
  return Object.entries(studioGeometryFieldTypes).every(([id, type]) => definition?.fields.some(field => field.id === id && field.type === type));
}
// The iframe is the same Work renderer used by Hyperlib. Studio adds editing
// handles around its saved blocks; the published renderer owns their geometry.
// No pointer gesture changes template source or bypasses revision-checked saves.
function installStudioCanvas(frame) {
  frame.__studioCanvas?.destroy();
  const doc = frame.contentDocument;
  const frameWindow = doc?.defaultView;
  if (!doc?.body || !frameWindow || !app.edit) return;
  const surface = doc.querySelector(".hl-document");
  if (!surface) return;
  const geometryFields = Object.keys(studioGeometryFieldTypes);
  const style = doc.createElement("style");
  style.id = "studio-canvas-tools";
  style.textContent = `
    [data-studio-canvas-overlay]{position:absolute;z-index:2147482000;box-sizing:border-box;pointer-events:none;border:1px solid var(--hl-accent,#a7d7c5);outline:1px solid #08100d80}
    [data-studio-canvas-overlay][hidden]{display:none!important}
    [data-studio-canvas-overlay] button{position:absolute;display:grid;place-items:center;padding:0;min-width:0;min-height:0;line-height:1;box-sizing:border-box;pointer-events:auto;touch-action:none;color:#10201a;background:var(--hl-accent,#a7d7c5);border:1px solid #10201a;border-radius:2px;font:14px system-ui;user-select:none}
    [data-studio-canvas-overlay] button:focus-visible{outline:2px solid var(--hl-accent,#a7d7c5);outline-offset:3px}
    [data-studio-canvas-overlay] [data-studio-handle="move"]{width:28px;height:24px;left:3px;top:-28px;cursor:move;box-shadow:0 1px 5px #0003}
    [data-studio-canvas-overlay] [data-studio-handle]:not([data-studio-handle="move"]){width:10px;height:10px}
    [data-studio-canvas-overlay] [data-studio-handle="n"]{left:calc(50% - 5px);top:-5px;cursor:ns-resize}
    [data-studio-canvas-overlay] [data-studio-handle="s"]{left:calc(50% - 5px);bottom:-5px;cursor:ns-resize}
    [data-studio-canvas-overlay] [data-studio-handle="e"]{right:-5px;top:calc(50% - 5px);cursor:ew-resize}
    [data-studio-canvas-overlay] [data-studio-handle="w"]{left:-5px;top:calc(50% - 5px);cursor:ew-resize}
    [data-studio-canvas-overlay] [data-studio-handle="ne"]{right:-5px;top:-5px;cursor:nesw-resize}
    [data-studio-canvas-overlay] [data-studio-handle="nw"]{left:-5px;top:-5px;cursor:nwse-resize}
    [data-studio-canvas-overlay] [data-studio-handle="se"]{right:-5px;bottom:-5px;cursor:nwse-resize}
    [data-studio-canvas-overlay] [data-studio-handle="sw"]{left:-5px;bottom:-5px;cursor:nesw-resize}
    body.studio-canvas-gesture,body.studio-canvas-gesture *{user-select:none!important;cursor:var(--studio-gesture-cursor,move)!important}
  `;
  doc.head.append(style);
  const overlay = doc.createElement("div");
  overlay.dataset.studioCanvasOverlay = "";
  overlay.hidden = true;
  const labels = { move: "Move element", n: "Resize top edge", s: "Resize bottom edge", e: "Resize right edge", w: "Resize left edge", ne: "Resize top right corner", nw: "Resize top left corner", se: "Resize bottom right corner", sw: "Resize bottom left corner" };
  for (const [handle, label] of Object.entries(labels)) {
    const button = doc.createElement("button");
    button.type = "button"; button.dataset.studioHandle = handle;
    button.setAttribute("aria-label", label); button.title = label;
    if (handle === "move") button.textContent = "✥";
    overlay.append(button);
  }
  doc.body.append(overlay);
  let gesture = null, scheduled = 0, disposed = false;
  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
  const rounded = value => Math.round(value * 100) / 100;
  function currentTarget() {
    const block = page()?.blocks.find(item => item.id === app.selected);
    const definition = app.template?.blocks.find(item => item.id === block?.type);
    const nested = studioElementEditor.context();
    if (block && nested?.path && nested.element.parentElement)
      return { block, element: nested.element, parent: nested.element.parentElement, nested };
    if (!block || sharedLayoutRegion(block) || !supportsStudioCanvasGeometry(definition)) return null;
    // Resolve only the saved outer block, never a nested tool's similarly named ID.
    const element = [...surface.querySelectorAll("[data-block-id]")].find(item => item.dataset.blockId === block.id && item.closest(".hl-region"));
    if (!element || element.closest("[data-studio-shared-template]")) return null;
    const parent = element.closest(".hl-region");
    return { block, element, parent };
  }
  function refresh() {
    if (disposed || gesture) return;
    const target = currentTarget();
    overlay.hidden = !target;
    if (!target) return;
    const rect = target.element.getBoundingClientRect();
    overlay.style.left = `${rect.left + frameWindow.scrollX}px`;
    overlay.style.top = `${rect.top + frameWindow.scrollY}px`;
    overlay.style.width = `${rect.width}px`;
    overlay.style.height = `${rect.height}px`;
    overlay.dataset.studioCanvasBlockId = target.block.id;
  }
  function schedule() {
    if (!disposed && !scheduled) scheduled = frameWindow.requestAnimationFrame(() => { scheduled = 0; refresh(); });
  }
  function readGeometry(target) {
    const rect = target.element.getBoundingClientRect(), bounds = target.parent.getBoundingClientRect();
    const width = target.parent.clientWidth;
    if (!width || !rect.width || !rect.height) return null;
    return { x: clamp(rect.left - bounds.left - target.parent.clientLeft, 0, width),
      y: clamp(rect.top - bounds.top - target.parent.clientTop, 0, 100000),
      width: Math.min(width, rect.width), height: Math.min(100000, rect.height), parentWidth: width };
  }
  function apply(target, geometry) {
    if (target.nested) {
      const runtime = frameWindow.hyperlibStudioElements;
      const parentPath = runtime.path(target.nested.owner, target.parent);
      if (parentPath !== null && frameWindow.getComputedStyle(target.parent).position === "static")
        studioElementEditor.patch({ styles: { position: "relative" } }, { ...target.nested, element: target.parent, path: parentPath });
      studioElementEditor.patch({ styles: { position: "absolute", left: `${rounded(geometry.x)}px`, top: `${rounded(geometry.y)}px`,
        width: `${rounded(geometry.width)}px`, height: `${rounded(geometry.height)}px`, "box-sizing": "border-box", margin: "0" } }, target.nested);
      const rect = target.element.getBoundingClientRect();
      Object.assign(overlay.style, { left: `${rect.left + frameWindow.scrollX}px`, top: `${rect.top + frameWindow.scrollY}px`, width: `${rect.width}px`, height: `${rect.height}px` });
      return;
    }
    const x = rounded(geometry.x / geometry.parentWidth * 100), width = rounded(geometry.width / geometry.parentWidth * 100);
    const values = { studioPositioned: true, studioX: clamp(x, 0, 100), studioY: rounded(clamp(geometry.y, 0, 100000)),
      studioWidth: clamp(width, 1, 100), studioHeight: rounded(clamp(geometry.height, 1, 100000)) };
    const draft = beginDraft(), block = draft.blocks.find(item => item.id === target.block.id);
    if (!block) return;
    Object.assign(block.values, values);
    target.element.classList.add("studio-canvas-positioned");
    target.element.dataset.studioSized = "true";
    target.parent.style.position = "relative";
    target.parent.style.minHeight = `${Math.max(gesture?.parentHeight || 0, geometry.y + geometry.height)}px`;
    Object.assign(target.element.style, { position: "absolute", left: `${values.studioX}%`, top: `${values.studioY}px`,
      width: `${values.studioWidth}%`, height: `${values.studioHeight}px`, margin: "0", maxWidth: "100%" });
    const rect = target.element.getBoundingClientRect();
    overlay.style.left = `${rect.left + frameWindow.scrollX}px`;
    overlay.style.top = `${rect.top + frameWindow.scrollY}px`;
    overlay.style.width = `${rect.width}px`;
    overlay.style.height = `${rect.height}px`;
    publishStudioState();
  }
  function adjusted(base, handle, dx, dy) {
    let left = base.x, top = base.y, right = base.x + base.width, bottom = base.y + base.height;
    const minWidth = Math.min(base.parentWidth, Math.max(16, base.parentWidth * .01));
    if (handle === "move") {
      left = clamp(base.x + dx, 0, base.parentWidth - base.width); top = clamp(base.y + dy, 0, 100000 - base.height);
      right = left + base.width; bottom = top + base.height;
    } else {
      if (handle.includes("w")) left = clamp(base.x + dx, 0, right - minWidth);
      if (handle.includes("e")) right = clamp(base.x + base.width + dx, left + minWidth, base.parentWidth);
      if (handle.includes("n")) top = clamp(base.y + dy, 0, bottom - 16);
      if (handle.includes("s")) bottom = clamp(base.y + base.height + dy, top + 16, 100000);
    }
    return { x: left, y: top, width: right - left, height: bottom - top, parentWidth: base.parentWidth };
  }
  function finish(cancelled = false) {
    if (!gesture) return;
    const previous = gesture; gesture = null;
    app.canvasGesture = false;
    doc.body.classList.remove("studio-canvas-gesture"); doc.body.style.removeProperty("--studio-gesture-cursor");
    try { previous.button.releasePointerCapture(previous.pointerId); } catch { }
    if (cancelled || !previous.changed) {
      previous.target.element.setAttribute("style", previous.elementStyle);
      previous.target.element.classList.toggle("studio-canvas-positioned", previous.wasPositionedClass);
      if (previous.sizedAttribute === null) previous.target.element.removeAttribute("data-studio-sized");
      else previous.target.element.setAttribute("data-studio-sized", previous.sizedAttribute);
      previous.target.parent.setAttribute("style", previous.parentStyle);
      const draftBlock = app.draft?.blocks.find(item => item.id === previous.target.block.id);
      if (draftBlock && previous.target.nested) {
        if (previous.elementEdits === undefined) delete draftBlock.values.studioElements;
        else draftBlock.values.studioElements = previous.elementEdits;
        frameWindow.hyperlibStudioElements.apply(previous.target.nested.owner, JSON.parse(previous.elementEdits || "[]"));
      }
      if (draftBlock) for (const field of geometryFields) {
        if (Object.hasOwn(previous.geometryValues, field)) draftBlock.values[field] = previous.geometryValues[field];
        else delete draftBlock.values[field];
      }
      if (app.draft && JSON.stringify(app.draft) === JSON.stringify(app.state.pages[app.draft.id])) app.draft = null;
      status(app.draft ? "Unsaved changes" : "Saved locally", app.draft ? "dirty" : "");
      if (app.draft) app.timer = setTimeout(() => flushDraft().catch(() => {}), 600);
      schedule(); return;
    }
    status("Saving…", "dirty");
    renderPanel();
    // One saved Page revision per gesture also makes Undo restore the whole move.
    flushDraft().catch(error => toast(error.message, true)).finally(schedule);
  }
  function pointerDown(event) {
    const button = event.target.closest?.("[data-studio-handle]");
    if (!button || event.button !== 0 || gesture || app.saving || app.recovery || app.invalidFields.size) return;
    const target = currentTarget(), geometry = target && readGeometry(target);
    if (!geometry) return;
    event.preventDefault(); event.stopPropagation();
    // Pointer default prevention also prevents focus from leaving a sidebar
    // breadcrumb. Keep Escape and keyboard resizing in this canvas document.
    button.focus({ preventScroll: true });
    clearTimeout(app.timer);
    beginDraft(); app.canvasGesture = true;
    gesture = { target, base: geometry, x: event.clientX, y: event.clientY, pointerId: event.pointerId,
      elementEdits: target.block.values.studioElements,
      handle: button.dataset.studioHandle, button, changed: false,
      geometryValues: Object.fromEntries(geometryFields.filter(field => Object.hasOwn(target.block.values, field)).map(field => [field, target.block.values[field]])),
      wasPositionedClass: target.element.classList.contains("studio-canvas-positioned"),
      sizedAttribute: target.element.getAttribute("data-studio-sized"),
      elementStyle: target.element.getAttribute("style") || "", parentStyle: target.parent.getAttribute("style") || "",
      parentHeight: target.parent.getBoundingClientRect().height };
    button.setPointerCapture(event.pointerId);
    doc.body.classList.add("studio-canvas-gesture");
    doc.body.style.setProperty("--studio-gesture-cursor", button.dataset.studioHandle === "move" ? "move" : frameWindow.getComputedStyle(button).cursor);
  }
  function pointerMove(event) {
    if (!gesture || event.pointerId !== gesture.pointerId) return;
    event.preventDefault();
    const dx = event.clientX - gesture.x, dy = event.clientY - gesture.y;
    if (!gesture.changed && Math.abs(dx) + Math.abs(dy) < 2) return;
    gesture.changed = true;
    apply(gesture.target, adjusted(gesture.base, gesture.handle, dx, dy));
  }
  function keyDown(event) {
    if (event.key === "Escape" && gesture) { event.preventDefault(); finish(true); return; }
    const button = event.target.closest?.("[data-studio-handle]");
    if (!button || !["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key) || gesture || app.saving || app.recovery || app.invalidFields.size) return;
    const target = currentTarget(), geometry = target && readGeometry(target);
    if (!geometry) return;
    event.preventDefault(); event.stopPropagation(); clearTimeout(app.timer);
    const step = event.shiftKey ? 10 : 1;
    const dx = event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0;
    const dy = event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0;
    apply(target, adjusted(geometry, button.dataset.studioHandle, dx, dy));
    status("Saving…", "dirty");
    app.timer = setTimeout(() => { renderPanel(); flushDraft().catch(error => toast(error.message, true)).finally(schedule); }, 350);
  }
  const onPointerUp = event => { if (gesture && event.pointerId === gesture.pointerId) finish(); };
  const onPointerCancel = event => { if (gesture && event.pointerId === gesture.pointerId) finish(true); };
  overlay.addEventListener("pointerdown", pointerDown);
  overlay.addEventListener("pointermove", pointerMove);
  overlay.addEventListener("pointerup", onPointerUp);
  overlay.addEventListener("pointercancel", onPointerCancel);
  overlay.addEventListener("lostpointercapture", onPointerCancel);
  doc.addEventListener("keydown", keyDown, true);
  frameWindow.addEventListener("scroll", schedule);
  frameWindow.addEventListener("resize", schedule);
  const observer = new MutationObserver(schedule);
  observer.observe(surface, { childList: true, subtree: true, attributes: true, attributeFilter: ["class", "data-selected"] });
  const resizeObserver = new ResizeObserver(schedule); resizeObserver.observe(surface);
  frame.__studioCanvas = { refresh: schedule, destroy: () => {
    disposed = true;
    if (scheduled) { frameWindow.cancelAnimationFrame(scheduled); scheduled = 0; }
    observer.disconnect(); resizeObserver.disconnect();
    if (gesture) finish(true);
    doc.removeEventListener("keydown", keyDown, true);
    frameWindow.removeEventListener("scroll", schedule); frameWindow.removeEventListener("resize", schedule);
    overlay.remove(); style.remove();
  } };
  schedule();
}
window.addEventListener("message", (event) => {
  if (
    event.origin !== location.origin ||
    event.data?.source !== "hyperlib-renderer"
  )
    return;
  const frame = $$("iframe").find((f) => f.contentWindow === event.source);
  if (!frame) return;
  const m = event.data;
  const routeId = frame.dataset.routeId || frame.dataset.pageId;
  if (frame.hidden || !routeId || routeId !== node()?.id || m.pageId !== routeId ||
      (frame.dataset.generationId && m.generationId !== frame.dataset.generationId)) return;
  if (m.kind === "height")
    frame.style.height = `${Math.max(immersiveWork() ? Math.max(1, window.innerHeight - (parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--header-height")) || 0)) : 120, Math.min(30000, Number(m.height) || 300))}px`;
  if (m.kind === "navigate" && typeof m.nodeId === "string" && node(m.nodeId)) navigate(m.nodeId).catch((error) => toast(error.message, true));
  if (m.kind === "ready" && frame.id === "page-frame") {
    app.frameReady = true;
    app.frameReadyPageId = m.pageId;
    app.frameReadyGenerationId = m.generationId;
    // Empty Phase 2 slots need readable editing hints before the author writes.
    // This touches only edit-mode placeholder styling, never the saved design.
    const document = frame.contentDocument;
    if (document?.head && !document.getElementById("studio-slot-hints")) {
      const hints = document.createElement("style");
      hints.id = "studio-slot-hints";
      hints.textContent = ".hl-editing [data-field]:empty:before{opacity:.7!important}";
      document.head.append(hints);
    }
    if (document?.body) {
      frame.__studioSharedTemplateGuard?.disconnect();
      const protect = () => document.querySelectorAll('[data-studio-shared-template] [data-field]').forEach(field => {
        if (field.contentEditable !== 'false') field.contentEditable = 'false';
        if (field.spellcheck) field.spellcheck = false;
      });
      protect();
      frame.__studioSharedTemplateGuard = new MutationObserver(protect);
      frame.__studioSharedTemplateGuard.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['contenteditable'] });
    }
    installStudioCanvas(frame);
    studioElementEditor.attach(frame);
    if (app.restoreScroll) {
      const position = app.restoreScroll;
      requestAnimationFrame(() => window.scrollTo(position.x, position.y));
      app.restoreScroll = null;
    }
    sendFrame({ kind: "select", blockId: app.selected });
    frame.inert = false;
    frame.removeAttribute("aria-busy");
    for (const ready of [...app.frameReadyWaiters]) ready();
  }
  if (frame.id === "page-frame" && m.pageId === app.id) {
    if (m.kind === "dirty" && (m.blockId == null || page()?.blocks.some(block => block.id === m.blockId && !sharedLayoutRegion(block))))
      app.frameDirty = true;
    if (m.kind === "flushed") {
      app.frameFlushes.get(m.requestId)?.();
      app.frameFlushes.delete(m.requestId);
    }
    if (m.kind === "history" && app.edit && ["undo", "redo"].includes(m.action))
      historyAction(m.action);
  }
  if (
    m.kind === "select" &&
    app.edit &&
    frame.id === "page-frame" &&
    m.pageId === app.id
  ) {
    let selected = page()?.blocks.find(block => block.id === m.blockId);
    if (!selected && typeof m.blockId === "string") {
      // A rendered library component has its own stable instance ID. Resolve selection through
      // this trusted frame's actual DOM to its owning saved layout, never to a guessed ID.
      const inner = [...(frame.contentDocument?.querySelectorAll('[data-studio-shared-template] [data-block-id]') || [])]
        .find(element => element.dataset.blockId === m.blockId);
      const owner = inner?.closest('[data-block-type="studio-header-row"][data-block-id],[data-block-type="studio-body-row"][data-block-id],[data-block-type="studio-footer-row"][data-block-id],[data-block-type="studio-navigation-layout"][data-block-id]');
      selected = page()?.blocks.find(block => block.id === owner?.dataset.blockId && sharedLayoutRegion(block));
    }
    if (!selected) return;
    const changed = app.selected !== selected.id;
    app.selected = selected.id;
    // A selection updates the active tool's context; it is not a request to
    // replace the user's chosen tool (or reopen a deliberately closed panel).
    if (!app.workspaceChrome?.leftPanel) { openStudioProperties(); renderPanel(); }
    else if (changed) renderStudioLeftPanel();
    frame.__studioCanvas?.refresh();
    if (selected.id !== m.blockId) sendFrame({ kind: "select", blockId: selected.id });
  }
  if (
    m.kind === "change" &&
    app.edit &&
    frame.id === "page-frame" &&
    m.pageId === app.id
  ) {
    if (!studioElementEditor.consumeInlineChange(m.blockId, m.field)) editBlock(m.blockId, m.field, m.value);
  }
});
function beginDraft() {
  if (!app.draft) app.draft = structuredClone(app.state.pages[app.id]);
  return app.draft;
}
function editBlock(id, field, value) {
  if (!app.state.pages[app.id] || app.libraryMutation) return;
  // Shared structure belongs to the template builder. Never turn an inline renderer
  // message into a replacement for its serialized, reusable layout.
  const target = page()?.blocks.find((b) => b.id === id);
  if (!target || (sharedLayoutRegion(target) && field !== "studioElements")) return;
  const draft = beginDraft();
  const block = draft.blocks.find((b) => b.id === id);
  if (!block) return;
  if (value === undefined) delete block.values[field];
  else block.values[field] = value;
  // Inline edits arrive from the trusted Work frame. Keep the already-open
  // inspector current without rebuilding its DOM or interrupting field typing.
  if (app.selected === id && app.leftPanel === "inspector" && !app.invalidFields.has(`${id}:${field}`)) {
    const control = $$("[data-field]", $("#properties-content")).find(input => input.dataset.field === field);
    if (control && control !== document.activeElement) {
      if (control.type === "checkbox") control.checked = value === true;
      else control.value = Array.isArray(value) ? JSON.stringify(value, null, 2) : value ?? "";
    }
  }
  status("Saving…", "dirty");
  clearTimeout(app.timer);
  if (!app.canvasGesture) app.timer = setTimeout(() => flushDraft().catch(() => {}), 600);
}
async function flushDraft() {
  if (app.canvasGesture) throw new Error("Finish moving or resizing the element before saving this Page.");
  if (app.invalidFields.size) throw new Error("Correct the invalid object field before saving this Page. Its draft is preserved.");
  try { await flushFrame(); }
  catch (error) { toast(error.message, true); throw error; }
  clearTimeout(app.timer);
  if (app.saving) return app.saving;
  if (!app.draft) return;
  app.saving = (async () => {
    while (app.draft) {
      const draft = structuredClone(app.draft);
      try {
        await mutate(
          [{ kind: "savePage", id: draft.id, page: draft }],
          `Edit ${draft.title}`,
          false,
        );
        if (JSON.stringify(app.draft) === JSON.stringify(draft))
          app.draft = null;
        else if (app.draft?.id === draft.id)
          app.draft.revision = app.state.pages[draft.id].revision;
        // A new keystroke may arrive while the save request is in flight.
        // Capture it before replacing any renderer DOM with the saved page.
        if (app.frameDirty) await flushFrame();
        if (!app.draft) {
          status();
          sendFrame({ kind: "refresh", pageId: draft.id });
          if (app.runtimePending) {
            app.runtimePending = false;
            await render();
          }
        }
      } catch (error) {
        status("Changes need attention", "error");
        toast(error.message, true);
        if (app.frameDirty) throw error;
        showConflict();
        break;
      }
    }
  })();
  try {
    await app.saving;
  } finally {
    app.saving = null;
  }
}
function mutate(operations, description, rerender = true) {
  const task = async () => {
    status("Saving…", "dirty");
    const state = await api("/api/operations", {
      method: "POST",
      body: JSON.stringify({
        expectedRevision: app.state.revision,
        description,
        operations,
      }),
    });
    app.state = state;
    status();
    if (rerender) await render();
    return state;
  };
  const result = app.mutation.then(task);
  app.mutation = result.catch(() => {});
  return result;
}
function showConflict() {
  const notice = $("#runtime-notice");
  notice.hidden = false;
  notice.classList.add("error");
  notice.replaceChildren(
    document.createTextNode(
      "Your unsaved draft is preserved. Another edit or template change prevented saving. ",
    ),
  );
  const download = document.createElement("button");
  download.className = "quiet-button";
  download.textContent = "Download draft";
  download.onclick = () => {
    const a = document.createElement("a");
    a.href = URL.createObjectURL(
      new Blob([JSON.stringify(app.draft, null, 2)], {
        type: "application/json",
      }),
    );
    a.download = `${app.draft.id}.draft.json`;
    a.click();
    URL.revokeObjectURL(a.href);
  };
  const reload = document.createElement("button");
  reload.className = "quiet-button";
  reload.textContent = "Reload saved version";
  reload.onclick = () =>
    openDialog(
      "Reload the saved page?",
      '<p class="muted">Download your draft first if you want to keep it. Reloading replaces the unsaved draft in this tab.</p>',
      "Reload",
      async () => {
        app.draft = null;
        app.invalidFields.clear();
        await loadState();
        await render();
      },
    );
  notice.append(download, reload);
}
async function historyAction(kind) {
  if (!app.edit) return;
  if (app.libraryMutation) { toast("Wait for the library action to finish."); return; }
  try {
    await flushDraft();
    if (app.draft) return;
    app.state = await api(`/api/${kind}`, {
      method: "POST",
      body: JSON.stringify({ expectedRevision: app.state.revision }),
    });
    app.templates.clear();
    await render();
    toast(kind === "undo" ? "Change undone." : "Change restored.");
  } catch (error) {
    toast(error.message, true);
  }
}
function renderPanel() {
  if (!app.edit) return;
  renderStudioLeftPanel();
  renderStudioLibrary();
}
function openStudioProperties() {
  app.leftPanel = "inspector";
  if (app.workspaceChrome?.leftPanel) {
    if (app.library.busy || app.libraryMutation) {
      app.deferredWorkspacePanel = { id: "properties", blockId: app.selected };
      return;
    }
    if (app.workspaceChrome.leftPanel.selectedId === "properties" && app.workspaceChrome.leftPanel.isOpen) return;
    sendWorkspacePanelAction("panel-select", "left", "properties", $("#studio-left-panel-selector"));
    renderStudioLeftPanel(); return;
  }
  app.navOpen = true; app.navCollapsed = false;
  if (matchMedia("(max-width: 960px)").matches) {
    $("#app-layout").classList.add("inspector-hidden"); $("#studio-inspector-toggle").setAttribute("aria-pressed", "false");
  }
  updateNavigationLayout();
}
function renderStudioLeftPanel() {
  if (app.workspaceChrome?.leftPanel && app.workspaceChrome?.rightPanel) {
    $("#studio-contents").hidden = app.workspaceChrome.leftPanel.selectedId !== "contents";
    const propertiesVisible = inspectorPanels.includes(app.workspaceChrome.leftPanel.selectedId);
    $("#properties-content").hidden = !propertiesVisible;
    $("#studio-save-page-template").disabled = !page() || !!app.libraryMutation;
    if (propertiesVisible) { app.leftPanel = "inspector"; renderInspector(); }
    return;
  }
  $$("[data-left-panel]").forEach(button => {
    const active = button.dataset.leftPanel === app.leftPanel;
    button.classList.toggle("active", active); button.setAttribute("aria-selected", String(active));
  });
  $("#studio-contents").hidden = app.leftPanel !== "contents";
  $("#properties-content").hidden = app.leftPanel !== "inspector";
  $("#studio-save-page-template").disabled = !page() || !!app.libraryMutation;
  if (app.leftPanel === "inspector") renderInspector();
}
const studioLibraryRequests = new Map();
function receiveStudioLibraryResult(message) {
  if (message?.type !== "studio:library-result" || typeof message.requestId !== "string") return;
  const request = studioLibraryRequests.get(message.requestId);
  if (!request) return;
  studioLibraryRequests.delete(message.requestId); clearTimeout(request.timer);
  if (message.success === true) request.resolve(message.result);
  else request.reject(new Error(typeof message.error === "string" ? message.error : "The library action could not finish."));
}
window.chrome?.webview?.addEventListener?.("message", event => receiveStudioLibraryResult(event.data));
window.addEventListener("studio:library-result", event => receiveStudioLibraryResult(event.detail));
function studioLibraryRequest(action, payload = {}) {
  if (!window.chrome?.webview?.postMessage) return Promise.reject(new Error("Open this project in Hyperpage Studio to browse the full library."));
  const requestId = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { studioLibraryRequests.delete(requestId); reject(new Error("The library action is still preparing. Please try again when the project is ready.")); }, 180000);
    studioLibraryRequests.set(requestId, { resolve, reject, timer });
    try { studioMessage("studio:library-request", { requestId, action, ...payload }); }
    catch (error) { clearTimeout(timer); studioLibraryRequests.delete(requestId); reject(error); }
  });
}
function nativeLibraryItems() {
  return (app.template?.blocks || []).filter(block => !isPrivateStudioBlockType(block.id)).map(block => ({
    id: "element:" + block.id, title: block.title, description: "", scope: "element", category: block.category || "Page elements",
    collection: "This Work", kind: block.component || block.id, nativeBlockTypeId: block.id, compatible: true, previewAvailable: false,
  }));
}
function studioLibraryItems() {
  const local = nativeLibraryItems(), result = [...(app.library.items || [])], common = [];
  for (const item of local) {
    const known = result.find(existing => existing.id === item.id || existing.id === "native:" + item.nativeBlockTypeId);
    if (known) { known.nativeBlockTypeId = item.nativeBlockTypeId; common.push(known); }
    else if (app.library.items === null || app.library.error || !window.chrome?.webview?.postMessage) common.push(item);
  }
  return [...common, ...result.filter(item => !common.some(first => first.id === item.id))];
}
function ensureStudioLibrary(force = false) {
  if (app.library.loading || (!force && app.library.items !== null)) return app.library.loading;
  app.library.error = null;
  app.library.loading = studioLibraryRequest("catalog", { workId: work()?.id || app.pipeline?.workId }).then(result => {
    const items = Array.isArray(result) ? result : result?.items;
    if (!Array.isArray(items)) throw new Error("The template collection could not be read.");
    app.library.items = items.filter(item => typeof item?.id === "string" && typeof item.title === "string" && ["page", "section", "module", "element"].includes(item.scope));
  }).catch(error => { app.library.items = []; app.library.error = error.message; }).finally(() => {
    app.library.loading = null; renderStudioLibrary();
  });
  return app.library.loading;
}
function studioLibraryContext() {
  const regions = app.template?.regions || [];
  if (!regions.some(region => region.id === app.region)) app.region = regions.some(region => region.id === "main") ? "main" : regions[0]?.id;
  return { regions, region: regions.find(region => region.id === app.region) };
}
function studioTemplateType(item) {
  return item.type || ({ page: "Page", section: "Section", module: "Module", element: "Element" })[item.scope] || "Template";
}
function studioTemplateProject(item) {
  return typeof item.project === "string" && item.project.trim() ? item.project.trim() : "No project";
}
function studioTemplateProjectKey(item) {
  return typeof item.project === "string" && item.project.trim() ? item.project.trim() : "none";
}
function studioTemplateRow(item) {
  const type = studioTemplateType(item), project = studioTemplateProject(item);
  return `<button type="button" class="studio-template-row${item.id === app.library.selectedId ? " selected" : ""}" data-library-id="${esc(item.id)}" data-library-scope="${esc(item.scope)}" aria-label="${esc(`${item.title}, Type: ${type}, Project: ${project}`)}" title="${esc(`${item.title} · ${type} · ${project}`)}" aria-pressed="${item.id === app.library.selectedId}" ${item.compatible === false ? 'aria-disabled="true"' : ""}><strong class="studio-template-name">${esc(item.title)}</strong><span class="studio-template-type">${esc(type)}</span><span class="studio-template-project">${esc(project)}</span></button>`;
}
function studioElementCard(item) {
  return `<button type="button" class="studio-library-card${item.id === app.library.selectedId ? " selected" : ""}" data-library-id="${esc(item.id)}" data-library-scope="${esc(item.scope)}" ${item.nativeBlockTypeId ? `data-add-block="${esc(item.nativeBlockTypeId)}"` : ""} aria-pressed="${item.id === app.library.selectedId}" ${item.compatible === false ? 'aria-disabled="true"' : ""}><span aria-hidden="true">${icon(blockIcon(item.nativeBlockTypeId || item.kind))}</span><strong>${esc(item.title)}</strong><small>${esc(item.collection || item.category || "")}</small></button>`;
}
function renderStudioLibrary() {
  const panel = $("#panel-content"), scroll = panel.scrollTop;
  const focus = document.activeElement;
  const focusedId = panel.contains(focus) ? focus.id : null;
  const focusedLibraryId = panel.contains(focus) ? focus.closest("[data-library-id]")?.dataset.libraryId : null;
  const selection = focusedId && "selectionStart" in focus ? [focus.selectionStart, focus.selectionEnd] : null;
  const library = app.library, templateMode = app.panel === "templates", { regions, region } = studioLibraryContext();
  $$("[data-panel]").forEach(button => button.classList.toggle("active", button.dataset.panel === app.panel));
  if (library.items === null && !library.loading) ensureStudioLibrary();
  const items = studioLibraryItems();
  const scopeItems = items.filter(item => templateMode ? item.scope === library.scope : item.scope === "element");
  const categories = [...new Set(scopeItems.map(item => item.category || "Other"))].sort();
  const kinds = [...new Set(scopeItems.map(item => item.kind || item.category || "Other"))].sort();
  const projects = [...new Set(items.filter(item => item.scope !== "element").map(studioTemplateProjectKey))].sort((left, right) => left === "none" ? -1 : right === "none" ? 1 : left.localeCompare(right));
  const filterSelect = (id, title, values, value) => `<label class="field"><span>${title}</span><select id="${id}"><option value="all">${title}</option>${values.map(option => `<option value="${esc(option)}" ${value === option ? "selected" : ""}>${esc(option)}</option>`).join("")}</select></label>`;
  const matches = scopeItems.filter(item => (library.category === "all" || (item.category || "Other") === library.category)
    && (!templateMode || library.project === "all" || studioTemplateProjectKey(item) === library.project)
    && (templateMode || library.kind === "all" || (item.kind || item.category || "Other") === library.kind)
    && `${item.title} ${item.sourceTitle || ""} ${item.description || ""} ${item.collection || ""} ${item.category || ""} ${studioTemplateType(item)} ${studioTemplateProject(item)}`.toLowerCase().includes(library.search.toLowerCase()));
  const visible = matches.slice(0, library.limit), selected = items.find(item => item.id === library.selectedId);
  const targetRequired = selected && selected.scope !== "page" && !page();
  const reason = targetRequired ? "Open a Page to add this object." : selected?.compatible === false ? selected.reason || "This object is unavailable for this project." : "";
  const regionControl = !templateMode || library.scope !== "page" ? (regions.length > 1 ? `<label class="field"><span>Page region</span><select id="region-select">${regions.map(item => `<option value="${esc(item.id)}" ${item.id === app.region ? "selected" : ""}>${esc(item.title)}</option>`).join("")}</select></label>` : "") : "";
  const projectFilter = `<label class="field"><span>Project</span><select id="template-project"><option value="all">All projects</option>${projects.map(project => `<option value="${esc(project)}" ${library.project === project ? "selected" : ""}>${esc(project === "none" ? "No project" : project)}</option>`).join("")}</select></label>`;
  const results = templateMode
    ? `<div class="studio-template-list" role="group" aria-label="Templates by name, type and project"><div class="studio-template-columns" aria-hidden="true"><span>Name</span><span>Type</span><span>Project</span></div>${visible.map(studioTemplateRow).join("")}</div>`
    : `<div class="studio-library-grid">${visible.map(studioElementCard).join("")}</div>`;
  panel.innerHTML = `<div class="studio-library-filters">${templateMode ? `<label class="field"><span>Type</span><select id="template-scope"><option value="page" ${library.scope === "page" ? "selected" : ""}>Page</option><option value="section" ${library.scope === "section" ? "selected" : ""}>Section</option><option value="module" ${library.scope === "module" ? "selected" : ""}>Module</option></select></label>` : ""}<input id="${templateMode ? "library-search" : "element-search"}" type="search" value="${esc(library.search)}" placeholder="${templateMode ? "Find a template" : "Find an element"}" aria-label="${templateMode ? "Find a template by name, type or project" : "Find an element"}"><div class="studio-library-filter-pair">${filterSelect(templateMode ? "template-category" : "element-category", templateMode ? "Category" : "Type", categories, library.category)}${templateMode ? projectFilter : filterSelect("element-type", "Object type", kinds, library.kind)}</div>${regionControl}</div><p class="studio-library-count">${matches.length} ${templateMode ? "templates" : "elements"}${matches.length > visible.length ? ` · showing ${visible.length}` : ""}</p>${library.loading ? '<p class="studio-library-message" role="status">Opening the collection…</p>' : ""}${library.error ? `<p class="studio-library-message">${esc(library.error)}</p><button id="library-retry" class="quiet-button">Try again</button>` : ""}${results}${!matches.length && !library.loading ? '<p class="studio-library-message">No objects match these filters.</p>' : ""}${matches.length > visible.length ? '<button id="library-more" class="quiet-button studio-library-more">Show more</button>' : ""}${selected ? `<section id="library-item-detail" class="studio-library-detail"><h3>${esc(selected.title)}</h3>${selected.description ? `<p class="panel-intro">${esc(selected.description)}</p>` : ""}${reason ? `<p class="panel-intro">${esc(reason)}</p>` : ""}<div class="studio-library-actions"><button id="library-add" class="primary-button" ${library.busy || reason ? "disabled" : ""}>Add to Page</button></div><div class="studio-library-status" role="status">${esc(library.status)}</div></section>` : library.status ? `<p class="studio-library-status" role="status">${esc(library.status)}</p>` : ""}`;
  const changed = () => { clearStudioLibraryPreview(); library.limit = 80; renderStudioLibrary(); };
  const search = templateMode ? $("#library-search") : $("#element-search");
  search.oninput = () => { library.search = search.value; changed(); };
  $("#template-scope")?.addEventListener("change", event => { library.scope = event.target.value; library.category = "all"; changed(); });
  $(templateMode ? "#template-category" : "#element-category").onchange = event => { library.category = event.target.value; changed(); };
  $("#template-project")?.addEventListener("change", event => { library.project = event.target.value; changed(); });
  $("#element-type")?.addEventListener("change", event => { library.kind = event.target.value; changed(); });
  $("#region-select")?.addEventListener("change", event => { clearStudioLibraryPreview(); app.region = event.target.value; renderStudioLibrary(); });
  $("#library-retry")?.addEventListener("click", () => { ensureStudioLibrary(true); renderStudioLibrary(); });
  $("#library-more")?.addEventListener("click", () => { library.limit += 80; renderStudioLibrary(); });
  $("#library-add")?.addEventListener("click", () => addStudioLibraryItem(selected));
  if (library.busy) $$("input,select,[data-library-id],#library-more,#library-retry", panel).forEach(control => { control.disabled = true; });
  panel.scrollTop = scroll;
  if (focusedId) {
    const restored = docById(focusedId, panel);
    restored?.focus({ preventScroll: true });
    if (selection && restored?.setSelectionRange && ["text", "search"].includes(restored.type)) restored.setSelectionRange(...selection);
  } else if (focusedLibraryId) $$("[data-library-id]", panel).find(element => element.dataset.libraryId === focusedLibraryId)?.focus({ preventScroll: true });
}
function docById(id, root) { return $$("[id]", root).find(element => element.id === id); }
// Native files remain authoritative. Only the Authoring surface mirrors their
// header so Contents can extend to the top of the workspace beside it.
let workspaceChromeRevision = 0;
const pendingWorkspacePanels = new Map();
function workspacePanelOptions(value) {
  if (!value || typeof value.selectedId !== "string" || !Array.isArray(value.options) || value.options.length > 50) return null;
  const options = value.options.filter(option => typeof option?.id === "string" && option.id.length > 0 && option.id.length <= 256
    && typeof option.title === "string" && option.title.length <= 256);
  if (options.length !== value.options.length || new Set(options.map(option => option.id)).size !== options.length
    || !options.some(option => option.id === value.selectedId)) return null;
  return { selectedId: value.selectedId, isOpen: value.isOpen === true, options };
}
function hasUnifiedElementCatalog() {
  return app.workspaceChrome?.rightPanel?.options.some(option => option.id === "native:elements") === true;
}
function switchStudioLibraryPanel(panelId) {
  if (hasUnifiedElementCatalog() && panelId !== "templates") return;
  if (!["templates", "elements"].includes(panelId) || app.panel === panelId) return;
  const keys = ["scope", "search", "category", "project", "kind", "selectedId", "limit", "status"];
  app.library.views ||= {};
  app.library.views[app.panel] = { ...Object.fromEntries(keys.map(key => [key, app.library[key]])), scroll: $("#panel-content").scrollTop };
  clearStudioLibraryPreview({ deselect: false });
  app.panel = panelId;
  const restored = app.library.views[panelId];
  Object.assign(app.library, restored || { search: "", category: "all", project: "all", kind: "all", selectedId: null, limit: 80, status: "" });
  renderStudioLibrary(); $("#panel-content").scrollTop = restored?.scroll || 0;
  const selected = studioLibraryItems().find(item => item.id === app.library.selectedId);
  if (selected) previewStudioLibraryItem(selected);
}
function renderWorkspacePanels() {
  const config = app.workspaceChrome;
  if (!config?.leftPanel || !config?.rightPanel) return false;
  document.body.classList.add("studio-coordinated-panels");
  document.body.classList.toggle("studio-left-panel-front", app.workspacePanelFront === "left");
  const layout = $("#app-layout"), left = { ...config.leftPanel, ...pendingWorkspacePanels.get("left")?.choice },
    right = { ...config.rightPanel, ...pendingWorkspacePanels.get("right")?.choice };
  const leftVisible = left.isOpen && ["contents", ...inspectorPanels].includes(left.selectedId);
  const rightVisible = right.isOpen && right.selectedId === "templates";
  const properties = $("#properties-content");
  const propertiesWereVisible = !properties.hidden && properties.parentElement === $("#navigation");
  if (properties.parentElement !== $("#navigation")) $("#navigation").append(properties);
  for (const [side, state, visible] of [["left", left, leftVisible], ["right", right, rightVisible]]) {
    const selector = $(`#studio-${side}-panel-selector`), focused = document.activeElement === selector;
    const markup = state.options.map(option => `<option value="${esc(option.id)}">${esc(option.title)}</option>`).join("");
    if (selector.innerHTML !== markup) selector.innerHTML = markup;
    selector.value = state.selectedId;
    selector.closest(".studio-panel-heading").hidden = false;
    $(`#studio-${side}-panel-collapse`).hidden = !visible;
    layout.classList.toggle(`studio-${side}-panel-hidden`, !visible);
    if (focused && visible) selector.focus({ preventScroll: true });
  }
  const propertiesVisible = inspectorPanels.includes(left.selectedId);
  $("#panel-content").hidden = false;
  properties.hidden = !propertiesVisible;
  if (leftVisible && propertiesVisible) {
    if (!propertiesWereVisible || app.leftPanel !== "inspector" || properties.dataset.panel !== left.selectedId) { app.leftPanel = "inspector"; renderInspector(); }
  }
  else if (leftVisible) app.leftPanel = "contents";
  if (rightVisible) {
    if (!(app.library.busy && app.deferredWorkspacePanel?.id === "templates")) switchStudioLibraryPanel(right.selectedId);
  }
  $("#studio-contents").hidden = left.selectedId !== "contents";
  $("#navigation").hidden = !leftVisible;
  const narrow = matchMedia("(max-width: 960px)").matches;
  layout.classList.toggle("nav-closed", !leftVisible);
  layout.classList.remove("narrow-nav");
  layout.classList.toggle("inspector-hidden", !rightVisible);
  $("#nav-expand").hidden = true;
  $("#nav-resizer").hidden = !leftVisible || narrow;
  if (!leftVisible || narrow) finishNavigationResize();
  updateNavigationWidth(); updateStudioToolbarInset(); scheduleStudioPreviewPosition();
  return true;
}
function sendWorkspacePanelAction(action, side, contentId, source) {
  const state = app.workspaceChrome?.[side + "Panel"];
  if (!state || !state.options.some(option => option.id === contentId) || app.library.busy || app.libraryMutation) return;
  if (action === "panel-select") {
    app.workspacePanelFront = side;
    document.body.classList.toggle("studio-left-panel-front", side === "left");
  }
  const rect = source.getBoundingClientRect();
  const webPanelOpen = state.isOpen && (side === "left" ? ["contents", ...inspectorPanels].includes(state.selectedId)
    : state.selectedId === "templates");
  // Hide the outgoing web surface before native layout reserves its column.
  // The authoritative native snapshot confirms the choice without recreating it.
  if ((action === "panel-select" && contentId.startsWith("native:")) || (action === "panel-toggle" && webPanelOpen)) {
    clearTimeout(pendingWorkspacePanels.get(side)?.timer);
    const pending = { choice: action === "panel-select" ? { selectedId: contentId, isOpen: true } : { isOpen: false }, timer: null };
    pendingWorkspacePanels.set(side, pending); renderWorkspacePanels();
    pending.timer = setTimeout(() => {
      if (pendingWorkspacePanels.get(side) !== pending) return;
      pendingWorkspacePanels.delete(side);
      renderWorkspacePanels();
    }, 2000);
  }
  studioMessage("studio:workspace-action", { action, side, contentId, anchor: {
    left: Math.max(0, rect.left), top: Math.max(0, rect.top), width: rect.width, height: rect.height,
    viewportWidth: innerWidth, viewportHeight: innerHeight,
  } });
}
function updateWorkspaceChromeControls() {
  const config = app.workspaceChrome;
  if (!config) return;
  const locked = app.library.busy || !!app.libraryMutation;
  $$("button", $("#studio-workspace-tabs")).forEach(button => { button.disabled = locked; });
  $$("button", $("#studio-workspace-tools")).forEach(button => {
    button.disabled = locked || (button.dataset.workspaceAction === "save" && !config.canSave);
  });
  for (const control of $$(".studio-panel-heading select,.studio-panel-edge")) control.disabled = locked;
}
function configureWorkspaceChrome(config) {
  if (!window.chrome?.webview?.postMessage || !config || !Array.isArray(config.tabs) || !config.tabs.length
    || typeof config.activeId !== "string" || !config.tabs.some(tab => tab?.id === config.activeId)) return false;
  const tabs = config.tabs.filter(tab => typeof tab?.id === "string" && tab.id.length > 0 && tab.id.length <= 4096
    && typeof tab.title === "string" && tab.title.length <= 512);
  if (!tabs.length || tabs.length !== config.tabs.length || tabs.length > 200 || new Set(tabs.map(tab => tab.id)).size !== tabs.length) return false;
  const leftPanel = workspacePanelOptions(config.leftPanel), rightPanel = workspacePanelOptions(config.rightPanel);
  if ((config.leftPanel != null || config.rightPanel != null) && (!leftPanel || !rightPanel)) return false;
  const previousChrome = app.workspaceChrome;
  if (leftPanel?.isOpen && previousChrome?.leftPanel
    && (leftPanel.selectedId !== previousChrome.leftPanel.selectedId || !previousChrome.leftPanel.isOpen)) app.workspacePanelFront = "left";
  else if (rightPanel?.isOpen && previousChrome?.rightPanel
    && (rightPanel.selectedId !== previousChrome.rightPanel.selectedId || !previousChrome.rightPanel.isOpen)) app.workspacePanelFront = "right";
  const revision = ++workspaceChromeRevision, host = $("#studio-workspace-tabs");
  const focused = document.activeElement;
  const focusAction = host.contains(focused) ? ["select", "help", "close"].map(action => [action, focused.dataset[`workspace${action[0].toUpperCase() + action.slice(1)}`]]).find(pair => pair[1]) : null;
  const scroll = $(".studio-workspace-tab-scroll", host)?.scrollLeft || 0;
  app.workspaceChrome = { tabs, activeId: config.activeId, canSave: config.canSave === true,
    leftPanel, rightPanel };
  if (hasUnifiedElementCatalog()) {
    const retired = $('[data-panel="elements"]');
    if (retired) { retired.hidden = true; retired.disabled = true; }
    if (app.panel !== "templates") switchStudioLibraryPanel("templates");
  }
  // Any authoritative snapshot supersedes an optimistic outgoing collapse,
  // including a rejected/changed choice. Missing replies restore the prior view.
  if (leftPanel && rightPanel) {
    for (const pending of pendingWorkspacePanels.values()) clearTimeout(pending.timer);
    pendingWorkspacePanels.clear();
  }
  const activeTab = tabs.find(tab => tab.id === config.activeId), helpTitle = activeTab.helpTitle || `About ${activeTab.title}`;
  host.innerHTML = `<div class="studio-workspace-tab-scroll"><div class="studio-workspace-tab-list" role="tablist" aria-label="Project files">${tabs.map(tab => `<div class="studio-workspace-tab${tab.id === config.activeId ? " active" : ""}"><button type="button" class="studio-workspace-tab-select" role="tab" data-workspace-select="${esc(tab.id)}" aria-selected="${tab.id === config.activeId}" tabindex="${tab.id === config.activeId ? 0 : -1}" title="${esc(tab.title)}">${esc(tab.title)}</button>${tab.canClose === true ? `<button type="button" class="studio-workspace-tab-close" data-workspace-close="${esc(tab.id)}" title="Close ${esc(tab.title)}" aria-label="Close ${esc(tab.title)}">${icon("close")}</button>` : ""}</div>`).join("")}<button type="button" id="studio-workspace-new" class="icon-button" data-workspace-action="new" title="New document" aria-label="New document">${icon("plus")}</button></div></div><button type="button" id="studio-workspace-help" class="icon-button" data-workspace-help="${esc(activeTab.id)}" title="${esc(helpTitle)}" aria-label="${esc(helpTitle)}">${icon("help")}</button>`;
  const tools = $("#studio-workspace-tools");
  tools.innerHTML = [
    ["layouts", "grid", "Reusable page layouts"], ["authoring", "pen", "Page editor"],
    ["publish", "upload", "Publish to Hyperlib"], ["save", "save", "Save project and Page content"],
  ].map(([action, glyph, title]) => `<button type="button" class="icon-button" data-workspace-action="${action}" title="${title}" aria-label="${title}">${icon(glyph)}</button>`).join("");
  host.hidden = false; tools.hidden = false;
  document.body.classList.add("studio-workspace-chrome");
  renderWorkspacePanels();
  $(".studio-workspace-tab-scroll", host).scrollLeft = scroll;
  if (focusAction) $$("button", host).find(button => button.dataset[`workspace${focusAction[0][0].toUpperCase() + focusAction[0].slice(1)}`] === focusAction[1])?.focus({ preventScroll: true });
  updateWorkspaceChromeControls();
  requestAnimationFrame(() => {
    if (revision !== workspaceChromeRevision) return;
    document.documentElement.style.setProperty("--studio-workspace-tabs-height", `${Math.ceil(host.getBoundingClientRect().height)}px`);
    updateStudioToolbarInset(); scheduleStudioPreviewPosition();
    requestAnimationFrame(() => {
      if (revision !== workspaceChromeRevision || host.hidden || tools.hidden || !host.getBoundingClientRect().height) return;
      studioMessage("studio:workspace-chrome-ready", { activeId: app.workspaceChrome.activeId });
    });
  });
  return true;
}
function sendWorkspaceAction(action, contentId, source) {
  if (!app.workspaceChrome || app.library.busy || app.libraryMutation) return;
  if (contentId && !app.workspaceChrome.tabs.some(tab => tab.id === contentId)) return;
  if (action === "save" && !app.workspaceChrome.canSave) return;
  if (app.libraryPreview.itemId) { clearStudioLibraryPreview(); renderStudioLibrary(); }
  const rect = source.getBoundingClientRect();
  const left = Math.max(0, Math.min(innerWidth, rect.left)), top = Math.max(0, Math.min(innerHeight, rect.top));
  studioMessage("studio:workspace-action", { action, ...(contentId ? { contentId } : {}), anchor: {
    left, top, width: Math.max(0, Math.min(rect.width, innerWidth - left)), height: Math.max(0, Math.min(rect.height, innerHeight - top)),
    viewportWidth: innerWidth, viewportHeight: innerHeight,
  } });
}
// Browsing uses a separate read-only surface. The live Page iframe, its draft,
// controller, selection and scroll stay mounted underneath until placement.
function selectStudioSectionRegion(category) {
  const section = String(category || "").toLowerCase();
  if (!["header", "navigation", "body", "footer"].includes(section)) return;
  const aliases = section === "body" ? ["body", "main", "content"] : section === "navigation" ? ["navigation", "nav"] : [section];
  const regions = app.template?.regions || [];
  const match = regions.find(region => aliases.includes(String(region.id).toLowerCase()))
    || regions.find(region => aliases.some(alias => String(region.title || "").toLowerCase().split(/[^a-z]+/).includes(alias)));
  // Some complete designs intentionally expose only one general content region.
  app.region = match?.id || regions.find(region => region.id === "main")?.id || regions[0]?.id;
}
function clearStudioLibraryPreview({ deselect = true } = {}) {
  const preview = app.libraryPreview, surface = $("#studio-library-preview");
  preview.sequence++; clearTimeout(preview.timer); preview.timer = null;
  if (preview.itemId) studioMessage("studio:library-preview-cancel");
  preview.itemId = null; preview.nodeId = null;
  const frame = $("#studio-library-preview-frame");
  frame.onload = null; frame.onerror = null;
  if (frame.hasAttribute("src")) frame.removeAttribute("src");
  frame.hidden = true; delete frame.dataset.loaded;
  surface.hidden = true; delete surface.dataset.itemId; delete surface.dataset.state;
  $("#main").classList.remove("studio-previewing");
  for (const section of $$("#landing,#work-view,#page-view")) {
    if (section.dataset.previewInert !== undefined) {
      section.inert = section.dataset.previewInert === "true"; delete section.dataset.previewInert;
    }
  }
  if (preview.scroll) { window.scrollTo(preview.scroll.x, preview.scroll.y); preview.scroll = null; }
  if (deselect) { app.library.selectedId = null; app.library.status = ""; }
}
let studioPreviewPositionFrame = null;
function positionStudioLibraryPreview() {
  studioPreviewPositionFrame = null;
  const surface = $("#studio-library-preview");
  if (surface.hidden) return;
  const main = $("#main").getBoundingClientRect(), toolbar = $("#main .content-topline").getBoundingClientRect();
  const files = $("#studio-workspace-tabs");
  const headerBottom = files.hidden ? toolbar.bottom : Math.max(toolbar.bottom, files.getBoundingClientRect().bottom);
  const inset = innerWidth <= 720 ? 10 : 18;
  const left = Math.max(0, main.left + inset), right = Math.min(innerWidth, main.right - inset);
  const top = Math.max(8, Math.min(innerHeight - 120, headerBottom + 12));
  surface.style.left = `${left}px`; surface.style.width = `${Math.max(1, right - left)}px`;
  surface.style.top = `${top}px`; surface.style.height = `${Math.max(100, innerHeight - top - 12)}px`;
}
function scheduleStudioPreviewPosition() {
  if (!studioPreviewPositionFrame && !$("#studio-library-preview").hidden)
    studioPreviewPositionFrame = requestAnimationFrame(positionStudioLibraryPreview);
}
window.addEventListener("resize", scheduleStudioPreviewPosition);
window.addEventListener("scroll", scheduleStudioPreviewPosition, { passive: true, capture: true });
new ResizeObserver(scheduleStudioPreviewPosition).observe($("#main"));
async function previewStudioLibraryItem(item, catalog = false) {
  clearStudioLibraryPreview({ deselect: false });
  if (!item || app.library.busy) return;
  if (!catalog) app.catalogElement = null;
  if (item.scope === "section" && item.id.startsWith("section:")) selectStudioSectionRegion(item.id.split(":")[1]);
  if (!window.chrome?.webview?.postMessage) {
    app.library.status = "Preview is available in Hyperpage Studio."; renderStudioLibrary(); return;
  }
  const preview = app.libraryPreview, sequence = preview.sequence;
  preview.itemId = item.id; preview.nodeId = app.id; preview.scroll = { x: scrollX, y: scrollY };
  const current = () => preview.sequence === sequence && preview.itemId === item.id && preview.nodeId === app.id
    && (catalog ? app.catalogElement?.id : app.library.selectedId) === item.id;
  const surface = $("#studio-library-preview");
  surface.dataset.itemId = item.id; surface.dataset.state = "preparing"; surface.hidden = false;
  $("#studio-library-preview-title").textContent = item.title;
  $("#studio-library-preview-status").textContent = "Preparing preview…";
  $("#studio-library-preview-status").hidden = false;
  $("#studio-library-preview-message").hidden = true;
  $("#main").classList.add("studio-previewing");
  for (const section of $$("#landing,#work-view,#page-view")) {
    section.dataset.previewInert = String(section.inert); section.inert = true;
  }
  positionStudioLibraryPreview();
  if (catalog && item.compatible === false) {
    surface.dataset.state = "unavailable";
    $("#studio-library-preview-status").textContent = item.reason || "This element needs additional source setup before it can be placed.";
    return;
  }
  // A small debounce avoids compiling every card passed during quick browsing.
  // New selections cancel native preparation and stale replies never repaint.
  preview.timer = setTimeout(async () => {
    preview.timer = null;
    try {
      const result = await studioLibraryRequest("preview", { itemId: item.id, pageId: page()?.id || app.pipeline?.landingPageId, workId: work()?.id || app.pipeline?.workId });
      if (!current()) return;
      const url = new URL(result.url);
      if (url.protocol !== "http:" || !["127.0.0.1", "localhost", "[::1]"].includes(url.hostname) || url.username || url.password)
        throw new Error("The preview did not return an owned local address.");
      const previous = $("#studio-library-preview-frame"), frame = previous.cloneNode(false);
      frame.removeAttribute("src"); delete frame.dataset.loaded;
      frame.title = `${result.title || item.title} preview`;
      frame.onload = () => {
        if (!current() || !frame.isConnected) return;
        frame.dataset.loaded = "true"; surface.dataset.state = "ready";
        $("#studio-library-preview-status").hidden = true;
      };
      frame.onerror = () => {
        if (!current()) return;
        clearStudioLibraryPreview(); app.library.status = "This preview could not open. Your Page has been preserved."; renderStudioLibrary();
      };
      frame.src = url.href; frame.hidden = false;
      previous.replaceWith(frame); surface.dataset.state = "loading";
      $("#studio-library-preview-title").textContent = result.title || item.title;
      $("#studio-library-preview-message").hidden = !result.message;
      $("#studio-library-preview-message").textContent = result.message || "";
      app.library.status = ""; renderStudioLibrary();
    } catch (error) {
      if (!current()) return;
      clearStudioLibraryPreview(); app.library.status = error.message; renderStudioLibrary(); toast(error.message, true);
    }
  }, 140);
}

// The native HTML/Razor catalog keeps its original list and filters. Selection
// only opens this temporary surface; placement goes through the same guarded
// flush and revision-checked writer as Templates.
function previewCatalogElement(item) {
  if (app.library.busy || app.libraryMutation) return false;
  if (item == null) {
    app.catalogElement = null; clearStudioLibraryPreview({ deselect: false }); return true;
  }
  if (typeof item.id !== "string" || !item.id.startsWith("catalog:") || item.id.length > 512
    || typeof item.title !== "string" || item.title.length > 512 || item.scope !== "element") return false;
  app.catalogElement = { ...item };
  previewStudioLibraryItem(app.catalogElement, true);
  return true;
}
async function commitCatalogElement(id) {
  const item = app.catalogElement;
  if (!item || item.id !== id || item.compatible === false || app.library.busy || app.libraryMutation) return false;
  if (!page()) { toast("Open a Page to add this element.", true); return false; }
  try {
    await mutateStudioLibrary("add", { itemId: item.id });
    return true;
  } catch (error) { toast(error.message, true); return false; }
}

function libraryParent() {
  const current = node();
  if (current && allowedChildTypes(current).includes("page")) return current;
  const parent = current?.parentId && node(current.parentId);
  return parent && allowedChildTypes(parent).includes("page") ? parent : work() || node(app.pipeline?.workId);
}
function setStudioLibraryLock(locked) {
  $("#main").inert = locked;
  $("#studio-contents").inert = locked;
  $("#properties-content").inert = locked;
  updateWorkspaceChromeControls();
}
function studioInsertionY() {
  const current = page(), frame = $("#page-frame");
  if (!current || frame.hidden || frame.dataset.pageId !== current.id) return 0;
  const blocks = current.blocks.filter(block => block.region === app.region);
  const elements = [...(frame.contentDocument?.querySelectorAll(".hl-document [data-block-id]") || [])]
    .filter(element => blocks.some(block => block.id === element.dataset.blockId));
  const parent = elements[0]?.closest(".hl-region"), bounds = parent?.getBoundingClientRect();
  const bottom = blocks.reduce((occupied, block) => {
    const element = elements.find(item => item.dataset.blockId === block.id), rect = element?.getBoundingClientRect();
    return Math.max(occupied, bounds && rect ? rect.bottom - bounds.top - parent.clientTop
      : (Number(block.values?.studioY) || 0) + (Number(block.values?.studioHeight) || 240));
  }, 0);
  const gap = parent ? parseFloat(frame.contentWindow.getComputedStyle(parent).gap) || 24 : 24;
  return Math.min(100000, Math.max(0, Math.ceil(bottom + (blocks.length ? Math.min(80, Math.max(8, gap)) : 0))));
}
async function mutateStudioLibrary(action, payload, localType = null) {
  if (app.libraryMutation || app.library.busy) throw new Error("Wait for the current library action to finish.");
  const placement = { pageId: payload.pageId || page()?.id || studioLandingNode()?.id,
    workId: payload.workId || studioWorkFor()?.id, regionId: app.region,
    ...(action === "add" && !payload.parentId ? { insertionY: studioInsertionY() } : {}) };
  clearStudioLibraryPreview();
  const library = app.library;
  app.deferredWorkspacePanel = null;
  // Capture placement intent and lock editing before the asynchronous flush.
  // Library browsing itself never flushes or modifies the current Page.
  library.busy = true; library.status = action === "save-page-template" ? "Saving template…" : "Adding to the project…";
  setStudioLibraryLock(true); renderStudioLibrary();
  try {
    if (!(await studioFlush(crypto.randomUUID()))) throw new Error(app.lastFlushError || "Save or recover the current Page before using the library.");
  } catch (error) {
    app.deferredWorkspacePanel = null;
    library.busy = false; library.status = error.message; setStudioLibraryLock(false); renderStudioLibrary(); throw error;
  }
  const operation = (async () => {
    if (localType) { await addBlock(localType); return null; }
    const result = await studioLibraryRequest(action, { ...payload, ...placement, expectedContentRevision: app.state.revision });
    if (Array.isArray(result?.state?.nodes) && result.state.pages && result.state.libraryId === app.state.libraryId) app.state = result.state;
    else await loadState();
    await loadStudioPipeline(); app.templates.clear();
    const saved = result?.item || (result?.scope && result?.id ? result : null);
    if (saved) {
      library.items = [...(library.items || []).filter(item => item.id !== saved.id), saved];
      app.panel = "templates"; library.scope = "page"; library.search = ""; library.category = "all"; library.project = "all"; library.selectedId = saved.id;
      if (app.workspaceChrome?.rightPanel) app.deferredWorkspacePanel = { id: "templates", itemId: saved.id };
    }
    return result;
  })();
  app.libraryMutation = operation; publishStudioState();
  let result, operationError;
  try { result = await operation; library.status = result?.warning || (action === "save-page-template" ? "Template saved." : "Added to the project."); }
  catch (error) { operationError = error; library.status = error.message; toast(error.message, true); }
  // Internal navigation must not await its own completed mutation. Keep the UI
  // locked until the resulting Page and its selected Properties are presented.
  finally { app.libraryMutation = null; publishStudioState(); }
  if (operationError) { app.deferredWorkspacePanel = null; library.busy = false; setStudioLibraryLock(false); renderStudioLibrary(); throw operationError; }
  const targetId = result?.pageId && node(result.pageId) ? result.pageId : app.id;
  let displayReady = false;
  try {
    if (targetId !== app.id) {
      await navigate(targetId);
    }
    else await render();
    if (node(targetId)?.type === "page") await waitForStudioNavigation(targetId);
    if (result?.addedBlockIds?.length) { app.selected = result.addedBlockIds[0]; openStudioProperties(); renderPanel(); sendFrame({ kind: "select", blockId: app.selected }); }
    displayReady = true;
  } catch (error) { library.status = result ? "The change was saved. Reopen the project to refresh its display. " + error.message : error.message; toast(library.status, true); }
  finally {
    const deferred = app.deferredWorkspacePanel; app.deferredWorkspacePanel = null;
    library.busy = false; setStudioLibraryLock(false); renderStudioLibrary(); publishStudioState();
    // Automatic placement requests run only after the Page is ready and the
    // mutation lock is released; user panel controls remain locked throughout.
    if (displayReady && deferred?.id === "properties" && deferred.blockId === app.selected
      && page()?.blocks.some(block => block.id === deferred.blockId)) openStudioProperties();
    else if (displayReady && deferred?.id === "templates" && library.selectedId === deferred.itemId
      && library.items?.some(item => item.id === deferred.itemId))
      sendWorkspacePanelAction("panel-select", "right", "templates", $("#studio-right-panel-selector"));
  }
  if (result?.warning) toast(result.warning, true);
  return result;
}
function addStudioLibraryItem(item) {
  if (!item || app.library.busy || item.compatible === false) return;
  clearStudioLibraryPreview({ deselect: false });
  if (item.scope === "page") {
    const owner = studioWorkFor(), landing = studioLandingNode(owner);
    if (landing?.id === app.id) {
      mutateStudioLibrary("add", { itemId: item.id, targetRole: "landing", workId: owner.id, pageId: landing.id })
        .catch(error => toast(error.message, true));
      return;
    }
    const parent = libraryParent();
    if (!parent) { toast("Open a Work before adding a Page.", true); return; }
    const parents = app.state.nodes.filter(candidate => (candidate.id === work()?.id || ancestors(candidate).some(owner => owner.id === work()?.id)) && allowedChildTypes(candidate).includes("page"));
    openDialog("Add Page", `${owner ? '<label class="field"><span>Type</span><select name="targetRole"><option value="page">Page</option><option value="landing">Landing</option></select></label>' : ""}<label class="field"><span>Page name</span><input name="title" required maxlength="160" value="${esc(item.title)}"></label><label class="field" id="studio-template-parent"><span>Add to</span><select name="parentId">${parents.map(candidate => `<option value="${esc(candidate.id)}" ${candidate.id === parent.id ? "selected" : ""}>${esc(candidate.title)}</option>`).join("")}</select></label>`, "Add to Page",
      form => mutateStudioLibrary("add", { itemId: item.id, title: form.get("title"), targetRole: form.get("targetRole") || "page",
        ...(form.get("targetRole") === "landing" ? { workId: owner.id, ...(landing ? { pageId: landing.id } : {}) } : { parentId: form.get("parentId") }) }));
    const role = $('[name="targetRole"]', $("#dialog"));
    role?.addEventListener("change", () => {
      const isLanding = role.value === "landing", parentField = $("#studio-template-parent");
      parentField.hidden = isLanding; $('[name="parentId"]', parentField).disabled = isLanding;
      if (isLanding) $('[name="title"]', $("#dialog")).value = landing?.title || "Landing page";
    });
    return;
  }
  if (!page()) { toast("Open a Page to add this object.", true); return; }
  const nativeType = item.nativeBlockTypeId;
  if (nativeType && app.template?.blocks.some(block => block.id === nativeType)) {
    const definition = app.template.blocks.find(block => block.id === nativeType), { region, regions } = studioLibraryContext();
    if (region?.allowedBlocks.length && !region.allowedBlocks.includes(nativeType)) {
      app.region = regions.find(candidate => !candidate.allowedBlocks.length || candidate.allowedBlocks.includes(nativeType))?.id;
      if (!app.region) { toast("This element has no compatible Page region.", true); return; }
    }
    mutateStudioLibrary("add", { itemId: item.id }, nativeType).catch(error => toast(error.message, true));
  } else mutateStudioLibrary("add", { itemId: item.id }).catch(error => toast(error.message, true));
}
function saveStudioPageTemplate() {
  if (!page() || app.libraryMutation) return;
  clearStudioLibraryPreview(); renderStudioLibrary();
  openDialog("Save Page as template", `<label class="field"><span>Template name</span><input name="title" required maxlength="160" value="${esc(node()?.title || "Page")} template"></label>`, "Save template",
    form => mutateStudioLibrary("save-page-template", { title: form.get("title") }));
}
function blockIcon(type) {
  return (
    {
      text: "text",
      richtext: "text",
      heading: "text",
      image: "image",
      gallery: "grid",
      video: "video",
      audio: "audio",
      callout: "quote",
      header: "text",
      footer: "text",
      banner: "image",
      divider: "more",
      form: "page",
      map: "grid",
      embed: "code",
      tool: "code",
      citation: "link",
      wave: "wave",
      counter: "code",
      "csharp-counter": "code",
    }[type] || "spark"
  );
}
function nextCanvasPlacement(currentPage, regionId, definition) {
  const existing = currentPage.blocks.filter(block => block.region === regionId);
  if (!supportsStudioCanvasGeometry(definition) || !existing.some(block => block.values?.studioPositioned === true)) return null;
  // Once a region contains freely placed elements, a newly added flow element
  // would start behind them. Begin below its occupied canvas instead.
  const frame = $("#page-frame");
  let elements = [], parent = null, gap = 24;
  try {
    if (frame.dataset.pageId === currentPage.id && !frame.hidden) {
      elements = [...(frame.contentDocument?.querySelectorAll(".hl-document [data-block-id]") || [])]
        .filter(element => existing.some(block => block.id === element.dataset.blockId));
      parent = elements[0]?.closest(".hl-region");
      if (parent) gap = Math.min(80, Math.max(8, parseFloat(frame.contentWindow.getComputedStyle(parent).gap) || gap));
    }
  } catch { /* A preparing renderer uses bounded saved geometry below. */ }
  const bounds = parent?.getBoundingClientRect();
  const occupied = existing.reduce((bottom, block) => {
    const element = elements.find(item => item.dataset.blockId === block.id && item.closest(".hl-region") === parent);
    const rendered = bounds && element?.getBoundingClientRect();
    if (rendered?.height > 0) return Math.max(bottom, rendered.bottom - bounds.top - parent.clientTop);
    if (block.values?.studioPositioned === true) {
      const y = Number(block.values.studioY) || 0, height = Number(block.values.studioHeight) || 240;
      return Math.max(bottom, Math.min(100000, Math.max(0, y) + Math.max(1, height)));
    }
    return bottom;
  }, 0);
  const y = Math.ceil(occupied + gap);
  if (y > 99760) throw new Error("This content region is full. Use another region or move its existing elements.");
  return { studioPositioned: true, studioX: 0, studioY: y, studioWidth: 100, studioHeight: 0 };
}
async function addBlock(type) {
  try {
    await flushDraft();
    if (app.draft) return;
    const definition = app.template.blocks.find((b) => b.id === type);
    const p = structuredClone(page());
    const values = {};
    for (const f of definition.fields) {
      if (f.defaultValue == null && !f.required) continue;
      values[f.id] =
        f.defaultValue ??
        (f.type === "number"
          ? 0
          : f.type === "boolean"
            ? false
            : f.type === "array"
              ? []
              : "");
    }
    const block = {
      id: crypto.randomUUID().replaceAll("-", ""),
      type,
      region: app.region || app.template.regions[0].id,
      values,
    };
    Object.assign(block.values, nextCanvasPlacement(p, block.region, definition));
    p.blocks.push(block);
    await mutate(
      [{ kind: "savePage", id: p.id, page: p }],
      `Add ${definition.title}`,
      false,
    );
    app.selected = block.id;
    openStudioProperties();
    renderPanel();
    sendFrame({ kind: "refresh", pageId: p.id });
    if (!$("#page-fallback").hidden) renderFallback();
    toast(`${definition.title} added.`);
  } catch (error) {
    toast(error.message, true);
  }
}
function fieldControl(f, value, scope = "field") {
  const id = `${scope}-${f.id}`;
  const v = value ?? f.defaultValue ?? "";
  let control;
  if (f.options?.length)
    control = `<select id="${esc(id)}" data-field="${esc(f.id)}">${!f.required ? `<option value="" ${v === "" ? "selected" : ""}>Template default</option>` : ""}${f.options.map((option) => `<option value="${esc(option)}" ${v === option ? "selected" : ""}>${esc(({ full: "Full width", half: "Half width", third: "One third", "two-thirds": "Two thirds", system: "System", serif: "Serif", monospace: "Monospace", humanist: "Humanist", geometric: "Geometric", left: "Left", center: "Center", right: "Right", justify: "Justified" })[option] || option)}</option>`).join("")}</select>`;
  else if (f.type === "color")
    control = `<div class="color-control"><input id="${esc(id)}" data-field="${esc(f.id)}" type="color" value="${/^#[0-9a-f]{6}$/i.test(v) ? esc(v) : "#283731"}" ${v === "" ? 'data-empty="true"' : ""}>${!f.required ? `<button type="button" class="quiet-button" data-clear-field="${esc(f.id)}" title="Use template default">${v === "" ? "Inherited" : "Reset"}</button>` : ""}</div>`;
  else if (f.type === "boolean")
    control = `<input id="${esc(id)}" data-field="${esc(f.id)}" type="checkbox" ${v ? "checked" : ""}>`;
  else if (
    ["text", "richtext", "markdown", "multiline"].includes(f.type) ||
    f.id === "text" ||
    f.id === "content"
  )
    control = `<textarea id="${esc(id)}" data-field="${esc(f.id)}">${esc(v)}</textarea>`;
  else if (f.type === "array")
    control = `<textarea id="${esc(id)}" data-field="${esc(f.id)}" data-json="true">${esc(JSON.stringify(v, null, 2))}</textarea>`;
  else
    control = `<input id="${esc(id)}" data-field="${esc(f.id)}" type="${f.type === "number" ? "number" : f.type === "color" ? "color" : "text"}" value="${esc(v)}" ${f.min != null ? `min="${f.min}"` : ""} ${f.max != null ? `max="${f.max}"` : ""}>`;
  return `<label class="field" for="${esc(id)}"><span>${esc(f.title || f.id)}${f.required ? " *" : ""}</span>${control}${["media", "image", "video", "audio"].includes(f.type) || ["src", "url"].includes(f.id) ? `<button type="button" class="quiet-button upload-field" data-upload-field="${esc(f.id)}">${icon("upload")}Import local media</button>` : ""}</label>`;
}
function fieldValue(f, input) {
  if (!f.required && (input.dataset.empty || (f.options?.length && input.value === ""))) return undefined;
  return f.type === "boolean" ? input.checked
    : f.type === "number" ? Number(input.value)
      : f.type === "array" ? JSON.parse(input.value) : input.value;
}
function bodySlotElements(layout) {
  const slots = [];
  function visit(elements, depth) {
    if (!Array.isArray(elements) || depth > 24) return;
    for (const element of elements) {
      if (!element || typeof element.id !== "string") continue;
      if (element.isContentSlot === true && ["text", "image", "video", "template"].includes(element.kind)) slots.push(element);
      visit(element.children, depth + 1);
    }
  }
  visit(layout?.elements, 0);
  return slots;
}
function bodySlotLayout(block) {
  if (block?.type !== "studio-body-row" || typeof block.values?.layout !== "string") return null;
  try { return JSON.parse(block.values.layout); } catch { return null; }
}
function isSlotContentField(field) { return ["text", "string", "url", "media"].includes(field?.type); }
function slotStringValue(field, value, nativeMedia = false) {
  if (typeof value !== "string" || value.length > 200000) throw new Error("Use text within the supported content size.");
  if (nativeMedia && value && !localMedia(value)) throw new Error("Choose an imported media file for this slot.");
  if (["url", "media"].includes(field.type) && value) {
    if (/[\x00-\x1f\x7f\\]/.test(value)) throw new Error("Use a safe media or web address.");
    if (value.startsWith("/") && !value.startsWith("//")) {
      if (value.split("/").some(part => [".", ".."].includes(decodeURIComponent(part)))) throw new Error("Use a safe relative address.");
    } else {
      const target = new URL(value);
      if (!["https:", "http:"].includes(target.protocol) || target.username || target.password) throw new Error("Use an HTTP or HTTPS address.");
    }
  }
  if (field.options?.length && value && !field.options.includes(value)) throw new Error("Choose an available field value.");
  return value;
}
function editBodySlot(rowId, slotId, templateId, field, value, nativeProperty) {
  const current = page()?.blocks.find(block => block.id === rowId && block.type === "studio-body-row");
  const layout = bodySlotLayout(current);
  const slot = bodySlotElements(layout).find(element => element.id === slotId);
  if (!slot) throw new Error("This content slot is no longer available. Reload the Page.");
  if (templateId) {
    const template = slot.kind === "template" && slot.templateBlocks?.find(block => block.id === templateId);
    const schema = template && app.template?.blocks.find(block => block.id === template.type);
    const declared = schema?.fields.find(item => item.id === field.id && isSlotContentField(item));
    if (!template || !declared) throw new Error("This object field is no longer available.");
    template.values = { ...template.values };
    if (value === undefined) delete template.values[declared.id];
    else template.values[declared.id] = slotStringValue(declared, value);
  } else {
    const allowed = slot.kind === "text" ? ["text"] : ["image", "video"].includes(slot.kind) ? ["mediaPath", "alt"] : [];
    if (!allowed.includes(nativeProperty)) throw new Error("This content field cannot change the layout.");
    if ((nativeProperty === "mediaPath" || nativeProperty === "alt") && value.length > (nativeProperty === "mediaPath" ? 1024 : 4000)) throw new Error("This media field is too long.");
    slot[nativeProperty] = slotStringValue(field, value, nativeProperty === "mediaPath");
  }
  const serialized = JSON.stringify(layout);
  if (serialized.length > 200000 || JSON.stringify(serialized).length > 400000) throw new Error("This Block is full. Use another content slot or a shorter value.");
  const draft = beginDraft();
  const target = draft.blocks.find(block => block.id === rowId && block.type === "studio-body-row");
  if (!target) throw new Error("The current Page changed. Reload its content slots.");
  target.values.layout = serialized;
  status("Saving…", "dirty");
  clearTimeout(app.timer);
  app.timer = setTimeout(() => flushDraft().catch(() => {}), 600);
}
function renderBodySlotInspector(block) {
  const slots = bodySlotElements(bodySlotLayout(block));
  if (!slots.length) return false;
  const records = [];
  const control = (slot, field, value, templateId = null, nativeProperty = null) => {
    const index = records.length;
    records.push({ slot, field, templateId, nativeProperty, key: `${block.id}:slot:${slot.id}:${templateId || "native"}:${field.id}` });
    return `<div data-slot-control="${index}">${fieldControl(field, value, "slot-" + index)}</div>`;
  };
  const sections = slots.map(slot => {
    let fields = "";
    if (slot.kind === "text") fields = control(slot, { id: "text", title: "Text", type: "text" }, slot.text || "", null, "text");
    else if (["image", "video"].includes(slot.kind)) {
      fields = control(slot, { id: "mediaPath", title: slot.kind === "image" ? "Image" : "Video", type: "media" }, slot.mediaPath || "", null, "mediaPath")
        + control(slot, { id: "alt", title: "Description", type: "string" }, slot.alt || "", null, "alt");
    } else for (const template of slot.templateBlocks || []) {
      const schema = app.template?.blocks.find(item => item.id === template.type);
      if (!schema) continue;
      fields += schema.fields.filter(isSlotContentField).map(field => control(slot, field, template.values?.[field.id], template.id)).join("");
    }
    return `<section class="body-slot-content" data-body-slot="${esc(slot.id)}"><h3>${esc(slot.slotName || slot.templateTitle || "Content slot")}</h3>${slot.slotHint ? `<p class="panel-intro">${esc(slot.slotHint)}</p>` : ""}${fields || '<p class="panel-intro">This tool uses the configuration from its Page template.</p>'}</section>`;
  });
  $("#properties-content").innerHTML = `<div class="panel-label">PAGE CONTENT</div><p class="panel-intro">Fill the slots from your Body template.</p><button type="button" class="quiet-button" data-edit-shared-layout>Edit reusable layout</button>${sections.join("")}`;
  records.forEach((record, index) => {
    const wrapper = $(`[data-slot-control="${index}"]`, $("#properties-content"));
    const input = $("[data-field]", wrapper);
    if (!input) return;
    if (app.invalidFields.has(record.key)) input.value = app.invalidFields.get(record.key);
    input.addEventListener("input", () => {
      try {
        editBodySlot(block.id, record.slot.id, record.templateId, record.field, fieldValue(record.field, input), record.nativeProperty);
        app.invalidFields.delete(record.key);
        input.removeAttribute("aria-invalid"); input.setCustomValidity?.("");
      } catch (error) {
        beginDraft(); app.invalidFields.set(record.key, input.value);
        input.setAttribute("aria-invalid", "true"); input.setCustomValidity?.(error.message);
        status(error.message, "error");
      }
    });
    $$("[data-upload-field]", wrapper).forEach(button => { button.onclick = () => {
      const picker = document.createElement("input"); picker.type = "file";
      picker.accept = record.slot.kind === "video" ? "video/*" : record.slot.kind === "image" ? "image/png,image/jpeg,image/webp,image/gif" : "image/png,image/jpeg,image/webp,image/gif,audio/*,video/*,application/pdf";
      picker.onchange = async () => {
        if (!picker.files?.[0]) return;
        try {
          const data = new FormData(); data.append("file", picker.files[0]);
          const asset = await api("/api/media", { method: "POST", body: data });
          editBodySlot(block.id, record.slot.id, record.templateId, record.field, asset.url, record.nativeProperty);
          app.invalidFields.delete(record.key); renderInspector(); await flushDraft();
        } catch (error) { toast(error.message, true); }
      }; picker.click();
    }; });
  });
  return true;
}
function renderInspector() {
  const mode = app.workspaceChrome?.leftPanel?.selectedId || "properties";
  const appearance = mode === "appearance";
  const panel = $("#properties-content");
  delete panel.dataset.elementBlock; delete panel.dataset.elementPath; delete panel.dataset.appearanceBlock;
  if (studioElementEditor.render(panel, mode)) return;
  panel.dataset.panel = inspectorPanels.includes(mode) ? mode : "properties";
  panel.setAttribute("aria-label", ({ appearance: "Appearance", actions: "Actions", code: "Code" })[mode] || "Properties");
  const p = page();
  const block = p?.blocks.find((b) => b.id === app.selected);
  if (appearance && block) panel.dataset.appearanceBlock = block.id;
  else delete panel.dataset.appearanceBlock;
  if (!block) {
    $("#properties-content").innerHTML =
      `<div class="panel-empty">${icon("pen")}Select an element on the page to edit its ${({ appearance: "appearance", actions: "actions", code: "code" })[mode] || "properties"}.</div>`;
    return;
  }
  const definition = app.template?.blocks.find((b) => b.id === block.type);
  const sharedRegion = sharedLayoutRegion(block);
  if (!appearance && sharedRegion === "Body" && renderBodySlotInspector(block)) return;
  if (sharedRegion) {
    $("#properties-content").innerHTML =
      `<div class="panel-label">SHARED ${sharedRegion.toUpperCase()}</div><p class="panel-intro">Its Blocks and elements are reused by every Page using this layout.</p><button type="button" class="quiet-button" data-edit-shared-layout>Edit reusable layout</button><p class="panel-intro">Your Page’s writing and media stay separate from this shared design.</p>`;
    return;
  }
  const styleFields = new Set(["span", "font", "align", "color"]);
  const canvasFields = new Set(["studioPositioned", "studioX", "studioY", "studioWidth", "studioHeight"]);
  const fields = (definition?.fields || []).filter(field => !canvasFields.has(field.id) && field.id !== "studioElements");
  if (appearance) {
    // Use this Page's validated template fields and the same draft/save route
    // as Properties. The legacy native CSS editor owns a separate source model.
    const styling = fields.filter(field => styleFields.has(field.id) || field.type === "color"
      || /^(background|border|padding|margin|radius|corner|opacity|font|textAlign|lineHeight|letterSpacing|width|height|fit|objectFit|gap|shadow)/i.test(field.id));
    panel.innerHTML = `<div class="panel-toolbar"><span class="block-name">${esc(definition?.title || block.type)}</span></div>${styling.length
      ? styling.map(field => fieldControl(field, block.values[field.id])).join("")
      : '<p class="panel-intro">This element uses its template’s appearance. Select a styled element or edit the reusable layout.</p>'}`;
    bindInspectorFields(block, definition);
    return;
  }
  $("#properties-content").innerHTML =
    `<div class="panel-toolbar"><span class="block-name">${esc(definition?.title || block.type)}</span><button class="icon-button" id="block-up" title="Move up" aria-label="Move up">${icon("up")}</button><button class="icon-button" id="block-down" title="Move down" aria-label="Move down">${icon("down")}</button><button class="icon-button danger-button" id="block-delete" title="Remove element" aria-label="Remove element">${icon("trash")}</button></div>${fields.filter((f) => !styleFields.has(f.id)).map((f) => fieldControl(f, block.values[f.id])).join("")}${fields.some((f) => styleFields.has(f.id)) ? `<div class="panel-label">STYLE & LAYOUT</div><div class="property-grid">${fields.filter((f) => styleFields.has(f.id)).map((f) => fieldControl(f, block.values[f.id])).join("")}</div>` : ""}<label class="field"><span>Content region</span><select id="block-region">${(app.template?.regions || []).filter(region => !region.allowedBlocks.length || region.allowedBlocks.includes(block.type)).map(region => `<option value="${esc(region.id)}" ${region.id === block.region ? "selected" : ""}>${esc(region.title)}</option>`).join("")}</select></label><p class="panel-intro">Move this object between compatible regions. Up and Down change its order within that region.</p>`;
  if (block.values.studioPositioned === true && supportsStudioCanvasGeometry(definition)) {
    const reset = document.createElement("button");
    reset.id = "block-reset-placement"; reset.type = "button"; reset.className = "quiet-button";
    reset.textContent = "Reset placement"; reset.title = "Return this element to the page’s normal layout";
    reset.onclick = async () => {
      try {
        await flushDraft();
        if (app.draft) return;
        const current = page()?.blocks.find(item => item.id === block.id);
        if (!current || sharedLayoutRegion(current)) return;
        const draft = beginDraft(), target = draft.blocks.find(item => item.id === current.id);
        for (const field of Object.keys(studioGeometryFieldTypes)) delete target.values[field];
        status("Saving…", "dirty"); await flushDraft(); renderPanel();
      } catch (error) { toast(error.message, true); }
    };
    $(".panel-toolbar", $("#properties-content")).after(reset);
  }
  bindInspectorFields(block, definition);
  $("#block-up").onclick = () => reorderBlock(-1);
  $("#block-down").onclick = () => reorderBlock(1);
  const ordered = p.blocks.filter(item => item.region === block.region);
  const position = ordered.findIndex(item => item.id === block.id);
  $("#block-up").disabled = position <= 0;
  $("#block-down").disabled = position >= ordered.length - 1;
  $("#block-region").onchange = event => moveBlockToRegion(event.target.value);
  $("#block-delete").onclick = () => removeBlock();
  $$("[data-upload-field]", $("#properties-content")).forEach(
    (b) => (b.onclick = () => uploadMedia(b.dataset.uploadField)),
  );
}
function bindInspectorFields(block, definition) {
  $$("[data-field]", $("#properties-content")).forEach((input) => {
    const key = `${block.id}:${input.dataset.field}`;
    if (app.invalidFields.has(key)) input.value = app.invalidFields.get(key);
    input.addEventListener("input", () => {
      try {
        const f = definition.fields.find((x) => x.id === input.dataset.field);
        delete input.dataset.empty;
        const value = fieldValue(f, input);
        app.invalidFields.delete(key);
        editBlock(block.id, f.id, value);
      } catch {
        beginDraft();
        app.invalidFields.set(key, input.value);
        status("Enter valid field data", "error");
      }
    });
  });
  $$("[data-clear-field]", $("#properties-content")).forEach((button) => {
    button.onclick = () => {
      app.invalidFields.delete(`${block.id}:${button.dataset.clearField}`);
      editBlock(block.id, button.dataset.clearField, undefined);
      renderInspector();
    };
  });
  $("#properties-content").onfocusin = () => sendFrame({ kind: "select", blockId: app.selected });
}
async function reorderBlock(delta) {
  try {
    await flushDraft();
    if (app.draft) return;
    const p = structuredClone(page());
    const i = p.blocks.findIndex((b) => b.id === app.selected);
    const ordered = p.blocks.filter(block => block.region === p.blocks[i].region);
    const neighbor = ordered[ordered.findIndex(block => block.id === app.selected) + delta];
    if (!neighbor) return;
    const j = p.blocks.findIndex(block => block.id === neighbor.id);
    [p.blocks[i], p.blocks[j]] = [p.blocks[j], p.blocks[i]];
    await mutate(
      [{ kind: "savePage", id: p.id, page: p }],
      "Reorder element",
      false,
    );
    sendFrame({ kind: "refresh", pageId: p.id });
    if (!$("#page-fallback").hidden) renderFallback();
    renderInspector();
  } catch (e) {
    toast(e.message, true);
  }
}
async function moveBlockToRegion(regionId) {
  try {
    await flushDraft();
    if (app.draft) return;
    const p = structuredClone(page());
    const block = p.blocks.find(item => item.id === app.selected);
    const region = app.template.regions.find(item => item.id === regionId);
    if (!block || !region || (region.allowedBlocks.length && !region.allowedBlocks.includes(block.type))) return;
    block.region = regionId;
    await mutate([{ kind: "savePage", id: p.id, page: p }], "Move object to " + region.title, false);
    sendFrame({ kind: "refresh", pageId: p.id });
    if (!$("#page-fallback").hidden) renderFallback();
    renderInspector();
  } catch (error) { toast(error.message, true); renderInspector(); }
}
async function removeBlock() {
  try {
    await flushDraft();
    if (app.draft) return;
    const p = structuredClone(page());
    p.blocks = p.blocks.filter((b) => b.id !== app.selected);
    p.sources.forEach(
      (source) =>
        (source.blockIds = source.blockIds.filter((id) => id !== app.selected)),
    );
    await mutate(
      [{ kind: "savePage", id: p.id, page: p }],
      "Remove element",
      false,
    );
    app.selected = null;
    renderPanel();
    sendFrame({ kind: "refresh", pageId: p.id });
    if (!$("#page-fallback").hidden) renderFallback();
    toast("Element removed. Undo is available.");
  } catch (e) {
    toast(e.message, true);
  }
}
async function uploadMedia(field) {
  const input = document.createElement("input");
  input.type = "file";
  input.accept =
    "image/png,image/jpeg,image/webp,image/gif,audio/*,video/*,application/pdf";
  input.onchange = async () => {
    if (!input.files[0]) return;
    const data = new FormData();
    data.append("file", input.files[0]);
    try {
      status("Importing media…", "dirty");
      const asset = await api("/api/media", { method: "POST", body: data });
      editBlock(app.selected, field, asset.url);
      renderInspector();
      await flushDraft();
    } catch (error) {
      toast(error.message, true);
    }
  };
  input.click();
}
function archiveBranchIds(nodes, id) {
  const known = new Map(nodes.map((item) => [item.id, item]));
  const ids = new Set();
  const pending = known.has(id) ? [id] : [];
  while (pending.length) {
    const current = pending.pop();
    if (ids.has(current)) continue;
    ids.add(current);
    for (const child of nodes) if (child.parentId === current) pending.push(child.id);
  }
  return ids;
}
function archiveCheckState(nodes, selection, id) {
  const branch = archiveBranchIds(nodes, id);
  const count = [...branch].filter((key) => selection.has(key)).length;
  return { checked: branch.size > 0 && count === branch.size, mixed: count > 0 && count < branch.size };
}
function archiveToggleSelection(nodes, selection, id, include) {
  const known = new Map(nodes.map((item) => [item.id, item]));
  const result = new Set([...selection].filter((key) => known.has(key)));
  if (!known.has(id)) return result;
  for (const key of archiveBranchIds(nodes, id)) include ? result.add(key) : result.delete(key);
  let parentId = known.get(id)?.parentId;
  const visited = new Set();
  while (known.has(parentId) && !visited.has(parentId)) {
    visited.add(parentId);
    const hasSelection = [...archiveBranchIds(nodes, parentId)].some((key) => key !== parentId && result.has(key));
    if (include || hasSelection) result.add(parentId);
    else result.delete(parentId);
    parentId = known.get(parentId).parentId;
  }
  return result;
}
function archiveTreeMarkup(draft, selection, collapsed = new Set()) {
  const known = new Set(draft.nodes.map((item) => item.id));
  const visited = new Set();
  const renderNode = (item) => {
    if (visited.has(item.id)) return "";
    visited.add(item.id);
    const children = draft.nodes.filter((child) => child.parentId === item.id);
    const state = archiveCheckState(draft.nodes, selection, item.id);
    return `<li><div class="archive-node-row">${children.length ? `<button type="button" class="archive-expand" data-archive-expand="${esc(item.id)}" aria-expanded="${!collapsed.has(item.id)}" aria-label="${collapsed.has(item.id) ? "Expand" : "Collapse"} ${esc(item.title)}">${icon(collapsed.has(item.id) ? "arrow" : "down")}</button>` : '<span class="archive-expand-placeholder"></span>'}<label><input type="checkbox" data-archive-node="${esc(item.id)}" ${state.checked ? "checked" : ""} aria-checked="${state.mixed ? "mixed" : state.checked}"><span class="archive-node-icon">${icon(item.type === "page" ? "page" : item.type === "work" ? "book" : "folder")}</span><span class="archive-node-title">${esc(item.title)}</span><span class="archive-node-type">${esc(label(item.type))}</span></label></div>${children.length ? `<ul ${collapsed.has(item.id) ? "hidden" : ""}>${children.map(renderNode).join("")}</ul>` : ""}</li>`;
  };
  const roots = draft.nodes.filter((item) => !known.has(item.parentId));
  return `<ul>${roots.map(renderNode).join("")}</ul>`;
}
async function flushForArchiveReview() {
  await flushDraft();
  await app.mutation;
  if (app.draft || app.frameDirty)
    throw new Error("Save or recover the current Page draft before preparing an Archives review. Your writing is preserved.");
}
function archiveReviewCurrent(review) { return app.archives === review; }
function renderArchiveReview(review) {
  if (!archiveReviewCurrent(review)) return;
  const focusedNode = document.activeElement?.dataset?.archiveNode;
  const focusedExpand = document.activeElement?.dataset?.archiveExpand;
  const draft = review.draft;
  const busy = review.busy || review.loading;
  $("#archives-title").textContent = draft ? `Publish ${draft.title}` : "Publish to Archives";
  $("#archives-target-title").textContent = review.target?.targetTitle || "Atlas Archives";
  $("#archives-target-status").textContent = review.target?.message || (review.target?.canSync ? "Ready to sync" : "Open this library in Hypershell to connect to Archives. You can keep preparing this review.");
  $("#archives-error").textContent = review.error || "";
  $("#archives-stale").hidden = !draft?.stale;
  $("#archives-refresh").disabled = busy || !draft;
  $("#archives-close").disabled = $("#archives-done").disabled = busy;
  $("#archives-sync").disabled = busy || !draft || !!draft.stale || review.dirty || !!review.saving || !!review.conflicted || !review.selected.size || !review.target?.canSync;
  $("#archives-sync").textContent = review.syncing ? "Syncing…" : "Sync to Archives";
  $("#archives-save-status").textContent = review.loading ? "Preparing a local review…" : review.saving ? "Saving selection locally…" : review.dirty ? "Selection has unsaved changes" : draft ? "Selection saved locally" : "";
  if (draft) {
    const selected = draft.nodes.filter((item) => review.selected.has(item.id));
    const pages = selected.filter((item) => item.type === "page").length;
    const containers = selected.length - pages;
    $("#archives-summary").textContent = `${pages} ${pages === 1 ? "Page" : "Pages"} · ${containers} ${containers === 1 ? "container" : "containers"} selected`;
    $("#archives-tree").innerHTML = archiveTreeMarkup(draft, review.selected, review.collapsed);
    $$("[data-archive-node]", $("#archives-tree")).forEach((input) => {
      const state = archiveCheckState(draft.nodes, review.selected, input.dataset.archiveNode);
      input.indeterminate = state.mixed;
      input.disabled = busy;
    });
    $$("[data-archive-expand]", $("#archives-tree")).forEach((button) => { button.disabled = busy; });
    if (focusedNode) $$("[data-archive-node]", $("#archives-tree")).find((input) => input.dataset.archiveNode === focusedNode)?.focus();
    if (focusedExpand) $$("[data-archive-expand]", $("#archives-tree")).find((button) => button.dataset.archiveExpand === focusedExpand)?.focus();
  } else {
    $("#archives-summary").textContent = review.loading ? "Loading review…" : "Review unavailable";
    $("#archives-tree").innerHTML = '<div class="panel-empty">Choose a branch in your library to prepare its publishing review.</div>';
  }
  const receipt = review.receipt || draft?.lastReceipt;
  $("#archives-receipt").hidden = !receipt;
  if (receipt) {
    const address = safeUrl(receipt.url || receipt.archiveUrl || "");
    $("#archives-receipt").innerHTML = `<strong>Synced to Archives</strong><span>${esc(receipt.message || "Checked items were updated. Your local Pages are preserved.")}</span>${address && (receipt.url || receipt.archiveUrl) ? `<a href="${esc(address)}" target="_blank" rel="noopener noreferrer">Open in Archives ${icon("arrow")}</a>` : ""}`;
  }
}
function setArchiveDraft(review, draft, keepSelection = false) {
  if (!draft?.id || !Array.isArray(draft.nodes) || !Array.isArray(draft.selectedNodeIds))
    throw new Error("The saved Archives review could not be read. Refresh the review and try again.");
  review.draft = draft;
  if (!keepSelection) review.selected = new Set(draft.selectedNodeIds);
  review.receipt = draft.lastReceipt || review.receipt;
}
async function getArchiveDraft(rootNodeId) {
  try { return await api(`/api/archives/drafts?rootNodeId=${encodeURIComponent(rootNodeId)}`); }
  catch (error) { if (error.status === 404) return null; throw error; }
}
async function loadArchiveTarget(review) {
  if (!window.chrome?.webview) return;
  try {
    const target = await native("archives.status", { libraryId: app.state.libraryId });
    if (archiveReviewCurrent(review)) review.target = { ...target, canSync: target.canSync ?? target.available ?? false };
  } catch (error) { review.target = { canSync: false, message: error.message }; }
}
async function openArchiveReview(rootNodeId = app.id) {
  if (!app.edit || !node(rootNodeId) || ["page", "library"].includes(node(rootNodeId).type)) return;
  try { await flushForArchiveReview(); }
  catch (error) { toast(error.message, true); return; }
  const existing = app.archives;
  if (existing?.draft?.rootNodeId === rootNodeId && (existing.dirty || existing.error)) {
    $("#archives-dialog").showModal(); renderArchiveReview(existing); return;
  }
  const review = { rootNodeId, draft: null, selected: new Set(), collapsed: new Set(), dirty: false, saving: null,
    version: 0, busy: false, loading: true, syncing: false, conflicted: false, target: null, error: "", receipt: null };
  app.archives = review;
  $("#archives-dialog").showModal(); renderArchiveReview(review);
  try {
    let draft = await getArchiveDraft(rootNodeId);
    if (!draft) draft = await api("/api/archives/drafts", { method: "POST", body: JSON.stringify({ rootNodeId, expectedContentRevision: app.state.revision }) });
    if (!archiveReviewCurrent(review)) return;
    setArchiveDraft(review, draft);
    if (draft.contentRevision !== app.state.revision) draft.stale = true;
    await loadArchiveTarget(review);
  } catch (error) { review.error = error.message; }
  finally { review.loading = false; renderArchiveReview(review); }
}
async function saveArchiveSelection(review) {
  if (review.saving) return review.saving;
  if (!review.dirty || !review.draft) return;
  if (review.conflicted) throw new Error("This review changed in another window. Refresh review to keep your selection and continue.");
  const save = (async () => {
    while (review.dirty) {
      const version = review.version;
      const saved = await api(`/api/archives/drafts/${encodeURIComponent(review.draft.id)}`, {
        method: "PUT", body: JSON.stringify({ expectedDraftRevision: review.draft.draftRevision, selectedNodeIds: [...review.selected] }),
      });
      setArchiveDraft(review, saved, version !== review.version);
      if (version === review.version) review.dirty = false;
      review.error = "";
    }
  })();
  review.saving = save; renderArchiveReview(review);
  try { await save; }
  catch (error) {
    review.conflicted = error.status === 409;
    review.error = review.conflicted ? "This review changed in another window. Your selection is preserved here. Refresh review to continue." : error.message;
    throw error;
  } finally { review.saving = null; renderArchiveReview(review); }
}
function changeArchiveSelection(id, include) {
  const review = app.archives;
  if (!review?.draft || review.busy || review.loading) return;
  review.selected = archiveToggleSelection(review.draft.nodes, review.selected, id, include);
  review.version++; review.dirty = true; review.receipt = null;
  renderArchiveReview(review);
  $$("[data-archive-node]", $("#archives-tree")).find((input) => input.dataset.archiveNode === id)?.focus();
  saveArchiveSelection(review).catch(() => {});
}
async function refreshArchiveReview() {
  const review = app.archives;
  if (!review?.draft || review.busy || review.loading) return;
  review.busy = true; review.error = ""; renderArchiveReview(review);
  try {
    await flushForArchiveReview();
    const kept = new Set(review.selected);
    const hadConflict = review.conflicted;
    if (hadConflict) {
      const latest = await getArchiveDraft(review.rootNodeId);
      if (!latest) throw new Error("This saved review is no longer available. Close and reopen the publishing review.");
      setArchiveDraft(review, latest, true); review.conflicted = false;
    } else await saveArchiveSelection(review);
    const refreshed = await api(`/api/archives/drafts/${encodeURIComponent(review.draft.id)}/refresh`, {
      method: "POST", body: JSON.stringify({ expectedDraftRevision: review.draft.draftRevision, expectedContentRevision: app.state.revision }),
    });
    setArchiveDraft(review, refreshed);
    if (hadConflict) {
      const known = new Set(refreshed.nodes.map((item) => item.id));
      review.selected = new Set([...kept].filter((id) => known.has(id)));
      review.version++; review.dirty = true;
      await saveArchiveSelection(review);
    }
    review.error = "";
    await loadArchiveTarget(review);
  } catch (error) {
    if (error.status === 409) review.conflicted = true;
    review.error = error.message;
  }
  finally { review.busy = false; renderArchiveReview(review); }
}
async function syncArchiveReview() {
  const review = app.archives;
  if (!review?.draft || review.busy || review.loading || !review.target?.canSync) return;
  review.busy = true; review.syncing = true; review.error = ""; renderArchiveReview(review);
  try {
    await flushForArchiveReview();
    await saveArchiveSelection(review);
    if (review.draft.stale || review.draft.contentRevision !== app.state.revision)
      throw new Error("The library changed since this review. Refresh review before syncing.");
    if (!review.selected.size) throw new Error("Check at least one item to sync.");
    const result = await native("archives.sync", {
      libraryId: app.state.libraryId, draftId: review.draft.id,
      expectedDraftRevision: review.draft.draftRevision, expectedContentRevision: review.draft.contentRevision,
      reviewHash: review.draft.reviewHash,
    });
    review.receipt = result.receipt || result;
    if (result.draft) setArchiveDraft(review, result.draft);
  } catch (error) { review.error = error.message; }
  finally { review.busy = false; review.syncing = false; renderArchiveReview(review); }
}
async function closeArchiveReview() {
  const review = app.archives;
  if (review?.busy || review?.loading) return;
  try {
    if (review) await saveArchiveSelection(review);
    $("#archives-dialog").close();
  } catch { renderArchiveReview(review); }
}
function openDialog(title, body, submit, onSubmit) {
  $("#dialog").classList.remove("template-dialog");
  $("#dialog-title").textContent = title;
  $("#dialog-body").innerHTML = body;
  $("#dialog-error").textContent = "";
  $("#dialog-submit").textContent = submit;
  $("#dialog-form").onsubmit = async (event) => {
    event.preventDefault();
    const button = $("#dialog-submit");
    button.disabled = true;
    try {
      await onSubmit(new FormData(event.target));
      $("#dialog").close();
    } catch (error) {
      $("#dialog-error").textContent = error.message;
    } finally {
      button.disabled = false;
    }
  };
  $("#dialog").showModal();
  setTimeout(() => $("input,select,textarea", $("#dialog-body"))?.focus(), 20);
}
function templatePreview(template) {
  const color = (value, fallback) => /^#[0-9a-f]{6}$/i.test(value || "") ? value : fallback;
  return `<div class="template-preview" data-design="${esc(template.id)}" style="--preview-bg:${color(template.background, "#f5f3ee")};--preview-accent:${color(template.accent, "#729b81")};--preview-ink:${color(template.ink, "#283731")}" aria-hidden="true"><div class="preview-top"><i></i><span></span><span></span></div><div class="preview-head"><small>${esc(template.category)}</small><strong>${esc(template.demoTitle || template.title)}</strong><span></span><span></span></div><div class="preview-picture"><i></i><i></i><i></i></div><div class="preview-columns"><div><i></i><span></span><span></span></div><div><i></i><span></span><span></span></div><div><i></i><span></span><span></span></div></div><div class="preview-bottom"></div></div>`;
}
function templateChoices(catalog, selectedId) {
  return catalog.map((template) => `<label class="template-option" data-template-id="${esc(template.id)}" data-category="${esc(template.category)}" data-template-search="${esc(`${template.title} ${template.description} ${template.category}`.toLowerCase())}"><input type="radio" name="starterId" value="${esc(template.id)}" ${template.id === selectedId ? "checked" : ""}>${templatePreview(template)}<span class="template-copy"><span class="template-title">${esc(template.title)}<span class="template-check">${icon("check")}</span></span><span class="template-description">${esc(template.description)}</span><span class="template-category">${esc(template.category)}${template.isOriginal ? " · Original" : ""}</span></span></label>`).join("");
}
function studioPageTemplates() {
  if (!app.pipeline) return [];
  if (Array.isArray(app.pipeline.pageTemplates) && app.pipeline.pageTemplates.length)
    return app.pipeline.pageTemplates.filter(item => typeof item.id === "string" && item.id.length)
      .map((item, index) => ({ ...item, name: item.name || `Page template ${index + 1}` }));
  return app.pipeline.pageTemplate ? [{ ...app.pipeline.pageTemplate,
    id: app.pipeline.defaultPageTemplateId || app.pipeline.pageTemplate.id || "default",
    name: app.pipeline.pageTemplate.name || "Content Page" }] : [];
}
function pageTemplateChooser() {
  const templates = studioPageTemplates();
  if (!templates.length) return "";
  const selected = templates.find(item => item.id === app.pipeline.defaultPageTemplateId) || templates[0];
  return `<section id="studio-page-template-chooser" class="studio-page-template-chooser"><label class="field"><span>Page template</span><select name="studioPageTemplateId" aria-label="Page template">${templates.map(item => `<option value="${esc(item.id)}" ${item.id === selected.id ? "selected" : ""}>${esc(item.name)}</option>`).join("")}</select></label><p class="panel-intro">Begin with this reusable design, then add this Page’s content. Later design updates preserve your writing and media.</p><p id="studio-page-template-summary" class="studio-page-template-summary" role="status"></p></section>`;
}
async function createNodeDialog(parent, preferredType = null) {
  const allowed = allowedChildTypes(parent).filter(type => !app.pipeline || type !== "work");
  const owner = studioWorkFor(parent), landing = studioLandingNode(owner);
  if (owner && allowed.includes("page")) allowed.splice(allowed.indexOf("page") + 1, 0, "landing");
  if (!allowed.length) return;
  try { await flushDraft(); } catch { return; }
  if (app.draft) return;
  let catalog = null;
  openDialog(
    "Add to " + parent.title,
    `<div class="create-fields"><label class="field"><span>Type</span><select name="type">${allowed.map((t) => `<option value="${t}" ${t === preferredType ? "selected" : ""}>${esc(label(t))}</option>`).join("")}</select></label><label class="field"><span>Name</span><input name="title" required maxlength="160" placeholder="Give it a name"${preferredType === "landing" ? ` value="${esc(landing?.title || "Landing page")}"` : ""}></label></div><p id="studio-landing-note" class="panel-intro" hidden>The Work has one landing page, kept above its chapters and other Pages.${landing ? " Creating it opens the existing landing page and keeps its content." : ""}</p><section id="base-template-library" aria-labelledby="base-template-heading" hidden><div class="template-library-heading"><div><h3 id="base-template-heading">Base Template Library</h3><p>Choose a starting design. Every template works for any subject.</p></div><span id="template-count" class="section-count"></span></div><div id="template-library-content"><div class="loading">Loading designs…</div></div></section>${pageTemplateChooser()}<p id="page-template-note" class="panel-intro">New Pages begin with your reusable Page layout. Add their content after creating them.</p>`,
    "Create",
    async (form) => {
      await flushDraft();
      if (app.draft) throw new Error("Save or recover the current page draft before creating an item.");
      if (form.get("type") === "landing") {
        await mutateStudioLibrary("create-landing", { title: form.get("title"), workId: owner.id });
        return;
      }
      const starterId = form.get("type") === "work" ? form.get("starterId") : null;
      if (form.get("type") === "work" && !catalog?.some((item) => item.id === starterId))
        throw new Error("Choose a base template for your new work.");
      const chosenTemplateId = form.get("type") === "page" ? form.get("studioPageTemplateId") : null;
      if (form.get("type") === "page" && app.pipeline && studioPageTemplates().length
        && !studioPageTemplates().some(item => item.id === chosenTemplateId))
        throw new Error("Choose a Page template for this Page.");
      const id = crypto.randomUUID().replaceAll("-", "");
      await mutate(
        [
          {
            kind: "createNode",
            id,
            parentId: parent.id,
            nodeType: form.get("type"),
            title: form.get("title"),
            ...(starterId ? { starterId } : {}),
            ...(chosenTemplateId ? { appearance: { studioPageTemplateId: chosenTemplateId } } : {}),
          },
        ],
        `Create ${form.get("title")}`,
      );
      await navigate(id);
    },
  );
  const type = $('[name="type"]', $("#dialog"));
  const showTemplates = () => {
    const isWork = type.value === "work";
    $("#base-template-library").hidden = !isWork;
    const isPage = type.value === "page";
    const isLanding = type.value === "landing";
    $("#studio-landing-note").hidden = !isLanding;
    $("#page-template-note").hidden = !isPage || !!$("#studio-page-template-chooser");
    const chooser = $("#studio-page-template-chooser");
    if (chooser) {
      chooser.hidden = !isPage;
      $('[name="studioPageTemplateId"]', chooser).disabled = !isPage;
    }
    $("#dialog").classList.toggle("template-dialog", isWork);
    $$("[name=starterId]", $("#dialog")).forEach((radio) => { radio.disabled = !isWork; });
  };
  type.addEventListener("change", () => {
    if (type.value === "landing") $('[name="title"]', $("#dialog")).value = landing?.title || "Landing page";
    showTemplates();
  });
  const pageTemplateSelect = $('[name="studioPageTemplateId"]', $("#dialog"));
  if (pageTemplateSelect) {
    const describe = () => {
      const selected = studioPageTemplates().find(item => item.id === pageTemplateSelect.value);
      const slots = selected?.blocks || [];
      const regions = [...new Set(slots.map(item => item.region))];
      $("#studio-page-template-summary").textContent = selected
        ? `${selected.name} · ${slots.length} reusable ${slots.length === 1 ? "slot" : "slots"}${regions.length ? ` · ${regions.join(", ")}` : ""}` : "";
    };
    pageTemplateSelect.addEventListener("change", describe); describe();
  }
  showTemplates();
  if (!allowed.includes("work")) return;
  const loadCatalog = async () => {
    try {
      const result = app.baseTemplates || await api("/api/base-templates");
      if (!Array.isArray(result) || !result.length) throw new Error("No base templates are available.");
      app.baseTemplates = result;
      // A closed or replaced dialog must not receive a late catalog response.
      if (!$("#base-template-library") || !$("#dialog").open || $('[name="type"]', $("#dialog")) !== type) return;
      catalog = result;
      const categories = [...new Set(catalog.map((item) => item.category))];
      $("#template-library-content").innerHTML = `<div class="template-filters"><div class="search-box">${icon("search")}<input id="template-search" type="search" placeholder="Find a design" aria-label="Find a base template"></div><select id="template-category" aria-label="Template category"><option value="">All categories</option>${categories.map((category) => `<option value="${esc(category)}">${esc(category)}</option>`).join("")}</select></div><div class="template-grid" role="radiogroup" aria-label="Base templates">${templateChoices(catalog, catalog[0].id)}</div><p id="template-empty" class="panel-intro" hidden>No designs match. Try another search or category.</p><p id="template-selection" class="template-selection" role="status"></p>`;
      const filter = () => {
        const query = $("#template-search").value.trim().toLowerCase();
        const category = $("#template-category").value;
        let visible = 0;
        $$("[data-template-id]", $("#dialog")).forEach((card) => {
          card.hidden = !card.dataset.templateSearch.includes(query) || !!category && card.dataset.category !== category;
          if (!card.hidden) visible++;
        });
        $("#template-count").textContent = `${visible} / ${catalog.length}`;
        $("#template-empty").hidden = visible !== 0;
      };
      const selection = () => {
        const selectedId = $('[name="starterId"]:checked', $("#dialog"))?.value;
        const selected = catalog.find((item) => item.id === selectedId);
        $("#template-selection").textContent = selected ? `${selected.title} selected · Customize any element after creation.` : "Choose a base template.";
      };
      $("#template-search").oninput = filter;
      $("#template-category").onchange = filter;
      $$("[name=starterId]", $("#dialog")).forEach((radio) => { radio.onchange = selection; });
      showTemplates(); filter(); selection();
    } catch (error) {
      if (!$("#template-library-content")) return;
      $("#template-library-content").innerHTML = `<p class="error-message">${esc(error.message)}</p><button type="button" id="retry-templates" class="quiet-button">Try again</button>`;
      $("#retry-templates").onclick = loadCatalog;
    }
  };
  await loadCatalog();
}
function organizeDialog() {
  const n = node();
  if (n.type === "library") return;
  const landingOwner = studioLandingNode()?.id === n.id ? studioWorkFor(n) : null;
  openDialog(
    "Organize " + n.title,
    `<label class="field"><span>Name</span><input name="title" value="${esc(n.title)}" required></label><label class="field"><span>Parent</span><select name="parentId">${[
      rootNode(),
      ...app.state.nodes,
    ]
      .filter(
        (p) =>
          (!landingOwner || p.id === landingOwner.id) &&
          allowedChildTypes(p).includes(n.type) &&
          !ancestors(p).some((ancestor) => ancestor.id === n.id) &&
          (n.type === "work" ||
            p.id === n.parentId ||
            p.workId === n.workId ||
            p.id === n.workId),
      )
      .map(
        (p) =>
          `<option value="${esc(p.id)}" ${p.id === n.parentId ? "selected" : ""}>${esc(p.title)} · ${esc(label(p.type))}</option>`,
      )
      .join(
        "",
      )}</select></label><label class="field"><span>Order</span><input name="order" type="number" value="${n.order}" step="1"${landingOwner ? " readonly" : ""}></label><button type="button" id="delete-node" class="quiet-button danger-button">${icon("trash")}Move to recovery</button>`,
    "Save",
    async (form) => {
      await flushDraft();
      if (app.draft) throw new Error("Save or recover the current draft first.");
      const ops = [{ kind: "updateNode", id: n.id, title: form.get("title") }];
      if (
        !landingOwner && (
        form.get("parentId") !== n.parentId ||
        Number(form.get("order")) !== n.order
        )
      )
        ops.push({
          kind: "moveNode",
          id: n.id,
          parentId: form.get("parentId"),
          order: Number(form.get("order")),
        });
      await mutate(ops, `Organize ${n.title}`);
    },
  );
  $("#delete-node").onclick = async () => {
    try {
      await flushDraft();
      if (app.draft) throw new Error("Save or recover the current draft first.");
      await mutate([{ kind: "deleteNode", id: n.id }], `Remove ${n.title}`);
      $("#dialog").close();
      navigate(n.parentId);
      toast("Moved to recovery. Undo restores this item and its contents.");
    } catch (error) {
      $("#dialog-error").textContent = error.message;
    }
  };
}
async function appearanceDialog() {
  await flushDraft();
  if (app.draft) return;
  await loadTemplate();
  const n = node();
  const appearance = n.type === "library" ? app.state.appearance : n.appearance;
  const inherited = Object.assign(
    {},
    app.state.appearance,
    ...ancestors(n).map((parent) => parent.appearance || {}),
  );
  // The landing identity is structural metadata, not an editable theme field.
  const fields = (app.template?.appearance || []).filter(field => !field.id.startsWith("studio") && field.id !== "workLandingPage");
  openDialog(
    "Appearance",
    `${!["page", "library"].includes(n.type) ? `<label class="field"><span>Cover image</span><div class="field-row"><input name="cover" value="${esc(n.cover || "")}" placeholder="Imported media URL"><button type="button" id="cover-import" class="icon-button" title="Import cover" aria-label="Import cover">${icon("upload")}</button></div></label>` : ""}${fields.map((f) => fieldControl(f, inherited[f.id], "appearance")).join("")}<p class="panel-intro">Background covers the whole page. These controls are defined by the template.</p>`,
    "Save",
    async (form) => {
      const values = { ...appearance };
      for (const f of fields) {
        const input = $$("[data-field]", $("#dialog")).find((control) => control.dataset.field === f.id);
        if (!input.dataset.changed) continue;
        const value = fieldValue(f, input);
        if (value === undefined) delete values[f.id];
        else values[f.id] = value;
      }
    const ops = [{ kind: "setAppearance", id: n.id, appearance: values }];
      if (!["page", "library"].includes(n.type))
        ops.push({ kind: "updateNode", id: n.id, cover: form.get("cover") });
      await mutate(ops, `Customize ${n.title}`);
    },
  );
  $$("[data-field]", $("#dialog")).forEach((input) => input.oninput = () => {
    input.dataset.changed = "true";
    delete input.dataset.empty;
  });
  $$("[data-clear-field]", $("#dialog")).forEach((button) => button.onclick = () => {
    const input = $$("[data-field]", $("#dialog")).find((control) => control.dataset.field === button.dataset.clearField);
    input.dataset.empty = "true";
    input.dataset.changed = "true";
    button.textContent = "Inherited";
  });
  $("#cover-import")?.addEventListener("click", () => {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = "image/png,image/jpeg,image/webp,image/gif";
    input.onchange = async () => {
      if (!input.files[0]) return;
      try {
        const form = new FormData();
        form.append("file", input.files[0]);
        const asset = await api("/api/media", { method: "POST", body: form });
        $('[name="cover"]', $("#dialog")).value = asset.url;
      } catch (e) {
        $("#dialog-error").textContent = e.message;
      }
    };
    input.click();
  });
}
function labelsDialog() {
  openDialog(
    "Names that fit your library",
    Object.entries(app.state.labels)
      .map(
        ([type, value]) =>
          `<label class="field"><span>${esc(type[0].toUpperCase() + type.slice(1))}</span><input name="${type}" value="${esc(value)}" required maxlength="40"></label>`,
      )
      .join(""),
    "Save",
    async (form) => {
      await mutate(
        [{ kind: "setLabels", labels: Object.fromEntries(form) }],
        "Customize structural names",
      );
    },
  );
}
function citedText(message) {
  const text = message.text || "";
  const citations = (message.citations || [])
    .filter(
      (c) =>
        safeUrl(c.url) &&
        Number.isInteger(c.startIndex) &&
        Number.isInteger(c.endIndex) &&
        c.startIndex >= 0 &&
        c.endIndex > c.startIndex &&
        c.endIndex <= text.length,
    )
    .sort((a, b) => a.startIndex - b.startIndex);
  let cursor = 0;
  let html = "";
  for (const citation of citations) {
    if (citation.startIndex < cursor) continue;
    html += esc(text.slice(cursor, citation.startIndex));
    html += `<a href="${esc(citation.url)}" target="_blank" rel="noopener noreferrer" title="${esc(citation.title || citation.url)}">${esc(text.slice(citation.startIndex, citation.endIndex))}</a>`;
    cursor = citation.endIndex;
  }
  return html + esc(text.slice(cursor));
}
function renderAssistant() {
  const available = app.native?.aiAvailable;
  $("#panel-content").innerHTML =
    `<div class="assistant-shell"><div class="assistant-capability"><span class="local-dot"></span>${app.native ? `${esc(app.native.model || app.native.provider || "Hypershell")} · ${app.native.canResearch ? "Web research" : "Writing"}` : "Open in Hypershell for AI"}</div><div class="assistant-log">${app.messages.length ? app.messages.map((m) => `<div class="chat-message ${m.role}"><span class="speaker">${m.role === "user" ? "You" : "Library assistant"}</span>${citedText(m)}${m.sources?.length ? `<div class="panel-label">SOURCES</div>${m.sources.map((s) => (safeUrl(s.url) ? `<a href="${esc(s.url)}" target="_blank" rel="noopener noreferrer">${esc(s.title || s.url)}</a>` : "")).join("\n")}` : ""}</div>`).join("") : `<div class="panel-empty">${icon("spark")}A research and writing partner for your library. Ask for an outline, new pages, or a closer look at a topic.</div><button class="suggested-prompt" data-prompt="Create 3 new works in Natural World called Birds, Mammals, and Reptiles. Give each 10 chapters divided into 2 parts, with placeholder chapter titles.">Organize a new collection of works</button><button class="suggested-prompt" data-prompt="Research the topic of this page and suggest a short, well-sourced addition with citations.">Research this page’s topic</button>`}</div><div class="assistant-compose"><textarea id="ai-prompt" placeholder="What would you like to explore?" aria-label="Message the library assistant" ${!available ? "disabled" : ""}></textarea><div><small id="ai-progress">${app.busy ? "Working…" : "Changes include undo."}</small><button id="ai-send" class="primary-button" ${!available ? "disabled" : ""}>${app.busy ? "Stop" : "Send"}</button></div></div></div>`;
  $$("[data-prompt]").forEach(
    (b) =>
      (b.onclick = () => {
        $("#ai-prompt").value = b.dataset.prompt;
        $("#ai-prompt").focus();
      }),
  );
  $("#ai-send").onclick = () =>
    app.busy
      ? native("ai.cancel", { libraryId: app.state.libraryId })
      : sendAi();
  $("#ai-prompt").onkeydown = (e) => {
    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      sendAi();
    }
  };
}
async function sendAi() {
  const prompt = $("#ai-prompt").value.trim();
  if (!prompt || app.busy) return;
  await flushDraft();
  if (app.draft) return;
  app.messages.push({ role: "user", text: prompt });
  app.busy = true;
  renderAssistant();
  try {
    const response = await native("ai.send", {
      libraryId: app.state.libraryId,
      prompt,
      editMode: app.edit,
      context: { workId: work()?.id, pageId: page()?.id },
    });
    app.messages.push({
      role: "assistant",
      text: response.text || "Done.",
      sources: response.sources,
      citations: response.citations,
    });
    await loadState();
    await render();
  } catch (error) {
    app.messages.push({ role: "assistant", text: error.message });
  } finally {
    app.busy = false;
    if (app.panel === "assistant") renderAssistant();
  }
}
async function setEditMode(enabled) {
  if (!app.chromeReady || app.editChanging || enabled === app.edit) {
    publishChromeState();
    return;
  }
  app.editChanging = true;
  publishChromeState();
  try {
    await flushDraft();
    if (app.draft) return;
    app.edit = enabled;
    document.body.classList.toggle("editing", app.edit);
    await render();
  } catch (error) { toast(error.message, true); }
  finally {
    app.editChanging = false;
    publishChromeState();
  }
}
$("#edit-toggle").onclick = () => setEditMode(!app.edit);
let navWidthLibrary;
let navPreferredWidth = null;
let navResize = null;
function navigationWidthKey() {
  return `hyperlib.navigation-width.${encodeURIComponent(app.state?.libraryId || "library")}`;
}
function navigationWidthBounds() {
  const inspector = app.edit ? $("#editor-panel").getBoundingClientRect().width : 0;
  return { min: 168, max: Math.max(168, Math.min(720,
    Math.floor($("#app-layout").getBoundingClientRect().width - inspector - 320 - 6))) };
}
function updateNavigationWidth() {
  const library = app.state?.libraryId || "library";
  if (navWidthLibrary !== library) {
    navWidthLibrary = library;
    navPreferredWidth = null;
    try {
      const saved = Number(localStorage.getItem(navigationWidthKey()));
      if (Number.isFinite(saved) && saved >= 168) navPreferredWidth = Math.min(saved, 720);
    } catch { /* Browsing remains available when preferences cannot be read. */ }
  }
  const layout = $("#app-layout");
  const divider = $("#nav-resizer");
  if (divider.hidden) return;
  const bounds = navigationWidthBounds();
  if (navPreferredWidth === null) layout.style.removeProperty("--nav-width");
  else layout.style.setProperty("--nav-width", `${Math.max(bounds.min, Math.min(bounds.max, navPreferredWidth))}px`);
  const width = Math.round($("#navigation").getBoundingClientRect().width);
  divider.setAttribute("aria-valuemin", bounds.min);
  divider.setAttribute("aria-valuemax", bounds.max);
  divider.setAttribute("aria-valuenow", width);
  divider.setAttribute("aria-valuetext", `${width} pixels`);
}
function saveNavigationWidth() {
  try {
    if (navPreferredWidth === null) localStorage.removeItem(navigationWidthKey());
    else localStorage.setItem(navigationWidthKey(), String(navPreferredWidth));
  } catch { /* Resizing remains available when preferences cannot be saved. */ }
}
function setNavigationWidth(width) {
  const bounds = navigationWidthBounds();
  navPreferredWidth = Math.round(Math.max(bounds.min, Math.min(bounds.max, width)));
  updateNavigationWidth();
}
function finishNavigationResize() {
  if (!navResize) return;
  const pointerId = navResize.pointerId;
  navResize = null;
  document.body.classList.remove("nav-resizing");
  const divider = $("#nav-resizer");
  if (divider.hasPointerCapture(pointerId)) divider.releasePointerCapture(pointerId);
  saveNavigationWidth();
}
$("#nav-resizer").addEventListener("pointerdown", (event) => {
  if (event.button !== 0 || navResize) return;
  event.preventDefault();
  event.currentTarget.focus({ preventScroll: true });
  navResize = { pointerId: event.pointerId, x: event.clientX,
    width: $("#navigation").getBoundingClientRect().width };
  event.currentTarget.setPointerCapture(event.pointerId);
  document.body.classList.add("nav-resizing");
});
$("#nav-resizer").addEventListener("pointermove", (event) => {
  if (navResize?.pointerId === event.pointerId)
    setNavigationWidth(navResize.width + event.clientX - navResize.x);
});
for (const type of ["pointerup", "pointercancel", "lostpointercapture"])
  $("#nav-resizer").addEventListener(type, finishNavigationResize);
window.addEventListener("blur", finishNavigationResize);
$("#nav-resizer").addEventListener("keydown", (event) => {
  const width = $("#navigation").getBoundingClientRect().width;
  const step = event.shiftKey ? 48 : 16;
  const bounds = navigationWidthBounds();
  const next = { ArrowLeft: width - step, ArrowRight: width + step,
    Home: bounds.min, End: bounds.max }[event.key];
  if (next === undefined) return;
  event.preventDefault();
  setNavigationWidth(next);
  saveNavigationWidth();
});
$("#nav-resizer").addEventListener("dblclick", () => {
  navPreferredWidth = null;
  saveNavigationWidth();
  updateNavigationWidth();
});
function updateNavigationLayout() {
  if (renderWorkspacePanels()) return;
  if (immersiveWork()) {
    $("#navigation").hidden = true;
    $("#nav-expand").hidden = true;
    $("#nav-resizer").hidden = true;
    $("#app-layout").classList.remove("narrow-nav");
    $("#app-layout").classList.add("nav-closed");
    finishNavigationResize();
    return;
  }
  const narrow =
    matchMedia("(max-width: 680px)").matches ||
    (app.edit && matchMedia("(max-width: 960px)").matches);
  const layout = $("#app-layout");
  if (narrow && !app.compactLayout) {
    app.compactLayout = true; app.wideLibraryHidden = layout.classList.contains("inspector-hidden");
  } else if (!narrow && app.compactLayout) {
    app.compactLayout = false; layout.classList.toggle("inspector-hidden", !!app.wideLibraryHidden);
    $("#studio-inspector-toggle").setAttribute("aria-pressed", String(!app.wideLibraryHidden));
  }
  const visible = narrow ? !!app.navOpen : !app.navCollapsed;
  if (narrow && visible) {
    layout.classList.add("inspector-hidden"); $("#studio-inspector-toggle").setAttribute("aria-pressed", "false");
  }
  $("#navigation").hidden = !visible;
  $("#app-layout").classList.toggle("narrow-nav", narrow);
  $("#app-layout").classList.toggle("nav-closed", narrow || !visible);
  $("#nav-expand").hidden = visible;
  $("#nav-resizer").hidden = narrow || !visible;
  if (narrow || !visible) finishNavigationResize();
  updateNavigationWidth();
  updateStudioToolbarInset();
}
function updateStudioToolbarInset() {
  if (!matchMedia("(max-width: 960px)").matches) return;
  const bottom = Math.max(0, Math.min(window.innerHeight - 120, Math.ceil($(".content-topline").getBoundingClientRect().bottom + 8)));
  const value = `${bottom}px`;
  if (document.documentElement.style.getPropertyValue("--studio-toolbar-bottom") !== value)
    document.documentElement.style.setProperty("--studio-toolbar-bottom", value);
}
let studioToolbarFrame = 0;
function scheduleStudioToolbarInset() {
  if (!studioToolbarFrame) studioToolbarFrame = requestAnimationFrame(() => { studioToolbarFrame = 0; updateStudioToolbarInset(); });
}
new ResizeObserver(scheduleStudioToolbarInset).observe($(".content-topline"));
window.addEventListener("scroll", scheduleStudioToolbarInset, { passive: true });
$("#nav-collapse").onclick = () => {
  app.navOpen = false;
  app.navCollapsed = true;
  updateNavigationLayout();
  $("#nav-expand").focus();
};
$("#nav-expand").onclick = () => {
  app.navOpen = true;
  app.navCollapsed = false;
  updateNavigationLayout();
  $("#nav-collapse").focus();
};
for (const side of ["left", "right"]) {
  $(`#studio-${side}-panel-selector`).onchange = event => {
    sendWorkspacePanelAction("panel-select", side, event.target.value, event.target);
  };
  $(`#studio-${side}-panel-collapse`).onclick = event => {
    const state = app.workspaceChrome?.[side + "Panel"];
    if (state) sendWorkspacePanelAction("panel-toggle", side, state.selectedId, event.currentTarget);
  };
}
window.addEventListener("resize", () => {
  updateNavigationLayout();
  if (immersiveWork()) {
    const frame = node()?.type === "page" ? $("#page-frame") : $("#work-frame");
    if (!frame.hidden) resetSurfaceHeight(frame);
  }
});
$("#undo-button").onclick = () => historyAction("undo");
$("#redo-button").onclick = () => historyAction("redo");
$("#appearance-button").onclick = () =>
  appearanceDialog().catch((e) => toast(e.message, true));
$("#more-button").onclick = organizeDialog;
$("#studio-button").onclick = () =>
  native("openStudio", {
    libraryId: app.state.libraryId,
    workId: work()?.id,
  }).catch((e) => toast(e.message, true));
$("#archives-button").onclick = () => openArchiveReview().catch((error) => toast(error.message, true));
$("#archives-close").onclick = $("#archives-done").onclick = closeArchiveReview;
$("#archives-refresh").onclick = refreshArchiveReview;
$("#archives-sync").onclick = syncArchiveReview;
$("#archives-dialog").addEventListener("cancel", (event) => {
  event.preventDefault();
  closeArchiveReview();
});
$("#archives-tree").addEventListener("change", (event) => {
  const checkbox = event.target.closest("[data-archive-node]");
  if (checkbox) changeArchiveSelection(checkbox.dataset.archiveNode, checkbox.checked);
});
$("#archives-tree").addEventListener("click", (event) => {
  const button = event.target.closest("[data-archive-expand]");
  const review = app.archives;
  if (!button || !review || review.busy || review.loading) return;
  const id = button.dataset.archiveExpand;
  review.collapsed.has(id) ? review.collapsed.delete(id) : review.collapsed.add(id);
  renderArchiveReview(review);
});
$("#dialog-close").onclick = $("#dialog-cancel").onclick = () =>
  $("#dialog").close();
document.addEventListener("click", (e) => {
  const workspace = e.target.closest("[data-workspace-select],[data-workspace-close],[data-workspace-help],[data-workspace-action]");
  if (workspace) {
    const action = workspace.dataset.workspaceAction || (workspace.hasAttribute("data-workspace-select") ? "select" : workspace.hasAttribute("data-workspace-close") ? "close" : "help");
    const contentId = workspace.dataset.workspaceSelect || workspace.dataset.workspaceClose || workspace.dataset.workspaceHelp;
    sendWorkspaceAction(action, contentId, workspace); return;
  }
  if (e.target.closest("[data-edit-shared-layout]")) {
    studioFlush(crypto.randomUUID()).then(saved => { if (saved) studioMessage("studio:page-templates", { workId: work()?.id }); });
    return;
  }
  const left = e.target.closest("[data-left-panel]");
  if (left) { clearStudioLibraryPreview(); app.leftPanel = left.dataset.leftPanel; renderStudioLeftPanel(); renderStudioLibrary(); return; }
  const card = e.target.closest("[data-library-id]");
  if (card) {
    if (app.library.busy) return;
    if (app.libraryPreview.itemId === card.dataset.libraryId) { clearStudioLibraryPreview(); renderStudioLibrary(); return; }
    app.library.selectedId = card.dataset.libraryId; app.library.status = "";
    previewStudioLibraryItem(studioLibraryItems().find(item => item.id === app.library.selectedId)); renderStudioLibrary();
    $$("[data-library-id]", $("#panel-content")).find(item => item.dataset.libraryId === app.library.selectedId)?.focus({ preventScroll: true });
    return;
  }
  const branch = e.target.closest("[data-nav-toggle]");
  if (branch) { toggleNavigationBranch(branch.dataset.navToggle); return; }
  const nav = e.target.closest("[data-navigate]");
  if (nav) navigate(nav.dataset.navigate);
  const panel = e.target.closest("[data-panel]");
  if (panel) {
    if (hasUnifiedElementCatalog() && panel.dataset.panel !== "templates") return;
    const stateKeys = ["scope", "search", "category", "kind", "selectedId", "limit"];
    app.library.views ||= {};
    clearStudioLibraryPreview();
    app.library.views[app.panel] = Object.fromEntries(stateKeys.map(key => [key, app.library[key]]));
    app.panel = panel.dataset.panel;
    Object.assign(app.library, app.library.views[app.panel] || { search: "", category: "all", kind: "all", selectedId: null, limit: 80 });
    // Library browsing does not rebuild the independent Properties inspector.
    renderStudioLibrary();
  }
  const add = e.target.closest("[data-add-block]");
  if (add) addBlock(add.dataset.addBlock);
  const select = e.target.closest("[data-select-block]");
  if (select && app.edit) {
    app.selected = select.dataset.selectBlock;
    if (!app.workspaceChrome?.leftPanel) openStudioProperties();
    renderPanel();
    renderFallback();
  }
});
window.addEventListener("beforeunload", (e) => {
  if (app.draft || app.frameDirty || app.libraryMutation || app.archives?.dirty || app.archives?.syncing) {
    e.preventDefault();
    e.returnValue = "";
  }
});
document.addEventListener("keydown", (e) => {
  const workspaceTab = e.target.closest?.("[data-workspace-select]");
  if (workspaceTab && ["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key) && !app.library.busy) {
    const tabs = $$("[data-workspace-select]", $("#studio-workspace-tabs"));
    const index = tabs.indexOf(workspaceTab);
    const next = e.key === "Home" ? 0 : e.key === "End" ? tabs.length - 1 : (index + (e.key === "ArrowRight" ? 1 : -1) + tabs.length) % tabs.length;
    e.preventDefault(); tabs[next]?.focus({ preventScroll: true }); tabs[next]?.click(); return;
  }
  if (e.key === "Escape" && app.libraryPreview.itemId && !$("dialog[open]")) {
    e.preventDefault(); clearStudioLibraryPreview(); renderStudioLibrary(); return;
  }
  if (app.edit && ["Enter", " "].includes(e.key) && e.target.matches?.("[data-select-block]")) {
    e.preventDefault();
    e.target.click();
    return;
  }
  if (e.key === "Escape" && app.navOpen) {
    app.navOpen = false;
    updateNavigationLayout();
    $("#nav-expand").focus();
  }
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
    e.preventDefault();
    flushDraft();
  }
  if (
    app.edit &&
    (e.ctrlKey || e.metaKey) &&
    ["z", "y"].includes(e.key.toLowerCase()) &&
    !["INPUT", "TEXTAREA"].includes(document.activeElement?.tagName)
  ) {
    e.preventDefault();
    historyAction(e.shiftKey || e.key.toLowerCase() === "y" ? "redo" : "undo");
  }
});
async function start() {
  hydrateIcons();
  initializeBrowserHistory();
  publishChromeState();
  $("#landing").innerHTML = '<div class="loading">Opening your project content…</div>';
  try {
    await loadState();
    await loadStudioPipeline();
    app.id = decodeURIComponent(location.hash.slice(1) || app.pipeline?.landingPageId || app.pipeline?.workId || "library");
    app.edit = true;
    document.body.classList.add("editing");
    await render();
    restoreStudioDraft();
    app.chromeReady = true;
    publishChromeState();
    const events = new EventSource("/api/events");
    events.addEventListener("content", async () => {
      if (app.draft || app.frameDirty || app.saving) return;
      const latest = await api("/api/state");
      if (latest.revision > app.state.revision) {
        app.state = latest;
        await render();
      }
    });
    events.addEventListener("runtime", async (event) => {
      const change = JSON.parse(event.data);
      app.templates.clear();
      if (change.packageId !== (work()?.id || "library")) return;
      if (app.draft || app.frameDirty || app.saving) {
        app.runtimePending = true;
        return;
      }
      await loadState();
      await loadStudioPipeline();
      await render();
    });
    events.addEventListener("runtime-status", (event) => {
      const change = JSON.parse(event.data);
      if (change.packageId !== (work()?.id || "library")) return;
      if (change.error)
        setRuntimeNotice(
          `The last working design is still available. ${change.error}`,
          true,
        );
      else if (change.state === "building")
        setRuntimeNotice(
          "Updating the design… Your content remains available.",
        );
      else if (!app.draft) setRuntimeNotice("");
    });
    events.onerror = () => status("Connection interrupted", "error");
    events.onopen = () => status();
  } catch (error) {
    app.chromeReady = false;
    studioMessage("studio:load-error", { message: error.message });
    publishChromeState();
    $("#landing").innerHTML =
      `<div class="empty-state"><h3>The project content could not open</h3><p>${esc(error.message)}</p><button class="primary-button" onclick="location.reload()">Try again</button></div>`;
  }
}
// The native host uses this small, same-origin bridge to save before closing,
// keep recoverable drafts on disk, and route project/design/publication actions.
let studioLastState = "";
function studioMessage(type, detail = {}) {
  const message = { type, ...detail };
  window.chrome?.webview?.postMessage(message);
  window.dispatchEvent(new CustomEvent(type, { detail }));
}
function studioDraftKey() { return `studio.authoring-draft.${app.state?.libraryId || "project"}`; }
function publishStudioState() {
  if (!app.state) return;
  const draft = app.draft || app.recovery?.draft || null;
  const dirty = !!(draft || app.frameDirty || app.saving || app.libraryMutation);
  const invalidFields = app.invalidFields.size ? [...app.invalidFields] : app.recovery?.invalidFields || [];
  const payload = { dirty, draft, invalidFields, libraryId: app.state.libraryId, revision: app.state.revision,
    nodeId: app.id, workId: work()?.id || null, status: $("#save-status")?.textContent || "Saved locally", ready: app.chromeReady };
  const serialized = JSON.stringify(payload);
  if (serialized === studioLastState) return;
  studioLastState = serialized;
  try {
    if (draft) localStorage.setItem(studioDraftKey(), JSON.stringify({ libraryId: app.state.libraryId, draft, invalidFields }));
    else if (app.chromeReady) localStorage.removeItem(studioDraftKey());
  } catch { /* The native host also keeps a project-local recovery. */ }
  studioMessage("studio:state", payload);
}
async function loadStudioPipeline() {
  try {
    const pipeline = await api("/api/studio/pipeline");
    if (pipeline.schemaVersion >= 2 && typeof pipeline.workId === "string" && Array.isArray(pipeline.toolkit))
      app.pipeline = pipeline;
  } catch (error) {
    if (error.status !== 404) throw error;
    // Existing Works retain their original element library and writing tools.
    app.pipeline = null;
  }
}
function restoreStudioDraft() {
  let recovery = window.__studioAuthoringRecovery;
  if (!recovery) {
    try { recovery = JSON.parse(localStorage.getItem(studioDraftKey())); } catch { }
  }
  if (!recovery?.draft || recovery.libraryId !== app.state.libraryId || !app.state.pages[recovery.draft.id]) return;
  // A completed save followed by a process interruption needs no recovery.
  const saved = app.state.pages[recovery.draft.id];
  if (!recovery.invalidFields?.length && JSON.stringify({ ...saved, revision: 0 }) === JSON.stringify({ ...recovery.draft, revision: 0 })) return;
  app.recovery = recovery;
  showStudioRecovery();
  publishStudioState();
}
function showStudioRecovery() {
  const notice = $("#runtime-notice");
  notice.hidden = false;
  notice.classList.add("studio-recovery");
  notice.replaceChildren();
  const explanation = document.createElement("span");
  explanation.textContent = "An unfinished Page draft was recovered from this project. Restore it to continue editing, or keep the saved version.";
  const restore = document.createElement("button");
  restore.className = "quiet-button";
  restore.textContent = "Restore draft";
  restore.onclick = async () => {
    const draft = app.recovery.draft;
    const invalidFields = app.recovery.invalidFields || [];
    if (app.draft) { toast("Save the current changes before restoring the recovered draft.", true); return; }
    // Recovery chooses its already-validated saved Page without creating a
    // hashchange navigation that could autosave the draft before Restore ends.
    app.id = draft.id;
    history.replaceState(history.state, "", `#${encodeURIComponent(draft.id)}`);
    app.draft = structuredClone(draft);
    app.recovery = null;
    app.invalidFields = new Map(invalidFields);
    await render();
    // The running renderer reads the saved gateway state. Until this recovered
    // draft is saved, show its recoverable values through the content inspector.
    app.selected = invalidFields[0]?.[0]?.split(":")[0] || draft.blocks[0]?.id || null;
    openStudioProperties();
    renderPanel();
    if (draft.revision !== app.state.pages[draft.id].revision) showConflict();
    else {
      status("Recovered draft — save to keep it", "dirty");
      sendFrame({ kind: "refresh", pageId: draft.id });
    }
    publishStudioState();
  };
  const keepSaved = document.createElement("button");
  keepSaved.className = "quiet-button";
  keepSaved.textContent = "Keep saved version";
  keepSaved.onclick = () => openDialog("Discard recovered draft?",
    "<p>This keeps the saved Page and removes the recovered draft. Choose Cancel to keep the draft.</p>",
    "Discard draft", async () => { app.recovery = null; setRuntimeNotice(""); publishStudioState(); });
  notice.append(explanation, restore, keepSaved);
}
async function studioFlush(requestId) {
  let error = null;
  try {
    if (app.libraryMutation) await app.libraryMutation;
    if (app.recovery) throw new Error("Restore or discard the recovered Page draft first.");
    // A user-requested save must flush the draft left by an earlier failed
    // autosave, rather than merely joining that autosave's completed attempt.
    if (app.saving) await app.saving;
    await flushDraft();
    await app.mutation;
    if (app.draft || app.frameDirty) throw new Error("The Page still has a draft that could not be saved. Review it before leaving.");
  } catch (failure) { error = failure.message; }
  publishStudioState();
  const saved = !error && !app.draft && !app.frameDirty && !app.recovery;
  app.lastFlushError = error;
  studioMessage("studio:flush-result", { requestId, saved, error });
  return saved;
}
window.__studioAuthoring = {
  configureWorkspaceChrome,
  previewCatalogElement,
  commitCatalogElement,
  receiveLibraryResult: receiveStudioLibraryResult,
  flush: studioFlush,
  refreshFoundation: async requestId => {
    let failure = null;
    try {
      if (app.draft || app.recovery || app.frameDirty || app.invalidFields.size)
        throw new Error("Save or recover the current Page before refreshing its foundation.");
      await app.mutation;
      await loadState();
      await loadStudioPipeline();
      app.templates.clear();
      await render();
      status();
    } catch (error) { failure = error.message; toast(failure, true); }
    publishStudioState();
    studioMessage("studio:refresh-result", { requestId, success: !failure, error: failure });
    return !failure;
  },
  navigate: async (id, requestId) => {
    try {
      if (!(await studioFlush(requestId))) return false;
      if (!node(id)) throw new Error("This item no longer exists in this project.");
      await navigate(id);
      await waitForStudioNavigation(id);
      return true;
    } catch (error) { toast(error.message, true); return false; }
  },
  discardDraft: async () => {
    app.draft = null; app.recovery = null; app.frameDirty = false; app.invalidFields.clear();
    clearTimeout(app.timer); await loadState(); await render(); publishStudioState();
  },
  state: () => ({ libraryId: app.state?.libraryId, revision: app.state?.revision, nodeId: app.id,
    selectedElement: studioElementEditor.state(),
    ready: app.chromeReady, dirty: !!(app.draft || app.frameDirty || app.saving || app.recovery || app.libraryMutation),
    panel: app.panel, leftPanel: app.leftPanel,
    libraryBusy: app.library.busy, libraryMutationPending: !!app.libraryMutation, libraryPendingRequests: studioLibraryRequests.size,
    libraryPreviewItemId: app.libraryPreview.itemId, libraryPreviewState: $("#studio-library-preview").dataset.state || null,
    catalogSelectedItemId: app.catalogElement?.id || null,
    workspaceChromeInstalled: !!app.workspaceChrome, workspaceActiveId: app.workspaceChrome?.activeId || null,
    workspaceTabIds: app.workspaceChrome?.tabs.map(tab => tab.id) || [],
    workspaceLeftPanel: app.workspaceChrome?.leftPanel || null, workspaceRightPanel: app.workspaceChrome?.rightPanel || null,
    frameDirty: app.frameDirty, frameReady: app.frameReady, hasDraft: !!app.draft, saving: !!app.saving,
    framePageId: $("#page-frame").dataset.pageId || null, frameGenerationId: $("#page-frame").dataset.generationId || null,
    frameReadyPageId: app.frameReadyPageId, frameReadyGenerationId: app.frameReadyGenerationId,
    frameLoading: $("#page-frame").inert, currentGenerationReady: currentPageFrameReady(),
    toolkit: app.pipeline?.toolkit || null }),
};
$("#studio-save").onclick = () => studioFlush(crypto.randomUUID());
$("#studio-landing").onclick = () => {
  const owner = studioWorkFor(), landing = studioLandingNode(owner);
  if (landing) navigate(landing.id).catch(error => toast(error.message, true));
  else if (owner) createNodeDialog(owner, "landing").catch(error => toast(error.message, true));
};
$("#studio-create-page").onclick = () => {
  const parent = allowedChildTypes(node()).includes("page") ? node() : work();
  if (parent) createNodeDialog(parent, "page");
};
$("#studio-add-item").onclick = () => {
  const current = node(), parent = current && allowedChildTypes(current).length ? current : node(current?.parentId) || work();
  if (parent) createNodeDialog(parent);
};
$("#studio-save-page-template").onclick = saveStudioPageTemplate;
$("#studio-new-work").onclick = () => studioMessage("studio:new-work");
$("#studio-page-templates").onclick = () => {
  clearStudioLibraryPreview();
  app.panel = "templates"; app.library.scope = "page"; app.library.search = ""; app.library.category = "all"; app.library.project = "all"; app.library.selectedId = null;
  $("#app-layout").classList.remove("inspector-hidden"); $("#studio-inspector-toggle").setAttribute("aria-pressed", "true");
  updateNavigationWidth(); renderStudioLibrary();
  if (app.workspaceChrome?.rightPanel) sendWorkspacePanelAction("panel-select", "right", "templates", $("#studio-page-templates"));
};
$("#studio-library-preview-close").onclick = () => { clearStudioLibraryPreview(); renderStudioLibrary(); };
$("#main .view-actions").addEventListener("click", () => {
  if (app.libraryPreview.itemId) { clearStudioLibraryPreview(); renderStudioLibrary(); }
}, { capture: true });
$("#panel-content").addEventListener("pointerdown", event => {
  if (event.button === 0 && event.target.closest("[data-library-id]")) event.preventDefault();
});
$("#studio-publish").onclick = async () => {
  if (await studioFlush(crypto.randomUUID())) studioMessage("studio:publish", { workId: work()?.id });
};
$("#studio-inspector-toggle").onclick = (event) => {
  const hidden = $("#app-layout").classList.toggle("inspector-hidden");
  event.currentTarget.setAttribute("aria-pressed", String(!hidden));
  if (!hidden && matchMedia("(max-width: 960px)").matches) { app.navOpen = false; updateNavigationLayout(); }
  updateNavigationWidth();
};
if (matchMedia("(max-width: 480px)").matches) $("#studio-inspector-toggle").click();
window.addEventListener("error", event => studioMessage("studio:error", { message: event.message }));
window.addEventListener("unhandledrejection", event => studioMessage("studio:error", { message: event.reason?.message || "The authoring action could not finish." }));
setInterval(publishStudioState, 250);
start();
setInterval(() => {
  if (!app.state) return;
  const packageId = work()?.id || "library";
  api(`/api/runtime/${encodeURIComponent(packageId)}/lease`, {
    method: "POST",
  }).catch(() => {});
}, 30000);
