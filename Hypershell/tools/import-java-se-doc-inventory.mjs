import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const sourceVersion = "Java SE 25";
const documentationRoot =
  "https://docs.oracle.com/en/java/javase/25/docs/api";
const files = [
  ["module", "module-search-index.js"],
  ["package", "package-search-index.js"],
  ["type", "type-search-index.js"],
  ["member", "member-search-index.js"]
];
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "java-se-25-api.json");

const inventories = {};
const sourceHashes = {};
for (const [key, fileName] of files) {
  const sourceUrl = `${documentationRoot}/${fileName}`;
  const response = await fetch(sourceUrl);
  if (!response.ok) {
    throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
  }

  const sourceBytes = Buffer.from(await response.arrayBuffer());
  sourceHashes[fileName] = createHash("sha256")
    .update(sourceBytes)
    .digest("hex");
  const source = sourceBytes.toString("utf8");
  const firstBracket = source.indexOf("[");
  const lastBracket = source.lastIndexOf("]");
  if (firstBracket < 0 || lastBracket <= firstBracket) {
    throw new Error(`${sourceUrl} did not contain a Javadoc search index.`);
  }
  inventories[key] = JSON.parse(source.slice(firstBracket, lastBracket + 1));
}

const packageModules = new Map(
  inventories.package
    .filter(item => item.m && item.l)
    .map(item => [item.l, item.m]));

const stableEntryId = (key, name) => {
  const slug = String(name)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 64) || "symbol";
  const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
  return `java.se.${slug}.${suffix}`;
};

const typePage = (moduleName, packageName, typeName) =>
  `${documentationRoot}/${moduleName}/${packageName.replaceAll(".", "/")}/${typeName}.html`;

const entries = [];
const typeKeys = new Set();
for (const item of inventories.type) {
  const packageName = item.p;
  const typeName = item.l;
  const moduleName = item.m ?? packageModules.get(packageName);
  if (!moduleName?.startsWith("java.") || !packageName || !typeName) continue;

  const key = [moduleName, packageName, typeName, "Type"].join("\u001f");
  if (typeKeys.has(key)) continue;
  typeKeys.add(key);
  entries.push({
    id: stableEntryId(key, typeName),
    moduleName,
    packageName,
    typeName,
    name: typeName,
    displayName: typeName,
    symbolKind: "Other",
    signature: typeName,
    documentationUrl: typePage(moduleName, packageName, typeName)
  });
}

const memberKeys = new Set();
for (const item of inventories.member) {
  const packageName = item.p;
  const typeName = item.c;
  const signature = item.l;
  const moduleName = item.m ?? packageModules.get(packageName);
  if (!moduleName?.startsWith("java.")
      || !packageName
      || !typeName
      || !signature) {
    continue;
  }

  const openingParenthesis = signature.indexOf("(");
  const name = openingParenthesis >= 0
    ? signature.slice(0, openingParenthesis)
    : signature;
  const simpleTypeName = typeName.split(".").at(-1);
  const symbolKind = openingParenthesis < 0
    ? "Field"
    : name === simpleTypeName
      ? "Constructor"
      : "Method";
  const key = [
    moduleName,
    packageName,
    typeName,
    symbolKind,
    signature
  ].join("\u001f");
  if (memberKeys.has(key)) continue;
  memberKeys.add(key);
  entries.push({
    id: stableEntryId(key, `${typeName}-${name}`),
    moduleName,
    packageName,
    typeName,
    name,
    displayName: name,
    symbolKind,
    signature,
    documentationUrl: typePage(moduleName, packageName, typeName)
  });
}

entries.sort((left, right) =>
  left.moduleName.localeCompare(right.moduleName, "en")
  || left.packageName.localeCompare(right.packageName, "en")
  || left.typeName.localeCompare(right.typeName, "en")
  || left.name.localeCompare(right.name, "en")
  || left.signature.localeCompare(right.signature, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion,
  sourceUrl: documentationRoot,
  sourceHashes,
  license: "Oracle documentation terms",
  moduleCount: new Set(entries.map(entry => entry.moduleName)).size,
  packageCount: new Set(entries.map(entry => entry.packageName)).size,
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${inventory.moduleCount}|${inventory.packageCount}|${outputPath}`);
