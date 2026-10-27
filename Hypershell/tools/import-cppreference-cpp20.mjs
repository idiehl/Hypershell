import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const indexUrl = "https://en.cppreference.com/cpp/header";
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "cpp20-standard-library-index.json");

const download = async (url) => {
  const response = await fetch(url, {
    headers: {
      "User-Agent": "Mozilla/5.0 HyperlibCatalogImporter/1.0"
    }
  });
  if (!response.ok) throw new Error(`Unable to download ${url}: HTTP ${response.status}.`);
  const bytes = Buffer.from(await response.arrayBuffer());
  return {
    text: bytes.toString("utf8"),
    sha256: createHash("sha256").update(bytes).digest("hex")
  };
};

const decode = (value) => String(value ?? "")
  .replace(/<[^>]+>/g, " ")
  .replaceAll("&lt;", "<")
  .replaceAll("&gt;", ">")
  .replaceAll("&amp;", "&")
  .replaceAll("&quot;", "\"")
  .replace(/&#(\d+);/g, (_, codePoint) =>
    String.fromCodePoint(Number.parseInt(codePoint, 10)))
  .replace(/&#x([0-9a-f]+);/gi, (_, codePoint) =>
    String.fromCodePoint(Number.parseInt(codePoint, 16)))
  .replace(/\[[^\]]*edit[^\]]*\]/gi, " ")
  .replace(/\s+/g, " ")
  .trim();

const index = await download(indexUrl);
const headerRows = [...index.text.matchAll(/<tr[\s\S]*?<\/tr>/g)]
  .map(match => match[0])
  .filter(row => row.includes("/cpp/header/"))
  .filter(row => !/\(C\+\+2[36]\)/i.test(decode(row)));
const headerMap = new Map();
for (const row of headerRows) {
  const match = row.match(/href="(\/cpp\/header\/([^"#]+))"/);
  if (!match) continue;
  const headerName = decode(match[2]).replaceAll(" ", "_");
  if (!/^[a-z][a-z0-9_]*$/i.test(headerName)) continue;
  headerMap.set(headerName, `https://en.cppreference.com${match[1]}`);
}
const headers = [...headerMap.entries()]
  .sort(([left], [right]) => left.localeCompare(right, "en"));

const downloadedHeaders = [];
let nextHeader = 0;
const workers = Array.from({ length: Math.min(6, headers.length) }, async () => {
  while (nextHeader < headers.length) {
    const index = nextHeader++;
    const [headerName, url] = headers[index];
    const result = await download(url);
    downloadedHeaders[index] = { headerName, url, ...result };
  }
});
await Promise.all(workers);

const symbolKind = (row) => {
  const value = decode(row).toLowerCase();
  if (value.includes("class template") || value.includes("class")) return "Class";
  if (value.includes("function template") || value.includes("function")) return "Function";
  if (value.includes("enumeration") || value.includes("enum")) return "Enum";
  if (value.includes("macro")) return "Macro";
  if (value.includes("variable")) return "Constant";
  if (value.includes("concept")) return "Other";
  return "Other";
};

const entries = [];
const canonicalLinks = new Set();
const sourceHashes = { "header-index": index.sha256 };
for (const header of downloadedHeaders) {
  sourceHashes[`<${header.headerName}>`] = header.sha256;
  const contentStart = header.text.indexOf("id=\"mw-content-text\"");
  const content = contentStart >= 0
    ? header.text.slice(contentStart)
    : header.text;
  for (const match of content.matchAll(/<tr class="t-dsc"[\s\S]*?<\/tr>/g)) {
    const row = match[0];
    if (/\(C\+\+2[36]\)/i.test(decode(row))) continue;
    const link = row.match(/href="(\/cpp\/(?!language|header)[^"#]+)"/);
    if (!link || canonicalLinks.has(link[1])) continue;
    canonicalLinks.add(link[1]);
    const cells = [...row.matchAll(/<td[^>]*>([\s\S]*?)(?=<td|<\/tr>)/g)]
      .map(cell => cell[1]);
    const name = decode(cells[0]);
    if (!name || name.length > 240) continue;
    const kind = symbolKind(row);
    const key = [header.headerName, "std", kind, name, link[1]].join("\u001f");
    const suffix = createHash("sha256").update(key).digest("hex").slice(0, 12);
    const slug = name.toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-|-$/g, "")
      .slice(0, 64) || "symbol";
    entries.push({
      id: `cpp.stdlib.${slug}.${suffix}`,
      header: `<${header.headerName}>`,
      namespace: kind === "Macro" ? "global" : "std",
      name,
      displayName: name,
      symbolKind: kind,
      signature: name,
      description: decode(cells[1]) || `${name} is declared by <${header.headerName}>.`,
      documentationUrl: `https://en.cppreference.com${link[1]}`
    });
  }
}

entries.sort((left, right) =>
  left.header.localeCompare(right.header, "en")
  || left.namespace.localeCompare(right.namespace, "en")
  || left.name.localeCompare(right.name, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion: "cppreference C++20 header inventory",
  sourceUrl: indexUrl,
  sourceHashes,
  sourceSha256: createHash("sha256")
    .update(Object.entries(sourceHashes)
      .sort(([left], [right]) => left.localeCompare(right, "en"))
      .map(([name, hash]) => `${name}\u001f${hash}`)
      .join("\n"))
    .digest("hex"),
  license: "CC-BY-SA-3.0 inventory; ISO C++ remains the canonical specification",
  headerCount: headers.length,
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${headers.length}|${entries.length}|${outputPath}`);
