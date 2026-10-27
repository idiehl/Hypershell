import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { pythonGuideExpansion } from "./catalog-sources/python-guide-expansion.mjs";
import { javascriptNodeGuideExpansion } from "./catalog-sources/javascript-node-guide-expansion.mjs";
import { typescriptGuideExpansion } from "./catalog-sources/typescript-guide-expansion.mjs";

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..");
const outputDirectory = path.join(
  repositoryRoot,
  "src",
  "Hypershell",
  "Assets",
  "CommandLibraries");
const pythonInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "python-3.14-objects.json");
const pythonInventory = JSON.parse(
  await readFile(pythonInventoryPath, "utf8"));
const nodeInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "node-24.14.0-api.json");
const nodeInventory = JSON.parse(
  await readFile(nodeInventoryPath, "utf8"));
const javaInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "java-se-25-api.json");
const javaInventory = JSON.parse(
  await readFile(javaInventoryPath, "utf8"));
const javaLanguageInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "java-se-25-language.json");
const javaLanguageInventory = JSON.parse(
  await readFile(javaLanguageInventoryPath, "utf8"));
const ecmascriptInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "ecmascript-2024-declarations.json");
const ecmascriptInventory = JSON.parse(
  await readFile(ecmascriptInventoryPath, "utf8"));
const webInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "web-platform-declarations.json");
const webInventory = JSON.parse(
  await readFile(webInventoryPath, "utf8"));
const cssInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "css-properties-2.31.0.json");
const cssInventory = JSON.parse(
  await readFile(cssInventoryPath, "utf8"));
const htmlInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "html-standard-index.json");
const htmlInventory = JSON.parse(
  await readFile(htmlInventoryPath, "utf8"));
const typeScriptCompilerInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "typescript-6.0.3-compiler-api.json");
const typeScriptCompilerInventory = JSON.parse(
  await readFile(typeScriptCompilerInventoryPath, "utf8"));
const csharpKeywordCatalogPath = path.join(
  repositoryRoot,
  "src",
  "Hypershell",
  "Assets",
  "CSharp",
  "keywords.json");
const csharpKeywordCatalog = JSON.parse(
  await readFile(csharpKeywordCatalogPath, "utf8"));
const cppInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "cpp20-standard-library-index.json");
const cppInventory = JSON.parse(
  await readFile(cppInventoryPath, "utf8"));
const cppLanguageInventoryPath = path.join(
  repositoryRoot,
  "tools",
  "catalog-sources",
  "cpp20-language-index.json");
const cppLanguageInventory = JSON.parse(
  await readFile(cppLanguageInventoryPath, "utf8"));

const parameter = (
  name,
  displayName,
  description,
  defaultValue,
  typeName = "Expression") => ({
  name,
  displayName,
  placeholder: name,
  description,
  typeName,
  defaultValue,
  isRequired: true,
  isSwitch: false,
  includeByDefault: true
});

const entry = ({
  id,
  name,
  displayName,
  description,
  definition,
  moduleName,
  action,
  object,
  documentationUrl,
  keywords,
  template,
  parameters = [],
  exampleTitle,
  exampleCode,
  exampleExplanation,
  shell
}) => ({
  id,
  name,
  displayName,
  description,
  definition,
  moduleName,
  commandType: "Language pattern",
  preferredShellKind: shell,
  action,
  object,
  documentationUrl,
  keywords,
  template,
  compositionMode: "Statement",
  parameters,
  examples: [
    {
      title: exampleTitle,
      code: exampleCode,
      explanation: exampleExplanation
    }
  ]
});

const library = ({
  id,
  displayName,
  description,
  language,
  keywords,
  isExecutionSupported = true,
  executionSupportMessage =
    "Runs through the matching installed Hypershell terminal adapter.",
  category = "Official starter pack",
  actionDescriptions,
  objectDescriptions,
  commands
}) => ({
  schemaVersion: 1,
  contentVersion: "1.0.0",
  id,
  displayName,
  description,
  language,
  category,
  kind: "Language",
  isExecutionSupported,
  executionSupportMessage,
  isStarterLibrary: true,
  createStandaloneProfile: true,
  keywords,
  executionTarget: "External",
  actionDescriptions,
  objectDescriptions,
  commands
});

const python = library({
  id: "python-essentials",
  displayName: "Python Essentials",
  description:
    "Official starter patterns for Python syntax, collections, functions, files, JSON, networking, and error handling.",
  language: "Python",
  keywords: ["python", "standard library", "learning", "starter"],
  actionDescriptions: {
    Output: "Display or format a value.",
    Transform: "Create a new value by transforming existing data.",
    Iterate: "Repeat an operation over an iterable.",
    Define: "Declare reusable Python behavior or data structures.",
    Read: "Load content from a local or remote source.",
    Parse: "Convert serialized content into Python values.",
    Handle: "Respond to exceptional conditions explicitly."
  },
  objectDescriptions: {
    Value: "Any Python expression that produces a value.",
    String: "Python text represented by the str type.",
    List: "An ordered, mutable Python collection.",
    Loop: "A statement that visits values from an iterable.",
    Function: "A named reusable block declared with def.",
    "Data class": "A class whose common data methods are generated by dataclasses.",
    File: "A filesystem-backed source addressed through pathlib.",
    JSON: "Structured JavaScript Object Notation data.",
    Exception: "An error value raised while Python executes.",
    URL: "A network resource identified by a URL."
  },
  commands: [
    entry({
      id: "python.print",
      name: "print",
      displayName: "Print a value",
      description: "Writes a Python value to standard output.",
      definition:
        "print evaluates its arguments, converts them to text, separates them with sep, and writes the result followed by end.",
      moduleName: "Built-ins",
      action: "Output",
      object: "Value",
      documentationUrl: "https://docs.python.org/3/library/functions.html#print",
      keywords: ["stdout", "console", "display"],
      template: "print({value})",
      parameters: [
        parameter(
          "value",
          "Value",
          "A Python expression to display.",
          "\"Hello from Hypershell\"",
          "Python expression")
      ],
      exampleTitle: "Display a greeting",
      exampleCode: "print(\"Hello from Hypershell\")",
      exampleExplanation:
        "The string literal is evaluated and written to the Python terminal.",
      shell: "Python"
    }),
    entry({
      id: "python.f-string",
      name: "f-string",
      displayName: "Format an f-string",
      description: "Embeds an expression directly inside a Python string literal.",
      definition:
        "An f-string prefixes a string with f and evaluates expressions inside braces when the string is created.",
      moduleName: "Language",
      action: "Output",
      object: "String",
      documentationUrl: "https://docs.python.org/3/tutorial/inputoutput.html#formatted-string-literals",
      keywords: ["format", "interpolation", "literal"],
      template: "print(f\"{label}: {{ {expression} }}\")",
      parameters: [
        parameter("label", "Label", "Text placed before the expression.", "Result", "Text without quotes"),
        parameter("expression", "Expression", "A Python expression evaluated inside the f-string.", "6 * 7", "Python expression")
      ],
      exampleTitle: "Embed a calculation",
      exampleCode: "print(f\"Result: {6 * 7}\")",
      exampleExplanation:
        "Python evaluates the expression between braces and inserts 42 into the string.",
      shell: "Python"
    }),
    entry({
      id: "python.list-comprehension",
      name: "list comprehension",
      displayName: "Build a list comprehension",
      description: "Transforms every item from an iterable into a new list.",
      definition:
        "A list comprehension combines an output expression with a for clause and optional filters inside square brackets.",
      moduleName: "Language",
      action: "Transform",
      object: "List",
      documentationUrl: "https://docs.python.org/3/tutorial/datastructures.html#list-comprehensions",
      keywords: ["collection", "map", "expression"],
      template: "result = [{expression} for {item} in {iterable}]\nprint(result)",
      parameters: [
        parameter("expression", "Output expression", "The value produced for each input item.", "number * number", "Python expression"),
        parameter("item", "Item name", "The loop variable used by the expression.", "number", "Identifier"),
        parameter("iterable", "Iterable", "The values to transform.", "range(1, 6)", "Python iterable")
      ],
      exampleTitle: "Square a range",
      exampleCode: "squares = [number * number for number in range(1, 6)]\nprint(squares)",
      exampleExplanation:
        "The comprehension visits numbers 1 through 5 and stores each square in a new list.",
      shell: "Python"
    }),
    entry({
      id: "python.enumerate-loop",
      name: "enumerate loop",
      displayName: "Loop with an index",
      description: "Visits iterable values together with a generated numeric index.",
      definition:
        "enumerate returns pairs containing a counter and each value from an iterable, avoiding a manually managed index variable.",
      moduleName: "Built-ins",
      action: "Iterate",
      object: "Loop",
      documentationUrl: "https://docs.python.org/3/library/functions.html#enumerate",
      keywords: ["for", "index", "iterable"],
      template: "for {index}, {item} in enumerate({iterable}, start={start}):\n    print({index}, {item})",
      parameters: [
        parameter("index", "Index name", "The variable that receives the counter.", "index", "Identifier"),
        parameter("item", "Item name", "The variable that receives each value.", "name", "Identifier"),
        parameter("iterable", "Iterable", "The values to visit.", "[\"Ada\", \"Grace\", \"Linus\"]", "Python iterable"),
        parameter("start", "Starting index", "The first counter value.", "1", "Integer expression")
      ],
      exampleTitle: "Number a list",
      exampleCode: "for index, name in enumerate([\"Ada\", \"Grace\", \"Linus\"], start=1):\n    print(index, name)",
      exampleExplanation:
        "enumerate supplies a one-based counter alongside each name without mutating the list.",
      shell: "Python"
    }),
    entry({
      id: "python.function",
      name: "function definition",
      displayName: "Define a function",
      description: "Declares reusable behavior and immediately demonstrates a call.",
      definition:
        "A def statement binds a function object to a name. Its indented body runs each time the function is called.",
      moduleName: "Language",
      action: "Define",
      object: "Function",
      documentationUrl: "https://docs.python.org/3/tutorial/controlflow.html#defining-functions",
      keywords: ["def", "return", "call"],
      template: "def {name}({argument}):\n    return {expression}\n\nprint({name}({sample}))",
      parameters: [
        parameter("name", "Function name", "The identifier bound to the function.", "double", "Identifier"),
        parameter("argument", "Argument name", "The parameter accepted by the function.", "value", "Identifier"),
        parameter("expression", "Return expression", "The expression returned by the function.", "value * 2", "Python expression"),
        parameter("sample", "Sample input", "A value used to call the function.", "21", "Python expression")
      ],
      exampleTitle: "Define and call double",
      exampleCode: "def double(value):\n    return value * 2\n\nprint(double(21))",
      exampleExplanation:
        "The call passes 21 to value; the function returns 42 and print displays it.",
      shell: "Python"
    }),
    entry({
      id: "python.dataclass",
      name: "dataclass",
      displayName: "Declare a data class",
      description: "Creates a typed data container with generated initialization and representation methods.",
      definition:
        "The dataclass decorator inspects annotated fields and generates common special methods such as __init__ and __repr__.",
      moduleName: "dataclasses",
      action: "Define",
      object: "Data class",
      documentationUrl: "https://docs.python.org/3/library/dataclasses.html",
      keywords: ["class", "decorator", "type hints"],
      template: "from dataclasses import dataclass\n\n@dataclass\nclass {name}:\n    {field}: {type}\n\nprint({name}({sample}))",
      parameters: [
        parameter("name", "Class name", "The data class identifier.", "Task", "Identifier"),
        parameter("field", "Field name", "The annotated instance field.", "title", "Identifier"),
        parameter("type", "Field type", "The field's type annotation.", "str", "Type expression"),
        parameter("sample", "Sample value", "A value passed to the generated initializer.", "\"Ship Hypershell\"", "Python expression")
      ],
      exampleTitle: "Create a Task value",
      exampleCode: "from dataclasses import dataclass\n\n@dataclass\nclass Task:\n    title: str\n\nprint(Task(\"Ship Hypershell\"))",
      exampleExplanation:
        "dataclass generates an initializer accepting title and a readable representation for print.",
      shell: "Python"
    }),
    entry({
      id: "python.pathlib-read-text",
      name: "Path.read_text",
      displayName: "Read a UTF-8 text file",
      description: "Reads an entire text file through pathlib using an explicit encoding.",
      definition:
        "Path.read_text opens the path in text mode, reads its contents, and closes the file. Supplying encoding makes decoding deterministic.",
      moduleName: "pathlib",
      action: "Read",
      object: "File",
      documentationUrl: "https://docs.python.org/3/library/pathlib.html#pathlib.Path.read_text",
      keywords: ["filesystem", "path", "utf-8"],
      template: "from pathlib import Path\ntext = Path({path}).read_text(encoding=\"utf-8\")\nprint(text)",
      parameters: [
        parameter("path", "Path", "A Python string or raw-string path literal.", "r\"README.md\"", "Path expression")
      ],
      exampleTitle: "Read a README",
      exampleCode: "from pathlib import Path\ntext = Path(r\"README.md\").read_text(encoding=\"utf-8\")\nprint(text)",
      exampleExplanation:
        "The raw string represents the path and pathlib handles opening and closing the file.",
      shell: "Python"
    }),
    entry({
      id: "python.json-loads",
      name: "json.loads",
      displayName: "Parse JSON text",
      description: "Converts a JSON string into Python dictionaries, lists, and scalar values.",
      definition:
        "json.loads deserializes a str, bytes, or bytearray containing one JSON document into corresponding Python values.",
      moduleName: "json",
      action: "Parse",
      object: "JSON",
      documentationUrl: "https://docs.python.org/3/library/json.html#json.loads",
      keywords: ["deserialize", "dictionary", "structured data"],
      template: "import json\ndata = json.loads({text})\nprint(data[{key}])",
      parameters: [
        parameter("text", "JSON text", "A Python string containing a JSON document.", "'{\"status\": \"ready\"}'", "String expression"),
        parameter("key", "Key", "A Python string key read from the parsed dictionary.", "\"status\"", "String expression")
      ],
      exampleTitle: "Read a JSON property",
      exampleCode: "import json\ndata = json.loads('{\"status\": \"ready\"}')\nprint(data[\"status\"])",
      exampleExplanation:
        "The JSON object becomes a Python dictionary, whose status value is then printed.",
      shell: "Python"
    }),
    entry({
      id: "python.try-except",
      name: "try except",
      displayName: "Handle an exception",
      description: "Runs an operation and handles one expected exception type.",
      definition:
        "A try statement executes its suite and transfers control to the first matching except clause when an exception is raised.",
      moduleName: "Language",
      action: "Handle",
      object: "Exception",
      documentationUrl: "https://docs.python.org/3/tutorial/errors.html#handling-exceptions",
      keywords: ["error", "exception", "recovery"],
      template: "try:\n    result = {expression}\nexcept {exception} as error:\n    print(f\"Unable to complete operation: {error}\")\nelse:\n    print(result)",
      parameters: [
        parameter("expression", "Expression", "The operation that may raise the expected exception.", "int(\"not-a-number\")", "Python expression"),
        parameter("exception", "Exception type", "The exception class to handle.", "ValueError", "Exception type")
      ],
      exampleTitle: "Handle invalid integer text",
      exampleCode: "try:\n    result = int(\"not-a-number\")\nexcept ValueError as error:\n    print(f\"Unable to complete operation: {error}\")\nelse:\n    print(result)",
      exampleExplanation:
        "int raises ValueError for invalid numeric text, and the matching handler reports it without terminating the session.",
      shell: "Python"
    }),
    entry({
      id: "python.urlopen",
      name: "urlopen",
      displayName: "Read text from a URL",
      description: "Performs a bounded standard-library HTTP request and decodes the response body.",
      definition:
        "urllib.request.urlopen opens a URL and returns a response object. A with statement guarantees that the response is closed.",
      moduleName: "urllib.request",
      action: "Read",
      object: "URL",
      documentationUrl: "https://docs.python.org/3/library/urllib.request.html#urllib.request.urlopen",
      keywords: ["http", "network", "response"],
      template: "from urllib.request import urlopen\nwith urlopen({url}, timeout={timeout}) as response:\n    text = response.read({limit}).decode(\"utf-8\", errors=\"replace\")\nprint(text)",
      parameters: [
        parameter("url", "URL", "A Python string containing an HTTP or HTTPS URL.", "\"https://example.com\"", "String expression"),
        parameter("timeout", "Timeout", "Maximum seconds to wait for the connection.", "10", "Number expression"),
        parameter("limit", "Byte limit", "Maximum response bytes read by this example.", "4096", "Integer expression")
      ],
      exampleTitle: "Read a small response",
      exampleCode: "from urllib.request import urlopen\nwith urlopen(\"https://example.com\", timeout=10) as response:\n    text = response.read(4096).decode(\"utf-8\", errors=\"replace\")\nprint(text)",
      exampleExplanation:
        "The response is closed automatically and the example limits how many bytes it reads into memory.",
      shell: "Python"
    })
  ]
});

