import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const sourceUrl =
  "https://docs.oracle.com/javase/specs/jls/se25/html/jls-3.html";
const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "java-se-25-language.json");

const response = await fetch(sourceUrl, {
  headers: {
    "User-Agent": "HyperlibCatalogImporter/1.0"
  }
});
if (!response.ok) {
  throw new Error(`Unable to download ${sourceUrl}: HTTP ${response.status}.`);
}
const bytes = Buffer.from(await response.arrayBuffer());
const document = bytes.toString("utf8");

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

const productionTokens = (anchor, nextAnchor) => {
  const start = document.indexOf(`<a name="${anchor}"`);
  const end = document.indexOf(`<a name="${nextAnchor}"`, start + 1);
  if (start < 0 || end < 0) {
    throw new Error(`Unable to locate ${anchor} in the Java language specification.`);
  }
  const fragment = document.slice(start, end);
  return [...fragment.matchAll(/<code class="literal">([\s\S]*?)<\/code>/g)]
    .flatMap(match => decode(match[1]).split(/\s+/))
    .filter(Boolean);
};

const reservedKeywords = productionTokens(
  "jls-ReservedKeyword",
  "jls-ContextualKeyword");
const contextualStart = document.indexOf('<a name="jls-ContextualKeyword"');
const contextualEnd = document.indexOf(
  '<p class="note">The keywords',
  contextualStart);
const contextualKeywords = [...document
  .slice(contextualStart, contextualEnd)
  .matchAll(/<code class="literal">([\s\S]*?)<\/code>/g)]
  .flatMap(match => decode(match[1]).split(/\s+/))
  .filter(Boolean);

const operatorStart = document.indexOf('<a name="jls-Operator"');
const operatorEnd = document.indexOf("</pre>", operatorStart);
const operators = decode(document.slice(operatorStart, operatorEnd))
  .replace(/^.*?\(one of\)\s*/i, "")
  .split(/\s+/)
  .filter(Boolean);

const groups = [
  ["Reserved Keywords", "Keyword", reservedKeywords, "#jls-3.9"],
  ["Contextual Keywords", "Keyword", contextualKeywords, "#jls-3.9"],
  ["Literals", "Literal", ["true", "false", "null"], "#jls-3.10"],
  ["Operators", "Operator", operators, "#jls-3.12"]
];
const entries = groups.flatMap(([category, symbolKind, names, anchor]) =>
  [...new Set(names)].map(name => {
    const suffix = createHash("sha256")
      .update([category, symbolKind, name].join("\u001f"))
      .digest("hex")
      .slice(0, 12);
    return {
      id: `java.core.${symbolKind.toLowerCase()}.${suffix}`,
      category,
      name,
      displayName: name,
      symbolKind,
      signature: name,
      description: `${name} is a Java SE 25 ${category.toLowerCase().replace(/s$/, "")}.`,
      documentationUrl: `${sourceUrl}${anchor}`
    };
  }));
entries.sort((left, right) =>
  left.category.localeCompare(right.category, "en")
  || left.name.localeCompare(right.name, "en"));

const inventory = {
  schemaVersion: 1,
  sourceVersion: "Java Language Specification SE 25 lexical inventory",
  sourceUrl,
  sourceSha256: createHash("sha256").update(bytes).digest("hex"),
  license: "Oracle documentation terms",
  entryCount: entries.length,
  entries
};

await mkdir(path.dirname(outputPath), { recursive: true });
await writeFile(outputPath, `${JSON.stringify(inventory, null, 2)}\n`, "utf8");
console.log(`${entries.length}|${outputPath}`);
