import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { typescript as ts } from
  "../src/Hypershell.EditorHost/node_modules/monaco-editor/esm/vs/languages/features/typescript/lib/typescriptServices.js";

const compilerApiVersion = "6.0.3";
const compatibilityPackageName = "@typescript/typescript6";
const compatibilityPackageVersion = "6.0.2";
const declarationPackageName = "typescript";
const sourceUrl =
  `https://unpkg.com/${declarationPackageName}@${compilerApiVersion}/lib/typescript.d.ts`;
const compatibilityPackageUrl =
  `https://unpkg.com/${compatibilityPackageName}@${compatibilityPackageVersion}/package.json`;
const documentationUrl =
  `https://github.com/microsoft/TypeScript/blob/v${compilerApiVersion}/lib/typescript.d.ts`;
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  `typescript-${compilerApiVersion}-compiler-api.json`);
const compatibilityBaselinePath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "typescript-5.9.3-compiler-api.json");

const packageMetadataUrl = (packageName, version) =>
  `https://registry.npmjs.org/${encodeURIComponent(packageName)}/${version}`;
const fetchJson = async url => {
  const jsonResponse = await fetch(url);
  if (!jsonResponse.ok) {
    throw new Error(`Unable to download ${url}: HTTP ${jsonResponse.status}.`);
  }
  return jsonResponse.json();
};

const [compatibilityPackage, compatibilityRegistry, declarationRegistry] =
  await Promise.all([
    fetchJson(compatibilityPackageUrl),
    fetchJson(packageMetadataUrl(
      compatibilityPackageName,
      compatibilityPackageVersion)),
    fetchJson(packageMetadataUrl(declarationPackageName, compilerApiVersion))
  ]);
if (compatibilityPackage.name !== compatibilityPackageName
    || compatibilityPackage.version !== compatibilityPackageVersion
    || compatibilityPackage.dependencies?.["@typescript/old"] !== "npm:typescript@^6") {
  throw new Error(
    `${compatibilityPackageName}@${compatibilityPackageVersion} no longer exposes the expected TypeScript 6 compatibility dependency.`);
}
if (declarationRegistry.name !== declarationPackageName
    || declarationRegistry.version !== compilerApiVersion) {
  throw new Error(`The pinned declaration package did not resolve to TypeScript ${compilerApiVersion}.`);
}

const response = await fetch(sourceUrl);
if (!response.ok) {
  throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
}
const sourceBytes = Buffer.from(await response.arrayBuffer());
const source = sourceBytes.toString("utf8");
const sourceSha256 = createHash("sha256").update(sourceBytes).digest("hex");
const sourceFile = ts.createSourceFile(
  "typescript.d.ts",
  source,
  ts.ScriptTarget.Latest,
  true,
  ts.ScriptKind.TS);
if (sourceFile.parseDiagnostics.length > 0) {
  throw new Error(
    `The TypeScript ${compilerApiVersion} declarations produced `
    + `${sourceFile.parseDiagnostics.length} parser diagnostics.`);
}
const rootNamespace = sourceFile.statements.find(statement =>
  ts.isModuleDeclaration(statement)
  && statement.name.getText(sourceFile).replaceAll("\"", "") === "ts");
if (!rootNamespace || !rootNamespace.body || !ts.isModuleBlock(rootNamespace.body)) {
  throw new Error("The TypeScript declaration file did not expose namespace ts.");
}

const normalizeSignature = (node) => node.getText(sourceFile)
  .replace(/\s+/g, " ")
  .replace(/;$/, "")
  .trim()
  .slice(0, 4000);
const declarationName = (node) => node?.name
  ? node.name.getText(sourceFile).replaceAll("\"", "")
  : "";
const stableEntryId = (key, name) => {
  const slug = String(name)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 64) || "symbol";
  const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
  return `typescript.compiler-api.${slug}.${suffix}`;
};

const entries = [];
const keys = new Set();
const addEntry = ({ namespacePath, typeName, name, symbolKind, signature }) => {
  if (!name || !signature) return;
  const key = [
    namespacePath.join("."),
    typeName ?? "",
    symbolKind,
    name,
    signature
  ].join("\u001f");
  if (keys.has(key)) return;
  keys.add(key);
  entries.push({
    id: stableEntryId(key, `${typeName ?? namespacePath.at(-1)}-${name}`),
    namespacePath,
    ...(typeName ? { typeName } : {}),
    name,
    displayName: name,
    symbolKind,
    signature,
    documentationUrl
  });
};

const addTypeMembers = (statement, namespacePath, typeName) => {
  for (const member of statement.members ?? []) {
    if (ts.isConstructorDeclaration(member)
        || ts.isConstructSignatureDeclaration(member)) {
      addEntry({
        namespacePath,
        typeName,
        name: typeName,
        symbolKind: "Constructor",
        signature: normalizeSignature(member)
      });
    } else if (ts.isMethodSignature(member)
               || ts.isMethodDeclaration(member)
               || ts.isCallSignatureDeclaration(member)) {
      addEntry({
        namespacePath,
        typeName,
        name: declarationName(member) || "[call]",
        symbolKind: "Method",
        signature: normalizeSignature(member)
      });
    } else if (ts.isPropertySignature(member)
               || ts.isPropertyDeclaration(member)
               || ts.isGetAccessorDeclaration(member)
               || ts.isSetAccessorDeclaration(member)
               || ts.isIndexSignatureDeclaration(member)) {
      addEntry({
        namespacePath,
        typeName,
        name: declarationName(member) || "[index]",
        symbolKind: "Property",
        signature: normalizeSignature(member)
      });
    }
  }
};