const javascript = library({
  id: "javascript-essentials",
  displayName: "JavaScript Essentials",
  description:
    "Official starter patterns for modern JavaScript values, collections, functions, classes, promises, files, JSON, and HTTP through Node.js.",
  language: "JavaScript",
  keywords: ["javascript", "node.js", "ecmascript", "learning", "starter"],
  actionDescriptions: {
    Output: "Display or format a JavaScript value.",
    Define: "Declare values, functions, or classes.",
    Transform: "Create new values from existing data.",
    Select: "Keep values that match a condition.",
    Read: "Load content from a local or remote source.",
    Parse: "Convert serialized text into JavaScript values.",
    Coordinate: "Combine asynchronous operations.",
    Handle: "Respond to thrown errors explicitly."
  },
  objectDescriptions: {
    Value: "Any JavaScript expression that produces a value.",
    String: "Text represented by the JavaScript string primitive.",
    Constant: "A block-scoped binding declared with const.",
    Array: "An ordered JavaScript collection.",
    Object: "A collection of named properties.",
    Function: "Reusable callable JavaScript behavior.",
    Class: "A template for constructing related objects.",
    Promise: "A value representing eventual asynchronous completion.",
    File: "A filesystem-backed source read by Node.js.",
    JSON: "Structured JavaScript Object Notation data.",
    URL: "A network resource identified by a URL.",
    Error: "A thrown JavaScript failure value."
  },
  commands: [
    entry({
      id: "javascript.console-log",
      name: "console.log",
      displayName: "Log a value",
      description: "Writes a JavaScript value to standard output.",
      definition:
        "console.log formats its arguments and writes them to the process standard-output stream followed by a line break.",
      moduleName: "Console",
      action: "Output",
      object: "Value",
      documentationUrl: "https://nodejs.org/api/console.html#consolelogdata-args",
      keywords: ["stdout", "console", "debug"],
      template: "console.log({value});",
      parameters: [
        parameter("value", "Value", "A JavaScript expression to display.", "\"Hello from Hypershell\"", "JavaScript expression")
      ],
      exampleTitle: "Display a greeting",
      exampleCode: "console.log(\"Hello from Hypershell\");",
      exampleExplanation:
        "The string literal is evaluated and written to the Node.js terminal.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.template-literal",
      name: "template literal",
      displayName: "Format a template literal",
      description: "Embeds an expression inside a JavaScript string.",
      definition:
        "A template literal uses backticks and evaluates expressions inside dollar-brace placeholders when the string is created.",
      moduleName: "Language",
      action: "Output",
      object: "String",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Template_literals",
      keywords: ["string", "interpolation", "backtick"],
      template: "console.log(`{label}: ${ {expression} }`);",
      parameters: [
        parameter("label", "Label", "Text placed before the expression.", "Result", "Text without quotes"),
        parameter("expression", "Expression", "A JavaScript expression evaluated inside the template.", "6 * 7", "JavaScript expression")
      ],
      exampleTitle: "Embed a calculation",
      exampleCode: "console.log(`Result: ${6 * 7}`);",
      exampleExplanation:
        "JavaScript evaluates the expression and inserts 42 into the resulting string.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.const",
      name: "const declaration",
      displayName: "Declare a constant binding",
      description: "Creates a block-scoped binding that cannot be reassigned.",
      definition:
        "const declares a block-scoped binding. The referenced value may still be mutable, but the binding itself cannot be assigned again.",
      moduleName: "Language",
      action: "Define",
      object: "Constant",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Statements/const",
      keywords: ["variable", "binding", "scope"],
      template: "const {name} = {value};\nconsole.log({name});",
      parameters: [
        parameter("name", "Name", "The identifier bound by the declaration.", "answer", "Identifier"),
        parameter("value", "Value", "The expression assigned to the binding.", "42", "JavaScript expression")
      ],
      exampleTitle: "Bind an answer",
      exampleCode: "const answer = 42;\nconsole.log(answer);",
      exampleExplanation:
        "answer becomes a block-scoped binding whose value is then logged.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.array-map",
      name: "Array.map",
      displayName: "Transform an array",
      description: "Creates a new array by applying a function to every input element.",
      definition:
        "Array.prototype.map calls a callback for each array element and returns a new array containing each callback result.",
      moduleName: "Array",
      action: "Transform",
      object: "Array",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/Array/map",
      keywords: ["collection", "callback", "functional"],
      template: "const result = {array}.map(({item}) => {expression});\nconsole.log(result);",
      parameters: [
        parameter("array", "Array", "The array expression to transform.", "[1, 2, 3, 4]", "Array expression"),
        parameter("item", "Item name", "The callback parameter for each value.", "number", "Identifier"),
        parameter("expression", "Output expression", "The value returned for each item.", "number * number", "JavaScript expression")
      ],
      exampleTitle: "Square each number",
      exampleCode: "const result = [1, 2, 3, 4].map((number) => number * number);\nconsole.log(result);",
      exampleExplanation:
        "map leaves the input array unchanged and returns a new array containing the squares.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.array-filter",
      name: "Array.filter",
      displayName: "Filter an array",
      description: "Creates a new array containing only values accepted by a predicate.",
      definition:
        "Array.prototype.filter calls a predicate for each element and returns a shallow copy containing elements whose predicate result is truthy.",
      moduleName: "Array",
      action: "Select",
      object: "Array",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/Array/filter",
      keywords: ["collection", "predicate", "selection"],
      template: "const result = {array}.filter(({item}) => {condition});\nconsole.log(result);",
      parameters: [
        parameter("array", "Array", "The array expression to filter.", "[1, 2, 3, 4, 5, 6]", "Array expression"),
        parameter("item", "Item name", "The predicate parameter for each value.", "number", "Identifier"),
        parameter("condition", "Condition", "A truthy expression for values that should be kept.", "number % 2 === 0", "Boolean expression")
      ],
      exampleTitle: "Keep even numbers",
      exampleCode: "const result = [1, 2, 3, 4, 5, 6].filter((number) => number % 2 === 0);\nconsole.log(result);",
      exampleExplanation:
        "Only numbers whose remainder is zero pass the predicate and appear in the result.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.destructure-object",
      name: "object destructuring",
      displayName: "Destructure object properties",
      description: "Extracts named properties into local bindings.",
      definition:
        "Object destructuring matches property names from an object value and binds them to local variables in one declaration.",
      moduleName: "Language",
      action: "Define",
      object: "Object",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Operators/Destructuring",
      keywords: ["object", "binding", "property"],
      template: "const { {properties} } = {object};\nconsole.log({properties});",
      parameters: [
        parameter("properties", "Properties", "Comma-separated property names to bind.", "name, role", "Property list"),
        parameter("object", "Object", "The object expression supplying the properties.", "{ name: \"Ada\", role: \"Programmer\" }", "Object expression")
      ],
      exampleTitle: "Extract two properties",
      exampleCode: "const { name, role } = { name: \"Ada\", role: \"Programmer\" };\nconsole.log(name, role);",
      exampleExplanation:
        "The declaration creates name and role bindings from properties with the same names.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.function",
      name: "function declaration",
      displayName: "Define a function",
      description: "Declares reusable behavior and demonstrates a call.",
      definition:
        "A function declaration binds a named function in its surrounding scope and may return a value to its caller.",
      moduleName: "Language",
      action: "Define",
      object: "Function",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Statements/function",
      keywords: ["function", "return", "call"],
      template: "function {name}({argument}) {\n  return {expression};\n}\nconsole.log({name}({sample}));",
      parameters: [
        parameter("name", "Function name", "The identifier bound to the function.", "double", "Identifier"),
        parameter("argument", "Argument name", "The parameter accepted by the function.", "value", "Identifier"),
        parameter("expression", "Return expression", "The expression returned to the caller.", "value * 2", "JavaScript expression"),
        parameter("sample", "Sample input", "A value used to call the function.", "21", "JavaScript expression")
      ],
      exampleTitle: "Define and call double",
      exampleCode: "function double(value) {\n  return value * 2;\n}\nconsole.log(double(21));",
      exampleExplanation:
        "The call passes 21 to value; the function returns 42 and console.log displays it.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.class",
      name: "class declaration",
      displayName: "Declare a class",
      description: "Defines a constructor and instance method, then creates an object.",
      definition:
        "A class declaration defines a constructor and methods shared through the class prototype. new creates an initialized instance.",
      moduleName: "Language",
      action: "Define",
      object: "Class",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Classes",
      keywords: ["constructor", "method", "instance"],
      template: "class {name} {\n  constructor({field}) {\n    this.{field} = {field};\n  }\n\n  describe() {\n    return `${this.{field}}`;\n  }\n}\nconsole.log(new {name}({sample}).describe());",
      parameters: [
        parameter("name", "Class name", "The class identifier.", "Task", "Identifier"),
        parameter("field", "Field name", "The constructor parameter and instance property.", "title", "Identifier"),
        parameter("sample", "Sample value", "A value passed to the constructor.", "\"Ship Hypershell\"", "JavaScript expression")
      ],
      exampleTitle: "Create a Task instance",
      exampleCode: "class Task {\n  constructor(title) {\n    this.title = title;\n  }\n\n  describe() {\n    return `${this.title}`;\n  }\n}\nconsole.log(new Task(\"Ship Hypershell\").describe());",
      exampleExplanation:
        "new calls the constructor, stores the title, and the instance method returns it.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.promise-all",
      name: "Promise.all",
      displayName: "Await several promises",
      description: "Waits for a group of asynchronous operations and preserves result order.",
      definition:
        "Promise.all returns a promise fulfilled with an array of results when every input fulfills, or rejected when the first input rejects.",
      moduleName: "Promise",
      action: "Coordinate",
      object: "Promise",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/Promise/all",
      keywords: ["async", "await", "concurrency"],
      template: "void (async () => {\n  const values = await Promise.all({promises});\n  console.log(values);\n})();",
      parameters: [
        parameter("promises", "Promises", "An iterable of promises or plain values.", "[Promise.resolve(1), Promise.resolve(2)]", "Iterable expression")
      ],
      exampleTitle: "Collect two results",
      exampleCode: "void (async () => {\n  const values = await Promise.all([Promise.resolve(1), Promise.resolve(2)]);\n  console.log(values);\n})();",
      exampleExplanation:
        "Both promises fulfill, so Promise.all produces the ordered result array [1, 2].",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.read-file",
      name: "fs.readFile",
      displayName: "Read a UTF-8 text file",
      description: "Reads an entire text file asynchronously through the Node.js promise API.",
      definition:
        "node:fs/promises readFile returns a promise for the file contents. Supplying utf8 resolves the promise with a string.",
      moduleName: "node:fs/promises",
      action: "Read",
      object: "File",
      documentationUrl: "https://nodejs.org/api/fs.html#fspromisesreadfilepath-options",
      keywords: ["filesystem", "async", "utf-8"],
      template: "void (async () => {\n  const { readFile } = require(\"node:fs/promises\");\n  const text = await readFile({path}, \"utf8\");\n  console.log(text);\n})();",
      parameters: [
        parameter("path", "Path", "A JavaScript string containing the file path.", "\"README.md\"", "String expression")
      ],
      exampleTitle: "Read an isolated temporary file",
      exampleCode: "void (async () => {\n  const { mkdtemp, readFile, rm, writeFile } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-read-\"));\n  try {\n    const path = join(root, \"answer.txt\");\n    await writeFile(path, \"42\", \"utf8\");\n    console.log(await readFile(path, \"utf8\"));\n  } finally {\n    await rm(root, { recursive: true, force: true });\n  }\n})();",
      exampleExplanation:
        "The promise resolves with decoded text from a file created inside an isolated temporary directory, which is then removed.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.json-parse",
      name: "JSON.parse",
      displayName: "Parse JSON text",
      description: "Converts a JSON string into JavaScript objects, arrays, and scalar values.",
      definition:
        "JSON.parse reads one JSON text and returns the corresponding JavaScript value, throwing SyntaxError for invalid input.",
      moduleName: "JSON",
      action: "Parse",
      object: "JSON",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/JSON/parse",
      keywords: ["deserialize", "object", "structured data"],
      template: "const data = JSON.parse({text});\nconsole.log(data[{key}]);",
      parameters: [
        parameter("text", "JSON text", "A JavaScript string containing one JSON document.", "'{\"status\":\"ready\"}'", "String expression"),
        parameter("key", "Key", "A JavaScript string key read from the parsed object.", "\"status\"", "String expression")
      ],
      exampleTitle: "Read a JSON property",
      exampleCode: "const data = JSON.parse('{\"status\":\"ready\"}');\nconsole.log(data[\"status\"]);",
      exampleExplanation:
        "The JSON object becomes a JavaScript object and its status property is logged.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.fetch",
      name: "fetch",
      displayName: "Fetch JSON from a URL",
      description: "Performs an HTTP request with Node.js fetch and parses a JSON response.",
      definition:
        "fetch returns a promise for a Response. Checking ok before reading the body prevents HTTP errors from being treated as successful data.",
      moduleName: "Web API",
      action: "Read",
      object: "URL",
      documentationUrl: "https://nodejs.org/api/globals.html#fetch",
      keywords: ["http", "network", "async", "response"],
      template: "void (async () => {\n  const response = await fetch({url});\n  if (!response.ok) throw new Error(`HTTP ${response.status}`);\n  const data = await response.json();\n  console.log(data);\n})();",
      parameters: [
        parameter("url", "URL", "A JavaScript string containing an HTTP or HTTPS URL.", "\"https://api.github.com\"", "String expression")
      ],
      exampleTitle: "Request JSON",
      exampleCode: "void (async () => {\n  const response = await fetch(\"https://api.github.com\");\n  if (!response.ok) throw new Error(`HTTP ${response.status}`);\n  const data = await response.json();\n  console.log(data);\n})();",
      exampleExplanation:
        "The async function checks the response status before parsing and logging the JSON body.",
      shell: "NodeJs"
    }),
    entry({
      id: "javascript.try-catch",
      name: "try catch",
      displayName: "Handle an error",
      description: "Runs an operation and handles a thrown error value.",
      definition:
        "A try statement runs its block and transfers control to catch when the block throws. finally may be added for unconditional cleanup.",
      moduleName: "Language",
      action: "Handle",
      object: "Error",
      documentationUrl: "https://developer.mozilla.org/docs/Web/JavaScript/Reference/Statements/try...catch",
      keywords: ["exception", "error", "recovery"],
      template: "try {\n  const result = {expression};\n  console.log(result);\n} catch (error) {\n  console.error(`Unable to complete operation: ${error.message}`);\n}",
      parameters: [
        parameter("expression", "Expression", "The operation that may throw.", "JSON.parse(\"not json\")", "JavaScript expression")
      ],
      exampleTitle: "Handle invalid JSON",
      exampleCode: "try {\n  const result = JSON.parse(\"not json\");\n  console.log(result);\n} catch (error) {\n  console.error(`Unable to complete operation: ${error.message}`);\n}",
      exampleExplanation:
        "JSON.parse throws SyntaxError, and catch reports the message without terminating the Node.js session.",
      shell: "NodeJs"
    })
  ]
});

const typescript = library({
  id: "typescript-essentials",
  displayName: "TypeScript Essentials",
  description:
    "Official starter patterns for TypeScript annotations, object models, unions, narrowing, generics, classes, and typed asynchronous code.",
  language: "TypeScript",
  keywords: ["typescript", "types", "node.js", "learning", "starter"],
  executionSupportMessage:
    "Runs erasable TypeScript through Node.js native type stripping. Execution does not perform semantic type-checking.",
  actionDescriptions: {
    Define: "Declare a typed value or reusable operation.",
    Model: "Describe the permitted shape of a value.",
    Combine: "Compose several possible types into one contract.",
    Narrow: "Refine a broad type before using type-specific behavior.",
    Reuse: "Express a type-safe operation that works across several types.",
    Constrain: "Preserve useful inference while checking a value against a contract.",
    Read: "Select a property while retaining its exact value type.",
    Coordinate: "Represent and await asynchronous typed work."
  },
  objectDescriptions: {
    Binding: "A name associated with a value and an optional static type annotation.",
    Function: "A callable declaration with typed parameters and return value.",
    Interface: "A named structural contract for object values and classes.",
    "Type alias": "A reusable name for any TypeScript type expression.",
    Union: "A value permitted to match one of several member types.",
    "Type guard": "A runtime check TypeScript uses to narrow a static type.",
    Generic: "A declaration parameterized by one or more reusable types.",
    Property: "A named field that may be readonly, optional, or selected by key.",
    Class: "A construct that creates objects and may implement structural contracts.",
    Object: "A JavaScript object checked against a TypeScript shape.",
    Promise: "A typed representation of an asynchronous result."
  },
  commands: [
    entry({
      id: "typescript.typed-binding",
      name: "typed const",
      displayName: "Declare a typed binding",
      description: "Annotates a constant binding with an explicit TypeScript type.",
      definition:
        "A variable annotation follows the binding name and is checked statically. The annotation is erased before JavaScript executes.",
      moduleName: "Language",
      action: "Define",
      object: "Binding",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#type-annotations-on-variables",
      keywords: ["const", "annotation", "inference"],
      template: "const {name}: {type} = {value};\nconsole.log({name});",
      parameters: [
        parameter("name", "Binding name", "The identifier receiving the value.", "answer", "Identifier"),
        parameter("type", "Type", "The TypeScript type annotation.", "number", "Type expression"),
        parameter("value", "Value", "A runtime expression assignable to the selected type.", "42", "TypeScript expression")
      ],
      exampleTitle: "Annotate a number",
      exampleCode: "const answer: number = 42;\nconsole.log(answer);",
      exampleExplanation:
        "TypeScript checks that 42 is assignable to number, then Node.js executes the emitted JavaScript value.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.typed-function",
      name: "typed function",
      displayName: "Define a typed function",
      description: "Declares parameter and return types for reusable behavior.",
      definition:
        "Function annotations document and check what callers may pass and what the function promises to return.",
      moduleName: "Language",
      action: "Define",
      object: "Function",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/functions.html#parameter-type-annotations",
      keywords: ["parameter", "return type", "call"],
      template: "function {name}({argument}: {argumentType}): {returnType} {\n  return {expression};\n}\nconsole.log({name}({sample}));",
      parameters: [
        parameter("name", "Function name", "The function identifier.", "double", "Identifier"),
        parameter("argument", "Parameter name", "The function parameter identifier.", "value", "Identifier"),
        parameter("argumentType", "Parameter type", "The type accepted by the function.", "number", "Type expression"),
        parameter("returnType", "Return type", "The type produced by the function.", "number", "Type expression"),
        parameter("expression", "Return expression", "An expression assignable to the return type.", "value * 2", "TypeScript expression"),
        parameter("sample", "Sample input", "A value used for the example call.", "21", "TypeScript expression")
      ],
      exampleTitle: "Type a numeric function",
      exampleCode: "function double(value: number): number {\n  return value * 2;\n}\nconsole.log(double(21));",
      exampleExplanation:
        "The function accepts and returns numbers, so the call produces 42.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.interface",
      name: "interface",
      displayName: "Model an object with an interface",
      description: "Declares a reusable structural contract for object values.",
      definition:
        "An interface names a required object shape. TypeScript uses structural compatibility, so a value matches when it supplies the required members.",
      moduleName: "Language",
      action: "Model",
      object: "Interface",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/object-types.html",
      keywords: ["shape", "structural typing", "property"],
      template: "interface {name} {\n  {property}: {propertyType};\n}\nconst value: {name} = { {property}: {sample} };\nconsole.log(value.{property});",
      parameters: [
        parameter("name", "Interface name", "The contract identifier.", "User", "Identifier"),
        parameter("property", "Property", "A required property name.", "name", "Identifier"),
        parameter("propertyType", "Property type", "The permitted property type.", "string", "Type expression"),
        parameter("sample", "Sample value", "A value assignable to the property type.", "\"Ada\"", "TypeScript expression")
      ],
      exampleTitle: "Describe a user",
      exampleCode: "interface User {\n  name: string;\n}\nconst value: User = { name: \"Ada\" };\nconsole.log(value.name);",
      exampleExplanation:
        "The object satisfies User because it contains the required string property.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.type-alias",
      name: "type alias",
      displayName: "Create a type alias",
      description: "Assigns a reusable name to a TypeScript type expression.",
      definition:
        "A type alias can name primitives, object shapes, unions, tuples, and other type expressions. It exists only during type analysis.",
      moduleName: "Language",
      action: "Model",
      object: "Type alias",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#type-aliases",
      keywords: ["alias", "object type", "reuse"],
      template: "type {name} = {type};\nconst value: {name} = {sample};\nconsole.log(value);",
      parameters: [
        parameter("name", "Alias name", "The reusable type name.", "Identifier", "Identifier"),
        parameter("type", "Type expression", "The type assigned to the alias.", "string | number", "Type expression"),
        parameter("sample", "Sample value", "A value assignable to the alias.", "42", "TypeScript expression")
      ],
      exampleTitle: "Name an identifier type",
      exampleCode: "type Identifier = string | number;\nconst value: Identifier = 42;\nconsole.log(value);",
      exampleExplanation:
        "Identifier can hold either a string or number, and the sample uses its number member.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.union",
      name: "union type",
      displayName: "Combine types in a union",
      description: "Allows a value to match one of several member types.",
      definition:
        "A union joins member types with the vertical bar. Code may use only behavior common to every member until it narrows the value.",
      moduleName: "Language",
      action: "Combine",
      object: "Union",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#union-types",
      keywords: ["union", "alternative", "member type"],
      template: "type {name} = {members};\nconst value: {name} = {sample};\nconsole.log(value);",
      parameters: [
        parameter("name", "Union name", "The alias for the union.", "Result", "Identifier"),
        parameter("members", "Member types", "Types joined with a vertical bar.", "string | number", "Union type"),
        parameter("sample", "Sample value", "A value matching one union member.", "\"ready\"", "TypeScript expression")
      ],
      exampleTitle: "Allow text or numbers",
      exampleCode: "type Result = string | number;\nconst value: Result = \"ready\";\nconsole.log(value);",
      exampleExplanation:
        "The string literal is valid because string is one member of Result.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.typeof-narrowing",
      name: "typeof narrowing",
      displayName: "Narrow with typeof",
      description: "Uses a runtime typeof check to select type-specific behavior.",
      definition:
        "Control-flow analysis narrows a union inside branches whose runtime checks prove which member is present.",
      moduleName: "Language",
      action: "Narrow",
      object: "Type guard",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/narrowing.html#typeof-type-guards",
      keywords: ["typeof", "control flow", "type guard"],
      template: "function describe(value: string | number): string {\n  if (typeof value === \"string\") return value.toUpperCase();\n  return value.toFixed(2);\n}\nconsole.log(describe({sample}));",
      parameters: [
        parameter("sample", "Sample", "A string or number expression to narrow.", "42", "TypeScript expression")
      ],
      exampleTitle: "Narrow a number",
      exampleCode: "function describe(value: string | number): string {\n  if (typeof value === \"string\") return value.toUpperCase();\n  return value.toFixed(2);\n}\nconsole.log(describe(42));",
      exampleExplanation:
        "The false branch knows value is a number and safely calls toFixed.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.generic-function",
      name: "generic function",
      displayName: "Define a generic function",
      description: "Relates an input type to an output type without losing specificity.",
      definition:
        "A type parameter acts as a placeholder chosen by each call. Returning the same parameter preserves the caller's exact type.",
      moduleName: "Language",
      action: "Reuse",
      object: "Generic",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/generics.html",
      keywords: ["generic", "type parameter", "inference"],
      template: "function identity<T>(value: T): T {\n  return value;\n}\nconst result = identity<{type}>({sample});\nconsole.log(result);",
      parameters: [
        parameter("type", "Type argument", "The concrete type supplied for T.", "string", "Type expression"),
        parameter("sample", "Sample value", "A value assignable to the type argument.", "\"typed\"", "TypeScript expression")
      ],
      exampleTitle: "Preserve a string type",
      exampleCode: "function identity<T>(value: T): T {\n  return value;\n}\nconst result = identity<string>(\"typed\");\nconsole.log(result);",
      exampleExplanation:
        "T is string for this call, so the return value remains a string.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.readonly-property",
      name: "readonly property",
      displayName: "Declare a readonly property",
      description: "Prevents assignment to an object property during type checking.",
      definition:
        "A readonly modifier protects a property from reassignment in TypeScript. It does not freeze the JavaScript object at runtime.",
      moduleName: "Language",
      action: "Model",
      object: "Property",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/object-types.html#readonly-properties",
      keywords: ["readonly", "immutability", "property"],
      template: "interface {name} {\n  readonly {property}: {type};\n}\nconst value: {name} = { {property}: {sample} };\nconsole.log(value.{property});",
      parameters: [
        parameter("name", "Interface name", "The containing object contract.", "Configuration", "Identifier"),
        parameter("property", "Property", "The readonly property identifier.", "mode", "Identifier"),
        parameter("type", "Property type", "The property's static type.", "string", "Type expression"),
        parameter("sample", "Sample value", "An initial property value.", "\"safe\"", "TypeScript expression")
      ],
      exampleTitle: "Protect a configuration mode",
      exampleCode: "interface Configuration {\n  readonly mode: string;\n}\nconst value: Configuration = { mode: \"safe\" };\nconsole.log(value.mode);",
      exampleExplanation:
        "The initial value is valid, while later assignment to mode would be a type error.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.optional-property",
      name: "optional property",
      displayName: "Use an optional property",
      description: "Marks an object property as possibly absent and reads it safely.",
      definition:
        "A question mark makes a property optional. Reading it produces the declared type combined with undefined, so optional chaining or narrowing is appropriate.",
      moduleName: "Language",
      action: "Model",
      object: "Property",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/object-types.html#optional-properties",
      keywords: ["optional", "undefined", "optional chaining"],
      template: "interface User {\n  {property}?: string;\n}\nconst user: User = {sample};\nconsole.log(user.{property}?.toUpperCase() ?? {fallback});",
      parameters: [
        parameter("property", "Optional property", "The property that may be absent.", "nickname", "Identifier"),
        parameter("sample", "Object contents", "Properties placed inside the sample object.", "{}", "Object expression"),
        parameter("fallback", "Fallback", "A string expression used when the property is absent.", "\"No nickname\"", "String expression")
      ],
      exampleTitle: "Handle an absent nickname",
      exampleCode: "interface User {\n  nickname?: string;\n}\nconst user: User = {};\nconsole.log(user.nickname?.toUpperCase() ?? \"No nickname\");",
      exampleExplanation:
        "Optional chaining returns undefined for the missing property and nullish coalescing supplies the fallback.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.discriminated-union",
      name: "discriminated union",
      displayName: "Model states with a discriminated union",
      description: "Uses a shared literal property to safely distinguish object variants.",
      definition:
        "A discriminated union gives each object variant a common property with a distinct literal value. Checking that property narrows the remaining fields.",
      moduleName: "Language",
      action: "Narrow",
      object: "Union",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/narrowing.html#discriminated-unions",
      keywords: ["discriminant", "state", "exhaustive"],
      template: "type Result =\n  | { kind: \"success\"; value: number }\n  | { kind: \"error\"; message: string };\nfunction summarize(result: Result): string {\n  return result.kind === \"success\" ? `Value: ${result.value}` : `Error: ${result.message}`;\n}\nconst result: Result = {sample};\nconsole.log(summarize(result));",
      parameters: [
        parameter("sample", "Variant", "An object matching one Result variant.", "{ kind: \"success\", value: 42 }", "Object expression")
      ],
      exampleTitle: "Handle a success state",
      exampleCode: "type Result =\n  | { kind: \"success\"; value: number }\n  | { kind: \"error\"; message: string };\nfunction summarize(result: Result): string {\n  return result.kind === \"success\" ? `Value: ${result.value}` : `Error: ${result.message}`;\n}\nconst result: Result = { kind: \"success\", value: 42 };\nconsole.log(summarize(result));",
      exampleExplanation:
        "Checking kind narrows result to the success variant before reading value.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.class-implements",
      name: "implements",
      displayName: "Implement an interface with a class",
      description: "Checks that class instances provide the members required by an interface.",
      definition:
        "An implements clause checks a class against a structural contract. The class still needs explicit property and method annotations where desired.",
      moduleName: "Language",
      action: "Model",
      object: "Class",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/classes.html#implements-clauses",
      keywords: ["class", "implements", "interface"],
      template: "interface Named {\n  name: string;\n}\nclass {className} implements Named {\n  name: string;\n  constructor(name: string) {\n    this.name = name;\n  }\n  describe(): string {\n    return this.name;\n  }\n}\nconsole.log(new {className}({sample}).describe());",
      parameters: [
        parameter("className", "Class name", "The implementing class identifier.", "Person", "Identifier"),
        parameter("sample", "Name value", "A string passed to the constructor.", "\"Ada\"", "String expression")
      ],
      exampleTitle: "Implement Named",
      exampleCode: "interface Named {\n  name: string;\n}\nclass Person implements Named {\n  name: string;\n  constructor(name: string) {\n    this.name = name;\n  }\n  describe(): string {\n    return this.name;\n  }\n}\nconsole.log(new Person(\"Ada\").describe());",
      exampleExplanation:
        "Person supplies the required name property and adds a typed describe method.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.satisfies",
      name: "satisfies",
      displayName: "Check an object with satisfies",
      description: "Validates compatibility without replacing the value's inferred type.",
      definition:
        "The satisfies operator checks that an expression is assignable to a type while preserving the expression's more specific inferred properties.",
      moduleName: "Language",
      action: "Constrain",
      object: "Object",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/release-notes/typescript-4-9.html#the-satisfies-operator",
      keywords: ["satisfies", "inference", "constraint"],
      template: "type Channel = \"stable\" | \"preview\";\nconst config = { channel: {channel}, retries: {retries} } satisfies { channel: Channel; retries: number };\nconsole.log(config.channel);",
      parameters: [
        parameter("channel", "Channel", "A string literal accepted by Channel.", "\"stable\"", "String literal"),
        parameter("retries", "Retries", "A numeric retry count.", "3", "Number expression")
      ],
      exampleTitle: "Validate configuration",
      exampleCode: "type Channel = \"stable\" | \"preview\";\nconst config = { channel: \"stable\", retries: 3 } satisfies { channel: Channel; retries: number };\nconsole.log(config.channel);",
      exampleExplanation:
        "The object is checked against the contract while config.channel retains its specific literal information.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.keyof",
      name: "keyof",
      displayName: "Read a property with keyof",
      description: "Constrains a property key and preserves the corresponding value type.",
      definition:
        "keyof produces a union of known property names. Combining it with indexed access lets a generic function return the exact type associated with a selected key.",
      moduleName: "Language",
      action: "Read",
      object: "Property",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/keyof-types.html",
      keywords: ["keyof", "indexed access", "generic"],
      template: "function getProperty<T, K extends keyof T>(object: T, key: K): T[K] {\n  return object[key];\n}\nconst settings = { theme: \"dark\", fontSize: 16 };\nconsole.log(getProperty(settings, {key}));",
      parameters: [
        parameter("key", "Property key", "A string literal key from the sample object.", "\"theme\"", "String literal")
      ],
      exampleTitle: "Read the theme",
      exampleCode: "function getProperty<T, K extends keyof T>(object: T, key: K): T[K] {\n  return object[key];\n}\nconst settings = { theme: \"dark\", fontSize: 16 };\nconsole.log(getProperty(settings, \"theme\"));",
      exampleExplanation:
        "The key is constrained to theme or fontSize, and the selected theme value is inferred as string.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.const-assertion",
      name: "as const",
      displayName: "Preserve literals with as const",
      description: "Keeps literal values narrow and makes object properties readonly in the inferred type.",
      definition:
        "A const assertion prevents widening of literal values and gives arrays or object properties readonly inferred types. It does not freeze the runtime value.",
      moduleName: "Language",
      action: "Constrain",
      object: "Object",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/release-notes/typescript-3-4.html#const-assertions",
      keywords: ["as const", "literal", "readonly"],
      template: "const route = { method: {method}, path: {path} } as const;\nconsole.log(route.method, route.path);",
      parameters: [
        parameter("method", "Method", "A string literal HTTP method.", "\"GET\"", "String literal"),
        parameter("path", "Path", "A string literal route path.", "\"/status\"", "String literal")
      ],
      exampleTitle: "Preserve route literals",
      exampleCode: "const route = { method: \"GET\", path: \"/status\" } as const;\nconsole.log(route.method, route.path);",
      exampleExplanation:
        "The inferred method and path remain their exact literal values rather than widening to string.",
      shell: "TypeScript"
    }),
    entry({
      id: "typescript.async-promise",
      name: "typed Promise",
      displayName: "Await a typed promise",
      description: "Declares the result type of asynchronous work and awaits it safely.",
      definition:
        "An async function returns a Promise whose type argument describes the eventual value. await unwraps that value inside another async operation.",
      moduleName: "Language",
      action: "Coordinate",
      object: "Promise",
      documentationUrl:
        "https://www.typescriptlang.org/docs/handbook/2/functions.html",
      keywords: ["async", "await", "Promise"],
      template: "async function resolveValue(value: number): Promise<number> {\n  return value;\n}\n(async (): Promise<void> => {\n  const value: number = await resolveValue({sample});\n  console.log(value);\n})()",
      parameters: [
        parameter("sample", "Value", "A numeric value resolved by the promise.", "42", "Number expression")
      ],
      exampleTitle: "Await a number",
      exampleCode: "async function resolveValue(value: number): Promise<number> {\n  return value;\n}\n(async (): Promise<void> => {\n  const value: number = await resolveValue(42);\n  console.log(value);\n})()",
      exampleExplanation:
        "The promise resolves to number, and await produces a number inside the async function.",
      shell: "TypeScript"
    })
  ]
});

