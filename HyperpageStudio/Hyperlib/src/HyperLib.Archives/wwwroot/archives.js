'use strict';
(() => {
  const $ = id => document.getElementById(id), libraries = [], frame = $('page');
  let viewSequence = 0;
  const text = (tag, value, cls) => { const e = document.createElement(tag); e.textContent = value; if (cls) e.className = cls; return e; };
  const path = (library, id) => '/Archives/' + encodeURIComponent(library.publisherId) + '/' + encodeURIComponent(id);
  const children = (library, id) => library.nodes.filter(n => n.parentId === id).sort((a,b) => a.order - b.order || a.title.localeCompare(b.title));
  const label = (library, node) => library.labels[node.type] || node.type;
  const link = (library, node) => { const a = document.createElement('a'); a.href = path(library,node.id); a.textContent = node.title; return a; };
  async function json(url) { const r = await fetch(url,{credentials:'omit'}); if (!r.ok) throw new Error('This part of the Archives is temporarily unavailable. Please try again.'); return r.json(); }
  function renderTree() {
    const query = $('search').value.trim().toLocaleLowerCase(); $('tree').replaceChildren();
    for (const library of libraries) {
      const matches = new Set(library.nodes.filter(n => n.title.toLocaleLowerCase().includes(query)).map(n => n.id));
      if (query) for (const id of [...matches]) { let n = library.nodes.find(n => n.id === id); const seen = new Set(); while(n && !seen.has(n.id)) { seen.add(n.id); matches.add(n.id); n = library.nodes.find(p => p.id === n.parentId); } }
      function branch(parent, id) {
        for (const n of children(library,id)) {
          if (query && !matches.has(n.id)) continue;
          const a = link(library,n); a.prepend(text('small',label(library,n)));
          if (location.pathname === a.getAttribute('href')) a.setAttribute('aria-current','page');
          if (children(library,n.id).length) {
            const details = document.createElement('details'); details.open = query.length > 0 || n.type === 'collection';
            const summary = text('summary',n.title), div = document.createElement('div'); div.className = 'children'; div.append(a); branch(div,n.id); details.append(summary,div); parent.append(details);
          } else parent.append(a);
        }
      }
      branch($('tree'),library.libraryId);
    }
    if (!$('tree').children.length) $('tree').append(text('p',query ? 'No matching titles.' : 'Published collections will appear here.','tree-empty'));
  }
  function card(library,n) {
    const a = link(library,n); a.className = 'card'; a.replaceChildren();
    if (n.cover && (/^https:\/\//i.test(n.cover) || n.cover.startsWith('/Archives/'))) { const img = document.createElement('img'); img.alt = ''; img.loading = 'lazy'; img.addEventListener('error',() => img.remove(),{once:true}); img.src = n.cover; a.append(img); }
    const copy = document.createElement('div'); copy.className = 'copy'; copy.append(text('span',label(library,n).toUpperCase(),'eyebrow'),text('h2',n.title));
    const count = children(library,n.id).length; if (count) copy.append(text('p',count + (count === 1 ? ' item' : ' items'))); a.append(copy); return a;
  }
  async function show() {
    const sequence = ++viewSequence; frame.hidden = true; frame.removeAttribute('src'); frame.style.height = ''; $('cards').replaceChildren(); $('breadcrumbs').replaceChildren(); $('status').textContent = ''; $('heading').hidden = false;
    const parts = location.pathname.split('/').filter(Boolean).map(decodeURIComponent), library = libraries.find(l => l.publisherId === parts[1]), node = library?.nodes.find(n => n.id === parts[2]);
    const home = text('a','Archives'); home.href = '/Archives'; $('breadcrumbs').append(home);
    renderTree();
    if (!library || !node) {
      $('heading').replaceChildren(text('p','PUBLIC LIBRARY','eyebrow'),text('h1','Curiosity, collected.'),text('p','Collections, Works and Pages from Hyperlib.','intro'));
      for (const lib of libraries) children(lib,lib.libraryId).forEach(n => $('cards').append(card(lib,n)));
      if (!$('cards').children.length) $('status').textContent = 'The Archives are ready. Published collections will appear here.';
      document.title = 'Archives · Atlas Universalis'; return;
    }
    const trail = []; let ancestor = node; const seen = new Set();
    while(ancestor && !seen.has(ancestor.id)) { seen.add(ancestor.id); trail.unshift(ancestor); ancestor = library.nodes.find(n => n.id === ancestor.parentId); }
    for (const n of trail) { $('breadcrumbs').append(text('span','/'),link(library,n)); }
    $('heading').replaceChildren(text('p',label(library,node).toUpperCase(),'eyebrow'),text('h1',node.title));
    document.title = node.title + ' · Archives';
    children(library,node.id).forEach(n => $('cards').append(card(library,n)));
    $('status').textContent = 'Opening ' + node.title + '…';
    try {
      const view = await json('/Archives/api/view/' + encodeURIComponent(library.publisherId) + '/' + encodeURIComponent(node.id));
      if (sequence !== viewSequence) return;
      if (!view.url?.startsWith('/Archives/_editions/')) throw new Error('The published page address is invalid.');
      frame.title = node.title; frame.src = view.url; frame.hidden = false; $('status').textContent = '';
      if (node.type === 'page') $('heading').hidden = true;
    } catch (e) { if (sequence === viewSequence) $('status').textContent = e.message; }
  }
  document.addEventListener('click', event => {
    const a = event.target.closest('a');
    if (!a || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || a.origin !== location.origin
      || a.hash || !(a.pathname === '/Archives' || a.pathname.startsWith('/Archives/'))) return;
    event.preventDefault(); history.pushState(null,'',a.href); show(); window.scrollTo({top:0});
  });
  window.addEventListener('popstate',show);
  function resizePage(reportedHeight = 0) {
    if (frame.hidden) return;
    const body = frame.contentDocument?.body;
    const contentHeight = Math.max(reportedHeight,body?.scrollHeight || 0,body?.getBoundingClientRect().height || 0);
    if (!contentHeight) return;
    // Only add the actual border: arbitrary padding feeds back into the
    // renderer's min-height:100vh and makes the iframe grow indefinitely.
    const style = getComputedStyle(frame);
    const border = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
    const height = Math.min(100000,Math.max(300,Math.ceil(contentHeight) + border));
    if (Math.abs(frame.getBoundingClientRect().height - height) > .5) frame.style.height = height + 'px';
  }
  let pageWidth = 0;
  new ResizeObserver(([entry]) => {
    if (entry.contentRect.width > 0 && Math.abs(entry.contentRect.width - pageWidth) > 1) {
      pageWidth = entry.contentRect.width;
      frame.style.height = '';
      // Resetting the viewport need not resize the body's intrinsic content,
      // so the renderer may not send another height notification afterward.
      requestAnimationFrame(() => resizePage());
    }
  }).observe(frame);
  frame.addEventListener('load',() => resizePage());
  window.addEventListener('message',event => {
    if (event.origin !== location.origin || event.source !== frame.contentWindow || event.data?.source !== 'hyperlib-renderer') return;
    if (event.data.kind === 'height' && Number.isFinite(event.data.height)) resizePage(event.data.height);
  });
  $('search').addEventListener('input',renderTree);
  $('toggle-tree').addEventListener('click',() => { document.body.classList.toggle('tree-hidden'); $('toggle-tree').setAttribute('aria-expanded',String(!document.body.classList.contains('tree-hidden'))); });
  if (window.matchMedia('(max-width:760px)').matches) {
    document.body.classList.add('tree-hidden');
    $('toggle-tree').setAttribute('aria-expanded','false');
  }
  const updateHeader = () => $('site-header').classList.toggle('site-header--scrolled',window.scrollY > 60);
  updateHeader();
  window.addEventListener('scroll',updateHeader,{passive:true});
  json('/Archives/api/catalog').then(data => { libraries.push(...data.libraries); return show(); }).catch(e => $('status').textContent = e.message);
})();
