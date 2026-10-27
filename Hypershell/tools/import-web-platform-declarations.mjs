import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const typescriptVersion = "5.9.3";
const sourceRoot = `https://unpkg.com/typescript@${typescriptVersion}/lib`;
const sourceFiles = ["lib.dom.d.ts", "lib.webworker.d.ts"];
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "web-platform-declarations.json");

const sources = [];
const sourceHashes = {};
for (const fileName of sourceFiles) {
  const sourceUrl = `${sourceRoot}/${fileName}`;
  const response = await fetch(sourceUrl);
  if (!response.ok) {
    throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
  }
  const bytes = Buffer.from(await response.arrayBuffer());
  sources.push(bytes.toString("utf8"));
  sourceHashes[fileName] = createHash("sha256").update(bytes).digest("hex");
}

const source = sources
  .join("\n")
  .replace(/\/\*[\s\S]*?\*\//g, "")
  .replace(/^\s*\/\/.*$/gm, "");

const surfaces = {
  "dom-standard": {
    sourceUrl: "https://dom.spec.whatwg.org/",
    types: [
      "AbortController", "AbortSignal", "Attr", "CDATASection",
      "CharacterData", "Comment", "CustomEvent", "Document",
      "DocumentFragment", "DocumentType", "DOMException",
      "DOMImplementation", "DOMTokenList", "Element", "Event",
      "EventTarget", "MutationObserver", "MutationRecord", "NamedNodeMap",
      "Node", "NodeIterator", "NodeList", "ProcessingInstruction", "Range",
      "ShadowRoot", "StaticRange", "Text", "TreeWalker", "XMLDocument"
    ],
    functions: []
  },
  "fetch-standard": {
    sourceUrl: "https://fetch.spec.whatwg.org/",
    types: ["Body", "Headers", "Request", "Response"],
    functions: ["fetch"]
  },
  "url-standard": {
    sourceUrl: "https://url.spec.whatwg.org/",
    types: ["URL", "URLSearchParams"],
    functions: []
  },
  "streams-standard": {
    sourceUrl: "https://streams.spec.whatwg.org/",
    types: [
      "ByteLengthQueuingStrategy", "CountQueuingStrategy", "QueuingStrategy",
      "ReadableByteStreamController", "ReadableStream",
      "ReadableStreamBYOBReader", "ReadableStreamBYOBRequest",
      "ReadableStreamDefaultController", "ReadableStreamDefaultReader",
      "TransformStream", "TransformStreamDefaultController",
      "WritableStream", "WritableStreamDefaultController",
      "WritableStreamDefaultWriter"
    ],
    functions: []
  },
  "web-storage-standard": {
    sourceUrl: "https://html.spec.whatwg.org/multipage/webstorage.html",
    types: ["Storage", "StorageEvent"],
    functions: []
  },
  "xhr-standard": {
    sourceUrl: "https://xhr.spec.whatwg.org/",
    types: [
      "FormData", "ProgressEvent", "XMLHttpRequest",
      "XMLHttpRequestEventTarget", "XMLHttpRequestUpload"
    ],
    functions: []
  },
  "workers-standard": {
    sourceUrl: "https://html.spec.whatwg.org/multipage/workers.html",
    types: [
      "BroadcastChannel", "DedicatedWorkerGlobalScope", "MessageChannel",
      "MessageEvent", "MessagePort", "SharedWorker",
      "SharedWorkerGlobalScope", "Worker", "WorkerGlobalScope",
      "WorkerLocation", "WorkerNavigator"
    ],
    functions: ["importScripts"]
  },
  "canvas-standard": {
    sourceUrl: "https://html.spec.whatwg.org/multipage/canvas.html",
    types: [
      "CanvasGradient", "CanvasPattern", "CanvasRenderingContext2D",
      "HTMLCanvasElement", "ImageBitmap", "ImageBitmapRenderingContext",
      "ImageData", "OffscreenCanvas", "OffscreenCanvasRenderingContext2D",
      "Path2D", "TextMetrics"
    ],
    functions: ["createImageBitmap"]
  },
  "web-crypto-standard": {
    sourceUrl: "https://www.w3.org/TR/WebCryptoAPI/",
    types: ["Crypto", "CryptoKey", "SubtleCrypto"],
    functions: []
  },
  "html-timers": {
    sourceUrl: "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html",
    types: [],
    functions: ["clearInterval", "clearTimeout", "setInterval", "setTimeout"]
  },
  "html-navigation": {
    sourceUrl: "https://html.spec.whatwg.org/multipage/nav-history-apis.html",
    types: ["History", "Location", "Navigation", "PopStateEvent"],
    functions: []
  }
};

const findClosingBrace = (text, openingBrace) => {
  let depth = 0;
  for (let index = openingBrace; index < text.length; index++) {
    if (text[index] === "{") depth++;
    if (text[index] === "}" && --depth === 0) return index;
  }
  throw new Error("A Web declaration interface was not balanced.");
};

const interfaceBodies = new Map();
const interfacePattern = /\binterface\s+([A-Za-z_$][\w$]*)(?:\s*<[^>{}]*>)?(?:\s+extends\s+[^{}]+)?\s*\{/g;
for (const match of source.matchAll(interfacePattern)) {
  const openingBrace = match.index + match[0].lastIndexOf("{");
  const closingBrace = findClosingBrace(source, openingBrace);
  const bodies = interfaceBodies.get(match[1]) ?? [];
  bodies.push(source.slice(openingBrace + 1, closingBrace));
  interfaceBodies.set(match[1], bodies);
}

const globalTypes = new Map();
for (const match of source.matchAll(
  /\bdeclare\s+(?:var|const)\s+([A-Za-z_$][\w$]*)\s*:\s*([^;]+);/g)) {
  globalTypes.set(match[1], match[2].trim());
}

const splitDeclarations = (body) => {
  const result = [];
  let start = 0;
  let round = 0;
  let square = 0;
  let curly = 0;
  for (let index = 0; index < body.length; index++) {
    const character = body[index];
    if (character === "(") round++;
    else if (character === ")") round--;
    else if (character === "[") square++;
    else if (character === "]") square--;
    else if (character === "{") curly++;
    else if (character === "}") curly--;
    else if (character === ";" && round === 0 && square === 0 && curly === 0) {
      const declaration = body.slice(start, index).replace(/\s+/g, " ").trim();
      if (declaration) result.push(declaration);
      start = index + 1;
    }
  }
  return result;
};

const stableEntryId = (surfaceId, key, name) => {
  const slug = String(name)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 56) || "symbol";
  const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
  return `web.${surfaceId}.${slug}.${suffix}`;
};

const entries = [];
const entryKeys = new Set();
const addEntry = ({ surfaceId, typeName, name, symbolKind, signature }) => {
  const key = [surfaceId, typeName ?? "", symbolKind, name, signature]
    .join("\u001f");
  if (entryKeys.has(key)) return;
  entryKeys.add(key);
  entries.push({
    id: stableEntryId(surfaceId, key, `${typeName ?? "global"}-${name}`),
    surfaceId,
    ...(typeName ? { typeName } : {}),
    name,
    displayName: name,
    symbolKind,
    signature,
    documentationUrl: surfaces[surfaceId].sourceUrl
  });
};

const parseInterface = (surfaceId, typeName, interfaceName, isStatic) => {
  for (const body of interfaceBodies.get(interfaceName) ?? []) {
    for (const declaration of splitDeclarations(body)) {
      const normalized = declaration
        .replace(/^readonly\s+/, "")
        .replace(/^abstract\s+/, "")
        .trim();
      const constructorMatch = normalized.match(/^new\s*(?:<[^>]+>)?\s*\((.*)\)\s*:\s*(.+)$/);
      if (constructorMatch) {
        addEntry({
          surfaceId,
          typeName,
          name: typeName,
          symbolKind: "Constructor",
          signature: `new ${typeName}(${constructorMatch[1]})`
        });
        continue;
      }

      const methodMatch = normalized.match(
        /^([A-Za-z_$][\w$]*|\[Symbol\.[^\]]+\])\??\s*(?:<[^>]+>)?\s*\((.*)\)\s*:\s*(.+)$/);
      if (methodMatch) {
        addEntry({
          surfaceId,
          typeName,
          name: methodMatch[1],
          symbolKind: isStatic ? "Function" : "Method",
          signature: `${methodMatch[1]}(${methodMatch[2]})`
        });
        continue;
      }

      const propertyMatch = normalized.match(
        /^([A-Za-z_$][\w$]*|\[Symbol\.[^\]]+\])\??\s*:\s*(.+)$/);
      if (propertyMatch) {
        addEntry({
          surfaceId,
          typeName,
          name: propertyMatch[1],
          symbolKind: "Property",
          signature: `${propertyMatch[1]}: ${propertyMatch[2]}`
        });
      }
    }
  }
};