const java = library({
  id: "java-essentials",
  displayName: "Java Essentials",
  description:
    "Official Java 17 starter patterns for values, collections, control flow, records, streams, files, exceptions, and asynchronous work.",
  language: "Java",
  keywords: ["java", "jdk", "javac", "standard library", "learning", "starter"],
  executionSupportMessage:
    "Compiles each statement in an isolated temporary Java 17 program and runs it with the detected JDK. Terminal snippet state is not persistent.",
  actionDescriptions: {
    Output: "Display a Java value through standard output.",
    Define: "Declare a value or local data shape.",
    Collect: "Create or traverse Java collection values.",
    Branch: "Choose behavior from conditions or cases.",
    Transform: "Produce values through functions, streams, or optionals.",
    Read: "Read text with the Java NIO file APIs.",
    Handle: "Respond to an exception explicitly.",
    Schedule: "Compose asynchronous work with completion stages."
  },
  objectDescriptions: {
    Value: "A Java expression evaluated by a compiled statement.",
    String: "Immutable text represented by java.lang.String.",
    Variable: "A named local value with an explicit or inferred type.",
    List: "An ordered collection of Java values.",
    Map: "A key/value collection.",
    Loop: "A statement that visits values from an iterable.",
    Switch: "An expression that selects one result from exhaustive cases.",
    Function: "Behavior represented by a Java functional interface.",
    Stream: "A lazy aggregate-operation pipeline.",
    Optional: "A container that may hold one non-null value.",
    Record: "A concise immutable data carrier.",
    "Text block": "A multiline Java string literal.",
    File: "A filesystem object addressed through java.nio.file.Path.",
    Exception: "A throwable condition handled by Java control flow.",
    Future: "An asynchronous result represented by CompletableFuture."
  },
  commands: [
    entry({
      id: "java.println",
      name: "System.out.println",
      displayName: "Print a value",
      description: "Writes a Java value followed by a line terminator.",
      definition:
        "System.out is the process standard-output PrintStream. println converts its argument to text, writes it, and terminates the line.",
      moduleName: "java.base",
      action: "Output",
      object: "Value",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/PrintStream.html#println(java.lang.Object)",
      keywords: ["stdout", "console", "print", "println"],
      template: "System.out.println({value});",
      parameters: [
        parameter("value", "Value", "A Java expression to display.", "\"Hello from Hypershell\"", "Java expression")
      ],
      exampleTitle: "Display a greeting",
      exampleCode: "System.out.println(\"Hello from Hypershell\");",
      exampleExplanation: "The string is compiled into the temporary program and written to standard output.",
      shell: "Java"
    }),
    entry({
      id: "java.formatted-string",
      name: "String.formatted",
      displayName: "Format a string",
      description: "Creates a formatted string from a format template and arguments.",
      definition:
        "String.formatted applies Formatter-style substitutions to the receiving string and returns the resulting text.",
      moduleName: "java.base",
      action: "Output",
      object: "String",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html#formatted(java.lang.Object...)",
      keywords: ["format", "string", "interpolation", "formatted"],
      template: "var {name} = \"{format}\".formatted({arguments});\nSystem.out.println({name});",
      parameters: [
        parameter("name", "Variable", "The local variable receiving the formatted string.", "message", "Java identifier"),
        parameter("format", "Format", "A Formatter-compatible string without surrounding quotes.", "Result: %d", "Format text"),
        parameter("arguments", "Arguments", "One or more comma-separated format arguments.", "6 * 7", "Java expressions")
      ],
      exampleTitle: "Format a calculation",
      exampleCode: "var message = \"Result: %d\".formatted(6 * 7);\nSystem.out.println(message);",
      exampleExplanation: "The integer substitution produces Result: 42.",
      shell: "Java"
    }),
    entry({
      id: "java.local-variable",
      name: "var",
      displayName: "Infer a local variable type",
      description: "Declares a strongly typed local variable with compiler inference.",
      definition:
        "Local-variable type inference lets javac derive the static type from the initializer. var is restricted to local declarations and does not make Java dynamically typed.",
      moduleName: "Java language",
      action: "Define",
      object: "Variable",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/language/local-variable-type-inference.html",
      keywords: ["var", "local", "type inference", "declaration"],
      template: "var {name} = {value};\nSystem.out.println({name});",
      parameters: [
        parameter("name", "Variable", "A valid Java local variable name.", "answer", "Java identifier"),
        parameter("value", "Initializer", "The expression whose static type is inferred.", "6 * 7", "Java expression")
      ],
      exampleTitle: "Infer an integer",
      exampleCode: "var answer = 6 * 7;\nSystem.out.println(answer);",
      exampleExplanation: "javac infers int and the program prints 42.",
      shell: "Java"
    }),
    entry({
      id: "java.array-list",
      name: "ArrayList",
      displayName: "Build a mutable list",
      description: "Creates a typed, resizable list and adds values to it.",
      definition:
        "ArrayList implements List with a resizable array. Its generic element type is checked at compile time.",
      moduleName: "java.base",
      action: "Collect",
      object: "List",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/ArrayList.html",
      keywords: ["arraylist", "list", "collection", "generic", "add"],
      template: "var {name} = new ArrayList<{type}>();\n{name}.add({first});\n{name}.add({second});\nSystem.out.println({name});",
      parameters: [
        parameter("name", "Variable", "The list variable name.", "items", "Java identifier"),
        parameter("type", "Element type", "The compile-time list element type.", "String", "Java type"),
        parameter("first", "First value", "The first compatible value.", "\"alpha\"", "Java expression"),
        parameter("second", "Second value", "The second compatible value.", "\"beta\"", "Java expression")
      ],
      exampleTitle: "Collect two labels",
      exampleCode: "var items = new ArrayList<String>();\nitems.add(\"alpha\");\nitems.add(\"beta\");\nSystem.out.println(items);",
      exampleExplanation: "The mutable list accepts two strings and prints them in insertion order.",
      shell: "Java"
    }),
    entry({
      id: "java.map-of",
      name: "Map.of",
      displayName: "Create an immutable map",
      description: "Creates a compact unmodifiable map from key/value pairs.",
      definition:
        "Map.of returns an unmodifiable map that rejects null keys and values and duplicate keys.",
      moduleName: "java.base",
      action: "Collect",
      object: "Map",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Map.html#of(K,V,K,V)",
      keywords: ["map", "dictionary", "key", "value", "immutable"],
      template: "var {name} = Map.of({firstKey}, {firstValue}, {secondKey}, {secondValue});\nSystem.out.println({name}.get({lookupKey}));",
      parameters: [
        parameter("name", "Variable", "The map variable name.", "status", "Java identifier"),
        parameter("firstKey", "First key", "The first non-null key.", "\"ready\"", "Java expression"),
        parameter("firstValue", "First value", "The first non-null value.", "true", "Java expression"),
        parameter("secondKey", "Second key", "The second unique key.", "\"count\"", "Java expression"),
        parameter("secondValue", "Second value", "The second value.", "42", "Java expression"),
        parameter("lookupKey", "Lookup key", "The key whose value is printed.", "\"count\"", "Java expression")
      ],
      exampleTitle: "Read a map value",
      exampleCode: "var status = Map.of(\"ready\", true, \"count\", 42);\nSystem.out.println(status.get(\"count\"));",
      exampleExplanation: "The immutable map returns 42 for the count key.",
      shell: "Java"
    }),
    entry({
      id: "java.enhanced-for",
      name: "enhanced for",
      displayName: "Iterate over values",
      description: "Runs a statement once for each value in an iterable.",
      definition:
        "The enhanced for statement obtains each element from an array or Iterable without exposing index or iterator bookkeeping.",
      moduleName: "Java language",
      action: "Collect",
      object: "Loop",
      documentationUrl: "https://docs.oracle.com/javase/specs/jls/se17/html/jls-14.html#jls-14.14.2",
      keywords: ["for", "foreach", "loop", "iterable"],
      template: "for (var {item} : List.of({values})) {\n    {statement}\n}",
      parameters: [
        parameter("item", "Item variable", "The local name bound to each value.", "number", "Java identifier"),
        parameter("values", "Values", "Comma-separated compatible expressions.", "1, 2, 3", "Java expressions"),
        parameter("statement", "Loop body", "A Java statement executed for each item.", "System.out.println(number * number);", "Java statement")
      ],
      exampleTitle: "Print three squares",
      exampleCode: "for (var number : List.of(1, 2, 3)) {\n    System.out.println(number * number);\n}",
      exampleExplanation: "The loop prints 1, 4, and 9.",
      shell: "Java"
    }),
    entry({
      id: "java.switch-expression",
      name: "switch expression",
      displayName: "Return a value from switch",
      description: "Selects one result with arrow-form switch cases.",
      definition:
        "A switch expression evaluates to a value. Arrow labels do not fall through, and the cases must cover every possible selector value or provide default.",
      moduleName: "Java language",
      action: "Branch",
      object: "Switch",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/language/switch-expressions-and-statements.html",
      keywords: ["switch", "expression", "case", "branch", "yield"],
      template: "var {selector} = {value};\nvar {result} = switch ({selector}) {\n    case {firstCase} -> {firstResult};\n    case {secondCase} -> {secondResult};\n    default -> {defaultResult};\n};\nSystem.out.println({result});",
      parameters: [
        parameter("selector", "Selector", "The variable tested by switch.", "status", "Java identifier"),
        parameter("value", "Selector value", "The initial selector expression.", "\"ready\"", "Java expression"),
        parameter("result", "Result variable", "The variable receiving the switch value.", "message", "Java identifier"),
        parameter("firstCase", "First case", "The first compatible case constant.", "\"ready\"", "Java case constant"),
        parameter("firstResult", "First result", "The expression returned by the first case.", "\"Ship it\"", "Java expression"),
        parameter("secondCase", "Second case", "The second compatible case constant.", "\"blocked\"", "Java case constant"),
        parameter("secondResult", "Second result", "The expression returned by the second case.", "\"Investigate\"", "Java expression"),
        parameter("defaultResult", "Default result", "The fallback expression.", "\"Unknown\"", "Java expression")
      ],
      exampleTitle: "Map status to a message",
      exampleCode: "var status = \"ready\";\nvar message = switch (status) {\n    case \"ready\" -> \"Ship it\";\n    case \"blocked\" -> \"Investigate\";\n    default -> \"Unknown\";\n};\nSystem.out.println(message);",
      exampleExplanation: "The ready case evaluates to Ship it without fall-through.",
      shell: "Java"
    }),
    entry({
      id: "java.lambda-function",
      name: "Function.apply",
      displayName: "Apply a lambda",
      description: "Stores behavior in a typed functional interface and invokes it.",
      definition:
        "A lambda supplies the implementation of a functional interface. Function<T,R> accepts one value and returns one result through apply.",
      moduleName: "java.base",
      action: "Transform",
      object: "Function",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/function/Function.html",
      keywords: ["lambda", "function", "functional interface", "apply"],
      template: "Function<{inputType}, {outputType}> {name} = {parameterName} -> {expression};\nSystem.out.println({name}.apply({sample}));",
      parameters: [
        parameter("inputType", "Input type", "The function input reference type.", "Integer", "Java type"),
        parameter("outputType", "Output type", "The function result reference type.", "Integer", "Java type"),
        parameter("name", "Function variable", "The local function name.", "doubleValue", "Java identifier"),
        parameter("parameterName", "Lambda parameter", "The lambda input name.", "value", "Java identifier"),
        parameter("expression", "Expression", "The result expression.", "value * 2", "Java expression"),
        parameter("sample", "Sample input", "The value passed to apply.", "21", "Java expression")
      ],
      exampleTitle: "Double an integer",
      exampleCode: "Function<Integer, Integer> doubleValue = value -> value * 2;\nSystem.out.println(doubleValue.apply(21));",
      exampleExplanation: "The typed lambda returns 42.",
      shell: "Java"
    }),
    entry({
      id: "java.stream-pipeline",
      name: "Stream pipeline",
      displayName: "Filter and transform a stream",
      description: "Builds a lazy stream pipeline and collects its results.",
      definition:
        "Stream intermediate operations such as filter and map are lazy. A terminal operation such as toList initiates traversal and produces the result.",
      moduleName: "java.base",
      action: "Transform",
      object: "Stream",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/stream/Stream.html",
      keywords: ["stream", "filter", "map", "tolist", "pipeline"],
      template: "var {name} = List.of({values}).stream()\n    .filter({item} -> {predicate})\n    .map({item} -> {expression})\n    .toList();\nSystem.out.println({name});",
      parameters: [
        parameter("name", "Result variable", "The collected result list.", "squares", "Java identifier"),
        parameter("values", "Values", "Comma-separated stream source values.", "1, 2, 3, 4", "Java expressions"),
        parameter("item", "Item variable", "The lambda parameter name.", "number", "Java identifier"),
        parameter("predicate", "Predicate", "The Boolean filter expression.", "number % 2 == 0", "Java expression"),
        parameter("expression", "Mapping", "The transformed result expression.", "number * number", "Java expression")
      ],
      exampleTitle: "Square even numbers",
      exampleCode: "var squares = List.of(1, 2, 3, 4).stream()\n    .filter(number -> number % 2 == 0)\n    .map(number -> number * number)\n    .toList();\nSystem.out.println(squares);",
      exampleExplanation: "The terminal toList operation produces [4, 16].",
      shell: "Java"
    }),
    entry({
      id: "java.optional",
      name: "Optional",
      displayName: "Transform an optional value",
      description: "Maps a present value and supplies a fallback when absent.",
      definition:
        "Optional models a value that may be absent. map transforms a present value without a null check, and orElse supplies a fallback.",
      moduleName: "java.base",
      action: "Transform",
      object: "Optional",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Optional.html",
      keywords: ["optional", "map", "orelse", "null", "fallback"],
      template: "var {name} = Optional.ofNullable({value})\n    .map({item} -> {expression})\n    .orElse({fallback});\nSystem.out.println({name});",
      parameters: [
        parameter("name", "Result variable", "The unwrapped result variable.", "label", "Java identifier"),
        parameter("value", "Nullable value", "The possibly null source expression.", "\"hypershell\"", "Java expression"),
        parameter("item", "Item variable", "The map lambda parameter.", "text", "Java identifier"),
        parameter("expression", "Mapping", "The expression applied when present.", "text.toUpperCase()", "Java expression"),
        parameter("fallback", "Fallback", "The value used when absent.", "\"UNKNOWN\"", "Java expression")
      ],
      exampleTitle: "Normalize a present label",
      exampleCode: "var label = Optional.ofNullable(\"hypershell\")\n    .map(text -> text.toUpperCase())\n    .orElse(\"UNKNOWN\");\nSystem.out.println(label);",
      exampleExplanation: "The present string is transformed to HYPERSHELL.",
      shell: "Java"
    }),
    entry({
      id: "java.local-record",
      name: "record",
      displayName: "Declare a local record",
      description: "Declares a concise immutable data carrier inside the current block.",
      definition:
        "A record declaration defines final component fields, accessors, a canonical constructor, and value-based Object methods from a compact header.",
      moduleName: "Java language",
      action: "Define",
      object: "Record",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/language/records.html",
      keywords: ["record", "immutable", "data", "component", "local"],
      template: "record {typeName}({componentType} {componentName}) {}\nvar {name} = new {typeName}({value});\nSystem.out.println({name}.{componentName}());",
      parameters: [
        parameter("typeName", "Record name", "The local record type name.", "WorkItem", "Java type identifier"),
        parameter("componentType", "Component type", "The record component type.", "String", "Java type"),
        parameter("componentName", "Component name", "The record component and accessor name.", "title", "Java identifier"),
        parameter("name", "Variable", "The record instance variable.", "item", "Java identifier"),
        parameter("value", "Value", "The constructor value.", "\"Build Java support\"", "Java expression")
      ],
      exampleTitle: "Create a work item",
      exampleCode: "record WorkItem(String title) {}\nvar item = new WorkItem(\"Build Java support\");\nSystem.out.println(item.title());",
      exampleExplanation: "The generated title accessor returns the immutable component value.",
      shell: "Java"
    }),
    entry({
      id: "java.text-block",
      name: "text block",
      displayName: "Create multiline text",
      description: "Creates a multiline string without escaped newline sequences.",
      definition:
        "A text block is a String literal delimited by triple quotation marks. Java removes incidental indentation based on the closing delimiter.",
      moduleName: "Java language",
      action: "Define",
      object: "Text block",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/text-blocks/index.html",
      keywords: ["text block", "multiline", "string", "triple quote"],
      template: "var {name} = \"\"\"\n{firstLine}\n{secondLine}\n\"\"\";\nSystem.out.println({name});",
      parameters: [
        parameter("name", "Variable", "The text variable name.", "message", "Java identifier"),
        parameter("firstLine", "First line", "Literal text for the first line.", "Hello", "Text"),
        parameter("secondLine", "Second line", "Literal text for the second line.", "Hypershell", "Text")
      ],
      exampleTitle: "Create a two-line message",
      exampleCode: "var message = \"\"\"\nHello\nHypershell\n\"\"\";\nSystem.out.println(message);",
      exampleExplanation: "The resulting string contains both lines and a trailing newline.",
      shell: "Java"
    }),
    entry({
      id: "java.files-read-string",
      name: "Files.readString",
      displayName: "Write and read UTF-8 text",
      description: "Uses NIO helpers to round-trip text through a temporary file.",
      definition:
        "Files.writeString and Files.readString provide compact text-file operations. Their no-charset overloads use UTF-8.",
      moduleName: "java.base",
      action: "Read",
      object: "File",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/file/Files.html#readString(java.nio.file.Path)",
      keywords: ["files", "path", "readstring", "writestring", "utf-8", "nio"],
      template: "var {pathName} = Files.createTempFile(\"{prefix}\", \".txt\");\ntry {\n    Files.writeString({pathName}, {text});\n    System.out.println(Files.readString({pathName}));\n} finally {\n    Files.deleteIfExists({pathName});\n}",
      parameters: [
        parameter("pathName", "Path variable", "The temporary Path variable.", "path", "Java identifier"),
        parameter("prefix", "File prefix", "At least three filename characters without surrounding quotes.", "hypershell", "Text"),
        parameter("text", "Text", "The Java string written to the file.", "\"ready\"", "Java string expression")
      ],
      exampleTitle: "Round-trip temporary text",
      exampleCode: "var path = Files.createTempFile(\"hypershell\", \".txt\");\ntry {\n    Files.writeString(path, \"ready\");\n    System.out.println(Files.readString(path));\n} finally {\n    Files.deleteIfExists(path);\n}",
      exampleExplanation: "The example prints ready and removes its temporary file even if reading fails.",
      shell: "Java"
    }),
    entry({
      id: "java.try-catch",
      name: "try catch",
      displayName: "Handle an exception",
      description: "Runs a potentially failing operation and handles its exception type.",
      definition:
        "A catch clause handles a compatible exception thrown by the try block. More specific exception types preserve clearer recovery behavior.",
      moduleName: "Java language",
      action: "Handle",
      object: "Exception",
      documentationUrl: "https://docs.oracle.com/javase/tutorial/essential/exceptions/handling.html",
      keywords: ["try", "catch", "exception", "error", "recovery"],
      template: "try {\n    {statement}\n} catch ({exceptionType} {exceptionName}) {\n    {handler}\n}",
      parameters: [
        parameter("statement", "Try body", "A Java statement that may throw.", "Integer.parseInt(\"not-a-number\");", "Java statement"),
        parameter("exceptionType", "Exception type", "The compatible exception class.", "NumberFormatException", "Java exception type"),
        parameter("exceptionName", "Exception variable", "The caught exception variable.", "exception", "Java identifier"),
        parameter("handler", "Handler", "The recovery statement.", "System.out.println(exception.getClass().getSimpleName());", "Java statement")
      ],
      exampleTitle: "Handle invalid integer text",
      exampleCode: "try {\n    Integer.parseInt(\"not-a-number\");\n} catch (NumberFormatException exception) {\n    System.out.println(exception.getClass().getSimpleName());\n}",
      exampleExplanation: "The invalid parse is handled and reports NumberFormatException.",
      shell: "Java"
    }),
    entry({
      id: "java.completable-future",
      name: "CompletableFuture",
      displayName: "Compose asynchronous work",
      description: "Runs a supplier asynchronously and transforms its eventual result.",
      definition:
        "CompletableFuture represents a value completed later. supplyAsync starts a supplier, thenApply transforms its result, and join waits while rethrowing failure as an unchecked completion exception.",
      moduleName: "java.base",
      action: "Schedule",
      object: "Future",
      documentationUrl: "https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/concurrent/CompletableFuture.html",
      keywords: ["completablefuture", "async", "future", "thenapply", "join"],
      template: "var {name} = CompletableFuture.supplyAsync(() -> {source})\n    .thenApply({item} -> {expression});\nSystem.out.println({name}.join());",
      parameters: [
        parameter("name", "Future variable", "The CompletableFuture variable.", "future", "Java identifier"),
        parameter("source", "Supplier result", "The value produced asynchronously.", "21", "Java expression"),
        parameter("item", "Result variable", "The transformation lambda parameter.", "value", "Java identifier"),
        parameter("expression", "Transformation", "The result transformation.", "value * 2", "Java expression")
      ],
      exampleTitle: "Double an asynchronous value",
      exampleCode: "var future = CompletableFuture.supplyAsync(() -> 21)\n    .thenApply(value -> value * 2);\nSystem.out.println(future.join());",
      exampleExplanation: "The asynchronous pipeline completes with 42 before join returns.",
      shell: "Java"
    })
  ]
});

