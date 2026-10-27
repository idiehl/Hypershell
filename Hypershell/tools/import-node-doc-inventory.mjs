import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const sourceVersion = "Node.js 24.14.0";
const sourceUrl = "https://nodejs.org/docs/v24.14.0/api/all.json";
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "node-24.14.0-api.json");

const response = await fetch(sourceUrl);
if (!response.ok) {
  throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
}

const sourceBytes = Buffer.from(await response.arrayBuffer());
const sourceSha256 = createHash("sha256").update(sourceBytes).digest("hex");
const documentation = JSON.parse(sourceBytes.toString("utf8"));

// These are importable Node.js modules. Documentation-only topics and the
// browser-compatible Web Crypto/Streams surfaces intentionally stay out of
// the Node.js Standard Library collection.
const sourceModuleNames = new Map([
  ["assert", "node:assert"],
  ["async_hooks", "node:async_hooks"],
  ["buffer", "node:buffer"],
  ["child_process", "node:child_process"],
  ["cluster", "node:cluster"],
  ["console", "node:console"],
  ["crypto", "node:crypto"],
  ["diagnostics_channel", "node:diagnostics_channel"],
  ["dns", "node:dns"],
  ["domain", "node:domain"],
  ["events", "node:events"],
  ["fs", "node:fs"],
  ["http", "node:http"],
  ["http2", "node:http2"],
  ["https", "node:https"],
  ["inspector", "node:inspector"],
  ["module", "node:module"],
  ["net", "node:net"],
  ["os", "node:os"],
  ["path", "node:path"],
  ["perf_hooks", "node:perf_hooks"],
  ["punycode", "node:punycode"],
  ["querystring", "node:querystring"],
  ["readline", "node:readline"],
  ["repl", "node:repl"],
  ["sqlite", "node:sqlite"],
  ["stream", "node:stream"],
  ["string_decoder", "node:string_decoder"],
  ["test", "node:test"],
  ["timers", "node:timers"],
  ["tls", "node:tls"],
  ["tracing", "node:trace_events"],
  ["tty", "node:tty"],
  ["dgram", "node:dgram"],
  ["url", "node:url"],
  ["util", "node:util"],
  ["v8", "node:v8"],
  ["vm", "node:vm"],
  ["wasi", "node:wasi"],
  ["worker_threads", "node:worker_threads"],
  ["zlib", "node:zlib"]
]);

const childCollections = [
  "modules",
  "classes",
  "ctors",
  "methods",
  "properties",
  "events"
];

const sourceName = (source) => path.basename(source ?? "", ".md");
const plainName = (value) => String(value ?? "")
  .replaceAll("`", "")
  .replace(/^Class:\s*/i, "")
  .replace(/^Event:\s*/i, "")
  .trim();

const signatureText = (signature, fallback) => {
  const parameters = (signature?.params ?? [])
    .map(item => item?.name)
    .filter(Boolean)
    .join(", ");
  return `${fallback}(${parameters})`;
};

const stableEntryId = (key, name) => {
  const slug = String(name)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 64) || "symbol";
  const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
  return `node.stdlib.${slug}.${suffix}`;
};

const entries = [];
const keys = new Set();

const addEntry = ({
  moduleName,
  typeName,
  node,
  symbolKind,
  documentationPage
}) => {
  const name = plainName(node.name || node.displayName);
  if (!name) return;

  const signatures = Array.isArray(node.signatures) && node.signatures.length > 0
    ? node.signatures
    : [null];
  for (let index = 0; index < signatures.length; index++) {
    const signature = signatureText(signatures[index], name);
    const key = [moduleName, typeName ?? "", symbolKind, name, signature]
      .join("\u001f").toLowerCase();
    if (keys.has(key)) continue;
    keys.add(key);
    entries.push({
      id: stableEntryId(key, name),
      moduleName,
      ...(typeName ? { typeName } : {}),
      name,
      displayName: plainName(node.displayName || node.name),
      symbolKind,
      signature,
      overloadIndex: index,
      overloadCount: signatures.length,
      documentationUrl: `https://nodejs.org/docs/v24.14.0/api/${documentationPage}.html`
    });
  }
};

const moduleForSection = (baseModule, sectionName) => {
  const value = String(sectionName ?? "").toLowerCase();
  if (!value.includes("promises_api")) return baseModule;
  if (["node:fs", "node:dns", "node:timers", "node:readline", "node:stream"].includes(baseModule)) {
    return `${baseModule}/promises`;
  }
  return baseModule;
};

const walk = ({ node, moduleName, documentationPage, typeName = null }) => {
  const nodeType = String(node?.type ?? "").toLowerCase();
  const currentType = nodeType === "class"
    ? plainName(node.name || node.displayName)
    : typeName;

  if (nodeType === "class") {
    addEntry({
      moduleName,
      typeName: null,
      node,
      symbolKind: "Class",
      documentationPage
    });
  } else if (nodeType === "ctor") {
    addEntry({ moduleName, typeName, node, symbolKind: "Constructor", documentationPage });
  } else if (nodeType === "method") {
    addEntry({
      moduleName,
      typeName,
      node,
      symbolKind: typeName ? "Method" : "Function",
      documentationPage
    });
  } else if (nodeType === "property") {
    addEntry({ moduleName, typeName, node, symbolKind: "Property", documentationPage });
  } else if (nodeType === "event") {
    addEntry({ moduleName, typeName, node, symbolKind: "Event", documentationPage });
  }

  for (const collectionName of childCollections) {
    for (const child of node?.[collectionName] ?? []) {
      const childModule = collectionName === "modules"
        ? moduleForSection(moduleName, child.name)
        : moduleName;
      walk({
        node: child,
        moduleName: childModule,
        documentationPage,
        typeName: currentType
      });
    }
  }
};

for (const node of documentation.modules ?? []) {
  const documentationPage = sourceName(node.source);
  const moduleName = sourceModuleNames.get(documentationPage);
  if (!moduleName) continue;

  entries.push({
    id: stableEntryId(`module\u001f${moduleName}`, moduleName),
    moduleName,
    name: moduleName,
    displayName: moduleName,
    symbolKind: "Module",
    signature: moduleName,
    overloadIndex: 0,
    overloadCount: 1,
    documentationUrl: `https://nodejs.org/docs/v24.14.0/api/${documentationPage}.html`
  });
  walk({ node, moduleName, documentationPage });
}

entries.sort((left, right) =>
  left.moduleName.localeCompare(right.moduleName, "en")
  || (left.typeName ?? "").localeCompare(right.typeName ?? "", "en")
  || left.name.localeCompare(right.name, "en")
  || left.signature.localeCompare(right.signature, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion,
  sourceUrl,
  sourceSha256,
  license: "MIT",
  exclusions: [
    "Browser-compatible Web Crypto API",
    "Browser-compatible Web Streams API",
    "Web-platform globals"
  ],
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${sourceSha256}|${outputPath}`);
