import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const sourceUrl = "https://html.spec.whatwg.org/multipage/indices.html";
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "html-standard-index.json");

const response = await fetch(sourceUrl);
if (!response.ok) {
  throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
}
const sourceBytes = Buffer.from(await response.arrayBuffer());
const source = sourceBytes.toString("utf8");
const sourceSha256 = createHash("sha256").update(sourceBytes).digest("hex");

const decode = (value) => String(value ?? "")
  .replace(/<[^>]+>/g, " ")
  .replaceAll("&lt;", "<")
  .replaceAll("&gt;", ">")
  .replaceAll("&amp;", "&")
  .replaceAll("&quot;", "\"")
  .replaceAll("&#39;", "'")
  .replace(/\s+/g, " ")
  .trim();

const absoluteUrl = (relative) => {
  if (!relative) return "https://html.spec.whatwg.org/";
  return new URL(relative, sourceUrl).toString();
};

const tableRows = (captionText) => {
  const captionIndex = source.indexOf(captionText);
  if (captionIndex < 0) throw new Error(`Missing HTML index table: ${captionText}`);
  const tableStart = source.lastIndexOf("<table", captionIndex);
  const tableEnd = source.indexOf("</table>", captionIndex);
  const table = source.slice(tableStart, tableEnd);
  const bodyStart = table.indexOf("<tbody>");
  return table.slice(bodyStart + "<tbody>".length)
    .split(/<tr>/g)
    .slice(1)
    .map(row => [...row.matchAll(
      /<(?:th|td)(?:\s[^>]*)?>([\s\S]*?)(?=<(?:th|td)(?:\s|>)|$)/g)]
      .map(match => match[1]));
};

const stableEntryId = (kind, name, scope = "") => {
  const key = [kind, name, scope].join("\u001f");
  const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
  const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  return `html.core.${kind.toLowerCase()}.${slug || "symbol"}.${suffix}`;
};

const elementCategory = (categories, name) => {
  const value = categories.toLowerCase();
  if (["html", "head", "body"].includes(name)) return "Document structure";
  if (value.includes("metadata")) return "Metadata";
  if (value.includes("sectioning")) return "Sectioning";
  if (value.includes("heading")) return "Headings";
  if (value.includes("embedded")) return "Embedded content";
  if (value.includes("interactive")) return "Interactive content";
  if (value.includes("phrasing")) return "Phrasing content";
  if (value.includes("flow")) return "Flow content";
  return "Other elements";
};

const voidElements = new Set([
  "area", "base", "br", "col", "embed", "hr", "img", "input", "link",
  "meta", "source", "track", "wbr"
]);

const entries = [];
for (const cells of tableRows("List of elements")) {
  if (cells.length < 3) continue;
  const name = decode(cells[0]);
  if (!/^[a-z][a-z0-9-]*$/.test(name)) continue;
  const href = cells[0].match(/href=([^ >]+)/)?.[1];
  const description = decode(cells[1]);
  const categories = decode(cells[2]);
  entries.push({
    id: stableEntryId("Element", name),
    name,
    displayName: `<${name}>`,
    symbolKind: "Element",
    category: elementCategory(categories, name),
    signature: voidElements.has(name) ? `<${name}>` : `<${name}></${name}>`,
    description,
    documentationUrl: absoluteUrl(href)
  });
}

for (const cells of tableRows("List of attributes (excluding event handler content attributes)")) {
  if (cells.length < 3) continue;
  const name = decode(cells[0]);
  if (!name || name.includes(" ")) continue;
  const elementScope = decode(cells[1]);
  const description = decode(cells[2]);
  const rowHtml = cells.join("");
  const href = rowHtml.match(/href=([^ >]+)/)?.[1];
  const category = /HTML elements|all elements/i.test(elementScope)
    ? "Global attributes"
    : "Element-specific attributes";
  entries.push({
    id: stableEntryId("Attribute", name, elementScope),
    name,
    displayName: name,
    symbolKind: "Attribute",
    category,
    signature: `${name}=\"\"`,
    description: `${description}${elementScope ? ` Applies to: ${elementScope}.` : ""}`,
    documentationUrl: absoluteUrl(href)
  });
}

entries.sort((left, right) =>
  left.symbolKind.localeCompare(right.symbolKind, "en")
  || left.category.localeCompare(right.category, "en")
  || left.name.localeCompare(right.name, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion: "WHATWG HTML Living Standard index",
  sourceUrl,
  sourceSha256,
  license: "WHATWG Living Standard",
  entryCount: entries.length,
  elementCount: entries.filter(entry => entry.symbolKind === "Element").length,
  attributeCount: entries.filter(entry => entry.symbolKind === "Attribute").length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${inventory.elementCount}|${inventory.attributeCount}|${sourceSha256}|${outputPath}`);