const html = library({
  id: "html-essentials",
  displayName: "HTML Essentials",
  description:
    "Official starter patterns for document structure, semantic content, navigation, media, forms, tables, disclosure, stylesheets, and modules.",
  language: "HTML",
  keywords: ["html", "markup", "semantic", "accessibility", "web", "starter"],
  isExecutionSupported: false,
  executionSupportMessage:
    "Insert these patterns into an HTML document and use Preview HTML in browser; HTML is not a terminal command language.",
  category: "Official web starter pack",
  actionDescriptions: {
    Structure: "Define the document or a semantic region.",
    Navigate: "Connect the document to another resource or location.",
    Embed: "Place media or another resource in the document.",
    Collect: "Accept or organize user-provided information.",
    Organize: "Give related content an explicit structure.",
    Reveal: "Expose optional content through a native interaction."
  },
  objectDescriptions: {
    Document: "The root structure of an HTML page.",
    Layout: "Semantic page regions that describe the role of their content.",
    Link: "A navigable connection to another URL or document location.",
    Image: "Embedded visual content with a text alternative.",
    Form: "Controls that collect and submit user input.",
    List: "An ordered or unordered collection of related items.",
    Table: "Two-dimensional tabular data with explicit headings.",
    Disclosure: "A native expandable details-and-summary interaction.",
    Stylesheet: "An external CSS resource linked to the document.",
    Module: "A JavaScript module loaded by the document."
  },
  commands: [
    entry({
      id: "html.document-shell",
      name: "document shell",
      displayName: "Create an HTML document shell",
      description: "Creates the required document structure with language, character encoding, viewport, and title metadata.",
      definition:
        "A standards-mode HTML document starts with a doctype and an html element. Metadata belongs in head, while rendered page content belongs in body.",
      moduleName: "Document",
      action: "Structure",
      object: "Document",
      documentationUrl: "https://html.spec.whatwg.org/multipage/syntax.html#writing-html-documents",
      keywords: ["doctype", "head", "body", "viewport", "charset"],
      template: "<!doctype html>\n<html lang=\"{language}\">\n<head>\n  <meta charset=\"utf-8\">\n  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n  <title>{title}</title>\n</head>\n<body>\n  <main>\n    <h1>{heading}</h1>\n  </main>\n</body>\n</html>",
      parameters: [
        parameter("language", "Language", "The page's primary BCP 47 language tag.", "en", "Language tag"),
        parameter("title", "Document title", "Text shown in the browser tab and used by assistive technology.", "Hypershell Web Page", "Plain text"),
        parameter("heading", "Page heading", "The visible primary heading.", "Hello, web", "Plain text")
      ],
      exampleTitle: "Start a standards-mode page",
      exampleCode: "<!doctype html>\n<html lang=\"en\">\n<head>\n  <meta charset=\"utf-8\">\n  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n  <title>Hypershell Web Page</title>\n</head>\n<body>\n  <main>\n    <h1>Hello, web</h1>\n  </main>\n</body>\n</html>",
      exampleExplanation:
        "The metadata establishes encoding and responsive sizing before the main landmark contains the visible page heading.",
      shell: "External"
    }),
    entry({
      id: "html.semantic-layout",
      name: "semantic layout",
      displayName: "Build semantic page regions",
      description: "Organizes a page with header, navigation, main, and footer landmarks.",
      definition:
        "Semantic sectioning and grouping elements communicate the purpose of page regions to browsers, assistive technology, and maintainers.",
      moduleName: "Sections",
      action: "Structure",
      object: "Layout",
      documentationUrl: "https://html.spec.whatwg.org/multipage/sections.html",
      keywords: ["header", "nav", "main", "footer", "landmark"],
      template: "<header>\n  <h1>{siteName}</h1>\n  <nav aria-label=\"Primary\">{navigation}</nav>\n</header>\n<main>\n  <h2>{heading}</h2>\n  <p>{content}</p>\n</main>\n<footer>{footer}</footer>",
      parameters: [
        parameter("siteName", "Site name", "The product or site heading.", "Hypershell", "Plain text"),
        parameter("navigation", "Navigation markup", "Links placed in the primary navigation region.", "<a href=\"/\">Home</a>", "HTML fragment"),
        parameter("heading", "Main heading", "The heading for the primary content.", "Workspace", "Plain text"),
        parameter("content", "Content", "A paragraph of primary content.", "Build something useful.", "Plain text"),
        parameter("footer", "Footer", "Supporting footer content.", "<small>Built with Hypershell</small>", "HTML fragment")
      ],
      exampleTitle: "Describe the page structure",
      exampleCode: "<header>\n  <h1>Hypershell</h1>\n  <nav aria-label=\"Primary\"><a href=\"/\">Home</a></nav>\n</header>\n<main>\n  <h2>Workspace</h2>\n  <p>Build something useful.</p>\n</main>\n<footer><small>Built with Hypershell</small></footer>",
      exampleExplanation:
        "Each region has a native role, while the navigation label distinguishes it from any other nav regions.",
      shell: "External"
    }),
    entry({
      id: "html.anchor",
      name: "anchor",
      displayName: "Create a hyperlink",
      description: "Creates a navigable link with meaningful text.",
      definition:
        "An a element with an href attribute creates a hyperlink. Link text should describe the destination without relying on surrounding prose.",
      moduleName: "Text-level semantics",
      action: "Navigate",
      object: "Link",
      documentationUrl: "https://html.spec.whatwg.org/multipage/links.html#links-created-by-a-and-area-elements",
      keywords: ["a", "href", "url", "navigation"],
      template: "<a href=\"{url}\">{label}</a>",
      parameters: [
        parameter("url", "Destination", "A relative or absolute URL.", "https://example.com", "URL"),
        parameter("label", "Link text", "Meaningful text describing the destination.", "Read the documentation", "Plain text")
      ],
      exampleTitle: "Link to documentation",
      exampleCode: "<a href=\"https://example.com\">Read the documentation</a>",
      exampleExplanation:
        "The href identifies the destination and the visible text explains what the user will open.",
      shell: "External"
    }),
    entry({
      id: "html.figure-image",
      name: "figure image",
      displayName: "Add an image with a caption",
      description: "Embeds an image with alternative text and an optional visible caption.",
      definition:
        "The img alt attribute provides a text alternative when the image carries meaning. figure and figcaption associate supporting caption content with it.",
      moduleName: "Embedded content",
      action: "Embed",
      object: "Image",
      documentationUrl: "https://html.spec.whatwg.org/multipage/embedded-content.html#the-img-element",
      keywords: ["img", "alt", "figure", "figcaption", "accessibility"],
      template: "<figure>\n  <img src=\"{source}\" alt=\"{alternative}\" width=\"{width}\" height=\"{height}\">\n  <figcaption>{caption}</figcaption>\n</figure>",
      parameters: [
        parameter("source", "Image source", "The image URL or relative file path.", "images/workspace.png", "URL"),
        parameter("alternative", "Alternative text", "A concise replacement for the image's meaning.", "Hypershell workspace with editor and browser tabs", "Plain text"),
        parameter("width", "Width", "The intrinsic image width in CSS pixels.", "1280", "Positive integer"),
        parameter("height", "Height", "The intrinsic image height in CSS pixels.", "720", "Positive integer"),
        parameter("caption", "Caption", "Visible supporting text associated with the image.", "A customizable Hypershell workspace.", "Plain text")
      ],
      exampleTitle: "Caption a product screenshot",
      exampleCode: "<figure>\n  <img src=\"images/workspace.png\" alt=\"Hypershell workspace with editor and browser tabs\" width=\"1280\" height=\"720\">\n  <figcaption>A customizable Hypershell workspace.</figcaption>\n</figure>",
      exampleExplanation:
        "Explicit dimensions reserve layout space, alt text replaces the image's meaning, and the caption remains visible to everyone.",
      shell: "External"
    }),
    entry({
      id: "html.form",
      name: "form",
      displayName: "Build a labeled form",
      description: "Collects a required value with an explicit label and submit button.",
      definition:
        "A label associated through for and id gives a form control an accessible name. Native input types and required constraints provide useful browser behavior.",
      moduleName: "Forms",
      action: "Collect",
      object: "Form",
      documentationUrl: "https://html.spec.whatwg.org/multipage/forms.html",
      keywords: ["form", "label", "input", "button", "required"],
      template: "<form action=\"{action}\" method=\"{method}\">\n  <label for=\"{id}\">{label}</label>\n  <input id=\"{id}\" name=\"{name}\" type=\"{type}\" required>\n  <button type=\"submit\">{button}</button>\n</form>",
      parameters: [
        parameter("action", "Action", "The URL that receives submitted form data.", "/subscribe", "URL"),
        parameter("method", "Method", "The form submission method, normally get or post.", "post", "Form method"),
        parameter("id", "Control ID", "A document-unique identifier shared with the label.", "email", "HTML identifier"),
        parameter("label", "Label", "Text naming the input.", "Email address", "Plain text"),
        parameter("name", "Field name", "The key used in submitted form data.", "email", "Form field name"),
        parameter("type", "Input type", "A native input type appropriate for the expected data.", "email", "Input type"),
        parameter("button", "Button label", "Text describing the submission action.", "Subscribe", "Plain text")
      ],
      exampleTitle: "Collect an email address",
      exampleCode: "<form action=\"/subscribe\" method=\"post\">\n  <label for=\"email\">Email address</label>\n  <input id=\"email\" name=\"email\" type=\"email\" required>\n  <button type=\"submit\">Subscribe</button>\n</form>",
      exampleExplanation:
        "The explicit label names the input, type=email enables email-specific validation, and required prevents an empty native submission.",
      shell: "External"
    }),
    entry({
      id: "html.list",
      name: "list",
      displayName: "Create a semantic list",
      description: "Groups related items as an ordered or unordered list.",
      definition:
        "Use ul when order is not meaningful and ol when sequence or ranking matters. Each direct item is represented by li.",
      moduleName: "Grouping content",
      action: "Organize",
      object: "List",
      documentationUrl: "https://html.spec.whatwg.org/multipage/grouping-content.html#the-ul-element",
      keywords: ["ul", "ol", "li", "items"],
      template: "<{listType}>\n  <li>{first}</li>\n  <li>{second}</li>\n  <li>{third}</li>\n</{listType}>",
      parameters: [
        parameter("listType", "List type", "Use ul for unordered content or ol for an ordered sequence.", "ul", "ul or ol"),
        parameter("first", "First item", "The first list item.", "Plan", "Plain text"),
        parameter("second", "Second item", "The second list item.", "Build", "Plain text"),
        parameter("third", "Third item", "The third list item.", "Verify", "Plain text")
      ],
      exampleTitle: "List development stages",
      exampleCode: "<ol>\n  <li>Plan</li>\n  <li>Build</li>\n  <li>Verify</li>\n</ol>",
      exampleExplanation:
        "An ordered list communicates that the stages have a meaningful sequence.",
      shell: "External"
    }),
    entry({
      id: "html.table",
      name: "data table",
      displayName: "Create a data table",
      description: "Represents row-and-column data with a caption and scoped headings.",
      definition:
        "table is for genuinely tabular data. caption names the table and th scope attributes identify whether headings apply to columns or rows.",
      moduleName: "Tabular data",
      action: "Organize",
      object: "Table",
      documentationUrl: "https://html.spec.whatwg.org/multipage/tables.html#the-table-element",
      keywords: ["table", "caption", "thead", "tbody", "th", "scope"],
      template: "<table>\n  <caption>{caption}</caption>\n  <thead>\n    <tr><th scope=\"col\">{firstHeading}</th><th scope=\"col\">{secondHeading}</th></tr>\n  </thead>\n  <tbody>\n    <tr><td>{firstValue}</td><td>{secondValue}</td></tr>\n  </tbody>\n</table>",
      parameters: [
        parameter("caption", "Caption", "A concise name for the data set.", "Build status", "Plain text"),
        parameter("firstHeading", "First heading", "The first column heading.", "Stage", "Plain text"),
        parameter("secondHeading", "Second heading", "The second column heading.", "Status", "Plain text"),
        parameter("firstValue", "First value", "The first row's first value.", "Web pack", "Plain text"),
        parameter("secondValue", "Second value", "The first row's second value.", "Ready", "Plain text")
      ],
      exampleTitle: "Show a build status",
      exampleCode: "<table>\n  <caption>Build status</caption>\n  <thead>\n    <tr><th scope=\"col\">Stage</th><th scope=\"col\">Status</th></tr>\n  </thead>\n  <tbody>\n    <tr><td>Web pack</td><td>Ready</td></tr>\n  </tbody>\n</table>",
      exampleExplanation:
        "The caption names the table and column scopes let tools associate both data cells with their headings.",
      shell: "External"
    }),
    entry({
      id: "html.details",
      name: "details disclosure",
      displayName: "Add native disclosure content",
      description: "Creates an expandable details region without requiring JavaScript.",
      definition:
        "details provides a native disclosure widget and summary supplies its always-visible label. The open attribute controls its initial state.",
      moduleName: "Interactive elements",
      action: "Reveal",
      object: "Disclosure",
      documentationUrl: "https://html.spec.whatwg.org/multipage/interactive-elements.html#the-details-element",
      keywords: ["details", "summary", "disclosure", "expand"],
      template: "<details>\n  <summary>{summary}</summary>\n  <p>{content}</p>\n</details>",
      parameters: [
        parameter("summary", "Summary", "The clickable label for the disclosure.", "What does Preview do?", "Plain text"),
        parameter("content", "Details", "The content revealed when the widget opens.", "It loads the saved page in a Hypershell browser tab.", "Plain text")
      ],
      exampleTitle: "Explain browser preview",
      exampleCode: "<details>\n  <summary>What does Preview do?</summary>\n  <p>It loads the saved page in a Hypershell browser tab.</p>\n</details>",
      exampleExplanation:
        "The browser supplies keyboard, pointer, and open-state behavior for the disclosure.",
      shell: "External"
    }),
    entry({
      id: "html.stylesheet-link",
      name: "stylesheet link",
      displayName: "Link an external stylesheet",
      description: "Loads a CSS file as a stylesheet for the current document.",
      definition:
        "A link element with rel=stylesheet creates an external resource link whose CSS rules apply to the document when loaded.",
      moduleName: "Document metadata",
      action: "Navigate",
      object: "Stylesheet",
      documentationUrl: "https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet",
      keywords: ["link", "rel", "stylesheet", "css", "href"],
      template: "<link rel=\"stylesheet\" href=\"{path}\">",
      parameters: [
        parameter("path", "Stylesheet path", "A URL or path relative to the HTML document.", "styles.css", "URL")
      ],
      exampleTitle: "Load the project stylesheet",
      exampleCode: "<link rel=\"stylesheet\" href=\"styles.css\">",
      exampleExplanation:
        "A relative path resolves beside the saved HTML file, so local browser preview can load styles.css from the same folder.",
      shell: "External"
    }),
    entry({
      id: "html.module-script",
      name: "module script",
      displayName: "Load a JavaScript module",
      description: "Loads a JavaScript file with module scope and deferred execution.",
      definition:
        "A script element with type=module loads JavaScript as a module, enabling import and export syntax and executing after document parsing.",
      moduleName: "Scripting",
      action: "Embed",
      object: "Module",
      documentationUrl: "https://html.spec.whatwg.org/multipage/scripting.html#attr-script-type",
      keywords: ["script", "module", "javascript", "src", "defer"],
      template: "<script type=\"module\" src=\"{path}\"></script>",
      parameters: [
        parameter("path", "Module path", "A URL or path to browser-compatible JavaScript.", "app.js", "URL")
      ],
      exampleTitle: "Load the browser entry module",
      exampleCode: "<script type=\"module\" src=\"app.js\"></script>",
      exampleExplanation:
        "The browser parses app.js as a module. TypeScript source must first be compiled to browser-compatible JavaScript.",
      shell: "External"
    })
  ]
});

