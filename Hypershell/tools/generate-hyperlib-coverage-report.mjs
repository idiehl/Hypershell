import { readdir, readFile, stat, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const catalogRoot = path.join(
  repositoryRoot,
  "src",
  "Hypershell",
  "Assets",
  "CommandLibraries");
const outputPath = path.join(repositoryRoot, "doc", "HYPERLIB_COVERAGE.md");

const descriptorFiles = (await readdir(catalogRoot))
  .filter(file => file.endsWith(".json"))
  .sort((left, right) => left.localeCompare(right, "en"));
const rows = [];
const ownedEntryIds = new Map();
let distributionBytes = 0;

for (const fileName of descriptorFiles) {
  const descriptorPath = path.join(catalogRoot, fileName);
  const descriptor = JSON.parse(await readFile(descriptorPath, "utf8"));
  if (descriptor.schemaVersion !== 2) continue;
  if (/essentials/i.test(descriptor.id) || /essentials/i.test(descriptor.displayName)) {
    throw new Error(`Visible schema-v2 catalog retained an Essentials name: ${descriptor.id}`);
  }

  const entries = [];
  let libraryBytes = (await stat(descriptorPath)).size;
  for (const relativePath of descriptor.entryFiles ?? []) {
    const shardPath = path.join(catalogRoot, relativePath);
    const shard = JSON.parse(await readFile(shardPath, "utf8"));
    if (shard.schemaVersion !== 2 || shard.libraryId !== descriptor.id) {
      throw new Error(`Entry shard ${relativePath} does not belong to ${descriptor.id}.`);
    }
    entries.push(...shard.entries);
    libraryBytes += (await stat(shardPath)).size;
  }
  for (const relativePath of descriptor.guideFiles ?? []) {
    libraryBytes += (await stat(path.join(catalogRoot, relativePath))).size;
  }

  if (entries.length !== descriptor.declaredEntryCount) {
    throw new Error(`${descriptor.id} declares ${descriptor.declaredEntryCount} entries but contains ${entries.length}.`);
  }
  if (entries.some(entry => !Array.isArray(entry.browsePath) || entry.browsePath.length === 0)) {
    throw new Error(`${descriptor.id} contains an entry without a browsePath.`);
  }

  const counts = { Indexed: 0, Guided: 0, Verified: 0 };
  for (const entry of entries) {
    const tier = entry.coverageTier ?? "Guided";
    counts[tier] = (counts[tier] ?? 0) + 1;
    const existingOwner = ownedEntryIds.get(entry.id.toLowerCase());
    if (existingOwner) {
      throw new Error(`Entry ${entry.id} is owned by both ${existingOwner} and ${descriptor.id}.`);
    }
    ownedEntryIds.set(entry.id.toLowerCase(), descriptor.id);
  }

  if (counts.Indexed !== descriptor.declaredIndexedCount
      || counts.Guided !== descriptor.declaredGuidedCount
      || counts.Verified !== descriptor.declaredVerifiedCount) {
    throw new Error(`${descriptor.id} coverage-tier counts do not match its descriptor.`);
  }

  distributionBytes += libraryBytes;
  rows.push({ descriptor, counts, libraryBytes });
}

rows.sort((left, right) =>
  left.descriptor.language.localeCompare(right.descriptor.language, "en")
  || left.descriptor.displayName.localeCompare(right.descriptor.displayName, "en"));

const escapeCell = (value) => String(value ?? "").replaceAll("|", "\\|");
const totalEntries = rows.reduce(
  (total, row) => total + row.descriptor.declaredEntryCount,
  0);
const totalGuides = rows.reduce(
  (total, row) => total + row.counts.Guided + row.counts.Verified,
  0);
const report = [
  "# Hyperlib official catalog coverage",
  "",
  "This report is generated from the shipped schema-v2 descriptors and their lazy symbol shards. Runtime-generated PowerShell and .NET reflection catalogs are intentionally reported by the application at run time instead.",
  "",
  `Static collections: **${rows.length.toLocaleString("en-US")}**`,
  "",
  `Public entries: **${totalEntries.toLocaleString("en-US")}**`,
  "",
  `Detailed guides: **${totalGuides.toLocaleString("en-US")}**`,
  "",
  `Packaged descriptor, index, and guide data: **${(distributionBytes / 1024 / 1024).toFixed(2)} MiB**`,
  "",
  "| Language | Collection | Kind | Target/source | Indexed | Guided | Verified | Data |",
  "|---|---|---|---|---:|---:|---:|---:|",
  ...rows.map(({ descriptor, counts, libraryBytes }) =>
    `| ${escapeCell(descriptor.language)} | [${escapeCell(descriptor.displayName)}](${descriptor.sourceUrl}) | ${escapeCell(descriptor.kind)} | ${escapeCell(descriptor.targetVersion)} / ${escapeCell(descriptor.sourceVersion)} | ${counts.Indexed.toLocaleString("en-US")} | ${counts.Guided.toLocaleString("en-US")} | ${counts.Verified.toLocaleString("en-US")} | ${(libraryBytes / 1024 / 1024).toFixed(2)} MiB |`),
  "",
  "Coverage tiers:",
  "",
  "- **Indexed**: searchable signature, canonical hierarchy, and authoritative documentation link.",
  "- **Guided**: Indexed metadata plus an original explanation, parameters, and a useful example.",
  "- **Verified**: Guided content whose example is explicitly promoted after its supported-runtime verification policy passes.",
  "",
  "The report is deterministic: it contains no generation timestamp, and generation fails on descriptor-count drift, missing browse paths, duplicate canonical entry IDs, or coverage-count mismatches.",
  ""
].join("\n");

await writeFile(outputPath, report, "utf8");
console.log(`${rows.length}|${totalEntries}|${totalGuides}|${distributionBytes}|${outputPath}`);