for (const [surfaceId, surface] of Object.entries(surfaces)) {
  for (const typeName of surface.types) {
    if (!interfaceBodies.has(typeName)) continue;
    addEntry({
      surfaceId,
      typeName,
      name: typeName,
      symbolKind: "Other",
      signature: typeName
    });
    parseInterface(surfaceId, typeName, typeName, false);
    const companionType = globalTypes.get(typeName)?.match(/^([A-Za-z_$][\w$]*)/)?.[1];
    if (companionType && companionType !== typeName && interfaceBodies.has(companionType)) {
      parseInterface(surfaceId, typeName, companionType, true);
    }
  }

  for (const functionName of surface.functions) {
    const functionPattern = new RegExp(
      `\\bdeclare\\s+function\\s+${functionName}(?:<[^;{]+>)?\\s*\\(([^;]*)\\)\\s*:\\s*([^;]+);`,
      "g");
    for (const match of source.matchAll(functionPattern)) {
      addEntry({
        surfaceId,
        typeName: null,
        name: functionName,
        symbolKind: "Function",
        signature: `${functionName}(${match[1].replace(/\s+/g, " ").trim()})`
      });
    }
  }
}

const overloadCounts = new Map();
for (const item of entries) {
  const key = [item.surfaceId, item.typeName ?? "", item.symbolKind, item.name]
    .join("\u001f");
  overloadCounts.set(key, (overloadCounts.get(key) ?? 0) + 1);
}
for (const item of entries) {
  const key = [item.surfaceId, item.typeName ?? "", item.symbolKind, item.name]
    .join("\u001f");
  item.overloadCount = overloadCounts.get(key);
}

entries.sort((left, right) =>
  left.surfaceId.localeCompare(right.surfaceId, "en")
  || (left.typeName ?? "").localeCompare(right.typeName ?? "", "en")
  || left.name.localeCompare(right.name, "en")
  || left.signature.localeCompare(right.signature, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion: `Web declarations from TypeScript ${typescriptVersion}`,
  inventorySourceUrl: sourceRoot,
  sourceHashes,
  license: "Apache-2.0 declaration inventory",
  surfaces: Object.fromEntries(Object.entries(surfaces).map(
    ([id, value]) => [id, { sourceUrl: value.sourceUrl }])),
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${Object.keys(surfaces).length}|${outputPath}`);