const css = library({
  id: "css-essentials",
  displayName: "CSS Essentials",
  description:
    "Official starter patterns for selectors, custom properties, sizing, Flexbox, Grid, responsive rules, interaction states, typography, transitions, and animation.",
  language: "CSS",
  keywords: ["css", "styles", "layout", "responsive", "web", "starter"],
  isExecutionSupported: false,
  executionSupportMessage:
    "Insert these rules into a CSS or SCSS document and link the saved stylesheet from HTML; CSS is not terminal-executable.",
  category: "Official web starter pack",
  actionDescriptions: {
    Select: "Target elements that match a selector.",
    Define: "Declare reusable design values or reusable motion.",
    Size: "Control the dimensions and box model of an element.",
    Arrange: "Lay out children along one or two dimensions.",
    Adapt: "Change presentation in response to the environment.",
    Respond: "Style an explicit interaction state.",
    Style: "Control the visual presentation of content.",
    Animate: "Interpolate visual values over time."
  },
  objectDescriptions: {
    Selector: "A pattern that matches document elements.",
    Variable: "A cascading custom property referenced with var().",
    Box: "An element's content, padding, border, and margin areas.",
    Flexbox: "A one-dimensional layout model.",
    Grid: "A two-dimensional row-and-column layout model.",
    Viewport: "The display environment tested by a media query.",
    State: "A user or element condition represented by a pseudo-class.",
    Typography: "Font, line, and text presentation.",
    Transition: "Interpolation between old and new property values.",
    Animation: "A keyframed sequence applied to an element."
  },
  commands: [
    entry({
      id: "css.class-selector",
      name: "class selector",
      displayName: "Style a class",
      description: "Targets every element containing a specified class token.",
      definition:
        "A class selector begins with a full stop and matches elements whose class list contains the named token. Classes are reusable and may be combined.",
      moduleName: "Selectors",
      action: "Select",
      object: "Selector",
      documentationUrl: "https://www.w3.org/TR/selectors-4/#class-html",
      keywords: ["selector", "class", "declaration"],
      template: ".{className} {\n  {property}: {value};\n}",
      parameters: [
        parameter("className", "Class name", "The class token without a leading dot.", "card", "CSS identifier"),
        parameter("property", "Property", "A CSS property name.", "padding", "CSS property"),
        parameter("value", "Value", "A value accepted by the selected property.", "1rem", "CSS value")
      ],
      exampleTitle: "Pad every card",
      exampleCode: ".card {\n  padding: 1rem;\n}",
      exampleExplanation:
        "Every element with class=card receives one root-em of inner spacing.",
      shell: "External"
    }),
    entry({
      id: "css.custom-property",
      name: "custom property",
      displayName: "Define and use a custom property",
      description: "Creates a cascading design value and reads it through var().",
      definition:
        "Custom property names begin with two hyphens. Their values participate in the cascade and var() substitutes the computed value where it is used.",
      moduleName: "Custom properties",
      action: "Define",
      object: "Variable",
      documentationUrl: "https://www.w3.org/TR/css-variables-2/",
      keywords: ["variable", "var", "token", "root", "cascade"],
      template: ":root {\n  --{name}: {value};\n}\n\n.{className} {\n  {property}: var(--{name});\n}",
      parameters: [
        parameter("name", "Property name", "The custom property name without the leading double hyphen.", "accent", "CSS identifier"),
        parameter("value", "Design value", "The reusable CSS token value.", "#58a6ff", "CSS value"),
        parameter("className", "Class", "The class that consumes the value.", "link", "CSS identifier"),
        parameter("property", "Property", "The property receiving the custom value.", "color", "CSS property")
      ],
      exampleTitle: "Reuse an accent color",
      exampleCode: ":root {\n  --accent: #58a6ff;\n}\n\n.link {\n  color: var(--accent);\n}",
      exampleExplanation:
        "Changing --accent at the root updates every declaration that references it unless a nearer value overrides the cascade.",
      shell: "External"
    }),
    entry({
      id: "css.box-sizing",
      name: "border box sizing",
      displayName: "Use predictable box sizing",
      description: "Makes declared dimensions include padding and borders.",
      definition:
        "With box-sizing:border-box, width and height apply to the border box instead of only the content box, simplifying layout calculations.",
      moduleName: "Box model",
      action: "Size",
      object: "Box",
      documentationUrl: "https://www.w3.org/TR/css-ui-4/#box-sizing",
      keywords: ["box-sizing", "border-box", "width", "padding"],
      template: "*,\n*::before,\n*::after {\n  box-sizing: border-box;\n}",
      exampleTitle: "Normalize element sizing",
      exampleCode: "*,\n*::before,\n*::after {\n  box-sizing: border-box;\n}",
      exampleExplanation:
        "The universal selector and both generated pseudo-elements use the same predictable sizing model.",
      shell: "External"
    }),
    entry({
      id: "css.flex-layout",
      name: "flex layout",
      displayName: "Arrange items with Flexbox",
      description: "Aligns children along a primary axis and controls wrapping and gaps.",
      definition:
        "A flex container lays out direct children as flex items. justify-content works along the main axis and align-items works along the cross axis.",
      moduleName: "Flexbox",
      action: "Arrange",
      object: "Flexbox",
      documentationUrl: "https://www.w3.org/TR/css-flexbox-1/",
      keywords: ["display", "flex", "gap", "align", "justify"],
      template: ".{className} {\n  display: flex;\n  flex-wrap: {wrap};\n  gap: {gap};\n  align-items: {align};\n  justify-content: {justify};\n}",
      parameters: [
        parameter("className", "Container class", "The class applied to the flex container.", "toolbar", "CSS identifier"),
        parameter("wrap", "Wrapping", "Whether items remain on one line or wrap.", "wrap", "Flex wrap value"),
        parameter("gap", "Gap", "Space inserted between flex items.", "0.75rem", "Length"),
        parameter("align", "Cross-axis alignment", "How items align across the main axis.", "center", "align-items value"),
        parameter("justify", "Main-axis alignment", "How free space is distributed along the main axis.", "space-between", "justify-content value")
      ],
      exampleTitle: "Build a responsive toolbar row",
      exampleCode: ".toolbar {\n  display: flex;\n  flex-wrap: wrap;\n  gap: 0.75rem;\n  align-items: center;\n  justify-content: space-between;\n}",
      exampleExplanation:
        "Items share a row when space permits, wrap when needed, and keep consistent spacing and alignment.",
      shell: "External"
    }),
    entry({
      id: "css.grid-layout",
      name: "grid layout",
      displayName: "Create a responsive Grid",
      description: "Builds repeating columns that adapt to available width.",
      definition:
        "Grid provides two-dimensional layout. repeat(), auto-fit, and minmax() can create as many columns as fit while retaining a useful minimum size.",
      moduleName: "Grid",
      action: "Arrange",
      object: "Grid",
      documentationUrl: "https://www.w3.org/TR/css-grid-2/",
      keywords: ["display", "grid", "repeat", "minmax", "auto-fit"],
      template: ".{className} {\n  display: grid;\n  grid-template-columns: repeat(auto-fit, minmax({minimum}, 1fr));\n  gap: {gap};\n}",
      parameters: [
        parameter("className", "Container class", "The class applied to the grid container.", "cards", "CSS identifier"),
        parameter("minimum", "Minimum column", "The smallest desired track size.", "14rem", "Track size"),
        parameter("gap", "Gap", "Space between rows and columns.", "1rem", "Length")
      ],
      exampleTitle: "Lay out responsive cards",
      exampleCode: ".cards {\n  display: grid;\n  grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));\n  gap: 1rem;\n}",
      exampleExplanation:
        "The browser adds or removes equal-width columns as the container changes size without a fixed column count.",
      shell: "External"
    }),
    entry({
      id: "css.media-query",
      name: "media query",
      displayName: "Adapt to viewport width",
      description: "Applies declarations only when a media condition matches.",
      definition:
        "A media query condition tests the output environment. Range syntax can express a minimum viewport width without device-specific assumptions.",
      moduleName: "Media queries",
      action: "Adapt",
      object: "Viewport",
      documentationUrl: "https://www.w3.org/TR/mediaqueries-5/",
      keywords: ["media", "responsive", "width", "breakpoint"],
      template: "@media (width >= {minimum}) {\n  .{className} {\n    {property}: {value};\n  }\n}",
      parameters: [
        parameter("minimum", "Minimum width", "The viewport width at which the rule begins to apply.", "48rem", "Media feature value"),
        parameter("className", "Class", "The class changed by the query.", "layout", "CSS identifier"),
        parameter("property", "Property", "The property changed at the breakpoint.", "grid-template-columns", "CSS property"),
        parameter("value", "Value", "The value used when the condition matches.", "16rem 1fr", "CSS value")
      ],
      exampleTitle: "Add a sidebar on wider viewports",
      exampleCode: "@media (width >= 48rem) {\n  .layout {\n    grid-template-columns: 16rem 1fr;\n  }\n}",
      exampleExplanation:
        "The two-column layout activates when the viewport has enough inline space.",
      shell: "External"
    }),
    entry({
      id: "css.focus-visible",
      name: "focus visible state",
      displayName: "Style keyboard focus",
      description: "Adds a clear focus indicator when the browser determines it should be visible.",
      definition:
        ":focus-visible matches a focused element when the user agent's interaction heuristics call for an obvious focus indicator, commonly during keyboard navigation.",
      moduleName: "Pseudo-classes",
      action: "Respond",
      object: "State",
      documentationUrl: "https://www.w3.org/TR/selectors-4/#the-focus-visible-pseudo",
      keywords: ["focus-visible", "keyboard", "outline", "accessibility"],
      template: ".{className}:focus-visible {\n  outline: {width} solid {color};\n  outline-offset: {offset};\n}",
      parameters: [
        parameter("className", "Class", "The interactive element class.", "button", "CSS identifier"),
        parameter("width", "Outline width", "The thickness of the focus indicator.", "3px", "Length"),
        parameter("color", "Outline color", "A high-contrast focus color.", "#58a6ff", "Color"),
        parameter("offset", "Outline offset", "Space between the border and outline.", "3px", "Length")
      ],
      exampleTitle: "Make button focus obvious",
      exampleCode: ".button:focus-visible {\n  outline: 3px solid #58a6ff;\n  outline-offset: 3px;\n}",
      exampleExplanation:
        "The indicator remains outside the component border and is not removed from keyboard users.",
      shell: "External"
    }),
    entry({
      id: "css.typography",
      name: "readable typography",
      displayName: "Set readable body typography",
      description: "Establishes a font stack, line height, and bounded readable line length.",
      definition:
        "Font family selects available typefaces, line-height controls line box spacing, and logical measure units can prevent prose from becoming difficult to scan.",
      moduleName: "Fonts",
      action: "Style",
      object: "Typography",
      documentationUrl: "https://www.w3.org/TR/css-fonts-4/",
      keywords: ["font-family", "line-height", "ch", "readability"],
      template: ".{className} {\n  max-inline-size: {measure};\n  font-family: {font};\n  font-size: {size};\n  line-height: {lineHeight};\n}",
      parameters: [
        parameter("className", "Text class", "The class applied to a prose region.", "prose", "CSS identifier"),
        parameter("measure", "Line measure", "The maximum inline line length.", "70ch", "Length"),
        parameter("font", "Font stack", "A prioritized CSS font-family list.", "system-ui, sans-serif", "Font family"),
        parameter("size", "Font size", "The base text size.", "1rem", "Length"),
        parameter("lineHeight", "Line height", "A unitless line-height multiplier.", "1.6", "Number")
      ],
      exampleTitle: "Create a readable prose column",
      exampleCode: ".prose {\n  max-inline-size: 70ch;\n  font-family: system-ui, sans-serif;\n  font-size: 1rem;\n  line-height: 1.6;\n}",
      exampleExplanation:
        "The text uses installed system UI fonts, comfortable line spacing, and a bounded line measure.",
      shell: "External"
    }),
    entry({
      id: "css.transition",
      name: "transition",
      displayName: "Transition an interaction state",
      description: "Interpolates a changed property over a bounded duration.",
      definition:
        "A transition observes changes to selected animatable properties and interpolates their computed values according to a duration and timing function.",
      moduleName: "Transitions",
      action: "Animate",
      object: "Transition",
      documentationUrl: "https://www.w3.org/TR/css-transitions-2/",
      keywords: ["transition", "duration", "timing", "hover"],
      template: ".{className} {\n  transition: {property} {duration} {timing};\n}\n\n.{className}:hover {\n  {property}: {value};\n}",
      parameters: [
        parameter("className", "Class", "The element receiving the transition.", "card", "CSS identifier"),
        parameter("property", "Property", "One animatable property to interpolate.", "transform", "CSS property"),
        parameter("duration", "Duration", "How long the interpolation takes.", "160ms", "Time"),
        parameter("timing", "Timing", "The rate-of-change function.", "ease-out", "Timing function"),
        parameter("value", "Hover value", "The property's value in the hover state.", "translateY(-2px)", "CSS value")
      ],
      exampleTitle: "Lift a card on hover",
      exampleCode: ".card {\n  transition: transform 160ms ease-out;\n}\n\n.card:hover {\n  transform: translateY(-2px);\n}",
      exampleExplanation:
        "Only transform is animated, keeping the transition explicit and short.",
      shell: "External"
    }),
    entry({
      id: "css.keyframes",
      name: "keyframes animation",
      displayName: "Define a keyframed animation",
      description: "Defines named keyframes and applies them to an element.",
      definition:
        "@keyframes describes property values at points in an animation timeline. animation applies the named sequence with duration, timing, and iteration settings.",
      moduleName: "Animations",
      action: "Animate",
      object: "Animation",
      documentationUrl: "https://www.w3.org/TR/css-animations-2/",
      keywords: ["keyframes", "animation", "opacity", "motion"],
      template: "@keyframes {name} {\n  from { {property}: {from}; }\n  to { {property}: {to}; }\n}\n\n.{className} {\n  animation: {name} {duration} {timing};\n}",
      parameters: [
        parameter("name", "Animation name", "The identifier shared by @keyframes and animation.", "fade-in", "CSS identifier"),
        parameter("property", "Property", "The animatable property controlled by the keyframes.", "opacity", "CSS property"),
        parameter("from", "Starting value", "The value at the beginning.", "0", "CSS value"),
        parameter("to", "Ending value", "The value at the end.", "1", "CSS value"),
        parameter("className", "Class", "The class receiving the animation.", "notice", "CSS identifier"),
        parameter("duration", "Duration", "How long one animation cycle takes.", "220ms", "Time"),
        parameter("timing", "Timing", "The animation timing function.", "ease-out", "Timing function")
      ],
      exampleTitle: "Fade in a notice",
      exampleCode: "@keyframes fade-in {\n  from { opacity: 0; }\n  to { opacity: 1; }\n}\n\n.notice {\n  animation: fade-in 220ms ease-out;\n}",
      exampleExplanation:
        "The notice interpolates opacity from transparent to opaque once when it appears.",
      shell: "External"
    })
  ]
});

const webApis = library({
  id: "web-api-essentials",
  displayName: "Web API Essentials",
  description:
    "Official browser patterns for DOM selection, events, classes, element creation, Fetch, storage, forms, URLs, timers, and navigation history.",
  language: "Web APIs",
  keywords: ["browser", "dom", "web api", "javascript", "typescript", "starter"],
  isExecutionSupported: false,
  executionSupportMessage:
    "Insert these patterns into browser JavaScript, TypeScript source, or an HTML module script. Node.js terminals do not provide a page DOM.",
  category: "Official web starter pack",
  actionDescriptions: {
    Select: "Find a node in the active document.",
    Listen: "Respond to an event dispatched by an event target.",
    Update: "Change document state or presentation.",
    Create: "Construct and attach new DOM content.",
    Request: "Fetch a network resource through the browser.",
    Store: "Persist a small string value for the current origin.",
    Collect: "Read values from a form or URL.",
    Schedule: "Run a callback after a delay.",
    Navigate: "Update session history and the displayed URL."
  },
  objectDescriptions: {
    Element: "A node representing an HTML element in the DOM.",
    Event: "A browser-dispatched occurrence delivered to listeners.",
    Class: "A token in an element's class attribute.",
    Node: "A DOM object that can participate in the document tree.",
    Response: "The result of a Fetch request.",
    Storage: "Origin-scoped key/value strings exposed by localStorage.",
    Form: "A set of successful form controls read through FormData.",
    URL: "Structured query parameters parsed by URLSearchParams.",
    Timer: "A task scheduled after a minimum delay.",
    History: "The current browsing context's session history."
  },
  commands: [
    entry({
      id: "web.query-selector",
      name: "querySelector",
      displayName: "Select one element",
      description: "Returns the first element matching a CSS selector or null.",
      definition:
        "querySelector parses a selector and returns the first matching descendant in tree order. Code must handle the possibility that nothing matches.",
      moduleName: "DOM",
      action: "Select",
      object: "Element",
      documentationUrl: "https://dom.spec.whatwg.org/#dom-parentnode-queryselector",
      keywords: ["document", "selector", "element", "null"],
      template: "const {name} = document.querySelector(\"{selector}\");\nif ({name}) {\n  {name}.textContent = \"{text}\";\n}",
      parameters: [
        parameter("name", "Variable", "The binding that receives the element or null.", "status", "JavaScript identifier"),
        parameter("selector", "Selector", "A CSS selector evaluated against the document.", "#status", "CSS selector"),
        parameter("text", "Text", "Replacement text for the matched element.", "Ready", "Plain text")
      ],
      exampleTitle: "Update a status element",
      exampleCode: "const status = document.querySelector(\"#status\");\nif (status) {\n  status.textContent = \"Ready\";\n}",
      exampleExplanation:
        "The null check prevents an exception when the page does not contain the expected element.",
      shell: "External"
    }),
    entry({
      id: "web.add-event-listener",
      name: "addEventListener",
      displayName: "Handle an event",
      description: "Registers a callback for an event without replacing other listeners.",
      definition:
        "addEventListener registers a listener on an EventTarget. The callback runs when a matching event reaches the target during the configured phase.",
      moduleName: "DOM Events",
      action: "Listen",
      object: "Event",
      documentationUrl: "https://dom.spec.whatwg.org/#dom-eventtarget-addeventlistener",
      keywords: ["event", "click", "listener", "callback"],
      template: "const {name} = document.querySelector(\"{selector}\");\n{name}?.addEventListener(\"{event}\", () => {\n  {statement}\n});",
      parameters: [
        parameter("name", "Variable", "The selected event target binding.", "button", "JavaScript identifier"),
        parameter("selector", "Selector", "A selector locating the event target.", "#action", "CSS selector"),
        parameter("event", "Event type", "The event type without an on prefix.", "click", "Event type"),
        parameter("statement", "Handler body", "Browser JavaScript executed for the event.", "console.log(\"Clicked\");", "JavaScript statement")
      ],
      exampleTitle: "Handle a button click",
      exampleCode: "const button = document.querySelector(\"#action\");\nbutton?.addEventListener(\"click\", () => {\n  console.log(\"Clicked\");\n});",
      exampleExplanation:
        "Optional chaining registers the listener only when the button exists.",
      shell: "External"
    }),
    entry({
      id: "web.class-list-toggle",
      name: "classList.toggle",
      displayName: "Toggle an element class",
      description: "Adds a class token when absent and removes it when present.",
      definition:
        "classList exposes an element's class tokens as a DOMTokenList. toggle changes one token and returns whether it is present afterward.",
      moduleName: "DOM",
      action: "Update",
      object: "Class",
      documentationUrl: "https://dom.spec.whatwg.org/#dom-element-classlist",
      keywords: ["classList", "toggle", "state", "css"],
      template: "const {name} = document.querySelector(\"{selector}\");\n{name}?.classList.toggle(\"{className}\");",
      parameters: [
        parameter("name", "Variable", "The selected element binding.", "panel", "JavaScript identifier"),
        parameter("selector", "Selector", "A selector locating the element.", ".panel", "CSS selector"),
        parameter("className", "Class", "The class token to toggle.", "is-open", "Class token")
      ],
      exampleTitle: "Toggle an open state",
      exampleCode: "const panel = document.querySelector(\".panel\");\npanel?.classList.toggle(\"is-open\");",
      exampleExplanation:
        "The element's class attribute and matching CSS state change without rebuilding the element.",
      shell: "External"
    }),
    entry({
      id: "web.create-element",
      name: "createElement and append",
      displayName: "Create and append an element",
      description: "Constructs an element, gives it text, and appends it to a parent.",
      definition:
        "createElement constructs an element in the document without attaching it. append adds the node or text at the end of a parent node's children.",
      moduleName: "DOM",
      action: "Create",
      object: "Node",
      documentationUrl: "https://dom.spec.whatwg.org/#dom-document-createelement",
      keywords: ["createElement", "append", "node", "dom"],
      template: "const {name} = document.createElement(\"{tag}\");\n{name}.textContent = \"{text}\";\ndocument.querySelector(\"{parent}\")?.append({name});",
      parameters: [
        parameter("name", "Variable", "The binding for the new element.", "item", "JavaScript identifier"),
        parameter("tag", "Tag name", "The local name of the HTML element to create.", "li", "HTML tag name"),
        parameter("text", "Text", "Text content placed in the new element.", "Verify", "Plain text"),
        parameter("parent", "Parent selector", "A selector locating the insertion parent.", "#steps", "CSS selector")
      ],
      exampleTitle: "Append a list item",
      exampleCode: "const item = document.createElement(\"li\");\nitem.textContent = \"Verify\";\ndocument.querySelector(\"#steps\")?.append(item);",
      exampleExplanation:
        "textContent inserts text without parsing it as HTML, then append attaches the finished item to the list.",
      shell: "External"
    }),
    entry({
      id: "web.fetch-json",
      name: "fetch JSON",
      displayName: "Fetch and read JSON",
      description: "Requests a resource, checks its status, and parses a JSON body.",
      definition:
        "fetch returns a promise for a Response. HTTP error statuses still produce responses, so code should check ok before consuming and trusting the body.",
      moduleName: "Fetch",
      action: "Request",
      object: "Response",
      documentationUrl: "https://fetch.spec.whatwg.org/#dom-global-fetch",
      keywords: ["fetch", "response", "json", "http", "async"],
      template: "async function {name}() {\n  const response = await fetch(\"{url}\");\n  if (!response.ok) throw new Error(`HTTP ${response.status}`);\n  return response.json();\n}\n\n{name}().then(console.log).catch(console.error);",
      parameters: [
        parameter("name", "Function name", "The async function that performs the request.", "loadStatus", "JavaScript identifier"),
        parameter("url", "URL", "The request URL, relative or absolute.", "/api/status", "URL")
      ],
      exampleTitle: "Load an API status",
      exampleCode: "async function loadStatus() {\n  const response = await fetch(\"/api/status\");\n  if (!response.ok) throw new Error(`HTTP ${response.status}`);\n  return response.json();\n}\n\nloadStatus().then(console.log).catch(console.error);",
      exampleExplanation:
        "The function rejects non-success HTTP responses and returns the promise produced by parsing a successful JSON body.",
      shell: "External"
    }),
    entry({
      id: "web.local-storage",
      name: "localStorage",
      displayName: "Store and read a local preference",
      description: "Persists a small string value for the page's origin.",
      definition:
        "localStorage exposes origin-scoped string key/value storage that normally persists across browsing sessions. Structured data must be serialized explicitly.",
      moduleName: "Web Storage",
      action: "Store",
      object: "Storage",
      documentationUrl: "https://html.spec.whatwg.org/multipage/webstorage.html#the-localstorage-attribute",
      keywords: ["localStorage", "preference", "origin", "persistence"],
      template: "localStorage.setItem(\"{key}\", \"{value}\");\nconst {name} = localStorage.getItem(\"{key}\") ?? \"{fallback}\";\nconsole.log({name});",
      parameters: [
        parameter("key", "Key", "The origin-scoped storage key.", "theme", "String"),
        parameter("value", "Value", "The string value to store.", "dark", "String"),
        parameter("name", "Variable", "The binding that receives the stored or fallback value.", "theme", "JavaScript identifier"),
        parameter("fallback", "Fallback", "The value used when the key is absent.", "system", "String")
      ],
      exampleTitle: "Remember a theme",
      exampleCode: "localStorage.setItem(\"theme\", \"dark\");\nconst theme = localStorage.getItem(\"theme\") ?? \"system\";\nconsole.log(theme);",
      exampleExplanation:
        "Both the key and value are strings; nullish coalescing handles a missing key without treating an empty stored string as absent.",
      shell: "External"
    }),
    entry({
      id: "web.form-data",
      name: "FormData",
      displayName: "Read submitted form data",
      description: "Collects successful controls from a form in a submit handler.",
      definition:
        "Constructing FormData with a form captures its successful controls as entries. preventDefault can keep the browser from performing the form's normal navigation.",
      moduleName: "Forms",
      action: "Collect",
      object: "Form",
      documentationUrl: "https://xhr.spec.whatwg.org/#interface-formdata",
      keywords: ["form", "FormData", "submit", "preventDefault"],
      template: "const {name} = document.querySelector(\"{selector}\");\n{name}?.addEventListener(\"submit\", (event) => {\n  event.preventDefault();\n  const data = new FormData({name});\n  console.log(data.get(\"{field}\"));\n});",
      parameters: [
        parameter("name", "Form variable", "The selected form binding.", "form", "JavaScript identifier"),
        parameter("selector", "Selector", "A selector locating the form.", "#profile", "CSS selector"),
        parameter("field", "Field", "The name of the form control to read.", "displayName", "Form field name")
      ],
      exampleTitle: "Read a profile name",
      exampleCode: "const form = document.querySelector(\"#profile\");\nform?.addEventListener(\"submit\", (event) => {\n  event.preventDefault();\n  const data = new FormData(form);\n  console.log(data.get(\"displayName\"));\n});",
      exampleExplanation:
        "The native submit event preserves keyboard submission behavior while FormData reads the named control.",
      shell: "External"
    }),
    entry({
      id: "web.url-search-params",
      name: "URLSearchParams",
      displayName: "Read a query-string value",
      description: "Parses and reads application parameters from the current URL.",
      definition:
        "URLSearchParams represents the ordered query parameter list of a URL. get returns the first matching value or null.",
      moduleName: "URL",
      action: "Collect",
      object: "URL",
      documentationUrl: "https://url.spec.whatwg.org/#urlsearchparams",
      keywords: ["url", "query", "searchParams", "get"],
      template: "const params = new URLSearchParams(window.location.search);\nconst {name} = params.get(\"{key}\") ?? \"{fallback}\";\nconsole.log({name});",
      parameters: [
        parameter("name", "Variable", "The binding that receives the query value.", "mode", "JavaScript identifier"),
        parameter("key", "Parameter", "The query parameter name.", "mode", "String"),
        parameter("fallback", "Fallback", "The value used when the parameter is absent.", "default", "String")
      ],
      exampleTitle: "Read a mode parameter",
      exampleCode: "const params = new URLSearchParams(window.location.search);\nconst mode = params.get(\"mode\") ?? \"default\";\nconsole.log(mode);",
      exampleExplanation:
        "The current query string is parsed according to URL rules and a missing mode receives an explicit fallback.",
      shell: "External"
    }),
    entry({
      id: "web.set-timeout",
      name: "setTimeout",
      displayName: "Schedule delayed work",
      description: "Queues a callback after at least the requested delay.",
      definition:
        "setTimeout schedules a timer task. The delay is a minimum rather than a guarantee because the callback waits for the event loop to run it.",
      moduleName: "Timers",
      action: "Schedule",
      object: "Timer",
      documentationUrl: "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timers",
      keywords: ["timer", "delay", "callback", "event loop"],
      template: "const timerId = window.setTimeout(() => {\n  {statement}\n}, {delay});\n\n// Call window.clearTimeout(timerId) before it runs to cancel it.",
      parameters: [
        parameter("statement", "Callback body", "Browser JavaScript run by the timer task.", "console.log(\"Ready\");", "JavaScript statement"),
        parameter("delay", "Delay", "The minimum delay in milliseconds.", "500", "Non-negative number")
      ],
      exampleTitle: "Report readiness after a delay",
      exampleCode: "const timerId = window.setTimeout(() => {\n  console.log(\"Ready\");\n}, 500);\n\n// Call window.clearTimeout(timerId) before it runs to cancel it.",
      exampleExplanation:
        "The identifier can cancel the pending task, while the callback runs no earlier than the requested half-second delay.",
      shell: "External"
    }),
    entry({
      id: "web.history-push-state",
      name: "history.pushState",
      displayName: "Update application history",
      description: "Adds a same-origin session-history entry without reloading the page.",
      definition:
        "pushState adds a session-history entry containing optional state and a same-origin URL. Applications should also handle popstate when users navigate backward or forward.",
      moduleName: "Navigation",
      action: "Navigate",
      object: "History",
      documentationUrl: "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-history-pushstate",
      keywords: ["history", "pushState", "spa", "popstate", "url"],
      template: "history.pushState({ {stateKey}: {stateValue} }, \"\", \"{url}\");\nwindow.addEventListener(\"popstate\", (event) => {\n  console.log(event.state);\n});",
      parameters: [
        parameter("stateKey", "State key", "A property stored in the history entry state object.", "view", "JavaScript identifier"),
        parameter("stateValue", "State value", "A structured-cloneable value stored for the entry.", "\"settings\"", "JavaScript expression"),
        parameter("url", "URL", "A same-origin URL represented by the new entry.", "/settings", "Same-origin URL")
      ],
      exampleTitle: "Navigate to a settings view",
      exampleCode: "history.pushState({ view: \"settings\" }, \"\", \"/settings\");\nwindow.addEventListener(\"popstate\", (event) => {\n  console.log(event.state);\n});",
      exampleExplanation:
        "The displayed URL and history stack change without a document load, and popstate observes later traversal.",
      shell: "External"
    })
  ]
});

