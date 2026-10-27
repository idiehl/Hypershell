const parameter = (
  name,
  displayName,
  description,
  defaultValue,
  typeName = "Python expression") => ({
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

const normalizeId = (value) => value
  .replaceAll("_", "-")
  .toLowerCase();

const guide = ({
  library,
  id,
  canonicalSymbolName,
  role,
  name = canonicalSymbolName,
  displayName,
  description,
  definition,
  documentationUrl,
  symbolKind,
  path,
  template,
  parameters = [],
  exampleTitle,
  exampleCode,
  exampleExplanation,
  keywords = []
}) => ({
  libraryId: library === "core"
    ? "python-core"
    : "python-standard-library",
  canonicalSymbolName: canonicalSymbolName ?? "",
  id: id ?? normalizeId(
    `python.${library === "core" ? "core" : "stdlib"}.${canonicalSymbolName}.${role.replace("py:", "")}`),
  name,
  displayName,
  description,
  definition,
  moduleName: path[0][1],
  commandType: library === "core"
    ? "Language pattern"
    : "Standard library API",
  preferredShellKind: "Python",
  documentationUrl,
  keywords: ["python", library === "core" ? "core" : "standard library", ...keywords],
  symbolKind,
  coverageTier: "Verified",
  browsePath: path.map(([kind, segmentName, segmentDisplayName]) => ({
    kind,
    name: segmentName,
    ...(segmentDisplayName && segmentDisplayName !== segmentName
      ? { displayName: segmentDisplayName }
      : {})
  })),
  template,
  compositionMode: "Statement",
  parameters,
  examples: [{
    title: exampleTitle,
    code: exampleCode,
    explanation: exampleExplanation
  }]
});

const coreFunction = (options) => guide({
  library: "core",
  role: "py:function",
  symbolKind: "Function",
  path: [
    ["Category", "Built-in Functions"],
    ["Function", options.canonicalSymbolName]
  ],
  documentationUrl:
    `https://docs.python.org/3/library/functions.html#${options.canonicalSymbolName}`,
  ...options
});

const standardGuide = (options) => guide({
  library: "standard",
  ...options
});

export const pythonGuideExpansion = [
  coreFunction({
    canonicalSymbolName: "len",
    displayName: "Count items",
    description: "Returns the number of items exposed by an object.",
    definition: "len calls an object's length protocol and returns a non-negative integer. It works with built-in sequences, collections, and custom objects that implement __len__.",
    template: "count = len({value})\nprint(count)",
    parameters: [
      parameter("value", "Value", "A sized Python object.", "[\"alpha\", \"beta\", \"gamma\"]")
    ],
    exampleTitle: "Count list items",
    exampleCode: "values = [\"alpha\", \"beta\", \"gamma\"]\nprint(len(values))",
    exampleExplanation: "The list exposes three elements, so len returns 3.",
    keywords: ["length", "size", "collection"]
  }),
  guide({
    library: "core",
    canonicalSymbolName: "range",
    role: "py:class",
    displayName: "Generate an integer range",
    description: "Creates an immutable sequence of evenly spaced integers.",
    definition: "range represents an arithmetic progression without storing every integer. The stop value is exclusive, and a custom step may be positive or negative.",
    documentationUrl: "https://docs.python.org/3/library/stdtypes.html#range",
    symbolKind: "Class",
    path: [["Category", "Built-in Types"], ["Class", "range"]],
    template: "values = list(range({start}, {stop}, {step}))\nprint(values)",
    parameters: [
      parameter("start", "Start", "The first generated integer.", "2", "Integer expression"),
      parameter("stop", "Stop", "The exclusive upper or lower bound.", "8", "Integer expression"),
      parameter("step", "Step", "The non-zero difference between values.", "2", "Integer expression")
    ],
    exampleTitle: "Generate even values",
    exampleCode: "print(list(range(2, 8, 2)))",
    exampleExplanation: "The progression starts at 2, advances by 2, and stops before 8.",
    keywords: ["sequence", "iteration", "integers"]
  }),
  coreFunction({
    canonicalSymbolName: "zip",
    displayName: "Pair iterable values",
    description: "Combines values from multiple iterables position by position.",
    definition: "zip returns an iterator of tuples. By default it stops with the shortest iterable; strict=True instead raises ValueError when lengths differ.",
    template: "pairs = list(zip({left}, {right}, strict={strict}))\nprint(pairs)",
    parameters: [
      parameter("left", "First iterable", "Values placed first in each tuple.", "[\"Ada\", \"Grace\"]", "Python iterable"),
      parameter("right", "Second iterable", "Values placed second in each tuple.", "[36, 37]", "Python iterable"),
      parameter("strict", "Require equal lengths", "Raise ValueError when the iterable lengths differ.", "True", "Boolean expression")
    ],
    exampleTitle: "Pair names and scores",
    exampleCode: "pairs = list(zip([\"Ada\", \"Grace\"], [36, 37], strict=True))\nprint(pairs)",
    exampleExplanation: "Each tuple contains values at the same position from the two equal-length lists.",
    keywords: ["pair", "tuple", "iterator", "strict"]
  }),
  coreFunction({
    canonicalSymbolName: "sorted",
    displayName: "Create a sorted list",
    description: "Returns a new list containing an iterable's values in sorted order.",
    definition: "sorted performs a stable sort and leaves the input object unchanged. key computes one comparison key per item, while reverse changes the direction.",
    template: "ordered = sorted({iterable}, key={key}, reverse={reverse})\nprint(ordered)",
    parameters: [
      parameter("iterable", "Iterable", "Values to order.", "[\"Grace\", \"ada\", \"Linus\"]", "Python iterable"),
      parameter("key", "Key function", "A callable that returns each comparison key.", "str.casefold", "Callable expression"),
      parameter("reverse", "Descending", "Use descending order when true.", "False", "Boolean expression")
    ],
    exampleTitle: "Sort names without case sensitivity",
    exampleCode: "names = [\"Grace\", \"ada\", \"Linus\"]\nprint(sorted(names, key=str.casefold))",
    exampleExplanation: "casefold supplies normalized comparison keys without changing the original strings.",
    keywords: ["sort", "stable", "key"]
  }),
  coreFunction({
    canonicalSymbolName: "any",
    displayName: "Test whether any value matches",
    description: "Returns true when at least one iterable value is truthy.",
    definition: "any short-circuits at the first truthy item and returns False for an empty iterable.",
    template: "matched = any({expression} for {item} in {iterable})\nprint(matched)",
    parameters: [
      parameter("expression", "Condition", "The Boolean expression tested for each item.", "number > 40"),
      parameter("item", "Item name", "The generator variable.", "number", "Identifier"),
      parameter("iterable", "Iterable", "Values to inspect.", "[12, 42, 7]", "Python iterable")
    ],
    exampleTitle: "Find a value above a threshold",
    exampleCode: "print(any(number > 40 for number in [12, 42, 7]))",
    exampleExplanation: "Evaluation stops when 42 satisfies the condition.",
    keywords: ["boolean", "predicate", "short circuit"]
  }),
  coreFunction({
    canonicalSymbolName: "all",
    displayName: "Test whether every value matches",
    description: "Returns true when every iterable value is truthy.",
    definition: "all short-circuits at the first false item and returns True for an empty iterable.",
    template: "matched = all({expression} for {item} in {iterable})\nprint(matched)",
    parameters: [
      parameter("expression", "Condition", "The Boolean expression tested for each item.", "number > 0"),
      parameter("item", "Item name", "The generator variable.", "number", "Identifier"),
      parameter("iterable", "Iterable", "Values to inspect.", "[3, 7, 42]", "Python iterable")
    ],
    exampleTitle: "Validate positive values",
    exampleCode: "print(all(number > 0 for number in [3, 7, 42]))",
    exampleExplanation: "Every number is positive, so all returns True.",
    keywords: ["boolean", "predicate", "validation"]
  }),
  coreFunction({
    canonicalSymbolName: "sum",
    displayName: "Add iterable values",
    description: "Adds numeric iterable values to an optional starting value.",
    definition: "sum accumulates items from left to right. It is intended for numeric addition; join should be used for strings.",
    template: "total = sum({iterable}, start={start})\nprint(total)",
    parameters: [
      parameter("iterable", "Iterable", "Numeric values to add.", "[10, 20, 12]", "Numeric iterable"),
      parameter("start", "Starting value", "A value added before the iterable items.", "0", "Numeric expression")
    ],
    exampleTitle: "Total a list",
    exampleCode: "print(sum([10, 20, 12]))",
    exampleExplanation: "The three integers add to 42.",
    keywords: ["total", "numbers", "aggregate"]
  }),
  coreFunction({
    canonicalSymbolName: "min",
    displayName: "Select the smallest value",
    description: "Returns the smallest item according to normal ordering or a key function.",
    definition: "min compares iterable values and returns the first minimal item. default supplies a result for an empty iterable.",
    template: "smallest = min({iterable}, key={key})\nprint(smallest)",
    parameters: [
      parameter("iterable", "Iterable", "Comparable values to inspect.", "[\"delta\", \"pi\", \"hypershell\"]", "Python iterable"),
      parameter("key", "Key function", "A callable producing comparison keys.", "len", "Callable expression")
    ],
    exampleTitle: "Find the shortest word",
    exampleCode: "print(min([\"delta\", \"pi\", \"hypershell\"], key=len))",
    exampleExplanation: "The key function compares word lengths, making pi the minimum.",
    keywords: ["minimum", "comparison", "key"]
  }),
  coreFunction({
    canonicalSymbolName: "max",
    displayName: "Select the largest value",
    description: "Returns the largest item according to normal ordering or a key function.",
    definition: "max compares iterable values and returns the first maximal item. default supplies a result for an empty iterable.",
    template: "largest = max({iterable}, key={key})\nprint(largest)",
    parameters: [
      parameter("iterable", "Iterable", "Comparable values to inspect.", "[\"delta\", \"pi\", \"hypershell\"]", "Python iterable"),
      parameter("key", "Key function", "A callable producing comparison keys.", "len", "Callable expression")
    ],
    exampleTitle: "Find the longest word",
    exampleCode: "print(max([\"delta\", \"pi\", \"hypershell\"], key=len))",
    exampleExplanation: "The key function compares word lengths, making hypershell the maximum.",
    keywords: ["maximum", "comparison", "key"]
  }),
  coreFunction({
    canonicalSymbolName: "isinstance",
    displayName: "Check a runtime type",
    description: "Tests whether an object is an instance of a type or type tuple.",
    definition: "isinstance accounts for inheritance and registered abstract base classes, making it preferable to comparing type(value) directly in most runtime checks.",
    template: "matched = isinstance({value}, {types})\nprint(matched)",
    parameters: [
      parameter("value", "Value", "The object to inspect.", "42"),
      parameter("types", "Type or tuple", "An accepted type or tuple of types.", "(int, float)", "Type expression")
    ],
    exampleTitle: "Accept numeric scalar types",
    exampleCode: "print(isinstance(42, (int, float)))",
    exampleExplanation: "The integer value is an instance of one of the accepted types.",
    keywords: ["type", "inheritance", "validation"]
  }),
  guide({
    library: "core",
    canonicalSymbolName: "dict.get",
    role: "py:method",
    displayName: "Read an optional dictionary value",
    description: "Returns a dictionary value or a fallback without raising KeyError.",
    definition: "dict.get looks up a key and returns default when the key is absent. It does not insert the fallback into the dictionary.",
    documentationUrl: "https://docs.python.org/3/library/stdtypes.html#dict.get",
    symbolKind: "Method",
    path: [["Category", "Built-in Types"], ["Type", "dict"], ["Method", "get"]],
    template: "value = {mapping}.get({key}, {default})\nprint(value)",
    parameters: [
      parameter("mapping", "Dictionary", "The dictionary expression.", "{\"status\": \"ready\"}", "Dictionary expression"),
      parameter("key", "Key", "The key to find.", "\"status\""),
      parameter("default", "Fallback", "The value returned when the key is absent.", "\"unknown\"")
    ],
    exampleTitle: "Read a status with a fallback",
    exampleCode: "settings = {\"status\": \"ready\"}\nprint(settings.get(\"status\", \"unknown\"))",
    exampleExplanation: "Because status exists, get returns ready rather than the fallback.",
    keywords: ["dictionary", "lookup", "fallback"]
  }),
  guide({
    library: "core",
    canonicalSymbolName: "dict.setdefault",
    role: "py:method",
    displayName: "Initialize a missing dictionary key",
    description: "Returns an existing value or inserts and returns a default.",
    definition: "dict.setdefault performs one lookup. If the key is absent, it stores default before returning it; mutable defaults should be created deliberately.",
    documentationUrl: "https://docs.python.org/3/library/stdtypes.html#dict.setdefault",
    symbolKind: "Method",
    path: [["Category", "Built-in Types"], ["Type", "dict"], ["Method", "setdefault"]],
    template: "value = {mapping}.setdefault({key}, {default})\nprint(value)",
    parameters: [
      parameter("mapping", "Dictionary", "The dictionary expression.", "{}", "Dictionary expression"),
      parameter("key", "Key", "The key to find or insert.", "\"attempts\""),
      parameter("default", "Default", "The value inserted when the key is absent.", "0")
    ],
    exampleTitle: "Initialize a counter",
    exampleCode: "state = {}\nprint(state.setdefault(\"attempts\", 0))\nprint(state)",
    exampleExplanation: "The missing attempts key is inserted with value 0 and that value is returned.",
    keywords: ["dictionary", "insert", "default"]
  }),
  guide({
    library: "core",
    canonicalSymbolName: "set.union",
    role: "py:method",
    displayName: "Combine unique values",
    description: "Returns a new set containing values from every supplied set or iterable.",
    definition: "set.union combines membership without mutating the receiver. The | operator provides equivalent behavior when both operands are sets.",
    documentationUrl: "https://docs.python.org/3/library/stdtypes.html#set.union",
    symbolKind: "Method",
    path: [["Category", "Built-in Types"], ["Type", "set"], ["Method", "union"]],
    template: "combined = {left}.union({right})\nprint(combined)",
    parameters: [
      parameter("left", "First set", "The receiving set.", "{1, 2, 3}", "Set expression"),
      parameter("right", "Additional values", "A set or iterable to combine.", "{3, 4}", "Iterable expression")
    ],
    exampleTitle: "Merge overlapping sets",
    exampleCode: "print(sorted({1, 2, 3}.union({3, 4})))",
    exampleExplanation: "The duplicate 3 appears once in the combined set.",
    keywords: ["set", "unique", "combine"]
  }),
  guide({
    library: "core",
    canonicalSymbolName: "str.join",
    role: "py:method",
    displayName: "Join text values",
    description: "Concatenates string values with a separator between them.",
    definition: "str.join consumes an iterable of strings and inserts the receiving string between adjacent items. Non-string values must be converted first.",
    documentationUrl: "https://docs.python.org/3/library/stdtypes.html#str.join",
    symbolKind: "Method",
    path: [["Category", "Built-in Types"], ["Type", "str"], ["Method", "join"]],
    template: "text = {separator}.join({values})\nprint(text)",
    parameters: [
      parameter("separator", "Separator", "A string placed between values.", "\", \"", "String expression"),
      parameter("values", "Values", "An iterable containing only strings.", "[\"alpha\", \"beta\", \"gamma\"]", "String iterable")
    ],
    exampleTitle: "Build comma-separated text",
    exampleCode: "print(\", \".join([\"alpha\", \"beta\", \"gamma\"]))",
    exampleExplanation: "join inserts a comma and space between each list item.",
    keywords: ["string", "separator", "concatenate"]
  }),
  guide({
    library: "core",
    canonicalSymbolName: "str.split",
    role: "py:method",
    displayName: "Split text into fields",
    description: "Breaks a string into a list around a delimiter or whitespace.",
    definition: "str.split returns substrings separated by sep. With sep omitted, runs of whitespace are treated as one separator and leading or trailing whitespace is ignored.",
    documentationUrl: "https://docs.python.org/3/library/stdtypes.html#str.split",
    symbolKind: "Method",
    path: [["Category", "Built-in Types"], ["Type", "str"], ["Method", "split"]],
    template: "parts = {text}.split({separator}, maxsplit={limit})\nprint(parts)",
    parameters: [
      parameter("text", "Text", "The string to divide.", "\"alpha,beta,gamma\"", "String expression"),
      parameter("separator", "Separator", "The delimiter string.", "\",\"", "String expression"),
      parameter("limit", "Maximum splits", "At most this many splits are performed.", "1", "Integer expression")
    ],
    exampleTitle: "Split once at a delimiter",
    exampleCode: "print(\"alpha,beta,gamma\".split(\",\", maxsplit=1))",
    exampleExplanation: "Only the first comma is split, leaving the rest in the second field.",
    keywords: ["string", "delimiter", "parse"]
  }),
  guide({
    library: "core",
    id: "python.core.dict-comprehension",
    name: "dictionary comprehension",
    displayName: "Build a dictionary comprehension",
    description: "Creates a dictionary by evaluating key and value expressions for iterable items.",
    definition: "A dictionary comprehension combines key and value expressions with a for clause and optional filters inside braces.",
    documentationUrl: "https://docs.python.org/3/tutorial/datastructures.html#dictionaries",
    symbolKind: "Syntax",
    path: [["Category", "Comprehensions"], ["Syntax", "dictionary comprehension"]],
    template: "result = {{ {key}: {value} for {item} in {iterable} }}\nprint(result)",
    parameters: [
      parameter("key", "Key expression", "The key produced for each item.", "number"),
      parameter("value", "Value expression", "The value produced for each item.", "number * number"),
      parameter("item", "Item name", "The loop variable.", "number", "Identifier"),
      parameter("iterable", "Iterable", "Values to transform.", "range(1, 4)", "Python iterable")
    ],
    exampleTitle: "Map numbers to squares",
    exampleCode: "squares = {number: number * number for number in range(1, 4)}\nprint(squares)",
    exampleExplanation: "Each number becomes a key whose value is its square.",
    keywords: ["comprehension", "dictionary", "transform"]
  }),
  guide({
    library: "core",
    id: "python.core.set-comprehension",
    name: "set comprehension",
    displayName: "Build a set comprehension",
    description: "Creates a set from transformed or filtered iterable values.",
    definition: "A set comprehension evaluates one expression per accepted item and automatically removes duplicate results.",
    documentationUrl: "https://docs.python.org/3/tutorial/datastructures.html#sets",
    symbolKind: "Syntax",
    path: [["Category", "Comprehensions"], ["Syntax", "set comprehension"]],
    template: "result = {{ {expression} for {item} in {iterable} }}\nprint(result)",
    parameters: [
      parameter("expression", "Output expression", "The value produced for each item.", "word.casefold()"),
      parameter("item", "Item name", "The loop variable.", "word", "Identifier"),
      parameter("iterable", "Iterable", "Values to transform.", "[\"Ada\", \"ADA\", \"Grace\"]", "Python iterable")
    ],
    exampleTitle: "Normalize unique names",
    exampleCode: "names = {word.casefold() for word in [\"Ada\", \"ADA\", \"Grace\"]}\nprint(sorted(names))",
    exampleExplanation: "Both Ada spellings normalize to one set member.",
    keywords: ["comprehension", "set", "unique"]
  }),
  guide({
    library: "core",
    id: "python.core.generator-expression",
    name: "generator expression",
    displayName: "Create a lazy generator expression",
    description: "Produces iterable values on demand instead of constructing a collection immediately.",
    definition: "A generator expression uses comprehension syntax inside parentheses and evaluates each result only when the iterator advances.",
    documentationUrl: "https://docs.python.org/3/reference/expressions.html#generator-expressions",
    symbolKind: "Syntax",
    path: [["Category", "Comprehensions"], ["Syntax", "generator expression"]],
    template: "values = ({expression} for {item} in {iterable})\nprint(list(values))",
    parameters: [
      parameter("expression", "Output expression", "The lazily produced value.", "number * 2"),
      parameter("item", "Item name", "The loop variable.", "number", "Identifier"),
      parameter("iterable", "Iterable", "Values consumed on demand.", "range(3)", "Python iterable")
    ],
    exampleTitle: "Double values lazily",
    exampleCode: "values = (number * 2 for number in range(3))\nprint(list(values))",
    exampleExplanation: "The generator computes each doubled value only while list consumes it.",
    keywords: ["generator", "lazy", "iterator"]
  }),
  guide({
    library: "core",
    id: "python.core.match-statement",
    name: "match statement",
    displayName: "Match structured values",
    description: "Selects a case by applying ordered structural patterns to one subject value.",
    definition: "A match statement evaluates its subject once and tries cases from top to bottom. The wildcard pattern _ provides a fallback.",
    documentationUrl: "https://docs.python.org/3/reference/compound_stmts.html#the-match-statement",
    symbolKind: "Statement",
    path: [["Category", "Control Flow"], ["Statement", "match"]],
    template: "match {subject}:\n    case {pattern}:\n        {statement}\n    case _:\n        {fallback}",
    parameters: [
      parameter("subject", "Subject", "The value matched once.", "status"),
      parameter("pattern", "Pattern", "The first structural pattern.", "200"),
      parameter("statement", "Matching body", "Code for a successful match.", "print(\"ok\")", "Python statement"),
      parameter("fallback", "Fallback body", "Code used when no earlier case matches.", "print(\"other\")", "Python statement")
    ],
    exampleTitle: "Classify a status code",
    exampleCode: "status = 200\nmatch status:\n    case 200:\n        print(\"ok\")\n    case _:\n        print(\"other\")",
    exampleExplanation: "The literal pattern 200 matches and executes the first case body.",
    keywords: ["match", "case", "pattern", "control flow"]
  }),
  guide({
    library: "core",
    id: "python.core.with-statement",
    name: "with statement",
    displayName: "Manage a context",
    description: "Enters a context and guarantees its exit protocol runs afterward.",
    definition: "A with statement calls the context manager's enter and exit methods, including when the body raises an exception.",
    documentationUrl: "https://docs.python.org/3/reference/compound_stmts.html#the-with-statement",
    symbolKind: "Statement",
    path: [["Category", "Resource Management"], ["Statement", "with"]],
    template: "with {context} as {name}:\n    {statement}",
    parameters: [
      parameter("context", "Context manager", "An expression implementing the context-manager protocol.", "StringIO(\"Hypershell\")"),
      parameter("name", "Bound name", "The value returned while entering the context.", "stream", "Identifier"),
      parameter("statement", "Body", "Code executed inside the managed context.", "print(stream.read())", "Python statement")
    ],
    exampleTitle: "Read a managed in-memory stream",
    exampleCode: "from io import StringIO\n\nwith StringIO(\"Hypershell\") as stream:\n    print(stream.read())",
    exampleExplanation: "The stream is available in the body and closes automatically when the block ends.",
    keywords: ["context manager", "resource", "cleanup"]
  }),
  guide({
    library: "core",
    id: "python.core.class-declaration",
    name: "class declaration",
    displayName: "Define a class",
    description: "Creates a user-defined type containing state and reusable behavior.",
    definition: "A class statement executes its body in a new namespace and creates a class object. Instance methods receive the instance through their first parameter by convention named self.",
    documentationUrl: "https://docs.python.org/3/tutorial/classes.html#class-objects",
    symbolKind: "Class",
    path: [["Category", "Classes"], ["Class", "class declaration"]],
    template: "class {name}:\n    def __init__(self, {field}):\n        self.{field} = {field}\n\n    def {method}(self):\n        return {expression}\n\nvalue = {name}({sample})\nprint(value.{method}())",
    parameters: [
      parameter("name", "Class name", "The new type's identifier.", "Counter", "Identifier"),
      parameter("field", "Field name", "An initialized instance attribute.", "value", "Identifier"),
      parameter("method", "Method name", "A method invoked by the example.", "increment", "Identifier"),
      parameter("expression", "Return expression", "The method result expression.", "self.value + 1"),
      parameter("sample", "Sample value", "The constructor argument.", "41")
    ],
    exampleTitle: "Create and use a counter",
    exampleCode: "class Counter:\n    def __init__(self, value):\n        self.value = value\n\n    def increment(self):\n        return self.value + 1\n\nprint(Counter(41).increment())",
    exampleExplanation: "Construction stores 41 and the instance method returns 42.",
    keywords: ["class", "object", "method", "self"]
  }),
  guide({
    library: "core",
    id: "python.core.sequence-unpacking",
    name: "sequence unpacking",
    displayName: "Unpack iterable values",
    description: "Assigns iterable elements to multiple targets in one statement.",
    definition: "Unpacking requires the target shape to match the iterable, unless one starred target collects the remaining values into a list.",
    documentationUrl: "https://docs.python.org/3/tutorial/datastructures.html#tuples-and-sequences",
    symbolKind: "Syntax",
    path: [["Category", "Assignment"], ["Syntax", "sequence unpacking"]],
    template: "{first}, *{middle}, {last} = {iterable}\nprint({first}, {middle}, {last})",
    parameters: [
      parameter("first", "First target", "Receives the first item.", "first", "Identifier"),
      parameter("middle", "Starred target", "Collects the middle items as a list.", "middle", "Identifier"),
      parameter("last", "Last target", "Receives the final item.", "last", "Identifier"),
      parameter("iterable", "Iterable", "The values to unpack.", "[1, 2, 3, 4]", "Python iterable")
    ],
    exampleTitle: "Capture endpoints and middle values",
    exampleCode: "first, *middle, last = [1, 2, 3, 4]\nprint(first, middle, last)",
    exampleExplanation: "The starred target receives the two values between the endpoints.",
    keywords: ["assignment", "starred", "tuple", "sequence"]
  }),

  standardGuide({
    canonicalSymbolName: "pathlib.Path.exists",
    role: "py:method",
    displayName: "Test whether a path exists",
    description: "Checks whether a filesystem path currently resolves to an existing object.",
    definition: "Path.exists follows the platform's filesystem rules and returns a Boolean instead of raising when the target is simply absent.",
    documentationUrl: "https://docs.python.org/3/library/pathlib.html#pathlib.Path.exists",
    symbolKind: "Method",
    path: [["Module", "pathlib"], ["Type", "Path"], ["Method", "exists"]],
    template: "from pathlib import Path\nexists = Path({path}).exists()\nprint(exists)",
    parameters: [parameter("path", "Path", "A path string or path-like expression.", "r\".\"", "Path expression")],
    exampleTitle: "Check the current directory",
    exampleCode: "from pathlib import Path\nprint(Path.cwd().exists())",
    exampleExplanation: "The process's current working directory exists while the program is running.",
    keywords: ["pathlib", "filesystem", "exists"]
  }),
  standardGuide({
    canonicalSymbolName: "pathlib.Path.glob",
    role: "py:method",
    displayName: "Find paths with a pattern",
    description: "Yields child paths matching a shell-style relative pattern.",
    definition: "Path.glob evaluates a pattern relative to the receiving path. The ** component enables recursive matching.",
    documentationUrl: "https://docs.python.org/3/library/pathlib.html#pathlib.Path.glob",
    symbolKind: "Method",
    path: [["Module", "pathlib"], ["Type", "Path"], ["Method", "glob"]],
    template: "from pathlib import Path\nmatches = sorted(Path({root}).glob({pattern}))\nfor match in matches:\n    print(match)",
    parameters: [
      parameter("root", "Root path", "The directory relative to which matching begins.", "r\".\"", "Path expression"),
      parameter("pattern", "Pattern", "A relative glob pattern.", "\"*.py\"", "String expression")
    ],
    exampleTitle: "Find Python files in a temporary folder",
    exampleCode: "from pathlib import Path\nfrom tempfile import TemporaryDirectory\n\nwith TemporaryDirectory() as folder:\n    root = Path(folder)\n    (root / \"one.py\").touch()\n    (root / \"two.txt\").touch()\n    print([path.name for path in root.glob(\"*.py\")])",
    exampleExplanation: "Only the file whose suffix matches .py is yielded.",
    keywords: ["pathlib", "glob", "pattern", "files"]
  }),
  standardGuide({
    canonicalSymbolName: "pathlib.Path.write_text",
    role: "py:method",
    displayName: "Write UTF-8 text",
    description: "Writes a complete string to a path using an explicit encoding.",
    definition: "Path.write_text opens the file in text mode, writes the supplied string, closes the file, and returns the number of characters written.",
    documentationUrl: "https://docs.python.org/3/library/pathlib.html#pathlib.Path.write_text",
    symbolKind: "Method",
    path: [["Module", "pathlib"], ["Type", "Path"], ["Method", "write_text"]],
    template: "from pathlib import Path\ncount = Path({path}).write_text({text}, encoding=\"utf-8\")\nprint(count)",
    parameters: [
      parameter("path", "Path", "The destination path.", "r\"output.txt\"", "Path expression"),
      parameter("text", "Text", "The complete string to write.", "\"Hypershell\\n\"", "String expression")
    ],
    exampleTitle: "Write and read temporary text",
    exampleCode: "from pathlib import Path\nfrom tempfile import TemporaryDirectory\n\nwith TemporaryDirectory() as folder:\n    path = Path(folder) / \"note.txt\"\n    path.write_text(\"Hypershell\\n\", encoding=\"utf-8\")\n    print(path.read_text(encoding=\"utf-8\").strip())",
    exampleExplanation: "The temporary file is encoded as UTF-8 and removed with its temporary directory.",
    keywords: ["pathlib", "write", "text", "utf-8"]
  }),
  standardGuide({
    canonicalSymbolName: "pathlib.Path.mkdir",
    role: "py:method",
    displayName: "Create a directory",
    description: "Creates one directory and optionally its missing parent directories.",
    definition: "Path.mkdir mirrors mkdir semantics. parents=True creates missing ancestors, while exist_ok=True suppresses the error for an existing directory.",
    documentationUrl: "https://docs.python.org/3/library/pathlib.html#pathlib.Path.mkdir",
    symbolKind: "Method",
    path: [["Module", "pathlib"], ["Type", "Path"], ["Method", "mkdir"]],
    template: "from pathlib import Path\npath = Path({path})\npath.mkdir(parents={parents}, exist_ok={exist_ok})\nprint(path)",
    parameters: [
      parameter("path", "Directory path", "The directory to create.", "r\"output/nested\"", "Path expression"),
      parameter("parents", "Create parents", "Create missing ancestor directories.", "True", "Boolean expression"),
      parameter("exist_ok", "Allow existing", "Do not fail when the directory already exists.", "True", "Boolean expression")
    ],
    exampleTitle: "Create nested temporary directories",
    exampleCode: "from pathlib import Path\nfrom tempfile import TemporaryDirectory\n\nwith TemporaryDirectory() as folder:\n    path = Path(folder) / \"output\" / \"nested\"\n    path.mkdir(parents=True)\n    print(path.is_dir())",
    exampleExplanation: "parents=True creates both missing levels beneath the temporary directory.",
    keywords: ["pathlib", "directory", "mkdir", "parents"]
  }),

  standardGuide({
    canonicalSymbolName: "os.getenv",
    role: "py:function",
    displayName: "Read an environment variable",
    description: "Returns an environment-variable value or a fallback.",
    definition: "os.getenv reads the process environment snapshot represented by os.environ and avoids KeyError when a name is absent.",
    documentationUrl: "https://docs.python.org/3/library/os.html#os.getenv",
    symbolKind: "Function",
    path: [["Module", "os"], ["Function", "getenv"]],
    template: "import os\nvalue = os.getenv({name}, {default})\nprint(value)",
    parameters: [
      parameter("name", "Variable name", "The environment-variable key.", "\"HYPERSHELL_MODE\"", "String expression"),
      parameter("default", "Fallback", "Returned when the key is absent.", "\"default\"")
    ],
    exampleTitle: "Read a temporary environment value",
    exampleCode: "import os\n\nos.environ[\"HYPERSHELL_MODE\"] = \"ready\"\ntry:\n    print(os.getenv(\"HYPERSHELL_MODE\", \"default\"))\nfinally:\n    os.environ.pop(\"HYPERSHELL_MODE\", None)",
    exampleExplanation: "getenv reads ready, and the finally block restores the process environment.",
    keywords: ["os", "environment", "configuration"]
  }),
  standardGuide({
    canonicalSymbolName: "os.listdir",
    role: "py:function",
    displayName: "List directory names",
    description: "Returns entry names contained directly in a directory.",
    definition: "os.listdir returns names without joining them to the supplied path and does not guarantee ordering. Sort the result when deterministic presentation matters.",
    documentationUrl: "https://docs.python.org/3/library/os.html#os.listdir",
    symbolKind: "Function",
    path: [["Module", "os"], ["Function", "listdir"]],
    template: "import os\nfor name in sorted(os.listdir({path})):\n    print(name)",
    parameters: [parameter("path", "Directory", "The directory to enumerate.", "r\".\"", "Path expression")],
    exampleTitle: "List temporary entries",
    exampleCode: "import os\nfrom pathlib import Path\nfrom tempfile import TemporaryDirectory\n\nwith TemporaryDirectory() as folder:\n    Path(folder, \"alpha.txt\").touch()\n    Path(folder, \"beta.txt\").touch()\n    print(sorted(os.listdir(folder)))",
    exampleExplanation: "The sorted result contains the two names created in the temporary directory.",
    keywords: ["os", "directory", "list"]
  }),
  standardGuide({
    canonicalSymbolName: "os.walk",
    role: "py:function",
    displayName: "Walk a directory tree",
    description: "Yields each visited directory with its child-directory and file names.",
    definition: "os.walk recursively traverses a tree and yields (directory, directories, files) tuples. Mutating the directories list can prune top-down traversal.",
    documentationUrl: "https://docs.python.org/3/library/os.html#os.walk",
    symbolKind: "Function",
    path: [["Module", "os"], ["Function", "walk"]],
    template: "import os\nfor directory, directories, files in os.walk({root}):\n    print(directory, sorted(files))",
    parameters: [parameter("root", "Root directory", "The traversal starting point.", "r\".\"", "Path expression")],
    exampleTitle: "Walk a temporary tree",
    exampleCode: "import os\nfrom pathlib import Path\nfrom tempfile import TemporaryDirectory\n\nwith TemporaryDirectory() as folder:\n    nested = Path(folder, \"nested\")\n    nested.mkdir()\n    (nested / \"item.txt\").touch()\n    files = [name for _, _, names in os.walk(folder) for name in names]\n    print(files)",
    exampleExplanation: "Traversal reaches the nested directory and reports its file.",
    keywords: ["os", "directory", "recursive", "tree"]
  }),
  standardGuide({
    canonicalSymbolName: "os.replace",
    role: "py:function",
    displayName: "Replace a filesystem destination",
    description: "Renames a source path to a destination, replacing an existing destination when permitted.",
    definition: "os.replace performs an atomic rename when the operating system supports it on the same filesystem. Cross-filesystem moves are not supported by this operation.",
    documentationUrl: "https://docs.python.org/3/library/os.html#os.replace",
    symbolKind: "Function",
    path: [["Module", "os"], ["Function", "replace"]],
    template: "import os\nos.replace({source}, {destination})",
    parameters: [
      parameter("source", "Source", "The existing source path.", "r\"draft.txt\"", "Path expression"),
      parameter("destination", "Destination", "The destination path to replace.", "r\"current.txt\"", "Path expression")
    ],
    exampleTitle: "Replace a temporary file",
    exampleCode: "import os\nfrom pathlib import Path\nfrom tempfile import TemporaryDirectory\n\nwith TemporaryDirectory() as folder:\n    source = Path(folder, \"draft.txt\")\n    destination = Path(folder, \"current.txt\")\n    source.write_text(\"new\", encoding=\"utf-8\")\n    destination.write_text(\"old\", encoding=\"utf-8\")\n    os.replace(source, destination)\n    print(destination.read_text(encoding=\"utf-8\"))",
    exampleExplanation: "The destination now contains the source file's new content.",
    keywords: ["os", "rename", "replace", "atomic"]
  }),
  standardGuide({
    canonicalSymbolName: "os.environ",
    role: "py:data",
    displayName: "Access the process environment",
    description: "Provides a mutable mapping of environment-variable names to string values.",
    definition: "os.environ synchronizes mapping changes with the current process environment. Values and keys are strings on Windows and most Python interfaces.",
    documentationUrl: "https://docs.python.org/3/library/os.html#os.environ",
    symbolKind: "Constant",
    path: [["Module", "os"], ["Constant", "environ"]],
    template: "import os\nos.environ[{name}] = {value}\nprint(os.environ[{name}])",
    parameters: [
      parameter("name", "Variable name", "The environment-variable key.", "\"HYPERSHELL_MODE\"", "String expression"),
      parameter("value", "Value", "The string value to store.", "\"ready\"", "String expression")
    ],
    exampleTitle: "Set and restore an environment variable",
    exampleCode: "import os\n\nkey = \"HYPERSHELL_MODE\"\nprevious = os.environ.get(key)\nos.environ[key] = \"ready\"\ntry:\n    print(os.environ[key])\nfinally:\n    if previous is None:\n        os.environ.pop(key, None)\n    else:\n        os.environ[key] = previous",
    exampleExplanation: "The mapping update is visible immediately and the original state is restored.",
    keywords: ["os", "environment", "mapping"]
  }),

  standardGuide({
    canonicalSymbolName: "sys.argv",
    role: "py:data",
    displayName: "Read command-line arguments",
    description: "Exposes the script name and command-line arguments as strings.",
    definition: "sys.argv is a list whose first item identifies the executed script or command context. Additional items preserve argument order.",
    documentationUrl: "https://docs.python.org/3/library/sys.html#sys.argv",
    symbolKind: "Constant",
    path: [["Module", "sys"], ["Constant", "argv"]],
    template: "import sys\nfor index, argument in enumerate(sys.argv):\n    print(index, argument)",
    exampleTitle: "Inspect the current argument list",
    exampleCode: "import sys\nprint(isinstance(sys.argv, list), len(sys.argv) >= 1)",
    exampleExplanation: "Python always exposes argv as a list containing at least its execution-context entry.",
    keywords: ["sys", "arguments", "command line"]
  }),
  standardGuide({
    canonicalSymbolName: "sys.version_info",
    role: "py:data",
    displayName: "Inspect the Python version",
    description: "Provides the interpreter version as a comparable named tuple.",
    definition: "sys.version_info exposes major, minor, micro, releaselevel, and serial fields and supports tuple-style version comparisons.",
    documentationUrl: "https://docs.python.org/3/library/sys.html#sys.version_info",
    symbolKind: "Constant",
    path: [["Module", "sys"], ["Constant", "version_info"]],
    template: "import sys\nprint(sys.version_info.major, sys.version_info.minor)",
    exampleTitle: "Require modern Python",
    exampleCode: "import sys\nprint(sys.version_info >= (3, 11))",
    exampleExplanation: "The named tuple compares lexicographically against a shorter version tuple.",
    keywords: ["sys", "version", "runtime"]
  }),
  standardGuide({
    canonicalSymbolName: "sys.platform",
    role: "py:data",
    displayName: "Identify the runtime platform",
    description: "Returns a stable platform identifier for broad operating-system branches.",
    definition: "sys.platform identifies the platform on which Python was built. Use platform for richer descriptive information and os interfaces for platform-neutral operations.",
    documentationUrl: "https://docs.python.org/3/library/sys.html#sys.platform",
    symbolKind: "Constant",
    path: [["Module", "sys"], ["Constant", "platform"]],
    template: "import sys\nprint(sys.platform)",
    exampleTitle: "Classify the host family",
    exampleCode: "import sys\nfamily = \"windows\" if sys.platform == \"win32\" else \"non-windows\"\nprint(family)",
    exampleExplanation: "The stable win32 value identifies Windows even on 64-bit Python.",
    keywords: ["sys", "platform", "operating system"]
  }),

  standardGuide({
    canonicalSymbolName: "json.dumps",
    role: "py:function",
    displayName: "Serialize a value to JSON text",
    description: "Converts supported Python values into one JSON string.",
    definition: "json.dumps serializes dictionaries, lists, strings, numbers, booleans, and None. Formatting and key ordering can be controlled for readable or deterministic output.",
    documentationUrl: "https://docs.python.org/3/library/json.html#json.dumps",
    symbolKind: "Function",
    path: [["Module", "json"], ["Function", "dumps"]],
    template: "import json\ntext = json.dumps({value}, indent={indent}, sort_keys={sort_keys})\nprint(text)",
    parameters: [
      parameter("value", "Value", "A JSON-serializable Python value.", "{\"status\": \"ready\", \"count\": 42}"),
      parameter("indent", "Indent", "Spaces used for each nesting level.", "2", "Integer expression"),
      parameter("sort_keys", "Sort keys", "Write object keys in sorted order.", "True", "Boolean expression")
    ],
    exampleTitle: "Create stable formatted JSON",
    exampleCode: "import json\nprint(json.dumps({\"status\": \"ready\", \"count\": 42}, sort_keys=True))",
    exampleExplanation: "sort_keys produces deterministic object-key order in the serialized text.",
    keywords: ["json", "serialize", "text"]
  }),
  standardGuide({
    canonicalSymbolName: "json.dump",
    role: "py:function",
    displayName: "Write JSON to a text stream",
    description: "Serializes one Python value directly into a writable text stream.",
    definition: "json.dump writes JSON incrementally to a file-like object's write method. The destination must accept strings rather than bytes.",
    documentationUrl: "https://docs.python.org/3/library/json.html#json.dump",
    symbolKind: "Function",
    path: [["Module", "json"], ["Function", "dump"]],
    template: "import json\njson.dump({value}, {stream}, indent={indent})",
    parameters: [
      parameter("value", "Value", "A JSON-serializable Python value.", "{\"answer\": 42}"),
      parameter("stream", "Text stream", "A writable text file-like object.", "stream", "Text stream expression"),
      parameter("indent", "Indent", "Spaces used for each nesting level.", "2", "Integer expression")
    ],
    exampleTitle: "Write JSON into memory",
    exampleCode: "import json\nfrom io import StringIO\n\nstream = StringIO()\njson.dump({\"answer\": 42}, stream, sort_keys=True)\nprint(stream.getvalue())",
    exampleExplanation: "StringIO provides the text-stream interface without creating a file.",
    keywords: ["json", "serialize", "stream"]
  }),
  standardGuide({
    canonicalSymbolName: "json.load",
    role: "py:function",
    displayName: "Read JSON from a stream",
    description: "Deserializes one JSON document from a readable text or binary stream.",
    definition: "json.load reads from a file-like object's read method and returns the corresponding Python value graph.",
    documentationUrl: "https://docs.python.org/3/library/json.html#json.load",
    symbolKind: "Function",
    path: [["Module", "json"], ["Function", "load"]],
    template: "import json\ndata = json.load({stream})\nprint(data)",
    parameters: [parameter("stream", "Input stream", "A readable stream containing one JSON document.", "stream", "Readable stream expression")],
    exampleTitle: "Read JSON from memory",
    exampleCode: "import json\nfrom io import StringIO\n\nstream = StringIO('{\"answer\": 42}')\ndata = json.load(stream)\nprint(data[\"answer\"])",
    exampleExplanation: "The in-memory JSON object becomes a dictionary whose answer value is 42.",
    keywords: ["json", "deserialize", "stream"]
  }),

  standardGuide({
    canonicalSymbolName: "datetime.date",
    role: "py:class",
    displayName: "Represent a calendar date",
    description: "Represents a year, month, and day without a time or timezone.",
    definition: "datetime.date is immutable and supports ISO parsing, calendar arithmetic with timedelta, ordering, and formatting.",
    documentationUrl: "https://docs.python.org/3/library/datetime.html#datetime.date",
    symbolKind: "Class",
    path: [["Module", "datetime"], ["Class", "date"]],
    template: "from datetime import date\nvalue = date.fromisoformat({text})\nprint(value.isoformat())",
    parameters: [parameter("text", "ISO date", "A YYYY-MM-DD string.", "\"2026-08-10\"", "String expression")],
    exampleTitle: "Parse an ISO date",
    exampleCode: "from datetime import date\nvalue = date.fromisoformat(\"2026-08-10\")\nprint(value.year, value.month, value.day)",
    exampleExplanation: "The ISO components become accessible integer fields.",
    keywords: ["datetime", "date", "iso"]
  }),
  standardGuide({
    canonicalSymbolName: "datetime.datetime",
    role: "py:class",
    displayName: "Represent a date and time",
    description: "Combines calendar fields with clock fields and an optional timezone.",
    definition: "datetime.datetime is immutable. A timezone-aware value includes tzinfo and can be converted between offsets without losing the represented instant.",
    documentationUrl: "https://docs.python.org/3/library/datetime.html#datetime.datetime",
    symbolKind: "Class",
    path: [["Module", "datetime"], ["Class", "datetime"]],
    template: "from datetime import datetime\nvalue = datetime.fromisoformat({text})\nprint(value.isoformat())",
    parameters: [parameter("text", "ISO timestamp", "An ISO 8601-compatible datetime string.", "\"2026-08-10T12:30:00+00:00\"", "String expression")],
    exampleTitle: "Parse an aware timestamp",
    exampleCode: "from datetime import datetime\nvalue = datetime.fromisoformat(\"2026-08-10T12:30:00+00:00\")\nprint(value.isoformat())",
    exampleExplanation: "The parsed value retains its explicit UTC offset.",
    keywords: ["datetime", "timestamp", "iso", "timezone"]
  }),
  standardGuide({
    canonicalSymbolName: "datetime.timedelta",
    role: "py:class",
    displayName: "Represent a duration",
    description: "Stores a normalized difference between date or datetime values.",
    definition: "datetime.timedelta represents durations with day, second, and microsecond resolution and supports arithmetic with dates, datetimes, and other durations.",
    documentationUrl: "https://docs.python.org/3/library/datetime.html#datetime.timedelta",
    symbolKind: "Class",
    path: [["Module", "datetime"], ["Class", "timedelta"]],
    template: "from datetime import timedelta\nduration = timedelta(days={days}, hours={hours})\nprint(duration.total_seconds())",
    parameters: [
      parameter("days", "Days", "Whole or fractional days.", "1", "Number expression"),
      parameter("hours", "Hours", "Whole or fractional hours.", "2", "Number expression")
    ],
    exampleTitle: "Convert a duration to seconds",
    exampleCode: "from datetime import timedelta\nduration = timedelta(minutes=42)\nprint(duration.total_seconds())",
    exampleExplanation: "Forty-two minutes equals 2520 seconds.",
    keywords: ["datetime", "duration", "arithmetic"]
  }),
  standardGuide({
    canonicalSymbolName: "datetime.timezone",
    role: "py:class",
    displayName: "Apply a fixed UTC offset",
    description: "Provides fixed-offset timezone implementations including UTC.",
    definition: "datetime.timezone implements tzinfo for fixed offsets. Use zoneinfo when daylight-saving or historical regional rules are required.",
    documentationUrl: "https://docs.python.org/3/library/datetime.html#datetime.timezone",
    symbolKind: "Class",
    path: [["Module", "datetime"], ["Class", "timezone"]],
    template: "from datetime import datetime, timezone\nvalue = datetime({year}, {month}, {day}, tzinfo=timezone.utc)\nprint(value.isoformat())",
    parameters: [
      parameter("year", "Year", "The calendar year.", "2026", "Integer expression"),
      parameter("month", "Month", "The month number.", "8", "Integer expression"),
      parameter("day", "Day", "The day of month.", "10", "Integer expression")
    ],
    exampleTitle: "Create an aware UTC datetime",
    exampleCode: "from datetime import datetime, timezone\nvalue = datetime(2026, 8, 10, 12, 30, tzinfo=timezone.utc)\nprint(value.utcoffset().total_seconds())",
    exampleExplanation: "timezone.utc supplies a zero-second offset from UTC.",
    keywords: ["datetime", "timezone", "utc", "offset"]
  }),

  standardGuide({
    canonicalSymbolName: "re.compile",
    role: "py:function",
    displayName: "Compile a regular expression",
    description: "Creates a reusable pattern object from regular-expression text.",
    definition: "re.compile parses a pattern once and returns an object with search, match, findall, and substitution methods.",
    documentationUrl: "https://docs.python.org/3/library/re.html#re.compile",
    symbolKind: "Function",
    path: [["Module", "re"], ["Function", "compile"]],
    template: "import re\npattern = re.compile({pattern}, flags={flags})\nprint(pattern.pattern)",
    parameters: [
      parameter("pattern", "Pattern", "The regular-expression source.", "r\"[A-Z]+\"", "String expression"),
      parameter("flags", "Flags", "Combined re flags modifying matching.", "re.IGNORECASE", "Flag expression")
    ],
    exampleTitle: "Compile a case-insensitive word pattern",
    exampleCode: "import re\npattern = re.compile(r\"hypershell\", re.IGNORECASE)\nprint(bool(pattern.fullmatch(\"HyperShell\")))",
    exampleExplanation: "The compiled pattern matches the complete string without case sensitivity.",
    keywords: ["re", "regex", "pattern", "compile"]
  }),
  standardGuide({
    canonicalSymbolName: "re.search",
    role: "py:function",
    displayName: "Search text with a pattern",
    description: "Finds the first location where a regular expression matches.",
    definition: "re.search scans the entire string and returns a Match object or None. Use fullmatch when the entire input must satisfy the pattern.",
    documentationUrl: "https://docs.python.org/3/library/re.html#re.search",
    symbolKind: "Function",
    path: [["Module", "re"], ["Function", "search"]],
    template: "import re\nmatch = re.search({pattern}, {text})\nprint(match.group(0) if match else None)",
    parameters: [
      parameter("pattern", "Pattern", "The regular expression to find.", "r\"\\d+\"", "String expression"),
      parameter("text", "Text", "The input string to scan.", "\"answer=42\"", "String expression")
    ],
    exampleTitle: "Find the first integer",
    exampleCode: "import re\nmatch = re.search(r\"\\d+\", \"answer=42\")\nprint(match.group(0) if match else None)",
    exampleExplanation: "The first digit run is captured as 42.",
    keywords: ["re", "regex", "search", "match"]
  }),
  standardGuide({
    canonicalSymbolName: "re.findall",
    role: "py:function",
    displayName: "Collect every regex match",
    description: "Returns all non-overlapping matches in left-to-right order.",
    definition: "re.findall returns matched strings or tuples of captured groups. Empty matches may be included according to regular-expression rules.",
    documentationUrl: "https://docs.python.org/3/library/re.html#re.findall",
    symbolKind: "Function",
    path: [["Module", "re"], ["Function", "findall"]],
    template: "import re\nmatches = re.findall({pattern}, {text})\nprint(matches)",
    parameters: [
      parameter("pattern", "Pattern", "The regular expression to collect.", "r\"\\d+\"", "String expression"),
      parameter("text", "Text", "The input string to scan.", "\"x=20, y=22\"", "String expression")
    ],
    exampleTitle: "Collect numeric fields",
    exampleCode: "import re\nprint(re.findall(r\"\\d+\", \"x=20, y=22\"))",
    exampleExplanation: "Both non-overlapping digit runs are returned in source order.",
    keywords: ["re", "regex", "find", "list"]
  }),
  standardGuide({
    canonicalSymbolName: "re.sub",
    role: "py:function",
    displayName: "Replace regex matches",
    description: "Returns text with non-overlapping matches replaced.",
    definition: "re.sub accepts either replacement text or a callable. count=0 replaces every match, while a positive count limits replacements.",
    documentationUrl: "https://docs.python.org/3/library/re.html#re.sub",
    symbolKind: "Function",
    path: [["Module", "re"], ["Function", "sub"]],
    template: "import re\nresult = re.sub({pattern}, {replacement}, {text})\nprint(result)",
    parameters: [
      parameter("pattern", "Pattern", "The regular expression to replace.", "r\"\\s+\"", "String expression"),
      parameter("replacement", "Replacement", "Text or a callable used for each match.", "\"-\""),
      parameter("text", "Text", "The input string.", "\"Hyper shell tools\"")
    ],
    exampleTitle: "Normalize whitespace",
    exampleCode: "import re\nprint(re.sub(r\"\\s+\", \"-\", \"Hyper   shell tools\"))",
    exampleExplanation: "Each whitespace run becomes one hyphen.",
    keywords: ["re", "regex", "replace", "substitution"]
  }),

  standardGuide({
    canonicalSymbolName: "collections.Counter",
    role: "py:class",
    displayName: "Count hashable values",
    description: "Builds a dictionary-like mapping from values to occurrence counts.",
    definition: "Counter supplies zero for missing keys and adds multiset operations plus helpers such as most_common and elements.",
    documentationUrl: "https://docs.python.org/3/library/collections.html#collections.Counter",
    symbolKind: "Class",
    path: [["Module", "collections"], ["Class", "Counter"]],
    template: "from collections import Counter\ncounts = Counter({values})\nprint(counts.most_common({limit}))",
    parameters: [
      parameter("values", "Values", "Hashable values to count.", "[\"ready\", \"busy\", \"ready\"]", "Python iterable"),
      parameter("limit", "Limit", "Maximum number of common items to return.", "2", "Integer expression")
    ],
    exampleTitle: "Find the most common state",
    exampleCode: "from collections import Counter\ncounts = Counter([\"ready\", \"busy\", \"ready\"])\nprint(counts.most_common(1))",
    exampleExplanation: "ready occurs twice and is returned as the most common item.",
    keywords: ["collections", "counter", "frequency"]
  }),
  standardGuide({
    canonicalSymbolName: "collections.defaultdict",
    role: "py:class",
    displayName: "Create missing values automatically",
    description: "Calls a factory to initialize values for missing keys.",
    definition: "defaultdict extends dict with a default_factory used by __missing__. Reading a missing key inserts the newly created value.",
    documentationUrl: "https://docs.python.org/3/library/collections.html#collections.defaultdict",
    symbolKind: "Class",
    path: [["Module", "collections"], ["Class", "defaultdict"]],
    template: "from collections import defaultdict\ngroups = defaultdict({factory})\nfor {key}, {value} in {pairs}:\n    groups[{key}].append({value})\nprint(dict(groups))",
    parameters: [
      parameter("factory", "Factory", "A zero-argument callable creating missing values.", "list", "Callable expression"),
      parameter("key", "Key name", "The pair variable used as a key.", "group", "Identifier"),
      parameter("value", "Value name", "The pair variable appended to the group.", "item", "Identifier"),
      parameter("pairs", "Pairs", "Key-value pairs to group.", "[(\"a\", 20), (\"a\", 22)]", "Python iterable")
    ],
    exampleTitle: "Group values by key",
    exampleCode: "from collections import defaultdict\n\ngroups = defaultdict(list)\nfor key, value in [(\"a\", 20), (\"a\", 22)]:\n    groups[key].append(value)\nprint(dict(groups))",
    exampleExplanation: "The list factory creates the first group before values are appended.",
    keywords: ["collections", "defaultdict", "group"]
  }),
  standardGuide({
    canonicalSymbolName: "collections.deque",
    role: "py:class",
    displayName: "Use a double-ended queue",
    description: "Supports efficient appends and removals at both ends.",
    definition: "deque stores a sequence optimized for operations at either endpoint. maxlen can enforce a bounded rolling window.",
    documentationUrl: "https://docs.python.org/3/library/collections.html#collections.deque",
    symbolKind: "Class",
    path: [["Module", "collections"], ["Class", "deque"]],
    template: "from collections import deque\nvalues = deque({iterable}, maxlen={limit})\nvalues.append({value})\nprint(list(values))",
    parameters: [
      parameter("iterable", "Initial values", "Values loaded from left to right.", "[1, 2, 3]", "Python iterable"),
      parameter("limit", "Maximum length", "The bounded queue size.", "3", "Integer expression"),
      parameter("value", "Appended value", "A new right-side value.", "4")
    ],
    exampleTitle: "Maintain a rolling window",
    exampleCode: "from collections import deque\nvalues = deque([1, 2, 3], maxlen=3)\nvalues.append(4)\nprint(list(values))",
    exampleExplanation: "Appending 4 evicts the oldest value to preserve the maximum length.",
    keywords: ["collections", "deque", "queue", "window"]
  }),
  standardGuide({
    canonicalSymbolName: "collections.namedtuple",
    role: "py:function",
    displayName: "Create a named tuple type",
    description: "Builds a tuple subclass whose fields are available by name and position.",
    definition: "namedtuple dynamically creates an immutable tuple subclass with generated field properties and useful conversion helpers.",
    documentationUrl: "https://docs.python.org/3/library/collections.html#collections.namedtuple",
    symbolKind: "Function",
    path: [["Module", "collections"], ["Function", "namedtuple"]],
    template: "from collections import namedtuple\n{name} = namedtuple({type_name}, {fields})\nvalue = {name}({values})\nprint(value)",
    parameters: [
      parameter("name", "Binding name", "The variable receiving the generated type.", "Point", "Identifier"),
      parameter("type_name", "Type name", "The generated class name.", "\"Point\"", "String expression"),
      parameter("fields", "Fields", "Field names as a string or iterable.", "[\"x\", \"y\"]", "String iterable"),
      parameter("values", "Values", "Constructor arguments for the fields.", "20, 22", "Argument list")
    ],
    exampleTitle: "Create a coordinate value",
    exampleCode: "from collections import namedtuple\nPoint = namedtuple(\"Point\", [\"x\", \"y\"])\npoint = Point(20, 22)\nprint(point.x + point.y)",
    exampleExplanation: "The generated immutable type exposes x and y as named fields.",
    keywords: ["collections", "namedtuple", "record", "immutable"]
  }),

  standardGuide({
    canonicalSymbolName: "itertools.chain",
    role: "py:function",
    displayName: "Chain iterables lazily",
    description: "Yields every value from each iterable in sequence.",
    definition: "itertools.chain advances through one input iterable at a time without creating an intermediate combined collection.",
    documentationUrl: "https://docs.python.org/3/library/itertools.html#itertools.chain",
    symbolKind: "Function",
    path: [["Module", "itertools"], ["Function", "chain"]],
    template: "from itertools import chain\nvalues = chain({first}, {second})\nprint(list(values))",
    parameters: [
      parameter("first", "First iterable", "The first values yielded.", "[1, 2]", "Python iterable"),
      parameter("second", "Second iterable", "Values yielded afterward.", "[3, 4]", "Python iterable")
    ],
    exampleTitle: "Combine two sequences",
    exampleCode: "from itertools import chain\nprint(list(chain([1, 2], [3, 4])))",
    exampleExplanation: "The iterator yields all four values without first concatenating the lists.",
    keywords: ["itertools", "chain", "lazy", "iterator"]
  }),
  standardGuide({
    canonicalSymbolName: "itertools.islice",
    role: "py:function",
    displayName: "Slice an iterator",
    description: "Yields a selected range from an iterable without requiring sequence indexing.",
    definition: "itertools.islice consumes an iterable using start, stop, and step values similar to slicing, but negative values are not supported.",
    documentationUrl: "https://docs.python.org/3/library/itertools.html#itertools.islice",
    symbolKind: "Function",
    path: [["Module", "itertools"], ["Function", "islice"]],
    template: "from itertools import islice\nvalues = list(islice({iterable}, {start}, {stop}, {step}))\nprint(values)",
    parameters: [
      parameter("iterable", "Iterable", "The source values.", "range(20)", "Python iterable"),
      parameter("start", "Start", "The first consumed index.", "2", "Integer expression"),
      parameter("stop", "Stop", "The exclusive ending index.", "8", "Integer expression"),
      parameter("step", "Step", "The positive interval between yielded values.", "2", "Integer expression")
    ],
    exampleTitle: "Take selected range values",
    exampleCode: "from itertools import islice\nprint(list(islice(range(20), 2, 8, 2)))",
    exampleExplanation: "The iterator yields indexes 2, 4, and 6 before the exclusive stop.",
    keywords: ["itertools", "slice", "lazy", "iterator"]
  }),
  standardGuide({
    canonicalSymbolName: "itertools.count",
    role: "py:function",
    displayName: "Generate an unbounded counter",
    description: "Yields evenly spaced values indefinitely.",
    definition: "itertools.count starts at a chosen value and repeatedly adds step. Consumers should impose a bound unless an endless stream is intentional.",
    documentationUrl: "https://docs.python.org/3/library/itertools.html#itertools.count",
    symbolKind: "Function",
    path: [["Module", "itertools"], ["Function", "count"]],
    template: "from itertools import count, islice\nvalues = list(islice(count({start}, {step}), {limit}))\nprint(values)",
    parameters: [
      parameter("start", "Start", "The first generated value.", "10", "Number expression"),
      parameter("step", "Step", "The amount added after each value.", "2", "Number expression"),
      parameter("limit", "Limit", "The number of values consumed by islice.", "4", "Integer expression")
    ],
    exampleTitle: "Bound an infinite counter",
    exampleCode: "from itertools import count, islice\nprint(list(islice(count(10, 2), 4)))",
    exampleExplanation: "islice safely consumes the first four values from the unbounded counter.",
    keywords: ["itertools", "count", "infinite", "iterator"]
  }),
  standardGuide({
    canonicalSymbolName: "itertools.zip_longest",
    role: "py:function",
    displayName: "Zip uneven iterables",
    description: "Pairs iterables until the longest input is exhausted.",
    definition: "zip_longest substitutes fillvalue after shorter iterables are exhausted, preserving rows that ordinary zip would omit.",
    documentationUrl: "https://docs.python.org/3/library/itertools.html#itertools.zip_longest",
    symbolKind: "Function",
    path: [["Module", "itertools"], ["Function", "zip_longest"]],
    template: "from itertools import zip_longest\npairs = list(zip_longest({left}, {right}, fillvalue={fill}))\nprint(pairs)",
    parameters: [
      parameter("left", "First iterable", "The first column of values.", "[1, 2, 3]", "Python iterable"),
      parameter("right", "Second iterable", "The second column of values.", "[\"a\", \"b\"]", "Python iterable"),
      parameter("fill", "Fill value", "Used after a shorter input ends.", "None")
    ],
    exampleTitle: "Preserve an unmatched row",
    exampleCode: "from itertools import zip_longest\nprint(list(zip_longest([1, 2, 3], [\"a\", \"b\"], fillvalue=None)))",
    exampleExplanation: "The final integer remains paired with the explicit fill value.",
    keywords: ["itertools", "zip", "fill", "iterator"]
  }),

  standardGuide({
    canonicalSymbolName: "subprocess.run",
    role: "py:function",
    displayName: "Run a child process",
    description: "Starts one process, waits for completion, and returns its result.",
    definition: "subprocess.run is the recommended high-level interface for bounded child-process execution. Pass an argument list instead of a shell string when shell syntax is unnecessary.",
    documentationUrl: "https://docs.python.org/3/library/subprocess.html#subprocess.run",
    symbolKind: "Function",
    path: [["Module", "subprocess"], ["Function", "run"]],
    template: "import subprocess\nresult = subprocess.run({arguments}, capture_output=True, text=True, timeout={timeout}, check=True)\nprint(result.stdout)",
    parameters: [
      parameter("arguments", "Arguments", "The executable followed by individual arguments.", "[sys.executable, \"-c\", \"print(42)\"]", "Argument-list expression"),
      parameter("timeout", "Timeout", "Maximum seconds before the child is terminated.", "10", "Number expression")
    ],
    exampleTitle: "Run the active Python interpreter",
    exampleCode: "import subprocess\nimport sys\n\nresult = subprocess.run(\n    [sys.executable, \"-c\", \"print(6 * 7)\"],\n    capture_output=True,\n    text=True,\n    timeout=10,\n    check=True)\nprint(result.stdout.strip())",
    exampleExplanation: "The argument list safely starts the current interpreter and captures its successful output.",
    keywords: ["subprocess", "process", "arguments", "timeout"]
  }),

  standardGuide({
    canonicalSymbolName: "asyncio.run",
    role: "py:function",
    displayName: "Run an async entry point",
    description: "Creates an event loop, runs one coroutine, and closes the loop.",
    definition: "asyncio.run is the normal synchronous entry point for an asyncio program and cannot be called while another event loop is already running in the same thread.",
    documentationUrl: "https://docs.python.org/3/library/asyncio-runner.html#asyncio.run",
    symbolKind: "Function",
    path: [["Module", "asyncio"], ["Function", "run"]],
    template: "import asyncio\n\nasync def {name}():\n    return {expression}\n\nprint(asyncio.run({name}()))",
    parameters: [
      parameter("name", "Coroutine name", "The async function identifier.", "main", "Identifier"),
      parameter("expression", "Result expression", "The coroutine's return value.", "6 * 7")
    ],
    exampleTitle: "Run a coroutine to completion",
    exampleCode: "import asyncio\n\nasync def main():\n    return 6 * 7\n\nprint(asyncio.run(main()))",
    exampleExplanation: "asyncio.run owns the event-loop lifecycle and returns the coroutine's value.",
    keywords: ["asyncio", "async", "event loop", "coroutine"]
  }),
  standardGuide({
    canonicalSymbolName: "asyncio.gather",
    role: "py:function",
    displayName: "Await concurrent operations",
    description: "Schedules awaitables concurrently and returns results in input order.",
    definition: "asyncio.gather aggregates multiple awaitables. Completion order may differ, but the returned result list follows the original argument order.",
    documentationUrl: "https://docs.python.org/3/library/asyncio-task.html#asyncio.gather",
    symbolKind: "Function",
    path: [["Module", "asyncio"], ["Function", "gather"]],
    template: "import asyncio\n\nasync def {worker}({argument}):\n    await asyncio.sleep(0)\n    return {expression}\n\nasync def main():\n    results = await asyncio.gather(*({worker}(value) for value in {values}))\n    print(results)\n\nasyncio.run(main())",
    parameters: [
      parameter("worker", "Worker name", "The coroutine function identifier.", "double", "Identifier"),
      parameter("argument", "Argument name", "The worker parameter.", "value", "Identifier"),
      parameter("expression", "Result expression", "The worker result.", "value * 2"),
      parameter("values", "Values", "Inputs used to create awaitables.", "[20, 21]", "Python iterable")
    ],
    exampleTitle: "Gather ordered results",
    exampleCode: "import asyncio\n\nasync def double(value):\n    await asyncio.sleep(0)\n    return value * 2\n\nasync def main():\n    print(await asyncio.gather(double(20), double(21)))\n\nasyncio.run(main())",
    exampleExplanation: "Both coroutines can make progress concurrently while results remain in argument order.",
    keywords: ["asyncio", "gather", "concurrency", "await"]
  }),
  standardGuide({
    canonicalSymbolName: "asyncio.timeout",
    role: "py:function",
    displayName: "Bound an async operation",
    description: "Cancels overdue work and converts cancellation into TimeoutError outside the context.",
    definition: "asyncio.timeout is an asynchronous context manager that applies a deadline to awaits in its body.",
    documentationUrl: "https://docs.python.org/3/library/asyncio-task.html#asyncio.timeout",
    symbolKind: "Function",
    path: [["Module", "asyncio"], ["Function", "timeout"]],
    template: "import asyncio\n\nasync def main():\n    async with asyncio.timeout({seconds}):\n        await {awaitable}\n\nasyncio.run(main())",
    parameters: [
      parameter("seconds", "Timeout", "Maximum seconds allowed inside the context.", "1", "Number expression"),
      parameter("awaitable", "Awaitable", "The operation governed by the deadline.", "asyncio.sleep(0)", "Awaitable expression")
    ],
    exampleTitle: "Complete work before a deadline",
    exampleCode: "import asyncio\n\nasync def main():\n    async with asyncio.timeout(1):\n        await asyncio.sleep(0)\n        print(\"ready\")\n\nasyncio.run(main())",
    exampleExplanation: "The cooperative sleep completes before the one-second deadline.",
    keywords: ["asyncio", "timeout", "deadline", "cancel"]
  }),
  standardGuide({
    canonicalSymbolName: "asyncio.TaskGroup",
    role: "py:class",
    displayName: "Manage related async tasks",
    description: "Runs a group of tasks and waits for all of them when leaving the context.",
    definition: "TaskGroup provides structured concurrency. If one child fails, remaining children are cancelled and failures are raised together after cleanup.",
    documentationUrl: "https://docs.python.org/3/library/asyncio-task.html#asyncio.TaskGroup",
    symbolKind: "Class",
    path: [["Module", "asyncio"], ["Class", "TaskGroup"]],
    template: "import asyncio\n\nasync def {worker}({argument}):\n    await asyncio.sleep(0)\n    return {expression}\n\nasync def main():\n    async with asyncio.TaskGroup() as group:\n        task = group.create_task({worker}({value}))\n    print(task.result())\n\nasyncio.run(main())",
    parameters: [
      parameter("worker", "Worker name", "The child coroutine identifier.", "double", "Identifier"),
      parameter("argument", "Argument name", "The child coroutine parameter.", "value", "Identifier"),
      parameter("expression", "Result expression", "The child task result.", "value * 2"),
      parameter("value", "Input", "The example input value.", "21")
    ],
    exampleTitle: "Read a completed child result",
    exampleCode: "import asyncio\n\nasync def double(value):\n    await asyncio.sleep(0)\n    return value * 2\n\nasync def main():\n    async with asyncio.TaskGroup() as group:\n        task = group.create_task(double(21))\n    print(task.result())\n\nasyncio.run(main())",
    exampleExplanation: "Leaving the TaskGroup waits for the child, after which its result is available.",
    keywords: ["asyncio", "task group", "structured concurrency"]
  })
];
