import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { inflateSync } from "node:zlib";
import { fileURLToPath } from "node:url";
import path from "node:path";

const pythonVersion = process.argv[2] ?? "3.14";
if (!/^3\.\d+$/.test(pythonVersion)) {
  throw new Error("Pass a pinned Python documentation version such as 3.14.");
}

const sourceUrl = `https://docs.python.org/${pythonVersion}/objects.inv`;
const response = await fetch(sourceUrl, {
  headers: { "User-Agent": "Hypershell-Hyperlib-Importer/2" }
});
if (!response.ok) {
  throw new Error(`Python documentation inventory returned HTTP ${response.status}.`);
}

const inventory = Buffer.from(await response.arrayBuffer());
let payloadOffset = 0;
for (let line = 0; line < 4; line += 1) {
  payloadOffset = inventory.indexOf(0x0a, payloadOffset);
  if (payloadOffset < 0) {
    throw new Error("The Python objects.inv header is incomplete.");
  }
  payloadOffset += 1;
}

const text = inflateSync(inventory.subarray(payloadOffset)).toString("utf8");
const supportedRoles = new Set([
  "py:module",
  "py:function",
  "py:class",
  "py:exception",
  "py:method",
  "py:classmethod",
  "py:staticmethod",
  "py:attribute",
  "py:property",
  "py:data"
]);
const entries = [];
const seen = new Set();
for (const line of text.split("\n")) {
  const match = /^(\S+)\s+(\S+)\s+(-?\d+)\s+(\S+)\s+(.*)$/.exec(line);
  if (!match) continue;
  const [, name, role, , rawUri, rawDisplayName] = match;
  if (!supportedRoles.has(role) || !rawUri.startsWith("library/")) continue;
  if (name.split(".").some(segment => segment.startsWith("_"))) continue;
  const key = `${role}\u001f${name}`.toLowerCase();
  if (seen.has(key)) continue;
  seen.add(key);
  const uri = rawUri.endsWith("$")
    ? rawUri.slice(0, -1) + name
    : rawUri;
  entries.push({
    name,
    role,
    uri,
    displayName: rawDisplayName === "-" ? name : rawDisplayName
  });
}

entries.sort((left, right) =>
  left.name.localeCompare(right.name, "en", { sensitivity: "base" })
  || left.role.localeCompare(right.role, "en"));
const output = {
  schemaVersion: 1,
  source: "Python documentation Sphinx object inventory",
  sourceVersion: `Python ${pythonVersion}`,
  sourceUrl,
  sourceSha256: createHash("sha256").update(inventory).digest("hex"),
  entries
};
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputDirectory = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources");
const outputPath = path.join(
  outputDirectory,
  `python-${pythonVersion}-objects.json`);
await mkdir(outputDirectory, { recursive: true });
await writeFile(outputPath, `${JSON.stringify(output, null, 2)}\n`, "utf8");
console.log(`${output.entries.length}|${output.sourceSha256}|${outputPath}`);