const entryDirectory = path.join(outputDirectory, "entries");
const guideDirectory = path.join(outputDirectory, "guides");

const pathSegment = (kind, name, displayName) => ({
  kind,
  name,
  ...(displayName && displayName !== name ? { displayName } : {})
});

const inferSymbolKind = (command) => {
  const value = `${command.name} ${command.object ?? ""}`.toLowerCase();
  if (value.includes("class") || value.includes("record")) return "Class";
  if (value.includes("interface")) return "Interface";
  if (value.includes("selector")) return "Selector";
  if (value.includes("element") || command.id.startsWith("html.")) return "Element";
  if (value.includes("property")) return "Property";
  if (value.includes("method") || command.name.includes(".")) return "Method";
  if (value.includes("function") || command.action === "Define") return "Function";
  if (value.includes("operator")) return "Operator";
  if (command.moduleName === "Built-ins") return "BuiltIn";
  return "Statement";
};

const taxonomyEntry = (
  command,
  browsePath,
  { symbolKind = inferSymbolKind(command), coverageTier = "Guided" } = {}) => ({
  ...command,
  symbolKind,
  coverageTier,
  browsePath
});

const corePath = (command) => [
  pathSegment(
    "Category",
    command.moduleName === "Language" ? command.action : command.moduleName),
  pathSegment(inferSymbolKind(command), command.name, command.displayName)
];

const standardPath = (command, typeName, memberName, memberKind = "Member") => [
  pathSegment("Module", command.moduleName),
  ...(typeName ? [pathSegment("Type", typeName)] : []),
  pathSegment(memberKind, memberName ?? command.name, command.displayName)
];

const indexedEntry = ({
  id,
  name,
  description,
  moduleName,
  documentationUrl,
  browsePath,
  shell,
  symbolKind = "Api",
  template = name,
  keywords = [],
  legacyIds = []
}) => ({
  id,
  name,
  displayName: name,
  description,
  definition: description,
  moduleName,
  commandType: "Reference index",
  preferredShellKind: shell,
  documentationUrl,
  keywords,
  symbolKind,
  coverageTier: "Indexed",
  browsePath,
  ...(legacyIds.length ? { legacyIds } : {}),
  template,
  compositionMode: "Statement",
  parameters: [],
  examples: []
});

const pythonStandardCoordinates = {
  "python.dataclass": ["dataclass", null, "Function"],
  "python.pathlib-read-text": ["Path", "read_text", "Method"],
  "python.json-loads": [null, "loads", "Function"],
  "python.urlopen": [null, "urlopen", "Function"]
};
const verifiedLegacyPythonStandardGuideIds = new Set([
  "python.dataclass",
  "python.json-loads"
]);

const javaStandardCoordinates = {
  "java.println": ["java.lang", "System.out", "println", "Method"],
  "java.formatted-string": ["java.lang", "String", "formatted", "Method"],
  "java.array-list": ["java.util", "ArrayList", "ArrayList", "Constructor"],
  "java.map-of": ["java.util", "Map", "of", "Method"],
  "java.lambda-function": ["java.util.function", "Function", "apply", "Method"],
  "java.stream-pipeline": ["java.util.stream", "Stream", "pipeline", "Method"],
  "java.optional": ["java.util", "Optional", "orElse", "Method"],
  "java.files-read-string": ["java.nio.file", "Files", "readString", "Method"],
  "java.completable-future": ["java.util.concurrent", "CompletableFuture", "supplyAsync", "Method"]
};

const pythonCoreEntries = python.commands
  .filter(command => ["Language", "Built-ins"].includes(command.moduleName))
  .map(command => taxonomyEntry(
    command,
    corePath(command),
    { coverageTier: "Verified" }));
const pythonStandardEntries = python.commands
  .filter(command => !["Language", "Built-ins"].includes(command.moduleName))
  .map(command => {
    const [typeName, memberName, memberKind] =
      pythonStandardCoordinates[command.id] ?? [null, command.name, "Function"];
    return taxonomyEntry(
      command,
      standardPath(command, typeName, memberName, memberKind),
      {
        symbolKind: memberKind === "Method" ? "Method" : "Function",
        coverageTier: verifiedLegacyPythonStandardGuideIds.has(command.id)
          ? "Verified"
          : "Guided"
      });
  });

const expandedPythonGuides = pythonGuideExpansion.map(item => {
  const {
    libraryId,
    canonicalSymbolName,
    ...command
  } = item;
  return { libraryId, canonicalSymbolName, command };
});
pythonCoreEntries.push(...expandedPythonGuides
  .filter(item => item.libraryId === "python-core")
  .map(item => item.command));
pythonStandardEntries.push(...expandedPythonGuides
  .filter(item => item.libraryId === "python-standard-library")
  .map(item => item.command));

const pythonInventoryModules = pythonInventory.entries
  .filter(entry => entry.role === "py:module")
  .map(entry => entry.name)
  .sort((left, right) => right.length - left.length);
const guidedPythonSymbols = new Set([
  "dataclasses.dataclass",
  "pathlib.Path.read_text",
  "json.loads",
  "urllib.request.urlopen",
  ...expandedPythonGuides
    .filter(item => item.libraryId === "python-standard-library")
    .map(item => item.canonicalSymbolName)
    .filter(Boolean)
]);
const pythonRoleKind = {
  "py:module": "Other",
  "py:function": "Function",
  "py:class": "Class",
  "py:exception": "Class",
  "py:method": "Method",
  "py:classmethod": "Method",
  "py:staticmethod": "Method",
  "py:attribute": "Property",
  "py:property": "Property",
  "py:data": "Constant"
};
const pythonCoreInventoryPattern =
  /^library\/(functions|stdtypes|exceptions|constants|builtins)\.html/;
const pythonCoreInventoryCategory = (entry) => {
  if (entry.uri.startsWith("library/functions.html")) {
    return "Built-in Functions";
  }
  if (entry.uri.startsWith("library/stdtypes.html")) {
    return "Built-in Types";
  }
  if (entry.uri.startsWith("library/exceptions.html")) {
    return "Built-in Exceptions";
  }
  if (entry.uri.startsWith("library/constants.html")) {
    return "Built-in Constants";
  }
  return "Built-in Namespace";
};
const guidedPythonCoreNames = new Set(
  pythonCoreEntries.map(command => command.name));
const pythonCoreIndexedSymbols = pythonInventory.entries
  .filter(entry => pythonCoreInventoryPattern.test(entry.uri))
  .filter(entry => !guidedPythonCoreNames.has(entry.name))
  .map(entry => {
    const symbolKind = pythonRoleKind[entry.role] ?? "Other";
    const category = pythonCoreInventoryCategory(entry);
    const segments = entry.name.split(".");
    const memberRole = new Set([
      "py:method",
      "py:classmethod",
      "py:staticmethod",
      "py:attribute",
      "py:property"
    ]).has(entry.role);
    const browsePath = [pathSegment("Category", category)];
    if (memberRole && segments.length > 1) {
      browsePath.push(pathSegment("Type", segments.slice(0, -1).join(".")));
      browsePath.push(pathSegment(symbolKind, segments.at(-1)));
    } else {
      browsePath.push(pathSegment(symbolKind, entry.name));
    }

    const roleSlug = entry.role.replace("py:", "");
    return indexedEntry({
      id: `python.core.${entry.name}.${roleSlug}`
        .replaceAll("_", "-")
        .toLowerCase(),
      name: entry.displayName,
      description: `${entry.name} is a public Python ${pythonInventory.sourceVersion} built-in ${roleSlug} available without importing a library.`,
      moduleName: category,
      documentationUrl: `https://docs.python.org/3.14/${entry.uri}`,
      browsePath,
      shell: "Python",
      symbolKind,
      template: entry.name,
      keywords: ["python", "core", "built-in", category, roleSlug]
    });
  });
pythonCoreEntries.push(...pythonCoreIndexedSymbols);
const pythonIndexedSymbols = pythonInventory.entries
  .filter(entry => !pythonCoreInventoryPattern.test(entry.uri))
  .filter(entry => !guidedPythonSymbols.has(entry.name))
  .map(entry => {
    const moduleName = entry.role === "py:module"
      ? entry.name
      : pythonInventoryModules.find(module =>
          entry.name === module || entry.name.startsWith(`${module}.`))
        ?? entry.name.split(".")[0];
    const remainder = entry.name === moduleName
      ? ""
      : entry.name.slice(moduleName.length + 1);
    const segments = remainder ? remainder.split(".") : [];
    const symbolKind = pythonRoleKind[entry.role] ?? "Other";
    const memberRole = new Set([
      "py:method",
      "py:classmethod",
      "py:staticmethod",
      "py:attribute",
      "py:property"
    ]).has(entry.role);
    const browsePath = [pathSegment("Module", moduleName)];
    if (memberRole && segments.length > 1) {
      browsePath.push(pathSegment("Type", segments.slice(0, -1).join(".")));
      browsePath.push(pathSegment(symbolKind, segments.at(-1)));
    } else if (segments.length > 0) {
      browsePath.push(pathSegment(symbolKind, segments.join(".")));
    }

    const roleSlug = entry.role.replace("py:", "");
    return indexedEntry({
      id: `python.stdlib.${entry.name}.${roleSlug}`
        .replaceAll("_", "-")
        .toLowerCase(),
      name: entry.displayName,
      description: `${entry.name} is a documented public Python ${pythonInventory.sourceVersion} standard-library ${roleSlug}.`,
      moduleName,
      documentationUrl: `https://docs.python.org/3.14/${entry.uri}`,
      browsePath,
      shell: "Python",
      symbolKind,
      template: entry.name,
      keywords: ["python", "standard library", moduleName, roleSlug]
    });
  });
pythonStandardEntries.push(...pythonIndexedSymbols);

const expandedJavaScriptNodeGuides = javascriptNodeGuideExpansion.map(item => {
  const inventory = item.libraryId === "javascript-core"
    ? ecmascriptInventory
    : nodeInventory;
  const matches = item.canonical
    ? inventory.entries.filter(candidate => {
        if (item.libraryId === "javascript-core") {
          return (candidate.typeName ?? null) === (item.canonical.typeName ?? null)
            && candidate.name === item.canonical.name
            && (!item.canonical.signatureIncludes
              || candidate.signature.includes(item.canonical.signatureIncludes));
        }

        return candidate.moduleName === item.canonical.moduleName
          && (candidate.typeName ?? null) === (item.canonical.typeName ?? null)
          && candidate.name === item.canonical.name;
      })
    : [];
  if (item.canonical && matches.length !== 1) {
    throw new Error(
      `Guide '${item.command.displayName}' matched ${matches.length} canonical `
      + `${item.libraryId} inventory entries.`);
  }

  const canonicalEntry = matches[0] ?? null;
  const browsePath = item.command.browsePath ?? (
    item.libraryId === "javascript-core"
      ? [
          canonicalEntry.typeName
            ? pathSegment("Built-in Object", canonicalEntry.typeName)
            : pathSegment("Category", "Global Functions"),
          pathSegment(
            canonicalEntry.symbolKind,
            canonicalEntry.name,
            canonicalEntry.displayName)
        ]
      : [
          pathSegment("Module", canonicalEntry.moduleName),
          ...(canonicalEntry.typeName
            ? [pathSegment("Type", canonicalEntry.typeName)]
            : []),
          pathSegment(
            canonicalEntry.symbolKind,
            canonicalEntry.name,
            canonicalEntry.displayName)
        ]);
  return {
    libraryId: item.libraryId,
    canonicalEntryId: canonicalEntry?.id ?? "",
    command: {
      ...item.command,
      id: item.command.id ?? canonicalEntry.id,
      name: item.command.name ?? canonicalEntry.name,
      moduleName: item.command.moduleName
        ?? canonicalEntry.typeName
        ?? canonicalEntry.moduleName
        ?? "Global Functions",
      documentationUrl: item.command.documentationUrl
        ?? canonicalEntry.documentationUrl,
      symbolKind: item.command.symbolKind ?? canonicalEntry.symbolKind,
      browsePath
    }
  };
});
const expandedEcmascriptEntryIds = new Set(expandedJavaScriptNodeGuides
  .filter(item => item.libraryId === "javascript-core")
  .map(item => item.canonicalEntryId)
  .filter(Boolean));
const expandedNodeEntryIds = new Set(expandedJavaScriptNodeGuides
  .filter(item => item.libraryId === "nodejs-standard-library")
  .map(item => item.canonicalEntryId)
  .filter(Boolean));

const javascriptCoreEntries = javascript.commands
  .filter(command => command.moduleName !== "node:fs/promises"
                     && command.moduleName !== "Console"
                     && command.moduleName !== "Web API")
  .map(command => taxonomyEntry(
    command,
    [
      pathSegment(
        command.moduleName === "Language" ? "Category" : "Built-in Object",
        command.moduleName === "Language" ? command.action : command.moduleName),
      pathSegment(inferSymbolKind(command), command.name, command.displayName)
    ],
    { coverageTier: "Verified" }));
javascriptCoreEntries.push(...expandedJavaScriptNodeGuides
  .filter(item => item.libraryId === "javascript-core")
  .map(item => item.command));
const guidedEcmascriptSymbols = new Set([
  "Array\u001fmap",
  "Array\u001ffilter",
  "Promise\u001fall",
  "JSON\u001fparse"
]);
const ecmascriptIndexedSymbols = ecmascriptInventory.entries
  .filter(item => !expandedEcmascriptEntryIds.has(item.id))
  .filter(item => !guidedEcmascriptSymbols.has([
    item.typeName ?? "",
    item.name
  ].join("\u001f")))
  .map(item => {
    const browsePath = item.typeName
      ? [pathSegment("Built-in Object", item.typeName)]
      : [pathSegment("Category", "Global Functions")];
    const isTypeEntry = item.typeName
      && item.typeName === item.name
      && new Set(["Class", "Constant"]).has(item.symbolKind);
    if (!isTypeEntry) {
      browsePath.push(pathSegment(item.symbolKind, item.name));
      if (item.overloadCount > 1) {
        browsePath.push(pathSegment("Overload", item.signature));
      }
    }

    return indexedEntry({
      id: item.id,
      name: item.displayName || item.name,
      description: `${item.signature} is a language-defined ${ecmascriptInventory.sourceVersion} built-in surface.`,
      moduleName: item.typeName ?? "Global Functions",
      documentationUrl: item.documentationUrl,
      browsePath,
      shell: "NodeJs",
      symbolKind: item.symbolKind,
      template: item.signature,
      keywords: [
        "javascript",
        "ecmascript",
        "built-in",
        item.typeName ?? "global",
        item.name
      ]
    });
  });
javascriptCoreEntries.push(...ecmascriptIndexedSymbols);
const nodeStandardEntries = javascript.commands
  .filter(command => command.moduleName === "node:fs/promises"
                     || command.moduleName === "Console")
  .map(command => {
    if (command.moduleName === "Console") {
      return taxonomyEntry(command, [
        pathSegment("Module", "node:console"),
        pathSegment("Type", "Console"),
        pathSegment("Method", "log", command.displayName)
      ], { symbolKind: "Method", coverageTier: "Verified" });
    }

    return taxonomyEntry(command, [
      pathSegment("Module", command.moduleName),
      pathSegment("Function", "readFile", command.displayName)
    ], { symbolKind: "Function", coverageTier: "Verified" });
  });
nodeStandardEntries.push(...expandedJavaScriptNodeGuides
  .filter(item => item.libraryId === "nodejs-standard-library")
  .map(item => item.command));

const guidedNodeSymbols = new Set([
  "node:console\u001fConsole\u001flog",
  "node:fs/promises\u001f\u001freadFile"
]);
const nodeIndexedSymbols = nodeInventory.entries
  .filter(item => !expandedNodeEntryIds.has(item.id))
  .filter(item => !guidedNodeSymbols.has([
    item.moduleName,
    item.typeName ?? "",
    item.name
  ].join("\u001f")))
  .map(item => {
    const browsePath = [pathSegment("Module", item.moduleName)];
    if (item.typeName) {
      browsePath.push(pathSegment("Type", item.typeName));
    }
    if (item.symbolKind !== "Module") {
      browsePath.push(pathSegment(item.symbolKind, item.name, item.displayName));
      if (item.overloadCount > 1) {
        browsePath.push(pathSegment("Overload", item.signature));
      }
    }

    return indexedEntry({
      id: item.id,
      name: item.displayName || item.name,
      description: `${item.signature} is a documented public ${nodeInventory.sourceVersion} API in ${item.moduleName}.`,
      moduleName: item.moduleName,
      documentationUrl: item.documentationUrl,
      browsePath,
      shell: "NodeJs",
      symbolKind: item.symbolKind === "Module" ? "Other" : item.symbolKind,
      template: item.signature,
      keywords: [
        "node.js",
        "standard library",
        item.moduleName,
        item.typeName ?? "",
        item.symbolKind
      ].filter(Boolean)
    });
  });
nodeStandardEntries.push(...nodeIndexedSymbols);

const typescriptCoreEntries = typescript.commands.map(command =>
  taxonomyEntry(
    command,
    corePath(command),
    { coverageTier: "Verified" }));
const typescriptUtilityTypes = [
  ["Awaited", "T", "Recursively unwraps the value type produced by a Promise-like input."],
  ["Partial", "T", "Makes every property in an object type optional."],
  ["Required", "T", "Makes every property in an object type required."],
  ["Readonly", "T", "Makes every property in an object type readonly."],
  ["Record", "Keys, Type", "Constructs an object type with the selected keys and value type."],
  ["Pick", "T, Keys", "Selects a named subset of properties from an object type."],
  ["Omit", "T, Keys", "Removes a named subset of properties from an object type."],
  ["Exclude", "UnionType, ExcludedMembers", "Removes assignable members from a union type."],
  ["Extract", "UnionType, Members", "Keeps assignable members of a union type."],
  ["NonNullable", "T", "Removes null and undefined from a type."],
  ["Parameters", "FunctionType", "Produces a tuple containing a function type's parameters."],
  ["ConstructorParameters", "ConstructorType", "Produces a tuple containing a constructor type's parameters."],
  ["ReturnType", "FunctionType", "Extracts the return type of a function type."],
  ["InstanceType", "ConstructorType", "Extracts the instance type constructed by a constructor type."],
  ["NoInfer", "T", "Blocks inference into a type while preserving the resulting type."],
  ["ThisParameterType", "FunctionType", "Extracts an explicit this parameter from a function type."],
  ["OmitThisParameter", "FunctionType", "Removes an explicit this parameter from a function type."],
  ["ThisType", "T", "Marks the contextual this type for an object literal."],
  ["Uppercase", "StringType", "Transforms string-literal characters to uppercase."],
  ["Lowercase", "StringType", "Transforms string-literal characters to lowercase."],
  ["Capitalize", "StringType", "Transforms the first character of a string-literal type to uppercase."],
  ["Uncapitalize", "StringType", "Transforms the first character of a string-literal type to lowercase."]
];
typescriptCoreEntries.push(...typescriptUtilityTypes.map(
  ([name, typeParameters, description]) => indexedEntry({
    id: `typescript.core.utility.${name.toLowerCase()}`,
    name,
    description,
    moduleName: "Type system",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/utility-types.html",
    browsePath: [
      pathSegment("Category", "Utility Types"),
      pathSegment("Built-in", name)
    ],
    shell: "TypeScript",
    symbolKind: "BuiltIn",
    template: `${name}<${typeParameters}>`,
    keywords: ["typescript", "utility type", name]
  })));
const expandedTypeScriptGuides = typescriptGuideExpansion.map(item => {
  if (item.libraryId === "typescript-core") {
    return {
      libraryId: item.libraryId,
      canonicalEntryId: "",
      command: item.command
    };
  }

  const matches = typeScriptCompilerInventory.entries.filter(candidate =>
    candidate.namespacePath.join(".") === item.canonical.namespacePath.join(".")
    && (candidate.typeName ?? null) === (item.canonical.typeName ?? null)
    && candidate.name === item.canonical.name
    && (!item.canonical.signatureIncludes
      || candidate.signature.includes(item.canonical.signatureIncludes)));
  if (matches.length !== 1) {
    throw new Error(
      `Guide '${item.command.displayName}' matched ${matches.length} canonical `
      + "TypeScript Compiler API inventory entries.");
  }

  const canonicalEntry = matches[0];
  const browsePath = [pathSegment(
    "Package",
    typeScriptCompilerInventory.package.name)];
  for (const namespaceName of canonicalEntry.namespacePath) {
    browsePath.push(pathSegment("Namespace", namespaceName));
  }
  if (canonicalEntry.typeName) {
    browsePath.push(pathSegment("Type", canonicalEntry.typeName));
  }
  browsePath.push(pathSegment(
    canonicalEntry.symbolKind,
    canonicalEntry.name,
    item.command.displayName));

  return {
    libraryId: item.libraryId,
    canonicalEntryId: canonicalEntry.id,
    command: {
      ...item.command,
      name: canonicalEntry.name,
      moduleName: canonicalEntry.typeName
        ?? canonicalEntry.namespacePath.join("."),
      symbolKind: canonicalEntry.symbolKind,
      browsePath,
      legacyIds: [...new Set([
        ...(item.command.legacyIds ?? []),
        canonicalEntry.id,
        ...(canonicalEntry.legacyIds ?? [])
      ].filter(id => id !== item.command.id))]
    }
  };
});
typescriptCoreEntries.push(...expandedTypeScriptGuides
  .filter(item => item.libraryId === "typescript-core")
  .map(item => item.command));
