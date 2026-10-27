import fs from "node:fs";
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

const errors = [];
const warnings = [];
const libraries = [];
const commandOwners = new Map();

const fail = (location, message) => errors.push(`${location}: ${message}`);
const warn = (location, message) => warnings.push(`${location}: ${message}`);
const nonEmpty = (value) => typeof value === "string" && value.trim().length > 0;

for (const fileName of fs
  .readdirSync(libraryDirectory)
  .filter((candidate) => candidate.toLowerCase().endsWith(".json"))
  .sort((left, right) => left.localeCompare(right))) {
  const filePath = path.join(libraryDirectory, fileName);
  let library;
  try {
    library = JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch (error) {
    fail(fileName, `invalid JSON (${error.message})`);
    continue;
  }

  const libraryLocation = `${fileName}`;
  libraries.push({ fileName, library });
  if (library.schemaVersion !== 1) {
    fail(libraryLocation, "schemaVersion must be 1");
  }
  if (!/^\d+\.\d+\.\d+$/.test(library.contentVersion ?? "")) {
    fail(libraryLocation, "contentVersion must use major.minor.patch");
  }
  if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(library.id ?? "")) {
    fail(libraryLocation, "id must be a non-empty lowercase kebab-case value");
  }
  if (!nonEmpty(library.displayName)) {
    fail(libraryLocation, "displayName is required");
  }
  if (!nonEmpty(library.description)) {
    fail(libraryLocation, "description is required");
  }
  if (library.language !== "C#") {
    fail(libraryLocation, "language must be C#");
  }
  if (!["Language", "Extension", "Module", "Package"].includes(
    library.kind,
  )) {
    fail(
      libraryLocation,
      "kind must describe the C# surface instead of relying on a topic category",
    );
  }
  if (library.kind === "Language") {
    if (nonEmpty(library.parentLibraryId)) {
      fail(
        libraryLocation,
        "a language surface must not be nested beneath a framework library",
      );
    }
  } else if (library.parentLibraryId !== "csharp-dotnet-8") {
    fail(
      libraryLocation,
      "bundled .NET 8 content must declare csharp-dotnet-8 as parentLibraryId",
    );
  }
  if (library.frameworkName !== ".NET") {
    fail(libraryLocation, "frameworkName must be .NET");
  }
  if (library.targetFramework !== "net8.0") {
    fail(libraryLocation, "bundled C# content must target net8.0");
  }
  if (library.isExecutionSupported !== true) {
    fail(libraryLocation, "bundled C# content must be executable by the current runner");
  }
  if (!nonEmpty(library.executionSupportMessage)) {
    fail(libraryLocation, "executionSupportMessage is required");
  }
  if (library.executionTarget !== "CSharp") {
    fail(libraryLocation, "executionTarget must be CSharp");
  }
  if (library.isStarterLibrary !== true) {
    warn(libraryLocation, "bundled C# library is not marked as a starter library");
  }
  if (!Array.isArray(library.commands) || library.commands.length === 0) {
    fail(libraryLocation, "commands must be a non-empty array");
    continue;
  }

  const actionDescriptions = library.actionDescriptions ?? {};
  const objectDescriptions = library.objectDescriptions ?? {};
  const localNames = new Set();
  for (const [index, command] of library.commands.entries()) {
    const name = command.name ?? `<command-${index}>`;
    const location = `${fileName} > ${name}`;
    const normalizedName = name.toLowerCase();
    if (localNames.has(normalizedName)) {
      fail(location, "duplicate command name inside this library");
    }
    localNames.add(normalizedName);
    if (commandOwners.has(normalizedName)) {
      fail(
        location,
        `command name is already owned by ${commandOwners.get(normalizedName)}`,
      );
    } else {
      commandOwners.set(normalizedName, library.id);
    }

    for (const property of [
      "name",
      "displayName",
      "description",
      "definition",
      "moduleName",
      "action",
      "object",
      "template",
      "documentationUrl",
    ]) {
      if (!nonEmpty(command[property])) {
        fail(location, `${property} is required`);
      }
    }
    for (const property of ["memberName", "memberKind"]) {
      if (!nonEmpty(command[property])) {
        fail(location, `${property} is required for C# navigation`);
      }
    }
    if (command.memberKind !== "Language pattern") {
      for (const property of ["namespaceName", "typeName"]) {
        if (!nonEmpty(command[property])) {
          fail(location, `${property} is required for .NET API navigation`);
        }
      }
      if (!/^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$/.test(
        command.namespaceName ?? "",
      )) {
        fail(location, "namespaceName must be a dotted C# namespace path");
      }
    }
    if (!/^https:\/\/learn\.microsoft\.com\/dotnet\//i.test(
      command.documentationUrl ?? "",
    )) {
      fail(location, "documentationUrl must point to official .NET documentation");
    }
    if (!nonEmpty(actionDescriptions[command.action])) {
      fail(location, `missing action description for ${command.action}`);
    }
    if (!nonEmpty(objectDescriptions[command.object])) {
      fail(location, `missing object description for ${command.object}`);
    }
    if (!Array.isArray(command.examples) || command.examples.length === 0) {
      fail(location, "at least one example is required");
    } else {
      for (const [exampleIndex, usage] of command.examples.entries()) {
        const exampleLocation = `${location} > example ${exampleIndex + 1}`;
        if (!nonEmpty(usage.title)) {
          fail(exampleLocation, "title is required");
        }
        if (!nonEmpty(usage.code)) {
          fail(exampleLocation, "code is required");
        }
        if (!nonEmpty(usage.explanation)) {
          fail(exampleLocation, "explanation is required");
        }
      }
    }

    const parameters = Array.isArray(command.parameters)
      ? command.parameters
      : [];
    const placeholders = new Set();
    for (const [parameterIndex, parameter] of parameters.entries()) {
      const parameterLocation =
        `${location} > parameter ${parameter.name ?? parameterIndex + 1}`;
      for (const property of [
        "name",
        "displayName",
        "placeholder",
        "description",
        "typeName",
      ]) {
        if (!nonEmpty(parameter[property])) {
          fail(parameterLocation, `${property} is required`);
        }
      }
      if (placeholders.has(parameter.placeholder)) {
        fail(parameterLocation, "placeholder must be unique within the command");
      }
      placeholders.add(parameter.placeholder);
      if (!command.template.includes(`{${parameter.placeholder}}`)) {
        fail(
          parameterLocation,
          `template does not contain {${parameter.placeholder}}`,
        );
      }
    }

    const templatePlaceholders = [
      ...command.template.matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/g),
    ].map((match) => match[1]);
    for (const placeholder of new Set(templatePlaceholders)) {
      if (!placeholders.has(placeholder)) {
        fail(location, `template placeholder {${placeholder}} is undefined`);
      }
    }
  }
}

const libraryIds = new Set();
for (const { fileName, library } of libraries) {
  const normalizedId = (library.id ?? "").toLowerCase();
  if (libraryIds.has(normalizedId)) {
    fail(fileName, `duplicate library id ${library.id}`);
  }
  libraryIds.add(normalizedId);
}

for (const warning of warnings) {
  console.warn(`WARNING ${warning}`);
}
for (const error of errors) {
  console.error(`ERROR ${error}`);
}

const commandCount = libraries.reduce(
  (sum, entry) => sum + (entry.library.commands?.length ?? 0),
  0,
);
console.log(
  `Validated ${libraries.length} C# libraries and ${commandCount} commands with ${warnings.length} warning(s) and ${errors.length} error(s).`,
);

if (errors.length > 0) {
  process.exitCode = 1;
}
