export const actionDescriptions = {
  Access:
    "Retrieves a value exposed through a language or runtime feature without intentionally changing it.",
  Add:
    "Adds a value, item, header, or contribution to an existing object.",
  Allocate:
    "Obtains reusable memory or another finite runtime resource for temporary work.",
  Build:
    "Constructs a value or operation from smaller pieces.",
  Cancel:
    "Requests that cooperative asynchronous or concurrent work stop.",
  Compare:
    "Compares values and reports their ordering or equality relationship.",
  Compress:
    "Encodes data into a smaller representation using a compression algorithm.",
  Convert:
    "Changes a value into another representation or data type.",
  Copy:
    "Copies bytes, values, or resources to another destination.",
  Create:
    "Constructs a new object, resource, or language structure.",
  Decode:
    "Converts an encoded representation back into its original form.",
  Decompress:
    "Restores compressed data to its uncompressed representation.",
  Encode:
    "Converts data into a representation suitable for storage or transport.",
  Enumerate:
    "Traverses a sequence of values or members one item at a time.",
  Execute:
    "Runs a language construct, delegate, or operation.",
  Find:
    "Searches for a value, member, address, or matching item.",
  Format:
    "Creates a particular textual or binary representation of a value.",
  Get:
    "Retrieves information without intentionally changing the source.",
  Hash:
    "Calculates a fixed-size digest from input data.",
  Inspect:
    "Examines runtime metadata, state, or structure.",
  Parse:
    "Interprets text or bytes as a structured value.",
  Protect:
    "Applies a security-sensitive transformation or comparison.",
  Read:
    "Reads text, bytes, messages, or values from a source.",
  Release:
    "Returns or frees a resource previously acquired by the program.",
  Resolve:
    "Translates a name or identifier into a concrete runtime or network value.",
  Run:
    "Starts work and waits for or observes its outcome.",
  Select:
    "Chooses one branch, value, or subset from the available alternatives.",
  Send:
    "Transmits a request, message, or bytes to another component.",
  Slice:
    "Creates a bounded view over part of an existing value or memory region.",
  Synchronize:
    "Coordinates access or progress across concurrent operations.",
  Test:
    "Evaluates a condition and returns whether it is satisfied.",
  Transform:
    "Produces a new value by applying a language or library operation.",
  Wait:
    "Asynchronously or synchronously pauses until a condition is met.",
  Write:
    "Writes text, bytes, messages, or values to a destination.",
};

export const parameter = (
  name,
  placeholder,
  description,
  typeName = "string",
  isRequired = true,
) => ({
  name,
  displayName: name,
  placeholder,
  description,
  typeName,
  isRequired,
});

export const example = (title, code, explanation) => ({
  title,
  code,
  explanation,
});

export const csharpCoordinates = (
  name,
  moduleName,
  template,
  overrides = {},
) => {
  const memberToken = (name.split(".").at(-1) ?? name)
    .replace(/<.*$/, "")
    .trim();
  if (moduleName.toLowerCase().startsWith("c# language")) {
    return {
      namespaceName: "",
      typeName: "",
      memberName: overrides.memberName ?? memberToken,
      memberKind: overrides.memberKind ?? "Language pattern",
      overloadSignature: overrides.overloadSignature ?? "",
    };
  }

  const typeToken = name.includes(".")
    ? name.slice(0, name.lastIndexOf(".")).trim()
    : "";
  const moduleSeparator = moduleName.lastIndexOf(".");
  const moduleTail = moduleSeparator >= 0
    ? moduleName.slice(moduleSeparator + 1)
    : moduleName;
  const removeGenericSuffix = (value) => value.replace(/<.*$/, "").trim();
  const moduleNamesType = typeToken
    && removeGenericSuffix(moduleTail).toLowerCase()
      === removeGenericSuffix(typeToken).toLowerCase();

  return {
    namespaceName: overrides.namespaceName
      ?? (moduleNamesType
        ? moduleName.slice(0, moduleSeparator)
        : moduleName),
    typeName: overrides.typeName
      ?? (moduleNamesType ? moduleTail : typeToken),
    memberName: overrides.memberName ?? memberToken,
    memberKind: overrides.memberKind
      ?? (template.includes("new ") ? "Constructor" : "Method"),
    overloadSignature: overrides.overloadSignature ?? "",
  };
};

export const command = ({
  name,
  description,
  definition,
  moduleName,
  action,
  object,
  template,
  documentationUrl,
  parameters = [],
  examples,
  keywords = [],
  namespaceName,
  typeName,
  memberName,
  memberKind,
  overloadSignature,
}) => ({
  name,
  displayName: name,
  description,
  definition,
  moduleName,
  action,
  object,
  ...csharpCoordinates(name, moduleName, template, {
    namespaceName,
    typeName,
    memberName,
    memberKind,
    overloadSignature,
  }),
  template,
  documentationUrl,
  parameters,
  examples,
  keywords,
});

export const createLibrary = ({
  id,
  displayName,
  description,
  category,
  objectDescriptions,
  commands,
  keywords = [],
  contentVersion = "1.0.0",
  kind = "Extension",
  parentLibraryId = "csharp-dotnet-8",
}) => {
  const usedActions = new Set(commands.map((entry) => entry.action));
  return {
    schemaVersion: 1,
    contentVersion,
    id,
    displayName,
    description,
    language: "C#",
    category,
    kind,
    parentLibraryId,
    frameworkName: ".NET",
    frameworkVersion: "8",
    targetFramework: "net8.0",
    isExecutionSupported: true,
    executionSupportMessage:
      "Compatible with the built-in .NET 10 C# runner; authored against .NET 8.",
    isStarterLibrary: true,
    createStandaloneProfile: false,
    keywords: ["csharp", "dotnet", "net8", ...keywords],
    executionTarget: "CSharp",
    actionDescriptions: Object.fromEntries(
      [...usedActions]
        .sort((left, right) => left.localeCompare(right))
        .map((action) => [
          action,
          actionDescriptions[action]
            ?? `${action} applies the selected operation to the target object.`,
        ]),
    ),
    objectDescriptions,
    commands,
  };
};