const javaCoreEntries = java.commands
  .filter(command => command.moduleName === "Java language")
  .map(command => taxonomyEntry(
    command,
    corePath(command),
    { coverageTier: "Verified" }));
javaCoreEntries.push(...javaLanguageInventory.entries.map(item =>
  indexedEntry({
    id: item.id,
    name: item.displayName || item.name,
    description: item.description,
    moduleName: item.category,
    documentationUrl: item.documentationUrl,
    browsePath: [
      pathSegment("Category", item.category),
      pathSegment(item.symbolKind, item.name, item.displayName)
    ],
    shell: "Java",
    symbolKind: item.symbolKind,
    template: item.signature,
    keywords: ["java", "core", item.category, item.name]
  })));
const javaStandardEntries = java.commands
  .filter(command => command.moduleName !== "Java language")
  .map(command => {
    const [packageName, typeName, memberName, memberKind] =
      javaStandardCoordinates[command.id]
      ?? ["java.lang", command.object, command.name, "Member"];
    return taxonomyEntry(command, [
      pathSegment("Module", "java.base"),
      pathSegment("Package", packageName),
      pathSegment("Type", typeName),
      pathSegment(memberKind, memberName, command.displayName)
    ], {
      symbolKind: memberKind === "Constructor" ? "Constructor" : "Method",
      coverageTier: "Verified"
    });
  });
const javaMemberOverloadCounts = new Map();
for (const item of javaInventory.entries) {
  if (!new Set(["Method", "Constructor"]).has(item.symbolKind)) continue;
  const key = [
    item.moduleName,
    item.packageName,
    item.typeName,
    item.symbolKind,
    item.name
  ].join("\u001f");
  javaMemberOverloadCounts.set(
    key,
    (javaMemberOverloadCounts.get(key) ?? 0) + 1);
}
const javaIndexedSymbols = javaInventory.entries.map(item => {
  const browsePath = [
    pathSegment("Module", item.moduleName),
    pathSegment("Package", item.packageName),
    pathSegment("Type", item.typeName)
  ];
  if (item.symbolKind !== "Other") {
    browsePath.push(pathSegment(item.symbolKind, item.name));
    const overloadKey = [
      item.moduleName,
      item.packageName,
      item.typeName,
      item.symbolKind,
      item.name
    ].join("\u001f");
    if ((javaMemberOverloadCounts.get(overloadKey) ?? 0) > 1) {
      browsePath.push(pathSegment("Overload", item.signature));
    }
  }

  return indexedEntry({
    id: item.id,
    name: item.displayName || item.name,
    description: `${item.signature} is a documented public ${javaInventory.sourceVersion} API in ${item.packageName}.`,
    moduleName: item.moduleName,
    documentationUrl: item.documentationUrl,
    browsePath,
    shell: "Java",
    symbolKind: item.symbolKind,
    template: item.signature,
    keywords: [
      "java",
      "java se",
      item.moduleName,
      item.packageName,
      item.typeName,
      item.symbolKind
    ]
  });
});
javaStandardEntries.push(...javaIndexedSymbols);
const htmlCoreEntries = html.commands.map(command => taxonomyEntry(command, [
  pathSegment("Element category", command.moduleName),
  pathSegment("Element", command.name, command.displayName)
], { symbolKind: "Element" }));
htmlCoreEntries.push(...htmlInventory.entries.map(item => indexedEntry({
  id: item.id,
  name: item.name,
  description: item.description || `${item.displayName} is defined by the HTML Standard.`,
  moduleName: item.category,
  documentationUrl: item.documentationUrl,
  browsePath: [
    pathSegment(
      item.symbolKind === "Attribute" ? "Attribute category" : "Element category",
      item.category),
    pathSegment(item.symbolKind, item.name, item.displayName)
  ],
  shell: "External",
  symbolKind: item.symbolKind,
  template: item.signature,
  keywords: ["html", item.symbolKind.toLowerCase(), item.category, item.name]
})));

const cssLibraryMap = {
  "css.flex-layout": "css-flexible-box-layout",
  "css.grid-layout": "css-grid-layout",
  "css.typography": "css-fonts",
  "css.transition": "css-transitions",
  "css.keyframes": "css-animations"
};
const cssCoreEntries = css.commands
  .filter(command => !cssLibraryMap[command.id])
  .map(command => taxonomyEntry(command, [
    pathSegment("Feature group", command.moduleName),
    pathSegment(inferSymbolKind(command), command.name, command.displayName)
  ]));
cssCoreEntries.push(...cssInventory.entries
  .filter(item => item.surfaceId === "css-core")
  .map(item => indexedEntry({
    id: item.id,
    name: item.name,
    description: `${item.signature} is a ${item.status} CSS property indexed from ${cssInventory.sourceVersion}.`,
    moduleName: "Properties",
    documentationUrl: item.documentationUrl,
    browsePath: [
      pathSegment("Feature group", "Properties"),
      pathSegment("Property", item.name)
    ],
    shell: "External",
    symbolKind: "Property",
    template: `${item.name}: `,
    keywords: ["css", "property", item.name, item.status]
  })));

const webLibraryMap = {
  DOM: "dom-standard",
  "DOM Events": "dom-standard",
  Fetch: "fetch-standard",
  "Web Storage": "web-storage-standard",
  Forms: "xhr-standard",
  URL: "url-standard",
  Timers: "html-timers",
  Navigation: "html-navigation"
};

const csharpGuideEntry = ({
  id,
  name,
  displayName,
  description,
  documentationUrl,
  template,
  parameters = [],
  symbolKind = "Statement",
  category = "Language constructs"
}) => ({
  id: `csharp.core.${id}`,
  name,
  displayName,
  description,
  definition: description,
  moduleName: "C# language",
  commandType: "Language pattern",
  preferredShellKind: "CSharp",
  documentationUrl,
  keywords: ["c#", "core", category.toLowerCase()],
  symbolKind,
  coverageTier: "Verified",
  browsePath: [
    pathSegment("Category", category),
    pathSegment(symbolKind, name, displayName)
  ],
  template,
  compositionMode: "Statement",
  parameters,
  examples: [{
    title: displayName,
    code: parameters.reduce(
      (code, item) => code.replaceAll(
        `{${item.placeholder ?? item.name}}`,
        item.defaultValue),
      template),
    explanation: description
  }]
});

const csharpLanguageDocumentation =
  "https://learn.microsoft.com/dotnet/csharp/language-reference";
const csharpCoreEntries = [
  csharpGuideEntry({
    id: "variable", name: "variable declaration", displayName: "Declare an inferred local",
    description: "A var local receives a compile-time type from its initializer; it is not dynamically typed.",
    documentationUrl: `${csharpLanguageDocumentation}/statements/declarations`,
    template: "var {name} = {value};",
    parameters: [
      parameter("name", "Variable name", "The local identifier.", "answer", "C# identifier"),
      parameter("value", "Initializer", "A C# expression that determines the local type.", "42", "C# expression")
    ]
  }),
  csharpGuideEntry({
    id: "constant", name: "const declaration", displayName: "Declare a constant",
    description: "A const local is evaluated at compile time and cannot be assigned after declaration.",
    documentationUrl: `${csharpLanguageDocumentation}/keywords/const`,
    template: "const int {name} = {value};",
    parameters: [
      parameter("name", "Constant name", "The constant identifier.", "Answer", "C# identifier"),
      parameter("value", "Constant value", "A compile-time integer expression.", "42", "C# expression")
    ]
  }),
  csharpGuideEntry({
    id: "if", name: "if statement", displayName: "Branch with if",
    description: "An if statement executes one branch when its Boolean condition is true and an optional else branch otherwise.",
    documentationUrl: `${csharpLanguageDocumentation}/statements/selection-statements`,
    template: "var value = 42;\nif (value > 0)\n{\n    value++;\n}\nelse\n{\n    value = 0;\n}"
  }),
  csharpGuideEntry({
    id: "switch-expression", name: "switch expression", displayName: "Map a value with switch",
    description: "A switch expression evaluates ordered patterns and produces one result value.",
    documentationUrl: `${csharpLanguageDocumentation}/operators/switch-expression`,
    template: "var code = 2;\nvar label = code switch\n{\n    1 => \"one\",\n    2 => \"two\",\n    _ => \"other\"\n};"
  }),
  csharpGuideEntry({
    id: "foreach", name: "foreach statement", displayName: "Iterate a sequence",
    description: "A foreach statement binds each element exposed by an enumerable or enumerator pattern.",
    documentationUrl: `${csharpLanguageDocumentation}/statements/iteration-statements`,
    template: "var total = 0;\nforeach (var value in new[] { 1, 2, 3 })\n{\n    total += value;\n}"
  }),
  csharpGuideEntry({
    id: "for", name: "for statement", displayName: "Run a counted loop",
    description: "A for statement combines initialization, a continuation condition, and iteration expressions.",
    documentationUrl: `${csharpLanguageDocumentation}/statements/iteration-statements`,
    template: "var total = 0;\nfor (var index = 0; index < 4; index++)\n{\n    total += index;\n}"
  }),
  csharpGuideEntry({
    id: "while", name: "while statement", displayName: "Repeat while a condition holds",
    description: "A while statement tests its Boolean condition before each iteration.",
    documentationUrl: `${csharpLanguageDocumentation}/statements/iteration-statements`,
    template: "var remaining = 3;\nwhile (remaining > 0)\n{\n    remaining--;\n}"
  }),
  csharpGuideEntry({
    id: "try-catch", name: "try and catch", displayName: "Handle an exception",
    description: "A try statement transfers control to a matching catch clause when an exception escapes its body.",
    documentationUrl: `${csharpLanguageDocumentation}/statements/exception-handling-statements`,
    template: "try\n{\n    throw new InvalidOperationException(\"example\");\n}\ncatch (InvalidOperationException)\n{\n    // Handle the expected failure.\n}"
  }),
  csharpGuideEntry({
    id: "local-function", name: "local function", displayName: "Declare a local function",
    description: "A local function is a named method-like declaration scoped to its containing member.",
    documentationUrl: `${csharpLanguageDocumentation}/statements/local-functions`,
    template: "static int Double(int value) => value * 2;\nvar answer = Double(21);",
    symbolKind: "Function",
    category: "Functions"
  }),
  csharpGuideEntry({
    id: "lambda", name: "lambda expression", displayName: "Create a lambda",
    description: "A lambda expression creates an anonymous function that converts to a compatible delegate or expression-tree type.",
    documentationUrl: `${csharpLanguageDocumentation}/operators/lambda-expressions`,
    template: "Func<int, int> doubleValue = value => value * 2;\nvar answer = doubleValue(21);",
    symbolKind: "Function",
    category: "Functions"
  }),
  csharpGuideEntry({
    id: "tuple", name: "tuple expression", displayName: "Create and deconstruct a tuple",
    description: "Tuple syntax groups a fixed number of values and supports positional or named deconstruction.",
    documentationUrl: `${csharpLanguageDocumentation}/builtin-types/value-tuples`,
    template: "var point = (X: 20, Y: 22);\nvar (x, y) = point;\nvar answer = x + y;",
    category: "Data shapes"
  }),
  csharpGuideEntry({
    id: "property-pattern", name: "property pattern", displayName: "Match object properties",
    description: "A property pattern applies nested patterns to accessible properties of a non-null input value.",
    documentationUrl: `${csharpLanguageDocumentation}/operators/patterns`,
    template: "var item = new { Status = \"ready\", Count = 42 };\nvar isReady = item is { Status: \"ready\", Count: > 0 };",
    category: "Pattern matching"
  }),
  csharpGuideEntry({
    id: "collection-expression", name: "collection expression", displayName: "Create a collection value",
    description: "A collection expression uses bracket syntax and spread elements to construct a compatible collection target.",
    documentationUrl: `${csharpLanguageDocumentation}/operators/collection-expressions`,
    template: "int[] first = [1, 2];\nint[] combined = [.. first, 3, 4];",
    category: "Data shapes"
  }),
  csharpGuideEntry({
    id: "null-coalescing", name: "null-coalescing operator", displayName: "Provide a null fallback",
    description: "The ?? operator evaluates its right operand only when the left operand is null.",
    documentationUrl: `${csharpLanguageDocumentation}/operators/null-coalescing-operator`,
    template: "string? value = null;\nvar result = value ?? \"fallback\";",
    symbolKind: "Operator",
    category: "Operators"
  }),
  csharpGuideEntry({
    id: "range", name: "range expression", displayName: "Slice with a range",
    description: "Range syntax selects an interval whose start is inclusive and end is exclusive.",
    documentationUrl: `${csharpLanguageDocumentation}/operators/member-access-operators#index-from-end-operator-`,
    template: "int[] values = [1, 2, 3, 4];\nvar middle = values[1..^1];",
    symbolKind: "Operator",
    category: "Operators"
  })
];

