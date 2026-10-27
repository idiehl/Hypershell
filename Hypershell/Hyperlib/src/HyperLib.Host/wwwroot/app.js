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
  edit: false,
  panel: "elements",
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
  frameDirty: false,
  frameFlushes: new Map(),
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
const label = (type) => app.state.labels[type] || type;
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
  await flushDraft();
  if (app.draft) return;
  app.navOpen = false;
  updateNavigationLayout();
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
  const root = rootNode();
  const path = prepareNavigationBranches();
  document.documentElement.style.setProperty(
    "--accent",
    app.state.appearance?.accent || "#a7d7c5",
  );
  $("#nav-tree").innerHTML = renderTree(root.id, 0, path);
  $$(".nav-home").forEach((b) =>
    b.classList.toggle("active", node()?.type === "library"),
  );
  $("#library-count").textContent =
    `${Object.keys(app.state.pages).length} pages · ${app.state.nodes.filter((n) => n.type === "work").length} works`;
}
function renderTree(id, depth, path, seen = new Set()) {
  if (seen.has(id)) return "";
  const branch = new Set(seen).add(id);
  return children(id)
    .filter((n) => !branch.has(n.id))
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
  document.title = `${current.type === "library" ? "Your library" : current.title} — HyperLib`;
  document.body.classList.toggle("immersive-work", immersiveWork());
  renderNavigation();
  updateNavigationLayout();
  $("#breadcrumbs").innerHTML = ancestors(current)
    .map(
      (n, i, a) =>
        `${i ? '<span class="sep">/</span>' : ""}${i === a.length - 1 ? `<span class="current">${esc(n.type === "library" ? "Library" : n.title)}</span>` : `<a href="#${encodeURIComponent(n.id)}">${esc(n.type === "library" ? "Library" : n.title)}</a>`}`,
    )
    .join("");
  $("#studio-button").hidden = !app.edit || !app.native?.canOpenStudio;
  $("#archives-button").hidden = !app.edit || ["page", "library"].includes(current.type);
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
  return `<a class="collection-card" href="#${encodeURIComponent(n.id)}" data-card-title="${esc(n.title.toLowerCase())}"><div class="card-art">${art && localMedia(art) ? `<img src="${esc(art)}" alt="${esc(n.title)} cover" loading="lazy">` : `<div class="card-empty-art">${icon(n.type === "work" ? "book" : "folder")}</div>`}<span class="card-number">${esc(label(n.type).toUpperCase())} / ${String(index + 1).padStart(2, "0")}</span></div><div class="card-body"><div class="card-kicker">${esc(label(n.type))}</div><h3 class="card-title">${esc(n.title)}</h3><p class="card-description">${esc(n.appearance?.description || (n.id === "natural-world" ? "Notes from the wild. A closer look at the world we share." : n.id === "science-lab" ? "Explore ideas through experiments, models, and a little curiosity." : `${count} ${count === 1 ? "page" : "pages"} to discover and make your own.`))}</p><div class="card-meta"><span>${works ? `${works} ${works === 1 ? label("work").toLowerCase() : plural("work").toLowerCase()} <span aria-hidden="true">·</span> ` : ""}${count} ${count === 1 ? "page" : "pages"}</span>${icon("arrow")}</div></div></a>`;
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
  return `<nav class="work-fallback-navigation" aria-label="Work navigation"><a class="work-fallback-home" href="#${encodeURIComponent(owner.id)}">${esc(owner.title)}</a><details><summary>Contents</summary><div class="work-fallback-contents">${links || '<p>No pages yet.</p>'}</div></details><a class="work-fallback-return" href="#${encodeURIComponent(collection?.id || "library")}">Back to library</a></nav>`;
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
function replaceFrameDocument(frame, url) {
  // Subframe src assignments add entries to the browser's joint history. Keep
  // browser and native Back/Forward focused on the host's library hash routes.
  const target = new URL(url, location.origin).href;
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
function fallbackBlock(b) {
  const v = b.values || {};
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
  if (frame && !frame.hidden)
    frame.contentWindow.postMessage(
      { source: "hyperlib-editor", blockId: app.selected, ...message },
      location.origin,
    );
}
async function flushFrame() {
  if (!app.edit || !app.frameReady || $("#page-frame").hidden) return;
  const requestId = crypto.randomUUID();
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      app.frameFlushes.delete(requestId);
      reject(new Error("The page is still saving its inline edit. Please try again when it is ready."));
    }, 2000);
    app.frameFlushes.set(requestId, () => {
      clearTimeout(timer);
      app.frameDirty = false;
      resolve();
    });
    sendFrame({ kind: "flush", requestId });
  });
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
    if (app.restoreScroll) {
      const position = app.restoreScroll;
      requestAnimationFrame(() => window.scrollTo(position.x, position.y));
      app.restoreScroll = null;
    }
    sendFrame({ kind: "select", blockId: app.selected });
  }
  if (frame.id === "page-frame" && m.pageId === app.id) {
    if (m.kind === "dirty") app.frameDirty = true;
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
    if (!page()?.blocks.some((b) => b.id === m.blockId)) return;
    app.selected = m.blockId;
    app.panel = "inspector";
    renderPanel();
  }
  if (
    m.kind === "change" &&
    app.edit &&
    frame.id === "page-frame" &&
    m.pageId === app.id
  ) {
    editBlock(m.blockId, m.field, m.value);
  }
});
function beginDraft() {
  if (!app.draft) app.draft = structuredClone(app.state.pages[app.id]);
  return app.draft;
}
function editBlock(id, field, value) {
  if (!app.state.pages[app.id]) return;
  const draft = beginDraft();
  const block = draft.blocks.find((b) => b.id === id);
  if (!block) return;
  if (value === undefined) delete block.values[field];
  else block.values[field] = value;
  status("Saving…", "dirty");
  clearTimeout(app.timer);
  app.timer = setTimeout(() => flushDraft().catch(() => {}), 600);
}
async function flushDraft() {
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
        await loadState();
        await render();
      },
    );
  notice.append(download, reload);
}
async function historyAction(kind) {
  if (!app.edit) return;
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
  $$("[data-panel]").forEach((b) =>
    b.classList.toggle("active", b.dataset.panel === app.panel),
  );
  if (app.panel === "assistant") {
    renderAssistant();
    return;
  }
  if (app.panel === "inspector") {
    renderInspector();
    return;
  }
  const p = page();
  if (!p) {
    $("#panel-content").innerHTML =
      `<div class="panel-empty">${icon("book")}Select a page to add elements, or create a new ${esc(label(allowedChildTypes(node())[0] || "page").toLowerCase())} here.</div><button class="primary-button" id="panel-add-node">${icon("plus")}Add item</button><div class="panel-label">YOUR STRUCTURE</div><p class="panel-intro">Use only the levels you need. Every page belongs to a work.</p><button class="quiet-button" id="labels-button">Customize names</button>`;
    $("#panel-add-node").onclick = () => createNodeDialog(node());
    $("#labels-button").onclick = labelsDialog;
    return;
  }
  const regions = app.template?.regions || [];
  const regionId =
    app.region && regions.some((r) => r.id === app.region)
      ? app.region
      : regions[0]?.id;
  app.region = regionId;
  const region = regions.find((r) => r.id === regionId);
  const blocks = (app.template?.blocks || []).filter(
    (b) =>
      !region ||
      !region.allowedBlocks.length ||
      region.allowedBlocks.includes(b.id),
  );
  $("#panel-content").innerHTML =
    `<p class="panel-intro">Elements from <strong>${esc(work()?.title)}</strong>. Select one to add it to your page.</p>${regions.length > 1 ? `<label class="field"><span>Page region</span><select id="region-select">${regions.map((r) => `<option value="${esc(r.id)}" ${r.id === regionId ? "selected" : ""}>${esc(r.title)}</option>`).join("")}</select></label>` : ""}<input id="element-search" type="search" placeholder="Find an element" aria-label="Find an element"><div class="block-list">${blocks.map((b) => `<button class="block-choice" data-add-block="${esc(b.id)}" data-search="${esc(b.title.toLowerCase())}">${icon(blockIcon(b.id))}<span>${esc(b.title)}</span></button>`).join("")}</div><div class="panel-label">DESIGNED IN STUDIO</div><p class="panel-intro">Your work’s templates keep every page consistent. Add or alter element definitions in Hyperpage Studio.</p>`;
  $("#element-search").oninput = (e) =>
    $$("[data-search]").forEach(
      (b) =>
        (b.hidden = !b.dataset.search.includes(e.target.value.toLowerCase())),
    );
  $("#region-select")?.addEventListener("change", (e) => {
    app.region = e.target.value;
    renderPanel();
  });
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
    p.blocks.push(block);
    await mutate(
      [{ kind: "savePage", id: p.id, page: p }],
      `Add ${definition.title}`,
      false,
    );
    app.selected = block.id;
    app.panel = "inspector";
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
function renderInspector() {
  const p = page();
  const block = p?.blocks.find((b) => b.id === app.selected);
  if (!block) {
    $("#panel-content").innerHTML =
      `<div class="panel-empty">${icon("pen")}Select an element on the page to edit its properties.</div>`;
    return;
  }
  const definition = app.template?.blocks.find((b) => b.id === block.type);
  const styleFields = new Set(["span", "font", "align", "color"]);
  const fields = definition?.fields || [];
  $("#panel-content").innerHTML =
    `<div class="panel-toolbar"><span class="block-name">${esc(definition?.title || block.type)}</span><button class="icon-button" id="block-up" title="Move up" aria-label="Move up">${icon("up")}</button><button class="icon-button" id="block-down" title="Move down" aria-label="Move down">${icon("down")}</button><button class="icon-button danger-button" id="block-delete" title="Remove element" aria-label="Remove element">${icon("trash")}</button></div>${fields.filter((f) => !styleFields.has(f.id)).map((f) => fieldControl(f, block.values[f.id])).join("")}${fields.some((f) => styleFields.has(f.id)) ? `<div class="panel-label">STYLE & LAYOUT</div><div class="property-grid">${fields.filter((f) => styleFields.has(f.id)).map((f) => fieldControl(f, block.values[f.id])).join("")}</div>` : ""}<div class="panel-label">CONTENT REGION</div><p class="panel-intro">${esc(app.template?.regions.find((r) => r.id === block.region)?.title || block.region)}</p>`;
  $$("[data-field]", $("#panel-content")).forEach((input) =>
    input.addEventListener("input", () => {
      try {
        const f = definition.fields.find((x) => x.id === input.dataset.field);
        delete input.dataset.empty;
        editBlock(block.id, f.id, fieldValue(f, input));
      } catch {
        status("Enter valid field data", "error");
      }
    }),
  );
  $$("[data-clear-field]", $("#panel-content")).forEach((button) => {
    button.onclick = () => {
      editBlock(block.id, button.dataset.clearField, undefined);
      renderInspector();
    };
  });
  $("#panel-content").onfocusin = () => sendFrame({ kind: "select", blockId: app.selected });
  $("#block-up").onclick = () => reorderBlock(-1);
  $("#block-down").onclick = () => reorderBlock(1);
  $("#block-delete").onclick = () => removeBlock();
  $$("[data-upload-field]", $("#panel-content")).forEach(
    (b) => (b.onclick = () => uploadMedia(b.dataset.uploadField)),
  );
}
async function reorderBlock(delta) {
  try {
    await flushDraft();
    if (app.draft) return;
    const p = structuredClone(page());
    const i = p.blocks.findIndex((b) => b.id === app.selected);
    const j = i + delta;
    if (j < 0 || j >= p.blocks.length) return;
    [p.blocks[i], p.blocks[j]] = [p.blocks[j], p.blocks[i]];
    await mutate(
      [{ kind: "savePage", id: p.id, page: p }],
      "Reorder element",
      false,
    );
    sendFrame({ kind: "refresh", pageId: p.id });
    if (!$("#page-fallback").hidden) renderFallback();
  } catch (e) {
    toast(e.message, true);
  }
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
async function createNodeDialog(parent) {
  const allowed = allowedChildTypes(parent);
  if (!allowed.length) return;
  try { await flushDraft(); } catch { return; }
  if (app.draft) return;
  let catalog = null;
  openDialog(
    "Add to " + parent.title,
    `<div class="create-fields"><label class="field"><span>Type</span><select name="type">${allowed.map((t) => `<option value="${t}">${esc(label(t))}</option>`).join("")}</select></label><label class="field"><span>Name</span><input name="title" required maxlength="160" placeholder="Give it a name"></label></div><section id="base-template-library" aria-labelledby="base-template-heading" hidden><div class="template-library-heading"><div><h3 id="base-template-heading">Base Template Library</h3><p>Choose a starting design. Every template works for any subject.</p></div><span id="template-count" class="section-count"></span></div><div id="template-library-content"><div class="loading">Loading designs…</div></div></section><p id="page-template-note" class="panel-intro">Pages inherit their work’s design. You can customize it in Studio.</p>`,
    "Create",
    async (form) => {
      await flushDraft();
      if (app.draft) throw new Error("Save or recover the current page draft before creating an item.");
      const starterId = form.get("type") === "work" ? form.get("starterId") : null;
      if (form.get("type") === "work" && !catalog?.some((item) => item.id === starterId))
        throw new Error("Choose a base template for your new work.");
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
    $("#page-template-note").hidden = isWork;
    $("#dialog").classList.toggle("template-dialog", isWork);
    $$("[name=starterId]", $("#dialog")).forEach((radio) => { radio.disabled = !isWork; });
  };
  type.addEventListener("change", showTemplates);
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
  openDialog(
    "Organize " + n.title,
    `<label class="field"><span>Name</span><input name="title" value="${esc(n.title)}" required></label><label class="field"><span>Parent</span><select name="parentId">${[
      rootNode(),
      ...app.state.nodes,
    ]
      .filter(
        (p) =>
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
      )}</select></label><label class="field"><span>Order</span><input name="order" type="number" value="${n.order}" step="1"></label><button type="button" id="delete-node" class="quiet-button danger-button">${icon("trash")}Move to recovery</button>`,
    "Save",
    async (form) => {
      await flushDraft();
      if (app.draft) throw new Error("Save or recover the current draft first.");
      const ops = [{ kind: "updateNode", id: n.id, title: form.get("title") }];
      if (
        form.get("parentId") !== n.parentId ||
        Number(form.get("order")) !== n.order
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
  const fields = app.template?.appearance || [];
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
  const visible = narrow ? !!app.navOpen : !app.navCollapsed;
  $("#navigation").hidden = !visible;
  $("#app-layout").classList.toggle("narrow-nav", narrow);
  $("#app-layout").classList.toggle("nav-closed", narrow || !visible);
  $("#nav-expand").hidden = visible;
  $("#nav-resizer").hidden = narrow || !visible;
  if (narrow || !visible) finishNavigationResize();
  updateNavigationWidth();
}
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
  const branch = e.target.closest("[data-nav-toggle]");
  if (branch) { toggleNavigationBranch(branch.dataset.navToggle); return; }
  const nav = e.target.closest("[data-navigate]");
  if (nav) navigate(nav.dataset.navigate);
  const panel = e.target.closest("[data-panel]");
  if (panel) {
    app.panel = panel.dataset.panel;
    renderPanel();
  }
  const add = e.target.closest("[data-add-block]");
  if (add) addBlock(add.dataset.addBlock);
  const select = e.target.closest("[data-select-block]");
  if (select && app.edit) {
    app.selected = select.dataset.selectBlock;
    app.panel = "inspector";
    renderPanel();
    renderFallback();
  }
});
window.addEventListener("beforeunload", (e) => {
  if (app.draft || app.frameDirty || app.archives?.dirty || app.archives?.syncing) {
    e.preventDefault();
    e.returnValue = "";
  }
});
document.addEventListener("keydown", (e) => {
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
  $("#landing").innerHTML = '<div class="loading">Opening your library…</div>';
  try {
    await loadState();
    app.id = decodeURIComponent(location.hash.slice(1) || "library");
    if (window.chrome?.webview && !hasNativeChrome()) {
      try {
        app.native = await native("capabilities", {
          libraryId: app.state.libraryId,
        });
      } catch {
        /* Ordinary browsers retain all local authoring. */
      }
    }
    await render();
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
    publishChromeState();
    $("#landing").innerHTML =
      `<div class="empty-state"><h3>The library could not open</h3><p>${esc(error.message)}</p><button class="primary-button" onclick="location.reload()">Try again</button></div>`;
  }
}
start();
setInterval(() => {
  if (!app.state) return;
  const packageId = work()?.id || "library";
  api(`/api/runtime/${encodeURIComponent(packageId)}/lease`, {
    method: "POST",
  }).catch(() => {});
}, 30000);
