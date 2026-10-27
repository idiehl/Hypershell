namespace Hypershell.Services;

/// <summary>Per-Page DOM edits augment compiled components without changing their shared source.</summary>
internal static class StudioElementEditingRuntime
{
    internal const string Marker = "/* studio-element-edits-v1 */";
    internal const string Script = """
        /* studio-element-edits-v1 */
        (() => {
          if (window.hyperlibStudioElements) return;
          const paths = new WeakMap(), states = new WeakMap();
          const excluded = element => element.matches('script,style,link,meta,template,[data-studio-canvas-overlay],[data-studio-element-metadata]');
          const children = element => [...element.children].filter(child => !excluded(child));
          const validPath = value => typeof value === 'string' && /^(?:@[^\x00-\x20]+|\d+(?:\/\d+)*)?$/.test(value) && value.length <= 1000;
          const allowedAttribute = name => /^[a-zA-Z_][\w:.-]*$/.test(name) && !/^on/i.test(name)
            && !/^(?:data-(?:block-|studio-|vp-id$|selected$)|contenteditable$|spellcheck$|_bl_|__internal_)/i.test(name);
          const styleName = name => /^(?:--[\w-]+|[a-zA-Z][a-zA-Z-]*)$/.test(name);
          const eligible = block => block?.matches?.('.hl-region > .hl-block[data-block-id]');
          const events = new Set(['click','dblclick','change','input','submit','focus','blur','keydown','keyup','pointerenter','pointerleave']);
          function index(block) {
            if (!eligible(block)) return;
            paths.set(block, '');
            const visit = (parent, prefix) => children(parent).forEach((child, i) => {
              const path = prefix ? prefix + '/' + i : String(i);
              if (!paths.has(child)) paths.set(child, path);
              visit(child, path);
            });
            visit(block, '');
          }
          function resolve(block, path) {
            if (!eligible(block) || !validPath(path)) return null;
            index(block);
            if (!path) return block;
            if (path.startsWith('@')) {
              const matches = [block, ...block.querySelectorAll('[data-vp-id]')].filter(element => element.getAttribute('data-vp-id') === path.slice(1));
              return matches.length === 1 ? matches[0] : null;
            }
            return [...block.querySelectorAll('*')].find(element => paths.get(element) === path && !excluded(element)) || null;
          }
          function path(block, element) {
            if (!eligible(block) || !element || element !== block && !block.contains(element) || excluded(element)) return null;
            index(block);
            const id = element.getAttribute('data-vp-id');
            if (id && validPath('@' + id) && resolve(block, '@' + id) === element) return '@' + id;
            return paths.get(element) ?? null;
          }
          function clean(element) {
            for (const child of [element, ...element.querySelectorAll('*')]) {
              for (const attr of [...child.attributes]) {
                if (/^(?:data-(?:block-|studio-)|contenteditable$|spellcheck$|_bl_|__internal_)/i.test(attr.name)
                  || attr.name === 'data-selected') child.removeAttribute(attr.name);
              }
              child.classList.remove('hl-selected', 'studio-canvas-positioned');
              if (!child.className && typeof child.className === 'string') child.removeAttribute('class');
            }
            return element;
          }
          function parseMarkup(value, namespaceURI) {
            const svg = 'http://www.w3.org/2000/svg', math = 'http://www.w3.org/1998/Math/MathML';
            if (namespaceURI === svg || namespaceURI === math) {
              const container = document.createElementNS(namespaceURI, namespaceURI === svg ? 'svg' : 'math');
              container.innerHTML = value.trim();
              return container;
            }
            const template = document.createElement('template'); template.innerHTML = value.trim();
            return template.content;
          }
          function validateMarkup(value, expectedTagName, namespaceURI) {
            if (typeof value !== 'string' || value.length > 200000) throw new Error('Element code must contain at most 200,000 characters.');
            const fragment = parseMarkup(value, namespaceURI);
            if (fragment.children.length !== 1 || [...fragment.childNodes].some(node => node.nodeType === 3 && node.textContent.trim()))
              throw new Error('Enter one complete element, including its opening and closing tags.');
            const element = fragment.firstElementChild;
            if (expectedTagName && element.tagName.toLowerCase() !== expectedTagName.toLowerCase())
              throw new Error('Keep the selected element\u2019s tag. Edit its contents and attributes inside that element.');
            if (element.matches('html,head,body,script,style,link,meta,base') || element.querySelector('script,style,link,meta,base'))
              throw new Error('Edit this element\u2019s markup here. Page scripts and styles belong in the project source files.');
            for (const child of [element, ...element.querySelectorAll('*')]) {
              for (const attr of [...child.attributes]) {
                if (/^on/i.test(attr.name)) throw new Error('Add event handlers in Actions so they remain editable.');
                if (/^(?:href|src|action|formaction|poster|xlink:href)$/i.test(attr.name) && /^\s*(?:javascript|vbscript):/i.test(attr.value))
                  throw new Error('Use a project asset or a valid web address.');
              }
            }
            return clean(element).outerHTML;
          }
          function snapshot(element) {
            if (!element?.cloneNode) return '';
            return clean(element.cloneNode(true)).outerHTML;
          }
          function restore(block, state) {
            for (const [element, name, value, priority] of state.styles) {
              if (!value) element.style.removeProperty(name); else element.style.setProperty(name, value, priority);
            }
            for (const [element, name, value] of state.attributes) {
              if (value === null) element.removeAttribute(name); else element.setAttribute(name, value);
            }
            for (const [element, name, listener] of state.events) element.removeEventListener(name, listener);
            // Markup is reapplied only when its source changes or the component renders a new node.
            state.attributes = []; state.styles = []; state.events = [];
          }
          function restoreMarkup(block, replacement) {
            if (replacement.root) {
              if (!block.isConnected || replacement.renderedChildren.some(node => node.parentNode !== block)) return;
              block.replaceChildren(...replacement.originalChildren);
              for (const attr of [...block.attributes]) if (allowedAttribute(attr.name)) block.removeAttribute(attr.name);
              for (const [name, value] of replacement.originalAttributes) block.setAttribute(name, value);
            } else if (replacement.rendered.isConnected && block.contains(replacement.rendered)) replacement.rendered.replaceWith(replacement.original);
          }
          function apply(block, records) {
            if (!eligible(block)) return false;
            if (typeof records === 'string') { try { records = JSON.parse(records || '[]'); } catch { return false; } }
            if (!Array.isArray(records) || records.length > 1000) return false;
            index(block);
            const state = states.get(block) || { attributes:[], styles:[], events:[], replacements:new Map() };
            restore(block, state); states.set(block, state);
            // Retain the original nodes (including their component event bindings) so Undo
            // can restore them without reconstituting unrelated component content.
            for (const [key, replacement] of state.replacements) {
              const current = records.find(record => record?.path === key);
              let html = null; try { if (typeof current?.html === 'string') html = validateMarkup(current.html, replacement.original.tagName, replacement.original.namespaceURI); } catch { }
              const mounted = replacement.root ? replacement.renderedChildren.every(node => node.parentNode === block)
                : replacement.rendered.isConnected && block.contains(replacement.rendered);
              if (html === replacement.html && mounted) continue;
              restoreMarkup(block, replacement);
              state.replacements.delete(key);
            }
            const depth = record => {
              let element = resolve(block, record?.path), count = 0;
              while (element && element !== block) { count++; element = element.parentElement; }
              return element === block ? count : 10000;
            };
            // Stable visual IDs do not encode depth. Apply parents before children so
            // editing a container cannot silently hide its embedded image's edits.
            const ordered = records.map(record => ({ record, depth:depth(record) })).sort((a, b) => a.depth - b.depth);
            for (const { record } of ordered) {
              if (!record || !validPath(record.path)) continue;
              let element = resolve(block, record.path); if (!element) continue;
              if (typeof record.html === 'string') {
                let html; try { html = validateMarkup(record.html, element.tagName, element.namespaceURI); } catch { continue; }
                const existing = state.replacements.get(record.path);
                if (!(existing?.root && element === block) && existing?.rendered !== element) {
                  const replacement = parseMarkup(html, element.namespaceURI).firstElementChild;
                  if (element.hasAttribute('data-vp-id')) replacement.setAttribute('data-vp-id', element.getAttribute('data-vp-id'));
                  const original = element;
                  if (element === block) {
                    const ownedClasses = [...block.classList].filter(name => name === 'hl-block' || name === 'hl-selected'
                      || name === 'studio-canvas-positioned' || /^hl-(?:type|span)-/.test(name));
                    const originalChildren = [...block.childNodes], renderedChildren = [...replacement.childNodes];
                    const originalAttributes = [...block.attributes].filter(attr => allowedAttribute(attr.name)).map(attr => [attr.name, attr.value]);
                    for (const attr of [...block.attributes]) if (allowedAttribute(attr.name)) block.removeAttribute(attr.name);
                    for (const attr of [...replacement.attributes]) if (allowedAttribute(attr.name)) block.setAttribute(attr.name, attr.value);
                    // Keep the renderer's saved wrapper and region ownership in place.
                    block.classList.add('hl-block', ...ownedClasses); block.replaceChildren(...renderedChildren);
                    state.replacements.set(record.path, { root:true, original, originalChildren, originalAttributes, renderedChildren, html });
                  } else {
                    paths.set(replacement, paths.get(original)); element.replaceWith(replacement); element = replacement;
                    state.replacements.set(record.path, { original, rendered:element, html });
                  }
                  index(block);
                }
              }
              for (const [name, value] of Object.entries(record.attrs || {})) {
                if (!allowedAttribute(name) || value !== null && typeof value !== 'string') continue;
                if (/^(?:href|src|action|formaction|poster|xlink:href)$/i.test(name) && /^\s*(?:javascript|vbscript):/i.test(value || '')) continue;
                state.attributes.push([element, name, element.getAttribute(name)]);
                if (value === null) element.removeAttribute(name); else element.setAttribute(name, value);
              }
              for (const [name, value] of Object.entries(record.styles || {})) {
                if (!styleName(name) || value !== null && typeof value !== 'string') continue;
                state.styles.push([element, name, element.style.getPropertyValue(name), element.style.getPropertyPriority(name)]);
                if (value === null || !value) element.style.removeProperty(name); else element.style.setProperty(name, value);
              }
              for (const [name, code] of Object.entries(record.events || {})) {
                if (!events.has(name) || typeof code !== 'string' || code.length > 20000) continue;
                let handler; try { handler = new Function('event', 'element', code); } catch { continue; }
                const listener = event => {
                  if (document.body.classList.contains('hl-editing')) return;
                  try { handler.call(element, event, element); } catch (error) { console.error('Element event handler failed', error); }
                };
                element.addEventListener(name, listener); state.events.push([element, name, listener]);
              }
            }
            for (const field of block.querySelectorAll('[data-field]')) {
              field.contentEditable = document.body.classList.contains('hl-editing') ? 'true' : 'false';
              field.spellcheck = document.body.classList.contains('hl-editing');
            }
            state.signature = block.getAttribute('data-studio-elements') || '[]';
            state.first = block.firstElementChild;
            state.recordPaths = records.filter(record => record && validPath(record.path)).map(record => record.path);
            state.targets = new Map(state.recordPaths.map(key => [key, resolve(block, key)]));
            return true;
          }
          function decorate() {
            for (const block of document.querySelectorAll('.hl-region > .hl-block[data-block-id]')) {
              index(block);
              const value = block.getAttribute('data-studio-elements') || '[]';
              const state = states.get(block);
              // Blazor can retain the outer block while replacing a component subtree.
              const changed = state?.signature !== value || state?.first !== block.firstElementChild
                || state?.recordPaths?.some(key => resolve(block, key) !== state.targets.get(key));
              if (changed || !state) {
                if (apply(block, value)) { const next = states.get(block); next.signature = value; next.first = block.firstElementChild; }
              }
            }
          }
          window.hyperlibStudioElements = { path, resolve, snapshot, apply, decorate, validateMarkup };
          let pending = 0;
          const schedule = () => { if (!pending) pending = requestAnimationFrame(() => { pending = 0; decorate(); }); };
          const start = () => {
            new MutationObserver(schedule).observe(document.body, { childList:true, subtree:true, attributes:true, attributeFilter:['data-studio-elements'] });
            schedule();
          };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once:true }); else start();
        })();
        """;
}