const csharpKeywordCategoryLabel = (category) => {
  const words = category.replaceAll("_", " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
};
csharpCoreEntries.push(...csharpKeywordCatalog.keywords
  .filter(item => !item.lexicalClass.toLowerCase().includes("preview"))
  .map(item => indexedEntry({
    id: `csharp.core.keyword.${item.keyword === "_" ? "underscore" : item.keyword}`,
    name: item.keyword,
    description: item.description,
    moduleName: csharpKeywordCategoryLabel(item.category),
    documentationUrl: `${csharpLanguageDocumentation}/keywords/`,
    browsePath: [
      pathSegment("Category", csharpKeywordCategoryLabel(item.category)),
      pathSegment("Keyword", item.keyword)
    ],
    shell: "CSharp",
    symbolKind: "Keyword",
    template: item.keyword,
    keywords: [
      "c#",
      "core",
      "keyword",
      item.lexicalClass,
      item.category
    ]
  })));

const cppGuideEntry = ({
  id,
  name,
  displayName,
  description,
  documentationUrl,
  template,
  example,
  browsePath,
  symbolKind,
  moduleName = "C++ language"
}) => ({
  id,
  name,
  displayName,
  description,
  definition: description,
  moduleName,
  commandType: "Language pattern",
  preferredShellKind: "Cpp",
  documentationUrl,
  keywords: ["c++", symbolKind.toLowerCase(), moduleName],
  symbolKind,
  coverageTier: "Guided",
  browsePath,
  template,
  compositionMode: "Statement",
  parameters: [],
  examples: [{
    title: displayName,
    code: example,
    explanation: description
  }]
});

const cppCoreDocumentation = "https://en.cppreference.com/w/cpp/language";
const cppCoreEntries = [
  ["variable", "variable declaration", "Declare a typed value", "A typed declaration creates an object whose lifetime and type are known at compile time.", "int answer = 42;\nvolatile int sink = answer;", "Statement"],
  ["conditional", "if statement", "Branch with if", "An if statement evaluates a contextual Boolean expression and executes one selected branch.", "int value = 42;\nif (value > 0) { value += 1; } else { value = 0; }", "Statement"],
  ["counted-loop", "for statement", "Run a counted loop", "A for statement combines initialization, a continuation condition, and an iteration expression.", "int total = 0;\nfor (int index = 0; index < 4; ++index) { total += index; }", "Statement"],
  ["range-loop", "range-for statement", "Iterate a native array", "A range-based for statement visits each element exposed by a compatible range expression.", "int values[]{1, 2, 3};\nint total = 0;\nfor (int value : values) { total += value; }", "Statement"],
  ["lambda", "lambda expression", "Create a lambda", "A lambda expression creates an unnamed function object and explicitly controls captured state.", "auto doubleValue = [](int value) { return value * 2; };\nvolatile int answer = doubleValue(21);", "Function"],
  ["class", "class declaration", "Declare a class", "A class defines a user type with explicit access control, constructors, state, and member functions.", "class Task { public: explicit Task(int value) : value_(value) {} int value() const { return value_; } private: int value_; };\nTask task{42};\nvolatile int answer = task.value();", "Class"],
  ["scoped-enum", "enum class declaration", "Declare a scoped enum", "A scoped enumeration keeps enumerator names inside the enum type and does not implicitly convert to integers.", "enum class Status { pending, ready };\nStatus status = Status::ready;\n(void)status;", "Enum"],
  ["generic-lambda", "generic lambda", "Create a generic lambda", "A generic lambda uses placeholder parameter types to generate a call operator for each used argument type.", "auto identity = [](auto value) { return value; };\nvolatile int answer = identity(42);", "Function"],
  ["structured-binding", "structured binding", "Decompose an object", "A structured binding introduces names for the elements of an array or aggregate-like object.", "struct Point { int x; int y; };\nauto [x, y] = Point{20, 22};\nvolatile int answer = x + y;", "Statement"],
  ["exception", "try and catch", "Handle an exception", "A try block transfers control to a matching catch handler when an exception is thrown.", "int result = 0;\ntry { throw 42; } catch (int value) { result = value; }\nvolatile int answer = result;", "Statement"]
].map(([id, name, displayName, description, template, symbolKind]) =>
  cppGuideEntry({
    id: `cpp.core.${id}`,
    name,
    displayName,
    description,
    documentationUrl: `${cppCoreDocumentation}.html`,
    template,
    example: template,
    browsePath: [
      pathSegment("Category", symbolKind === "Class" || symbolKind === "Enum" ? "Types" : "Language constructs"),
      pathSegment(symbolKind, name, displayName)
    ],
    symbolKind
  }));

const cppCoreIndexedEntries = cppLanguageInventory.entries.map(item =>
  indexedEntry({
    id: item.id,
    name: item.displayName || item.name,
    description: item.description,
    moduleName: item.category,
    documentationUrl: item.documentationUrl,
    browsePath: [
      pathSegment("Category", item.category),
      pathSegment(item.symbolKind, item.name, item.displayName)
    ],
    shell: "Cpp",
    symbolKind: item.symbolKind,
    template: item.signature,
    keywords: ["c++", "core", item.category, item.name]
  }));
cppCoreEntries.push(...cppCoreIndexedEntries);

const cppStandardGuides = [
  ["iostream.cout", "<iostream>", "ostream", "operator<<", "Write to standard output", "Writes values to the standard output stream through the formatted insertion operator.", "#include <iostream>\nstd::cout << \"Hello from Hypershell\" << '\\n';", "Method"],
  ["vector", "<vector>", "vector", "vector", "Create a dynamic array", "std::vector owns a contiguous, dynamically sized sequence of elements.", "#include <vector>\nstd::vector<int> values{1, 2, 3};\nvolatile auto count = values.size();", "Constructor"],
  ["string.find", "<string>", "basic_string", "find", "Find text in a string", "std::string::find returns the first matching position or std::string::npos.", "#include <string>\nstd::string text = \"Hypershell\";\nvolatile auto position = text.find(\"shell\");", "Method"],
  ["algorithm.sort", "<algorithm>", "algorithm", "sort", "Sort a range", "std::sort orders a random-access range using an optional comparison function.", "#include <algorithm>\n#include <vector>\nstd::vector<int> values{3, 1, 2};\nstd::sort(values.begin(), values.end());", "Function"],
  ["unordered-map", "<unordered_map>", "unordered_map", "operator[]", "Store key-value pairs", "std::unordered_map stores unique keys in an average constant-time hash table.", "#include <string>\n#include <unordered_map>\nstd::unordered_map<std::string, int> counts{{\"ready\", 42}};\nvolatile int answer = counts[\"ready\"];", "Method"],
  ["filesystem.path", "<filesystem>", "filesystem::path", "path", "Represent a filesystem path", "std::filesystem::path represents a native filesystem pathname without performing I/O by itself.", "#include <filesystem>\nstd::filesystem::path path{\"README.md\"};\nvolatile bool empty = path.empty();", "Constructor"],
  ["optional", "<optional>", "optional", "value_or", "Represent an optional value", "std::optional stores either one value or an explicit empty state.", "#include <optional>\nstd::optional<int> value = 42;\nvolatile int answer = value.value_or(0);", "Method"],
  ["unique-ptr", "<memory>", "unique_ptr", "make_unique", "Own an object uniquely", "std::unique_ptr provides exclusive RAII ownership and destroys its object automatically.", "#include <memory>\nauto value = std::make_unique<int>(42);\nvolatile int answer = *value;", "Function"],
  ["tuple", "<tuple>", "tuple", "make_tuple", "Create a heterogeneous tuple", "std::tuple groups a fixed number of values whose element types may differ.", "#include <tuple>\nauto value = std::make_tuple(20, 22);\nvolatile int answer = std::get<0>(value) + std::get<1>(value);", "Function"],
  ["chrono.duration", "<chrono>", "chrono::duration", "milliseconds", "Represent a duration", "std::chrono durations store a tick count and a compile-time period.", "#include <chrono>\nauto duration = std::chrono::milliseconds{42};\nvolatile auto count = duration.count();", "Constructor"]
].map(([id, header, typeName, memberName, displayName, description, template, symbolKind]) =>
  cppGuideEntry({
    id: `cpp.std.${id}`,
    name: memberName,
    displayName,
    description,
    documentationUrl: `https://en.cppreference.com/w/cpp/header/${header.slice(1, -1)}`,
    template,
    example: template,
    browsePath: [
      pathSegment("Header", header),
      pathSegment("Namespace", "std"),
      pathSegment("Type", typeName),
      pathSegment(symbolKind, memberName, displayName)
    ],
    symbolKind,
    moduleName: header
  }));

const cppIndexedSymbols = [
  ["<array>", "array", "Class"], ["<deque>", "deque", "Class"],
  ["<forward_list>", "forward_list", "Class"], ["<list>", "list", "Class"],
  ["<map>", "map", "Class"], ["<map>", "multimap", "Class"],
  ["<set>", "set", "Class"], ["<set>", "multiset", "Class"],
  ["<span>", "span", "Class"], ["<stack>", "stack", "Class"],
  ["<queue>", "queue", "Class"], ["<queue>", "priority_queue", "Class"],
  ["<variant>", "variant", "Class"], ["<any>", "any", "Class"],
  ["<bitset>", "bitset", "Class"], ["<string_view>", "string_view", "Class"],
  ["<regex>", "basic_regex", "Class"], ["<random>", "mt19937", "Class"],
  ["<fstream>", "basic_ifstream", "Class"], ["<sstream>", "basic_stringstream", "Class"],
  ["<ranges>", "ranges::filter_view", "Class"], ["<ranges>", "ranges::transform_view", "Class"],
  ["<numeric>", "accumulate", "Function"], ["<numeric>", "iota", "Function"],
  ["<algorithm>", "find", "Function"], ["<algorithm>", "transform", "Function"],
  ["<functional>", "function", "Class"], ["<future>", "future", "Class"],
  ["<mutex>", "mutex", "Class"], ["<thread>", "thread", "Class"],
  ["<atomic>", "atomic", "Class"], ["<format>", "format", "Function"],
  ["<source_location>", "source_location", "Class"], ["<type_traits>", "is_same", "Class"]
].map(([header, symbol, symbolKind]) => indexedEntry({
  id: `cpp.std.${header.slice(1, -1)}.${symbol}`
    .replaceAll("::", ".")
    .replaceAll("_", "-")
    .toLowerCase(),
  name: symbol,
  description: `std::${symbol} is declared by the C++ Standard Library ${header} header.`,
  moduleName: header,
  documentationUrl: `https://en.cppreference.com/w/cpp/header/${header.slice(1, -1)}`,
  browsePath: [
    pathSegment("Header", header),
    pathSegment("Namespace", "std"),
    pathSegment(symbolKind, symbol)
  ],
  shell: "Cpp",
  symbolKind,
  template: `std::${symbol}`,
  keywords: ["c++", "standard library", header]
}));
const cppImportedSymbols = cppInventory.entries.map(item => indexedEntry({
  id: item.id,
  name: item.displayName || item.name,
  description: item.description,
  moduleName: item.header,
  documentationUrl: item.documentationUrl,
  browsePath: [
    pathSegment("Header", item.header),
    pathSegment("Namespace", item.namespace),
    pathSegment(item.symbolKind, item.name, item.displayName)
  ],
  shell: "Cpp",
  symbolKind: item.symbolKind,
  template: item.signature,
  keywords: [
    "c++",
    "standard library",
    item.header,
    item.namespace,
    item.name
  ]
}));
const cppStandardEntries = [
  ...cppStandardGuides,
  ...cppIndexedSymbols,
  ...cppImportedSymbols
];

const typeScriptCompilerApiGuides = expandedTypeScriptGuides
  .filter(item => item.libraryId === "typescript-compiler-api")
  .map(item => item.command);
const guidedCompilerApiEntryIds = new Set(expandedTypeScriptGuides
  .filter(item => item.libraryId === "typescript-compiler-api")
  .map(item => item.canonicalEntryId));
const typeScriptCompilerApiIndexed = typeScriptCompilerInventory.entries
  .filter(item => !guidedCompilerApiEntryIds.has(item.id))
  .map(item => {
    const browsePath = [pathSegment(
      "Package",
      typeScriptCompilerInventory.package.name)];
    for (const namespaceName of item.namespacePath) {
      browsePath.push(pathSegment("Namespace", namespaceName));
    }
    const isTypeEntry = item.typeName
      && item.name === item.typeName
      && new Set(["Interface", "Class", "Enum", "Other"]).has(
        item.symbolKind);
    if (item.typeName) {
      browsePath.push(pathSegment("Type", item.typeName));
    }
    if (!isTypeEntry) {
      browsePath.push(pathSegment(item.symbolKind, item.name));
      if (item.overloadCount > 1) {
        browsePath.push(pathSegment("Overload", item.signature));
      }
    }

    return indexedEntry({
      id: item.id,
      name: item.displayName || item.name,
      description: `${item.signature} is exported by the ${typeScriptCompilerInventory.sourceVersion}.`,
      moduleName: item.namespacePath.join("."),
      documentationUrl: item.documentationUrl,
      browsePath,
      shell: "TypeScript",
      symbolKind: item.symbolKind,
      template: item.signature,
      legacyIds: item.legacyIds ?? [],
      keywords: [
        "typescript",
        "compiler api",
        ...item.namespacePath,
        item.typeName ?? "",
        item.name
      ].filter(Boolean)
    });
  });
const typeScriptCompilerApiEntries = [
  ...typeScriptCompilerApiGuides,
  ...typeScriptCompilerApiIndexed
];

const libraries = [];
const chunk = (items, maximum) => {
  const chunks = [];
  for (let index = 0; index < items.length; index += maximum) {
    chunks.push(items.slice(index, index + maximum));
  }
  return chunks;
};
const addLibrary = ({
  id,
  displayName,
  description,
  language,
  kind,
  canonicalName,
  ecosystem,
  sourceUrl,
  license,
  entries,
  legacyIds = [],
  isExecutionSupported = true,
  shellMessage = "Runs through the matching installed Hypershell terminal adapter.",
  keywords = [],
  targetVersion = "Bundled baseline",
  sourceVersion = "Generated baseline",
  executionTarget = "External"
}) => {
  const coverageCounts = entries.reduce(
    (counts, entry) => {
      const tier = entry.coverageTier ?? "Guided";
      counts[tier] = (counts[tier] ?? 0) + 1;
      return counts;
    },
    { Indexed: 0, Guided: 0, Verified: 0 });
  const rawEntryChunks = chunk(entries, 5000);
  const relativeEntryPaths = rawEntryChunks.map((_, index) =>
    rawEntryChunks.length === 1
      ? `entries/${id}.entries.json`
      : `entries/${id}.${String(index + 1).padStart(3, "0")}.entries.json`);
  const relativeGuidePath = `guides/${id}.guides.json`;
  const guideEntries = entries
    .filter(entry => entry.coverageTier !== "Indexed")
    .map(entry => ({
      entryId: entry.id,
      definition: entry.definition,
      parameters: entry.parameters ?? [],
      examples: entry.examples ?? []
    }));
  const indexEntries = entries.map(entry =>
    entry.coverageTier === "Indexed"
      ? entry
      : {
          ...entry,
          definition: entry.description,
          parameters: [],
          examples: [],
          guideFile: relativeGuidePath
        });
  const indexChunks = chunk(indexEntries, 5000);
  libraries.push({
    descriptor: {
      schemaVersion: 2,
      contentVersion: "2.0.0",
      id,
      displayName,
      description,
      language,
      category: kind === "Core" ? "Language core" : "Official reference",
      kind,
      canonicalName,
      ecosystem,
      targetVersion,
      sourceVersion,
      sourceUrl,
      license,
      isExecutionSupported,
      executionSupportMessage: shellMessage,
      isStarterLibrary: kind === "Core",
      createStandaloneProfile: true,
      keywords,
      executionTarget,
      declaredEntryCount: entries.length,
      declaredIndexedCount: coverageCounts.Indexed,
      declaredGuidedCount: coverageCounts.Guided,
      declaredVerifiedCount: coverageCounts.Verified,
      entryFiles: relativeEntryPaths,
      declaredGuideCount: guideEntries.length,
      guideFiles: guideEntries.length > 0 ? [relativeGuidePath] : [],
      legacyIds
    },
    shards: indexChunks.map((shardEntries, index) => ({
      relativePath: relativeEntryPaths[index],
      content: {
        schemaVersion: 2,
        libraryId: id,
        entries: shardEntries
      }
    })),
    guideShard: guideEntries.length > 0
      ? {
          schemaVersion: 2,
          libraryId: id,
          guides: guideEntries
        }
      : null
  });
};

addLibrary({
  id: "csharp-core",
  displayName: "C# Core",
  description: "C# syntax, statements, operators, declarations, pattern matching, and language-defined constructs kept separate from .NET APIs.",
  language: "C#",
  kind: "Core",
  canonicalName: "C# Language Core",
  ecosystem: "C# / ECMA-334",
  sourceUrl: "https://learn.microsoft.com/dotnet/csharp/language-reference/",
  license: "Microsoft documentation terms; Hypershell summaries are original",
  entries: csharpCoreEntries,
  keywords: ["c#", "core", "syntax", "language"],
  targetVersion: "C# 14",
  sourceVersion: "C# 14 language reference · 127 non-preview keyword entries",
  executionTarget: "CSharp"
});

addLibrary({
  id: "python-core",
  displayName: "Python Core",
  description: "Python syntax, control flow, literals, declarations, and language-defined built-ins that require no import.",
  language: "Python",
  kind: "Core",
  canonicalName: "Python Language Core",
  ecosystem: "Python",
  sourceUrl: "https://docs.python.org/3/reference/",
  license: "Python Software Foundation License",
  entries: pythonCoreEntries,
  legacyIds: ["python-essentials"],
  keywords: ["python", "core", "syntax", "built-ins"],
  targetVersion: "Python 3.14",
  sourceVersion: `${pythonInventory.sourceVersion} · objects.inv ${pythonInventory.sourceSha256}`
});
addLibrary({
  id: "python-standard-library",
  displayName: "Python Standard Library",
  description: "Importable modules distributed with Python, organized by module, type, function, and member.",
  language: "Python",
  kind: "StandardLibrary",
  canonicalName: "The Python Standard Library",
  ecosystem: "Python",
  sourceUrl: "https://docs.python.org/3/library/",
  license: "Python Software Foundation License",
  entries: pythonStandardEntries,
  keywords: ["python", "standard library", "stdlib", "modules"],
  targetVersion: "Python 3.14",
  sourceVersion: `${pythonInventory.sourceVersion} · objects.inv ${pythonInventory.sourceSha256}`
});
addLibrary({
  id: "javascript-core",
  displayName: "JavaScript Core",
  description: "ECMAScript syntax and language-defined built-in objects without browser or Node.js platform APIs.",
  language: "JavaScript",
  kind: "Core",
  canonicalName: "ECMAScript Language Core",
  ecosystem: "ECMAScript",
  sourceUrl: "https://tc39.es/ecma262/",
  license: "Ecma International",
  entries: javascriptCoreEntries,
  legacyIds: ["javascript-essentials"],
  keywords: ["javascript", "ecmascript", "core", "built-ins"],
  targetVersion: "ECMAScript 2024",
  sourceVersion: `${ecmascriptInventory.sourceVersion} · ${Object.values(ecmascriptInventory.sourceHashes).length} pinned declaration files`
});
addLibrary({
  id: "nodejs-standard-library",
  displayName: "Node.js Standard Library",
  description: "Modules distributed with Node.js, separated from ECMAScript and browser APIs.",
  language: "JavaScript",
  kind: "StandardLibrary",
  canonicalName: "Node.js API",
  ecosystem: "Node.js",
  sourceUrl: "https://nodejs.org/api/",
  license: "MIT",
  entries: nodeStandardEntries,
  keywords: ["node.js", "standard library", "modules"],
  targetVersion: "Node.js 24",
  sourceVersion: `${nodeInventory.sourceVersion} · all.json ${nodeInventory.sourceSha256}`
});
addLibrary({
  id: "typescript-core",
  displayName: "TypeScript Core",
  description: "TypeScript syntax, type-system constructs, declarations, and globally available utility patterns.",
  language: "TypeScript",
  kind: "Core",
  canonicalName: "TypeScript Language Core",
  ecosystem: "TypeScript",
  sourceUrl: "https://www.typescriptlang.org/docs/",
  license: "Apache-2.0",
  entries: typescriptCoreEntries,
  legacyIds: ["typescript-essentials"],
  keywords: ["typescript", "core", "types", "syntax"],
  targetVersion: "TypeScript 7.0",
  sourceVersion: "TypeScript 7.0 language baseline"
});
addLibrary({
  id: "typescript-compiler-api",
  displayName: "TypeScript Compiler API",
  description: "The Microsoft-supported TypeScript 6 compatibility API for TypeScript 7 projects, separated from the current language and type-system surface.",
  language: "TypeScript",
  kind: "PlatformApi",
  canonicalName: "TypeScript Compiler API",
  ecosystem: "npm / TypeScript",
  sourceUrl: typeScriptCompilerInventory.compatibilityPackageUrl,
  license: "Apache-2.0",
  entries: typeScriptCompilerApiEntries,
  isExecutionSupported: false,
  shellMessage: "Install @typescript/typescript6@6.0.2 and retain the project lockfile before executing Compiler API code; this catalog targets its current TypeScript 6.0.3 API dependency.",
  keywords: ["typescript", "compiler api", "typescript6", "ast", "package"],
  targetVersion: "TypeScript 6.0.3 compatibility API",
  sourceVersion: `${typeScriptCompilerInventory.sourceVersion} · ${typeScriptCompilerInventory.sourceSha256}`
});
addLibrary({
  id: "java-core",
  displayName: "Java Core",
  description: "Java syntax, primitives, control flow, declarations, records, and intrinsic language constructs.",
  language: "Java",
  kind: "Core",
  canonicalName: "Java Language Core",
  ecosystem: "Java",
  sourceUrl: "https://docs.oracle.com/javase/specs/",
  license: "Oracle documentation terms",
  entries: javaCoreEntries,
  legacyIds: ["java-essentials"],
  keywords: ["java", "core", "language", "syntax"],
  targetVersion: "Java 25",
  sourceVersion: `${javaLanguageInventory.sourceVersion} · ${javaLanguageInventory.sourceSha256}`
});
addLibrary({
  id: "java-se-standard-library",
  displayName: "Java SE Standard Library",
  description: "Java SE modules and public APIs organized by module, package, type, and member.",
  language: "Java",
  kind: "StandardLibrary",
  canonicalName: "Java Platform, Standard Edition API",
  ecosystem: "Java SE",
  sourceUrl: "https://docs.oracle.com/en/java/javase/",
  license: "Oracle documentation terms",
  entries: javaStandardEntries,
  keywords: ["java", "java se", "standard library", "modules"],
  targetVersion: "Java SE 25",
  sourceVersion: `${javaInventory.sourceVersion} · Javadoc search indexes ${Object.values(javaInventory.sourceHashes).map(hash => hash.slice(0, 12)).join("/")}`
});
addLibrary({
  id: "cpp-core",
  displayName: "C++ Core",
  description: "C++ syntax, declarations, control flow, types, lambdas, structured bindings, and exception constructs that do not belong to a header library.",
  language: "C++",
  kind: "Core",
  canonicalName: "ISO C++ Language Core",
  ecosystem: "ISO C++",
  sourceUrl: "https://isocpp.org/std/the-standard",
  license: "ISO copyright; Hypershell summaries are original",
  entries: cppCoreEntries,
  keywords: ["c++", "core", "syntax", "language"],
  targetVersion: "C++20",
  sourceVersion: `${cppLanguageInventory.sourceVersion} · ${cppLanguageInventory.sourceSha256}`
});
addLibrary({
  id: "cpp-standard-library",
  displayName: "C++ Standard Library",
  description: "The C++ Standard Library organized by header, namespace, type or function, member, and overload.",
  language: "C++",
  kind: "StandardLibrary",
  canonicalName: "ISO C++ Standard Library",
  ecosystem: "ISO C++",
  sourceUrl: "https://isocpp.org/std/the-standard",
  license: "CC-BY-SA-3.0 inventory; ISO copyright; Hypershell guides original",
  entries: cppStandardEntries,
  keywords: ["c++", "standard library", "headers", "std"],
  targetVersion: "C++20",
  sourceVersion: `${cppInventory.sourceVersion} · ${cppInventory.sourceSha256}`
});
addLibrary({
  id: "html-core",
  displayName: "HTML Core",
  description: "The HTML language surface organized by canonical element and attribute categories.",
  language: "HTML",
  kind: "Core",
  canonicalName: "HTML Standard",
  ecosystem: "Web Platform",
  sourceUrl: "https://html.spec.whatwg.org/",
  license: "WHATWG Living Standard",
  entries: htmlCoreEntries,
  legacyIds: ["html-essentials"],
  isExecutionSupported: false,
  shellMessage: "HTML entries insert into compatible editor documents; HTML is not a terminal runtime.",
  keywords: ["html", "core", "elements", "attributes", "standard"],
  targetVersion: "Living Standard inventory",
  sourceVersion: `${htmlInventory.sourceVersion} · indices ${htmlInventory.sourceSha256}`
});
addLibrary({
  id: "css-core",
  displayName: "CSS Core",
  description: "CSS syntax and foundational cascade, selector, box, custom-property, and media-query features.",
  language: "CSS",
  kind: "Core",
  canonicalName: "CSS Core",
  ecosystem: "CSS",
  sourceUrl: "https://www.w3.org/Style/CSS/",
  license: "W3C document license",
  entries: cssCoreEntries,
  legacyIds: ["css-essentials"],
  isExecutionSupported: false,
  shellMessage: "CSS entries insert into compatible editor documents; CSS is not a terminal runtime.",
  keywords: ["css", "core", "cascade", "selectors"],
  targetVersion: "Living specification inventory",
  sourceVersion: `${cssInventory.sourceVersion} · properties ${cssInventory.sourceSha256}`
});

const cssSpecifications = [
  ["css-flexible-box-layout", "CSS Flexible Box Layout", "https://www.w3.org/TR/css-flexbox-1/"],
  ["css-grid-layout", "CSS Grid Layout", "https://www.w3.org/TR/css-grid-2/"],
  ["css-fonts", "CSS Fonts", "https://www.w3.org/TR/css-fonts-4/"],
  ["css-transitions", "CSS Transitions", "https://www.w3.org/TR/css-transitions-2/"],
  ["css-animations", "CSS Animations", "https://www.w3.org/TR/css-animations-2/"],
  ["css-color", "CSS Color", "https://www.w3.org/TR/css-color-4/"],
  ["css-transforms", "CSS Transforms", "https://www.w3.org/TR/css-transforms-2/"]
];
for (const [id, name, sourceUrl] of cssSpecifications) {
  const guideEntries = css.commands
    .filter(command => cssLibraryMap[command.id] === id)
    .map(command => taxonomyEntry(command, [
      pathSegment("Feature", command.moduleName),
      pathSegment(inferSymbolKind(command), command.name, command.displayName)
    ]));
  const indexedEntries = cssInventory.entries
    .filter(item => item.surfaceId === id)
    .map(item => indexedEntry({
      id: item.id,
      name: item.name,
      description: `${item.signature} is a ${item.status} property in ${name}.`,
      moduleName: "Properties",
      documentationUrl: item.documentationUrl,
      browsePath: [
        pathSegment("Feature", "Properties"),
        pathSegment("Property", item.name)
      ],
      shell: "External",
      symbolKind: "Property",
      template: `${item.name}: `,
      keywords: ["css", name, "property", item.name, item.status]
    }));
  const entries = [...guideEntries, ...indexedEntries];
  addLibrary({
    id,
    displayName: name,
    description: `${name} properties, values, and composition patterns.`,
    language: "CSS",
    kind: "Specification",
    canonicalName: name,
    ecosystem: "CSS",
    sourceUrl,
    license: "W3C document license",
    entries,
    isExecutionSupported: false,
    shellMessage: "CSS specification entries insert into compatible editor documents.",
    keywords: ["css", "specification", name],
    targetVersion: "Living specification inventory",
    sourceVersion: `${cssInventory.sourceVersion} · properties ${cssInventory.sourceSha256}`
  });
}

const webDescriptors = {
  "dom-standard": ["DOM Standard", "https://dom.spec.whatwg.org/", "WHATWG Living Standard"],
  "fetch-standard": ["Fetch Standard", "https://fetch.spec.whatwg.org/", "WHATWG Living Standard"],
  "url-standard": ["URL Standard", "https://url.spec.whatwg.org/", "WHATWG Living Standard"],
  "streams-standard": ["Streams Standard", "https://streams.spec.whatwg.org/", "WHATWG Living Standard"],
  "web-storage-standard": ["Web Storage", "https://html.spec.whatwg.org/multipage/webstorage.html", "WHATWG Living Standard"],
  "xhr-standard": ["XMLHttpRequest and FormData", "https://xhr.spec.whatwg.org/", "WHATWG Living Standard"],
  "workers-standard": ["Web Workers", "https://html.spec.whatwg.org/multipage/workers.html", "WHATWG Living Standard"],
  "canvas-standard": ["Canvas", "https://html.spec.whatwg.org/multipage/canvas.html", "WHATWG Living Standard"],
  "web-crypto-standard": ["Web Cryptography API", "https://www.w3.org/TR/WebCryptoAPI/", "W3C document license"],
  "html-timers": ["HTML Timers", "https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html", "WHATWG Living Standard"],
  "html-navigation": ["HTML Navigation and History", "https://html.spec.whatwg.org/multipage/nav-history-apis.html", "WHATWG Living Standard"]
};
for (const [id, [name, sourceUrl, license]] of Object.entries(webDescriptors)) {
  const guideEntries = webApis.commands
    .filter(command => webLibraryMap[command.moduleName] === id)
    .map(command => taxonomyEntry(command, [
      pathSegment("Interface group", command.moduleName),
      pathSegment(inferSymbolKind(command), command.name, command.displayName)
    ], { symbolKind: "Api" }))
    .map(command => command.id === "web.fetch-json"
      ? { ...command, legacyIds: ["javascript.fetch-json"] }
      : command);
  const indexedEntries = webInventory.entries
    .filter(item => item.surfaceId === id)
    .map(item => {
      const browsePath = item.typeName
        ? [pathSegment("Interface", item.typeName)]
        : [pathSegment("Category", "Global Functions")];
      const isTypeEntry = item.typeName
        && item.typeName === item.name
        && item.symbolKind === "Other";
      if (!isTypeEntry) {
        browsePath.push(pathSegment(item.symbolKind, item.name));
        if (item.overloadCount > 1) {
          browsePath.push(pathSegment("Overload", item.signature));
        }
      }
      return indexedEntry({
        id: item.id,
        name: item.displayName || item.name,
        description: `${item.signature} is a documented ${name} surface indexed from ${webInventory.sourceVersion}.`,
        moduleName: item.typeName ?? "Global Functions",
        documentationUrl: item.documentationUrl,
        browsePath,
        shell: "External",
        symbolKind: item.symbolKind,
        template: item.signature,
        keywords: ["web api", name, item.typeName ?? "global", item.name]
      });
    });
  const entries = [...guideEntries, ...indexedEntries];
  addLibrary({
    id,
    displayName: name,
    description: `${name} interfaces and browser-platform operations.`,
    language: "Web APIs",
    kind: "PlatformApi",
    canonicalName: name,
    ecosystem: "Web Platform",
    sourceUrl,
    license,
    entries,
    legacyIds: id === "dom-standard" ? ["web-api-essentials"] : [],
    isExecutionSupported: false,
    shellMessage: "Web API entries target compatible JavaScript or TypeScript editor documents.",
    keywords: ["web api", "browser", name],
    targetVersion: "Living Standard inventory",
    sourceVersion: `${webInventory.sourceVersion} · ${Object.values(webInventory.sourceHashes).map(hash => hash.slice(0, 12)).join("/")}`
  });
}

await mkdir(outputDirectory, { recursive: true });
await mkdir(entryDirectory, { recursive: true });
await mkdir(guideDirectory, { recursive: true });
for (const legacyId of [
  "python-essentials",
  "javascript-essentials",
  "typescript-essentials",
  "java-essentials",
  "html-essentials",
  "css-essentials",
  "web-api-essentials"
]) {
  await rm(path.join(outputDirectory, `${legacyId}.json`), { force: true });
}

for (const library of libraries) {
  const descriptorPath = path.join(
    outputDirectory,
    `${library.descriptor.id}.json`);
  const guidePath = path.join(
    guideDirectory,
    `${library.descriptor.id}.guides.json`);
  await writeFile(
    descriptorPath,
    `${JSON.stringify(library.descriptor, null, 2)}\n`,
    "utf8");
  for (const shard of library.shards) {
    const shardPath = path.join(outputDirectory, shard.relativePath);
    await writeFile(
      shardPath,
      // Index shards are generated data and dominate distribution size. Keep
      // descriptors and authored guides readable, but serialize indexes
      // compactly so comprehensive catalogs do not bloat the installer.
      `${JSON.stringify(shard.content)}\n`,
      "utf8");
  }
  if (library.shards.length > 1) {
    await rm(
      path.join(entryDirectory, `${library.descriptor.id}.entries.json`),
      { force: true });
  }
  if (library.guideShard) {
    await writeFile(
      guidePath,
      `${JSON.stringify(library.guideShard, null, 2)}\n`,
      "utf8");
  } else {
    await rm(guidePath, { force: true });
  }
  console.log(
    `${library.descriptor.id}|${library.descriptor.declaredEntryCount}|${library.shards.length} shard(s)|${descriptorPath}`);
}
