import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const sources = [
  {
    category: "Language Reference",
    url: "https://en.cppreference.com/w/cpp/language",
    linkPattern: /href="(\/cpp\/language\/([^"#?]+))"[^>]*>([\s\S]*?)<\/a>/g
  },
  {
    category: "Keywords",
    url: "https://en.cppreference.com/w/cpp/keyword",
    linkPattern: /href="(\/cpp\/keyword\/([^"#?]+))"[^>]*>([\s\S]*?)<\/a>/g
  }
];
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "cpp20-language-index.json");

const download = async (url) => {
  const response = await fetch(url, {
    headers: {
      "User-Agent": "Mozilla/5.0 HyperlibCatalogImporter/1.0"
    }
  });
  if (!response.ok) {
    throw new Error(`Unable to download ${url}: HTTP ${response.status}.`);
  }
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
  .replace(/\s+/g, " ")
  .trim();

const inferSymbolKind = (category, slug, name) => {
  if (category === "Keywords") return "Keyword";
  if (/literal|escape|character_set/.test(slug)) return "Literal";
  if (/operator|conversion|overload|precedence/.test(slug)) return "Operator";
  if (/^(if|switch|for|range-for|while|do|continue|break|goto|return|try|catch|throw)$/.test(slug)) {
    return "Statement";
  }
  if (/function|lambda/.test(slug)) return "Function";
  if (/enum/.test(slug)) return "Enum";
  if (/class|union/.test(slug)) return "Class";
  if (/declaration|definition/.test(name.toLowerCase())) return "Statement";
  return "Other";
};

const entries = [];
const sourceHashes = {};
for (const source of sources) {
  const document = await download(source.url);
  sourceHashes[source.url] = document.sha256;
  const uniqueLinks = new Map();
  for (const match of document.text.matchAll(source.linkPattern)) {
    const end = match.index + match[0].length;
    const trailingMarkup = document.text
      .slice(end, end + 300)
      .split(/<br\s*\/?\s*>|<\/li>|<\/td>/i)[0];
    if (/\(C\+\+2[36]\)|\bTS\b|t-mark-ts/i.test(trailingMarkup)) {
      continue;
    }
    const displayName = decode(match[3]);
    if (!displayName || displayName.length > 240) continue;
    uniqueLinks.set(match[1], {
      slug: match[2],
      displayName
    });
  }

  for (const [relativeUrl, item] of uniqueLinks) {
    const symbolKind = inferSymbolKind(
      source.category,
      item.slug,
      item.displayName);
    const identity = [source.category, item.slug, symbolKind].join("\u001f");
    const suffix = createHash("sha256")
      .update(identity)
      .digest("hex")
      .slice(0, 12);
    entries.push({
      id: `cpp.core.${source.category === "Keywords" ? "keyword" : "language"}.${item.slug.replaceAll("/", ".")}.${suffix}`,
      category: source.category,
      name: item.displayName,
      displayName: item.displayName,
      symbolKind,
      signature: item.displayName,
      description: `${item.displayName} is part of the C++20 ${source.category.toLowerCase()} surface.`,
      documentationUrl: `https://en.cppreference.com${relativeUrl}`
    });
  }
}

entries.sort((left, right) =>
  left.category.localeCompare(right.category, "en")
  || left.name.localeCompare(right.name, "en")
  || left.documentationUrl.localeCompare(right.documentationUrl, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion: "cppreference C++20 language and keyword inventory",
  sourceUrl: sources[0].url,
  sourceHashes,
  sourceSha256: createHash("sha256")
    .update(Object.entries(sourceHashes)
      .sort(([left], [right]) => left.localeCompare(right, "en"))
      .map(([name, hash]) => `${name}\u001f${hash}`)
      .join("\n"))
    .digest("hex"),
  license: "CC-BY-SA-3.0 inventory; ISO C++ remains the canonical specification",
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${outputPath}`);
