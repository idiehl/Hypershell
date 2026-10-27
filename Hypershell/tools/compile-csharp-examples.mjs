import childProcess from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const libraryDirectory = path.resolve(
  scriptDirectory,
  "..",
  "src",
  "Hypershell",
  "Assets",
  "CommandLibraries",
);
const temporaryRoot = fs.mkdtempSync(
  path.join(os.tmpdir(), "Hypershell-csharp-examples-"),
);

const globalUsings = [
  "System",
  "System.Buffers",
  "System.Collections.Concurrent",
  "System.Collections.Generic",
  "System.Diagnostics",
  "System.Globalization",
  "System.IO",
  "System.IO.Compression",
  "System.Linq",
  "System.Net",
  "System.Net.Http",
  "System.Net.Http.Headers",
  "System.Net.Http.Json",
  "System.Net.Sockets",
  "System.Reflection",
  "System.Runtime.InteropServices",
  "System.Security.Cryptography",
  "System.Text",
  "System.Text.Json",
  "System.Text.RegularExpressions",
  "System.Threading",
  "System.Threading.Channels",
  "System.Threading.Tasks",
];

const project = `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
</Project>
`;

const extractTrailingTypeDeclarations = (code) => {
  const bodyLines = [];
  const typeLines = [];
  for (const line of code.split(/\r?\n/)) {
    if (/^(?:(?:public|internal|private|protected|sealed|abstract|static|readonly|partial)\s+)*(?:record|class|struct|enum|interface)\s/.test(
      line.trimStart(),
    )) {
      typeLines.push(line.trim());
    } else {
      bodyLines.push(line);
    }
  }
  return {
    body: bodyLines.join("\n"),
    trailingTypes: typeLines.join("\n"),
  };
};

try {
  fs.writeFileSync(
    path.join(temporaryRoot, "CatalogExamples.csproj"),
    project,
    "utf8",
  );

  let exampleCount = 0;
  for (const fileName of fs
    .readdirSync(libraryDirectory)
    .filter((candidate) => candidate.toLowerCase().endsWith(".json"))
    .sort((left, right) => left.localeCompare(right))) {
    const library = JSON.parse(
      fs.readFileSync(path.join(libraryDirectory, fileName), "utf8"),
    );
    for (const command of library.commands ?? []) {
      for (const usage of command.examples ?? []) {
        const className = `Example${String(exampleCount + 1).padStart(4, "0")}`;
        const { body, trailingTypes } = extractTrailingTypeDeclarations(
          usage.code,
        );
        const usesAwait = /\bawait\b/.test(body);
        const returnType = usesAwait ? "async Task" : "void";
        const methodName = usesAwait ? "RunAsync" : "Run";
        const source = `#nullable enable
${globalUsings.map((namespaceName) => `global using ${namespaceName};`).join("\n")}

namespace Hypershell.CatalogExampleVerification;

// ${library.id} · ${command.name} · ${usage.title}
internal static class ${className}
{
    public static ${returnType} ${methodName}()
    {
${body.split("\n").map((line) => `        ${line}`).join("\n")}
    }
}

${trailingTypes}
`;
        fs.writeFileSync(
          path.join(temporaryRoot, `${className}.cs`),
          source,
          "utf8",
        );
        exampleCount++;
      }
    }
  }

  const build = childProcess.spawnSync(
    "dotnet",
    [
      "build",
      path.join(temporaryRoot, "CatalogExamples.csproj"),
      "--configuration",
      "Release",
      "--nologo",
      "--verbosity",
      "minimal",
      "--disable-build-servers",
    ],
    {
      cwd: temporaryRoot,
      encoding: "utf8",
      windowsHide: true,
    },
  );
  if (build.stdout) {
    process.stdout.write(build.stdout);
  }
  if (build.stderr) {
    process.stderr.write(build.stderr);
  }
  if (build.status !== 0) {
    process.exitCode = build.status ?? 1;
  } else {
    console.log(`Compiled ${exampleCount} C# catalog examples.`);
  }
} finally {
  const resolvedTemporaryRoot = path.resolve(temporaryRoot);
  const expectedPrefix = path.resolve(os.tmpdir(), "Hypershell-csharp-examples-");
  if (resolvedTemporaryRoot.startsWith(expectedPrefix)) {
    fs.rmSync(resolvedTemporaryRoot, { recursive: true, force: true });
  }
}
