import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import concurrency from "./csharp/modules/concurrency.mjs";
import language from "./csharp/modules/language.mjs";
import networking from "./csharp/modules/networking.mjs";
import runtime from "./csharp/modules/runtime.mjs";
import streams from "./csharp/modules/streams.mjs";

const modules = [
  language,
  streams,
  runtime,
  concurrency,
  networking,
];

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const outputDirectory = path.resolve(
  scriptDirectory,
  "..",
  "src",
  "Hypershell",
  "Assets",
  "CommandLibraries",
);

fs.mkdirSync(outputDirectory, { recursive: true });
for (const library of modules) {
  const outputPath = path.join(outputDirectory, `${library.id}.json`);
  fs.writeFileSync(
    outputPath,
    `${JSON.stringify(library, null, 2)}\n`,
    "utf8",
  );
  console.log(
    `Generated ${library.commands.length} commands in ${library.id}.`,
  );
}

const total = modules.reduce(
  (sum, library) => sum + library.commands.length,
  0,
);
console.log(
  `Generated ${modules.length} C# starter modules with ${total} commands.`,
);