const walkStatements = (statements, namespacePath) => {
  for (const statement of statements) {
    if (ts.isModuleDeclaration(statement)) {
      const name = statement.name.getText(sourceFile).replaceAll("\"", "");
      const nestedPath = [...namespacePath, name];
      let body = statement.body;
      while (body && ts.isModuleDeclaration(body)) {
        nestedPath.push(body.name.getText(sourceFile).replaceAll("\"", ""));
        body = body.body;
      }
      if (body && ts.isModuleBlock(body)) {
        walkStatements(body.statements, nestedPath);
      }
      continue;
    }

    if (ts.isFunctionDeclaration(statement)) {
      addEntry({
        namespacePath,
        typeName: null,
        name: declarationName(statement),
        symbolKind: "Function",
        signature: normalizeSignature(statement)
      });
      continue;
    }

    if (ts.isVariableStatement(statement)) {
      for (const declaration of statement.declarationList.declarations) {
        addEntry({
          namespacePath,
          typeName: null,
          name: declarationName(declaration),
          symbolKind: statement.declarationList.flags & ts.NodeFlags.Const
            ? "Constant"
            : "Property",
          signature: normalizeSignature(declaration)
        });
      }
      continue;
    }

    const typeName = declarationName(statement);
    if (ts.isInterfaceDeclaration(statement)
        || ts.isClassDeclaration(statement)
        || ts.isEnumDeclaration(statement)
        || ts.isTypeAliasDeclaration(statement)) {
      const symbolKind = ts.isInterfaceDeclaration(statement)
        ? "Interface"
        : ts.isClassDeclaration(statement)
          ? "Class"
          : ts.isEnumDeclaration(statement)
            ? "Enum"
            : "Other";
      addEntry({
        namespacePath,
        typeName,
        name: typeName,
        symbolKind,
        signature: ts.isTypeAliasDeclaration(statement)
          ? normalizeSignature(statement)
          : `${ts.SyntaxKind[statement.kind]} ${typeName}`
      });
      if (ts.isInterfaceDeclaration(statement) || ts.isClassDeclaration(statement)) {
        addTypeMembers(statement, namespacePath, typeName);
      } else if (ts.isEnumDeclaration(statement)) {
        for (const member of statement.members) {
          addEntry({
            namespacePath,
            typeName,
            name: declarationName(member),
            symbolKind: "Constant",
            signature: normalizeSignature(member)
          });
        }
      }
    }
  }
};

walkStatements(rootNamespace.body.statements, ["ts"]);

const overloadCounts = new Map();
for (const item of entries) {
  const key = [
    item.namespacePath.join("."),
    item.typeName ?? "",
    item.symbolKind,
    item.name
  ].join("\u001f");
  overloadCounts.set(key, (overloadCounts.get(key) ?? 0) + 1);
}
for (const item of entries) {
  const key = [
    item.namespacePath.join("."),
    item.typeName ?? "",
    item.symbolKind,
    item.name
  ].join("\u001f");
  item.overloadCount = overloadCounts.get(key);
}

entries.sort((left, right) =>
  left.namespacePath.join(".").localeCompare(
    right.namespacePath.join("."),
    "en")
  || (left.typeName ?? "").localeCompare(right.typeName ?? "", "en")
  || left.name.localeCompare(right.name, "en")
  || left.signature.localeCompare(right.signature, "en"));

const compatibilityBaseline = JSON.parse(
  await readFile(compatibilityBaselinePath, "utf8"));
const coordinateKey = item => [
  item.namespacePath.join("."),
  item.typeName ?? "",
  item.symbolKind,
  item.name
].join("\u001f");
const relaxedCoordinateKeys = item => [
  [item.typeName ?? "", item.symbolKind, item.name].join("\u001f"),
  [item.namespacePath.at(-1) ?? "", item.typeName ?? "", item.symbolKind, item.name]
    .join("\u001f"),
  [item.symbolKind, item.name, item.signature].join("\u001f")
];
const groupBy = (items, keySelector) => {
  const groups = new Map();
  for (const item of items) {
    const key = keySelector(item);
    const group = groups.get(key) ?? [];
    group.push(item);
    groups.set(key, group);
  }
  return groups;
};
const addLegacyId = (entry, legacyId) => {
  if (!legacyId || legacyId === entry.id) return;
  entry.legacyIds ??= [];
  if (!entry.legacyIds.includes(legacyId)) entry.legacyIds.push(legacyId);
};

const currentGroups = groupBy(entries, coordinateKey);
const baselineGroups = groupBy(compatibilityBaseline.entries, coordinateKey);
const resolvedBaselineIds = new Set();
let preservedIdCount = 0;
let aliasedIdCount = 0;

