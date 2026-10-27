import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const dataVersion = "2.31.0";
const sourceUrl = `https://unpkg.com/mdn-data@${dataVersion}/css/properties.json`;
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "css-properties-2.31.0.json");

const response = await fetch(sourceUrl);
if (!response.ok) {
  throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
}
const sourceBytes = Buffer.from(await response.arrayBuffer());
const sourceSha256 = createHash("sha256").update(sourceBytes).digest("hex");
const properties = JSON.parse(sourceBytes.toString("utf8"));

const surfaces = {
  "css-flexible-box-layout": {
    groups: ["CSS Flexible Box Layout"],
    sourceUrl: "https://www.w3.org/TR/css-flexbox-1/"
  },
  "css-grid-layout": {
    groups: ["CSS Grid Layout"],
    sourceUrl: "https://www.w3.org/TR/css-grid-2/"
  },
  "css-fonts": {
    groups: ["CSS Fonts"],
    sourceUrl: "https://www.w3.org/TR/css-fonts-4/"
  },
  "css-transitions": {
    groups: ["CSS Transitions"],
    sourceUrl: "https://www.w3.org/TR/css-transitions-2/"
  },
  "css-animations": {
    groups: ["CSS Animations", "Scroll-driven Animations"],
    sourceUrl: "https://www.w3.org/TR/css-animations-2/"
  },
  "css-color": {
    groups: ["CSS Color"],
    sourceUrl: "https://www.w3.org/TR/css-color-4/"
  },
  "css-transforms": {
    groups: ["CSS Transforms", "Motion Path"],
    sourceUrl: "https://www.w3.org/TR/css-transforms-2/"
  },
  "css-core": {
    groups: [
      "CSS Cascading and Inheritance",
      "CSS Custom Properties for Cascading Variables",
      "CSS Conditional Rules",
      "CSS Display",
      "CSS Box Model",
      "CSS Box Sizing",
      "CSS Values and Units"
    ],
    sourceUrl: "https://www.w3.org/Style/CSS/"
  }
};

const groupOwners = new Map();
for (const [surfaceId, surface] of Object.entries(surfaces)) {
  for (const group of surface.groups) {
    if (!groupOwners.has(group)) groupOwners.set(group, surfaceId);
  }
}

const entries = [];
for (const [name, metadata] of Object.entries(properties)) {
  if ((name.startsWith("-") && name !== "--*")
      || metadata.status === "nonstandard"
      || metadata.status === "obsolete") {
    continue;
  }
  const surfaceId = (metadata.groups ?? [])
    .map(group => groupOwners.get(group))
    .find(Boolean);
  if (!surfaceId) continue;
  const key = [surfaceId, name].join("\u001f");
  const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
  entries.push({
    id: `css.${surfaceId}.${name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "custom-property"}.${suffix}`,
    surfaceId,
    name,
    displayName: name,
    symbolKind: "Property",
    signature: `${name}: ${metadata.syntax}`,
    status: metadata.status,
    documentationUrl: surfaces[surfaceId].sourceUrl,
    inventoryDocumentationUrl: metadata.mdn_url
  });
}

entries.sort((left, right) =>
  left.surfaceId.localeCompare(right.surfaceId, "en")
  || left.name.localeCompare(right.name, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion: `mdn-data ${dataVersion}`,
  sourceUrl,
  sourceSha256,
  license: "CC0-1.0 inventory",
  surfaces: Object.fromEntries(Object.entries(surfaces).map(
    ([id, surface]) => [id, { sourceUrl: surface.sourceUrl }])),
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${Object.keys(surfaces).length}|${sourceSha256}|${outputPath}`);
