import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const typescriptVersion = "5.9.3";
const rootLibrary = "es2024.full";
const sourceRoot = `https://unpkg.com/typescript@${typescriptVersion}/lib`;
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "ecmascript-2024-declarations.json");

const sources = new Map();
const hashes = {};
const loadLibrary = async (libraryName) => {
  if (sources.has(libraryName)) return;
  const fileName = `lib.${libraryName}.d.ts`;
  const sourceUrl = `${sourceRoot}/${fileName}`;
  const response = await fetch(sourceUrl);
  if (!response.ok) {
    throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
  }

  const bytes = Buffer.from(await response.arrayBuffer());
  const source = bytes.toString("utf8");
  sources.set(libraryName, source);
  hashes[fileName] = createHash("sha256").update(bytes).digest("hex");
  const references = [...source.matchAll(
    /\/\/\/\s*<reference\s+lib=["']([^"']+)["']\s*\/>/gi)]
    .map(match => match[1])
    .filter(name => name === "es5" || name.startsWith("es20") || name.startsWith("esnext"));
  for (const reference of references) {
    await loadLibrary(reference);
  }
};

await loadLibrary(rootLibrary);

const withoutComments = [...sources.values()]
  .join("\n")
  .replace(/\/\*[\s\S]*?\*\//g, "")
  .replace(/^\s*\/\/.*$/gm, "");

const findClosingBrace = (source, openingBrace) => {
  let depth = 0;
  for (let index = openingBrace; index < source.length; index++) {
    if (source[index] === "{") depth++;
    if (source[index] === "}" && --depth === 0) return index;
  }
  throw new Error("An ECMAScript declaration interface was not balanced.");
};

const interfaceBodies = new Map();
const interfacePattern = /\binterface\s+([A-Za-z_$][\w$]*)(?:\s*<[^>{}]*>)?(?:\s+extends\s+[^{}]+)?\s*\{/g;
for (const match of withoutComments.matchAll(interfacePattern)) {
  const openingBrace = match.index + match[0].lastIndexOf("{");
  const closingBrace = findClosingBrace(withoutComments, openingBrace);
  const bodies = interfaceBodies.get(match[1]) ?? [];
  bodies.push(withoutComments.slice(openingBrace + 1, closingBrace));
  interfaceBodies.set(match[1], bodies);
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

const globalTypes = new Map();
for (const match of withoutComments.matchAll(
  /\bdeclare\s+(?:var|const)\s+([A-Za-z_$][\w$]*)\s*:\s*([^;]+);/g)) {
  globalTypes.set(match[1], match[2].trim());
}

const stableEntryId = (key, name) => {
  const slug = String(name)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 64) || "symbol";
  const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
  return `javascript.core.${slug}.${suffix}`;
};

const entries = [];
const entryKeys = new Set();
const addEntry = ({ typeName, name, symbolKind, signature }) => {
  const key = [typeName ?? "", symbolKind, name, signature]
    .join("\u001f");
  if (entryKeys.has(key)) return;
  entryKeys.add(key);
  entries.push({
    id: stableEntryId(key, `${typeName ?? "global"}-${name}`),
    ...(typeName ? { typeName } : {}),
    name,
    displayName: name,
    symbolKind,
    signature,
    documentationUrl: "https://tc39.es/ecma262/"
  });
};

const parseInterface = (globalName, interfaceName, isStatic) => {
  for (const body of interfaceBodies.get(interfaceName) ?? []) {
    for (const declaration of splitDeclarations(body)) {
      const normalized = declaration
        .replace(/^readonly\s+/, "")
        .replace(/^abstract\s+/, "")
        .trim();
      const constructorMatch = normalized.match(/^new\s*(?:<[^>]+>)?\s*\((.*)\)\s*:\s*(.+)$/);
      if (constructorMatch) {
        addEntry({
          typeName: globalName,
          name: globalName,
          symbolKind: "Constructor",
          signature: `new ${globalName}(${constructorMatch[1]})`
        });
        continue;
      }

      const callMatch = normalized.match(/^\s*(?:<[^>]+>)?\s*\((.*)\)\s*:\s*(.+)$/);
      if (callMatch) {
        addEntry({
          typeName: globalName,
          name: globalName,
          symbolKind: "Function",
          signature: `${globalName}(${callMatch[1]})`
        });
        continue;
      }

      const methodMatch = normalized.match(
        /^([A-Za-z_$][\w$]*|\[Symbol\.[^\]]+\])\??\s*(?:<[^>]+>)?\s*\((.*)\)\s*:\s*(.+)$/);
      if (methodMatch) {
        addEntry({
          typeName: globalName,
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
          typeName: globalName,
          name: propertyMatch[1],
          symbolKind: "Property",
          signature: `${propertyMatch[1]}: ${propertyMatch[2]}`
        });
      }
    }
  }
};

for (const [globalName, declaredType] of globalTypes) {
  const companionType = declaredType.match(/^([A-Za-z_$][\w$]*)/)?.[1];
  const hasInstanceInterface = interfaceBodies.has(globalName);
  const hasCompanionInterface = companionType && interfaceBodies.has(companionType);
  const isObjectSurface = hasInstanceInterface || hasCompanionInterface;
  addEntry({
    typeName: globalName,
    name: globalName,
    symbolKind: isObjectSurface ? "Class" : "Constant",
    signature: `${globalName}: ${declaredType}`
  });
  if (hasInstanceInterface) parseInterface(globalName, globalName, false);
  if (hasCompanionInterface && companionType !== globalName) {
    parseInterface(globalName, companionType, true);
  }
}

for (const match of withoutComments.matchAll(
  /\bdeclare\s+function\s+([A-Za-z_$][\w$]*)(?:<[^;{]+>)?\s*\(([^;]*)\)\s*:\s*([^;]+);/g)) {
  addEntry({
    typeName: null,
    name: match[1],
    symbolKind: "Function",
    signature: `${match[1]}(${match[2].replace(/\s+/g, " ").trim()})`
  });
}

const overloadCounts = new Map();
for (const item of entries) {
  const key = [item.typeName ?? "", item.symbolKind, item.name].join("\u001f");
  overloadCounts.set(key, (overloadCounts.get(key) ?? 0) + 1);
}
for (const item of entries) {
  const key = [item.typeName ?? "", item.symbolKind, item.name].join("\u001f");
  item.overloadCount = overloadCounts.get(key);
}

entries.sort((left, right) =>
  (left.typeName ?? "").localeCompare(right.typeName ?? "", "en")
  || left.name.localeCompare(right.name, "en")
  || left.signature.localeCompare(right.signature, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion: `ECMAScript 2024 declarations from TypeScript ${typescriptVersion}`,
  canonicalSourceUrl: "https://tc39.es/ecma262/",
  inventorySourceUrl: `${sourceRoot}/lib.${rootLibrary}.d.ts`,
  sourceHashes: hashes,
  license: "Apache-2.0 declaration inventory; ECMA-262 is Ecma International copyright",
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${sources.size}|${outputPath}`);