for (const [key, currentGroup] of currentGroups) {
  const baselineGroup = baselineGroups.get(key);
  if (!baselineGroup) continue;

  const availableBaseline = [...baselineGroup];
  for (const current of currentGroup) {
    const exactIndex = availableBaseline.findIndex(
      baseline => baseline.signature === current.signature);
    if (exactIndex < 0) continue;
    const [baseline] = availableBaseline.splice(exactIndex, 1);
    current.id = baseline.id;
    resolvedBaselineIds.add(baseline.id);
    preservedIdCount += 1;
  }

  const unmatchedCurrent = currentGroup.filter(current =>
    !compatibilityBaseline.entries.some(baseline =>
      baseline.id === current.id && baseline.signature === current.signature));
  for (let index = 0;
       index < Math.min(unmatchedCurrent.length, availableBaseline.length);
       index += 1) {
    const current = unmatchedCurrent[index];
    const baseline = availableBaseline[index];
    current.id = baseline.id;
    resolvedBaselineIds.add(baseline.id);
    preservedIdCount += 1;
  }

  const successor = currentGroup[0];
  for (const baseline of availableBaseline.slice(unmatchedCurrent.length)) {
    addLegacyId(successor, baseline.id);
    resolvedBaselineIds.add(baseline.id);
    aliasedIdCount += 1;
  }
}

const relaxedIndexes = relaxedCoordinateKeys(entries[0]).map((_, keyIndex) => {
  const index = new Map();
  for (const entry of entries) {
    const key = relaxedCoordinateKeys(entry)[keyIndex];
    const candidates = index.get(key) ?? [];
    candidates.push(entry);
    index.set(key, candidates);
  }
  return index;
});
for (const baseline of compatibilityBaseline.entries) {
  if (resolvedBaselineIds.has(baseline.id)) continue;
  let successor = null;
  for (let keyIndex = 0; keyIndex < relaxedIndexes.length; keyIndex += 1) {
    const candidates = relaxedIndexes[keyIndex].get(
      relaxedCoordinateKeys(baseline)[keyIndex]) ?? [];
    if (candidates.length === 1) {
      [successor] = candidates;
      break;
    }
    const exactSignature = candidates.filter(
      candidate => candidate.signature === baseline.signature);
    if (exactSignature.length === 1) {
      [successor] = exactSignature;
      break;
    }
  }
  if (!successor) continue;
  addLegacyId(successor, baseline.id);
  resolvedBaselineIds.add(baseline.id);
  aliasedIdCount += 1;
}

for (const entry of entries) {
  if (entry.legacyIds) entry.legacyIds.sort((left, right) => left.localeCompare(right, "en"));
}
const unresolvedLegacyIds = compatibilityBaseline.entries
  .filter(entry => !resolvedBaselineIds.has(entry.id))
  .map(entry => ({
    id: entry.id,
    namespacePath: entry.namespacePath,
    ...(entry.typeName ? { typeName: entry.typeName } : {}),
    name: entry.name,
    symbolKind: entry.symbolKind,
    signature: entry.signature
  }));
const currentIds = new Set(entries.map(entry => entry.id));
if (currentIds.size !== entries.length) {
  throw new Error("The migrated TypeScript Compiler API inventory contains duplicate entry IDs.");
}
const legacyIds = entries.flatMap(entry => entry.legacyIds ?? []);
if (new Set(legacyIds).size !== legacyIds.length
    || legacyIds.some(id => currentIds.has(id))) {
  throw new Error("The TypeScript Compiler API migration produced colliding legacy aliases.");
}
if (unresolvedLegacyIds.length > 0) {
  throw new Error(
    `${unresolvedLegacyIds.length} TypeScript 5.9.3 entry IDs did not resolve `
    + "to the TypeScript 6 compatibility inventory.");
}

const inventory = {
  schemaVersion: 1,
  sourceVersion: `TypeScript ${compilerApiVersion} public compiler declarations`,
  sourceUrl,
  compatibilityPackageUrl,
  documentationUrl,
  sourceSha256,
  license: "Apache-2.0",
  package: {
    name: compatibilityPackageName,
    version: compatibilityPackageVersion,
    integrity: compatibilityRegistry.dist.integrity,
    tarball: compatibilityRegistry.dist.tarball,
    declarationPackage: {
      name: declarationPackageName,
      version: compilerApiVersion,
      integrity: declarationRegistry.dist.integrity,
      tarball: declarationRegistry.dist.tarball
    }
  },
  compatibility: {
    baselineSourceVersion: compatibilityBaseline.sourceVersion,
    baselineEntryCount: compatibilityBaseline.entries.length,
    preservedIdCount,
    aliasedIdCount,
    unresolvedEntryCount: unresolvedLegacyIds.length,
    unresolvedEntries: unresolvedLegacyIds
  },
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(
  `${entries.length}|${sourceSha256}|preserved=${preservedIdCount}`
  + `|aliased=${aliasedIdCount}|unresolved=${unresolvedLegacyIds.length}`
  + `|${outputPath}`);
