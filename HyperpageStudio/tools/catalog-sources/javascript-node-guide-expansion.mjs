const parameter = (
  name,
  displayName,
  description,
  defaultValue,
  typeName = "JavaScript expression") => ({
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

const guide = ({
  libraryId,
  canonical = null,
  id,
  name,
  displayName,
  description,
  definition,
  moduleName,
  documentationUrl,
  symbolKind,
  browsePath,
  template,
  parameters,
  exampleTitle,
  exampleCode,
  exampleExplanation,
  keywords = []
}) => ({
  libraryId,
  canonical,
  command: {
    ...(id ? { id } : {}),
    ...(name ? { name } : {}),
    displayName,
    description,
    definition,
    ...(moduleName ? { moduleName } : {}),
    commandType: libraryId === "javascript-core"
      ? "ECMAScript language surface"
      : "Node.js standard library API",
    preferredShellKind: "NodeJs",
    ...(documentationUrl ? { documentationUrl } : {}),
    keywords: [
      libraryId === "javascript-core" ? "javascript" : "node.js",
      libraryId === "javascript-core" ? "ecmascript" : "standard library",
      ...keywords
    ],
    ...(symbolKind ? { symbolKind } : {}),
    coverageTier: "Verified",
    ...(browsePath ? { browsePath } : {}),
    template,
    compositionMode: "Statement",
    parameters,
    examples: [{
      title: exampleTitle,
      code: exampleCode,
      explanation: exampleExplanation
    }]
  }
});

const jsBuiltIn = ({
  typeName = null,
  name,
  signatureIncludes,
  keywords = [],
  ...options
}) =>
  guide({
    libraryId: "javascript-core",
    canonical: { typeName, name, ...(signatureIncludes ? { signatureIncludes } : {}) },
    ...options,
    keywords: [typeName ?? "global", name, ...keywords]
  });

const jsPattern = ({
  id,
  category,
  name,
  symbolKind = "Keyword",
  keywords = [],
  ...options
}) =>
  guide({
    libraryId: "javascript-core",
    id,
    name,
    moduleName: "Language",
    symbolKind,
    browsePath: [
      { kind: "Category", name: category },
      { kind: symbolKind, name }
    ],
    ...options,
    keywords: ["syntax", category, ...keywords]
  });

const nodeApi = ({
  moduleName,
  typeName = null,
  name,
  keywords = [],
  ...options
}) => guide({
  libraryId: "nodejs-standard-library",
  canonical: { moduleName, typeName, name },
  ...options,
  keywords: [moduleName, typeName ?? "function", name, ...keywords]
});

const nodeExplicit = ({
  id,
  moduleName,
  typeName = null,
  name,
  symbolKind,
  keywords = [],
  ...options
}) =>
  guide({
    libraryId: "nodejs-standard-library",
    id,
    name,
    moduleName,
    symbolKind,
    browsePath: [
      { kind: "Module", name: moduleName },
      ...(typeName ? [{ kind: "Type", name: typeName }] : []),
      { kind: symbolKind, name }
    ],
    ...options,
    keywords: [moduleName, typeName ?? "function", name, ...keywords]
  });

const javascriptGuides = [
  jsBuiltIn({
    typeName: "Array",
    name: "reduce",
    signatureIncludes: "initialValue: U",
    displayName: "Reduce an array to one value",
    description: "Combines array elements into a single accumulated result.",
    definition: "Array.prototype.reduce invokes a reducer for each element in order. Supplying an initial value makes the accumulator type and empty-array behavior explicit.",
    template: "const result = {array}.reduce(({accumulator}, {item}) => {expression}, {initial});\nconsole.log(result);",
    parameters: [
      parameter("array", "Array", "The array expression to reduce.", "[10, 20, 12]", "Array expression"),
      parameter("accumulator", "Accumulator name", "The reducer parameter holding the current result.", "total", "Identifier"),
      parameter("item", "Item name", "The reducer parameter holding the current element.", "number", "Identifier"),
      parameter("expression", "Reducer expression", "The next accumulated value.", "total + number"),
      parameter("initial", "Initial value", "The accumulator value before the first element.", "0")
    ],
    exampleTitle: "Total numeric values",
    exampleCode: "const total = [10, 20, 12].reduce((sum, number) => sum + number, 0);\nconsole.log(total);",
    exampleExplanation: "The reducer starts at zero and adds each number to produce 42.",
    keywords: ["aggregate", "accumulator", "reducer"]
  }),
  jsBuiltIn({
    typeName: "Array",
    name: "find",
    signatureIncludes: "=> unknown",
    displayName: "Find the first matching item",
    description: "Returns the first array element accepted by a predicate.",
    definition: "Array.prototype.find evaluates elements in order and returns the first value whose predicate is truthy, or undefined when no value matches.",
    template: "const match = {array}.find(({item}) => {condition});\nconsole.log(match);",
    parameters: [
      parameter("array", "Array", "The array expression to search.", "[12, 42, 7]", "Array expression"),
      parameter("item", "Item name", "The predicate parameter.", "number", "Identifier"),
      parameter("condition", "Condition", "The matching predicate expression.", "number > 40")
    ],
    exampleTitle: "Find a value above a threshold",
    exampleCode: "console.log([12, 42, 7].find((number) => number > 40));",
    exampleExplanation: "The search stops at 42, the first value greater than 40.",
    keywords: ["search", "predicate"]
  }),
  jsBuiltIn({
    typeName: "Array",
    name: "some",
    displayName: "Test whether any item matches",
    description: "Returns true when at least one array element satisfies a predicate.",
    definition: "Array.prototype.some short-circuits at the first truthy predicate result and returns false for an empty array.",
    template: "const matched = {array}.some(({item}) => {condition});\nconsole.log(matched);",
    parameters: [
      parameter("array", "Array", "The values to test.", "[12, 42, 7]", "Array expression"),
      parameter("item", "Item name", "The predicate parameter.", "number", "Identifier"),
      parameter("condition", "Condition", "The predicate expression.", "number > 40")
    ],
    exampleTitle: "Detect a matching value",
    exampleCode: "console.log([12, 42, 7].some((number) => number > 40));",
    exampleExplanation: "42 satisfies the condition, so some returns true without testing later values.",
    keywords: ["predicate", "boolean", "short circuit"]
  }),
  jsBuiltIn({
    typeName: "Array",
    name: "every",
    signatureIncludes: "=> unknown",
    displayName: "Test whether every item matches",
    description: "Returns true only when every array element satisfies a predicate.",
    definition: "Array.prototype.every short-circuits at the first false predicate result and returns true for an empty array.",
    template: "const matched = {array}.every(({item}) => {condition});\nconsole.log(matched);",
    parameters: [
      parameter("array", "Array", "The values to test.", "[3, 7, 42]", "Array expression"),
      parameter("item", "Item name", "The predicate parameter.", "number", "Identifier"),
      parameter("condition", "Condition", "The predicate expression.", "number > 0")
    ],
    exampleTitle: "Validate all values",
    exampleCode: "console.log([3, 7, 42].every((number) => number > 0));",
    exampleExplanation: "Every number is positive, so every returns true.",
    keywords: ["predicate", "validation", "short circuit"]
  }),
  jsBuiltIn({
    typeName: "Array",
    name: "flatMap",
    displayName: "Map and flatten one level",
    description: "Transforms each item and flattens one level of returned arrays.",
    definition: "Array.prototype.flatMap behaves like map followed by flat with depth one, while allowing each input item to produce zero, one, or several output values.",
    template: "const values = {array}.flatMap(({item}) => {expression});\nconsole.log(values);",
    parameters: [
      parameter("array", "Array", "The source array.", "[1, 2, 3]", "Array expression"),
      parameter("item", "Item name", "The callback parameter.", "number", "Identifier"),
      parameter("expression", "Mapped value", "An item or array returned for each source value.", "[number, number * 10]")
    ],
    exampleTitle: "Expand every value",
    exampleCode: "console.log([1, 2, 3].flatMap((number) => [number, number * 10]));",
    exampleExplanation: "Each input produces two values and flatMap joins them into one array.",
    keywords: ["transform", "flatten"]
  }),
  jsBuiltIn({
    typeName: "Array",
    name: "includes",
    displayName: "Check whether an array contains a value",
    description: "Tests whether an array contains a specified value.",
    definition: "Array.prototype.includes uses SameValueZero comparison, so it can find NaN and treats positive and negative zero as equal.",
    template: "console.log({array}.includes({value}));",
    parameters: [
      parameter("array", "Array", "The array to inspect.", "[12, 42, 7]", "Array expression"),
      parameter("value", "Value", "The value to locate.", "42")
    ],
    exampleTitle: "Check for a value",
    exampleCode: "console.log([12, 42, 7].includes(42));",
    exampleExplanation: "The array contains 42, so includes returns true.",
    keywords: ["contains", "membership"]
  }),
  jsBuiltIn({
    typeName: "Array",
    name: "sort",
    displayName: "Sort an array in place",
    description: "Orders an array in place using an optional comparison function.",
    definition: "Array.prototype.sort mutates and returns the same array. A numeric comparison function is required for numeric ordering because the default compares string forms.",
    template: "const values = {array};\nvalues.sort(({left}, {right}) => {comparison});\nconsole.log(values);",
    parameters: [
      parameter("array", "Array", "The array expression to sort.", "[12, 3, 42]", "Array expression"),
      parameter("left", "Left item", "The first comparison parameter.", "a", "Identifier"),
      parameter("right", "Right item", "The second comparison parameter.", "b", "Identifier"),
      parameter("comparison", "Comparison", "A negative, zero, or positive ordering result.", "a - b")
    ],
    exampleTitle: "Sort numbers ascending",
    exampleCode: "const values = [12, 3, 42];\nvalues.sort((a, b) => a - b);\nconsole.log(values);",
    exampleExplanation: "The numeric comparator orders the values as 3, 12, and 42.",
    keywords: ["order", "mutate", "comparator"]
  }),
  jsBuiltIn({
    typeName: "Object",
    name: "keys",
    signatureIncludes: "o: object",
    displayName: "List enumerable property names",
    description: "Returns an array of an object's own enumerable string-keyed property names.",
    definition: "Object.keys excludes inherited, symbol-keyed, and non-enumerable properties. Its ordering follows the language's property-order rules.",
    template: "console.log(Object.keys({object}));",
    parameters: [parameter("object", "Object", "The object whose keys are returned.", "{ alpha: 1, beta: 2 }", "Object expression")],
    exampleTitle: "List object keys",
    exampleCode: "console.log(Object.keys({ alpha: 1, beta: 2 }));",
    exampleExplanation: "The two own enumerable keys are returned as strings.",
    keywords: ["properties", "enumeration"]
  }),
  jsBuiltIn({
    typeName: "Object",
    name: "entries",
    signatureIncludes: "o: {}",
    displayName: "List enumerable property pairs",
    description: "Returns an array containing an object's own enumerable string-keyed entries.",
    definition: "Object.entries returns each property as a two-item key-value array and excludes inherited, symbol-keyed, and non-enumerable properties.",
    template: "for (const [key, value] of Object.entries({object})) {\n  console.log(key, value);\n}",
    parameters: [parameter("object", "Object", "The object to enumerate.", "{ answer: 42 }", "Object expression")],
    exampleTitle: "Read key-value pairs",
    exampleCode: "for (const [key, value] of Object.entries({ answer: 42 })) {\n  console.log(key, value);\n}",
    exampleExplanation: "Destructuring reads the key and value from each returned pair.",
    keywords: ["properties", "pairs", "enumeration"]
  }),
  jsBuiltIn({
    typeName: "Object",
    name: "fromEntries",
    signatureIncludes: "PropertyKey, T",
    displayName: "Create an object from entries",
    description: "Builds an object from an iterable of key-value pairs.",
    definition: "Object.fromEntries consumes two-item entries and assigns each key and value to a new ordinary object. Later duplicate keys replace earlier values.",
    template: "const object = Object.fromEntries({entries});\nconsole.log(object);",
    parameters: [parameter("entries", "Entries", "An iterable of key-value pairs.", "[[\"answer\", 42]]", "Iterable expression")],
    exampleTitle: "Build an object from pairs",
    exampleCode: "console.log(Object.fromEntries([[\"answer\", 42], [\"ready\", true]]));",
    exampleExplanation: "Each pair becomes one own property on the new object.",
    keywords: ["properties", "pairs", "construction"]
  }),
  jsBuiltIn({
    typeName: "Object",
    name: "assign",
    signatureIncludes: "...sources",
    displayName: "Copy enumerable properties",
    description: "Copies own enumerable properties from source objects onto a target.",
    definition: "Object.assign mutates and returns the target. Source properties are processed from left to right, so later sources overwrite the same keys.",
    template: "const result = Object.assign({target}, {source});\nconsole.log(result);",
    parameters: [
      parameter("target", "Target", "The object that receives properties.", "{}", "Object expression"),
      parameter("source", "Source", "An object supplying enumerable properties.", "{ answer: 42 }", "Object expression")
    ],
    exampleTitle: "Merge settings onto a target",
    exampleCode: "console.log(Object.assign({ theme: \"dark\" }, { answer: 42 }));",
    exampleExplanation: "answer is copied onto the existing target object.",
    keywords: ["copy", "merge", "mutate"]
  }),
  jsBuiltIn({
    typeName: "Object",
    name: "hasOwn",
    displayName: "Check for an own property",
    description: "Tests whether an object directly owns a property.",
    definition: "Object.hasOwn safely checks own properties even when an object has no prototype or defines a property named hasOwnProperty.",
    template: "console.log(Object.hasOwn({object}, {key}));",
    parameters: [
      parameter("object", "Object", "The object to inspect.", "{ answer: 42 }", "Object expression"),
      parameter("key", "Property key", "The string or symbol property key.", "\"answer\"", "Property-key expression")
    ],
    exampleTitle: "Check an object's own property",
    exampleCode: "console.log(Object.hasOwn({ answer: 42 }, \"answer\"));",
    exampleExplanation: "answer exists directly on the object, so the result is true.",
    keywords: ["property", "ownership", "prototype"]
  }),
  jsBuiltIn({
    typeName: "Map",
    name: "set",
    displayName: "Set a map entry",
    description: "Adds or replaces a key-value entry and returns the map.",
    definition: "Map.prototype.set stores a value under a key using SameValueZero key comparison. Returning the map enables chained set calls.",
    template: "const values = new Map();\nvalues.set({key}, {value});\nconsole.log(values.get({key}));",
    parameters: [
      parameter("key", "Key", "The map key expression.", "\"answer\""),
      parameter("value", "Value", "The value stored under the key.", "42")
    ],
    exampleTitle: "Store a keyed value",
    exampleCode: "const values = new Map();\nvalues.set(\"answer\", 42);\nconsole.log(values.get(\"answer\"));",
    exampleExplanation: "set creates the entry and get retrieves its value.",
    keywords: ["collection", "key", "value"]
  }),
  jsBuiltIn({
    typeName: "Map",
    name: "get",
    displayName: "Get a map value",
    description: "Returns the value for a key or undefined when the key is absent.",
    definition: "Map.prototype.get retrieves the value associated with a key without changing the map. Use has when undefined could itself be a stored value.",
    template: "const values = new Map({entries});\nconsole.log(values.get({key}));",
    parameters: [
      parameter("entries", "Entries", "Initial key-value pairs.", "[[\"answer\", 42]]", "Iterable expression"),
      parameter("key", "Key", "The key to retrieve.", "\"answer\"")
    ],
    exampleTitle: "Retrieve a keyed value",
    exampleCode: "const values = new Map([[\"answer\", 42]]);\nconsole.log(values.get(\"answer\"));",
    exampleExplanation: "get returns the value stored under answer.",
    keywords: ["collection", "lookup"]
  }),
  jsBuiltIn({
    typeName: "Map",
    name: "has",
    displayName: "Check for a map key",
    description: "Tests whether a map contains a specified key.",
    definition: "Map.prototype.has checks key presence independently of the stored value and does not modify the map.",
    template: "const values = new Map({entries});\nconsole.log(values.has({key}));",
    parameters: [
      parameter("entries", "Entries", "Initial key-value pairs.", "[[\"answer\", 42]]", "Iterable expression"),
      parameter("key", "Key", "The key to locate.", "\"answer\"")
    ],
    exampleTitle: "Check key presence",
    exampleCode: "console.log(new Map([[\"answer\", 42]]).has(\"answer\"));",
    exampleExplanation: "The map contains the key even if its value is not inspected.",
    keywords: ["collection", "membership"]
  }),
  jsBuiltIn({
    typeName: "Set",
    name: "add",
    displayName: "Add a set value",
    description: "Adds a unique value and returns the set.",
    definition: "Set.prototype.add inserts a value using SameValueZero equality. Adding an existing value leaves the set unchanged.",
    template: "const values = new Set({values});\nvalues.add({value});\nconsole.log([...values]);",
    parameters: [
      parameter("values", "Initial values", "An iterable used to initialize the set.", "[12, 7]", "Iterable expression"),
      parameter("value", "Value", "The value to add.", "42")
    ],
    exampleTitle: "Add one unique value",
    exampleCode: "const values = new Set([12, 7]);\nvalues.add(42);\nconsole.log([...values]);",
    exampleExplanation: "42 is added once and the spread operator exposes the set values as an array.",
    keywords: ["collection", "unique", "insert"]
  }),
  jsBuiltIn({
    typeName: "Set",
    name: "has",
    displayName: "Check for a set value",
    description: "Tests whether a set contains a specified value.",
    definition: "Set.prototype.has performs a SameValueZero membership test without changing the set.",
    template: "console.log(new Set({values}).has({value}));",
    parameters: [
      parameter("values", "Values", "An iterable used to initialize the set.", "[12, 42, 7]", "Iterable expression"),
      parameter("value", "Value", "The value to locate.", "42")
    ],
    exampleTitle: "Check set membership",
    exampleCode: "console.log(new Set([12, 42, 7]).has(42));",
    exampleExplanation: "42 is present, so has returns true.",
    keywords: ["collection", "membership", "unique"]
  }),
  jsBuiltIn({
    typeName: "String",
    name: "includes",
    displayName: "Check for text within a string",
    description: "Tests whether a string contains a specified substring.",
    definition: "String.prototype.includes performs a case-sensitive search from an optional starting position and rejects regular expressions.",
    template: "console.log({text}.includes({search}));",
    parameters: [
      parameter("text", "Text", "The string expression to search.", "\"Hypershell\"", "String expression"),
      parameter("search", "Search text", "The substring to locate.", "\"shell\"", "String expression")
    ],
    exampleTitle: "Find a substring",
    exampleCode: "console.log(\"Hypershell\".includes(\"shell\"));",
    exampleExplanation: "The exact lowercase substring appears at the end of the string.",
    keywords: ["substring", "search"]
  }),
  jsBuiltIn({
    typeName: "String",
    name: "split",
    signatureIncludes: "string | RegExp",
    displayName: "Split text into pieces",
    description: "Divides a string around separator matches and returns an array.",
    definition: "String.prototype.split accepts a string or regular-expression separator and an optional result limit. The original string is unchanged.",
    template: "console.log({text}.split({separator}));",
    parameters: [
      parameter("text", "Text", "The string expression to divide.", "\"alpha,beta,gamma\"", "String expression"),
      parameter("separator", "Separator", "A string or regular expression identifying boundaries.", "\",\"", "String or RegExp expression")
    ],
    exampleTitle: "Split comma-separated text",
    exampleCode: "console.log(\"alpha,beta,gamma\".split(\",\"));",
    exampleExplanation: "Each comma becomes a boundary between returned strings.",
    keywords: ["text", "separator", "array"]
  }),
  jsBuiltIn({
    typeName: "String",
    name: "replaceAll",
    signatureIncludes: "replaceValue: string",
    displayName: "Replace every text match",
    description: "Returns a string with every matching substring replaced.",
    definition: "String.prototype.replaceAll leaves the source unchanged. A regular-expression search value must use the global flag.",
    template: "console.log({text}.replaceAll({search}, {replacement}));",
    parameters: [
      parameter("text", "Text", "The source string expression.", "\"red blue red\"", "String expression"),
      parameter("search", "Search value", "The string or global regular expression to replace.", "\"red\"", "String or RegExp expression"),
      parameter("replacement", "Replacement", "The replacement string.", "\"orange\"", "String expression")
    ],
    exampleTitle: "Replace repeated text",
    exampleCode: "console.log(\"red blue red\".replaceAll(\"red\", \"orange\"));",
    exampleExplanation: "Both occurrences are replaced in the returned string.",
    keywords: ["text", "replacement", "immutable"]
  }),
  jsBuiltIn({
    typeName: "String",
    name: "matchAll",
    displayName: "Iterate every regular-expression match",
    description: "Returns an iterator over all matches of a global regular expression.",
    definition: "String.prototype.matchAll exposes each match array, capture group, index, and input without mutating the supplied regular expression's lastIndex.",
    template: "const matches = [...{text}.matchAll({pattern})];\nconsole.log(matches.map((match) => match[0]));",
    parameters: [
      parameter("text", "Text", "The string expression to search.", "\"A1 B22\"", "String expression"),
      parameter("pattern", "Pattern", "A global regular expression.", "/\\d+/g", "RegExp expression")
    ],
    exampleTitle: "Collect every number",
    exampleCode: "const matches = [...\"A1 B22\".matchAll(/\\d+/g)];\nconsole.log(matches.map((match) => match[0]));",
    exampleExplanation: "The iterator yields match records for 1 and 22.",
    keywords: ["regex", "iterator", "captures"]
  }),
  jsBuiltIn({
    typeName: "Number",
    name: "isFinite",
    displayName: "Test for a finite number",
    description: "Returns true only for number values that are neither infinity nor NaN.",
    definition: "Number.isFinite performs no string coercion, unlike the legacy global isFinite function.",
    template: "console.log(Number.isFinite({value}));",
    parameters: [parameter("value", "Value", "The value to test.", "42")],
    exampleTitle: "Validate a finite number",
    exampleCode: "console.log(Number.isFinite(42), Number.isFinite(Infinity));",
    exampleExplanation: "42 is finite while positive infinity is not.",
    keywords: ["number", "validation", "NaN", "infinity"]
  }),
  jsBuiltIn({
    typeName: "Promise",
    name: "allSettled",
    signatureIncludes: "Iterable",
    displayName: "Wait for every promise outcome",
    description: "Returns a promise that fulfills after every input settles.",
    definition: "Promise.allSettled never rejects because an input rejects. It preserves input order and reports each outcome with a fulfilled or rejected status.",
    template: "void (async () => {\n  const outcomes = await Promise.allSettled({values});\n  console.log(outcomes);\n})();",
    parameters: [parameter("values", "Values", "An iterable of values or promises.", "[Promise.resolve(42), Promise.reject(new Error(\"nope\"))]", "Iterable expression")],
    exampleTitle: "Inspect mixed promise outcomes",
    exampleCode: "void (async () => {\n  const outcomes = await Promise.allSettled([Promise.resolve(42), Promise.reject(new Error(\"nope\"))]);\n  console.log(outcomes.map((outcome) => outcome.status));\n})();",
    exampleExplanation: "The result records one fulfilled and one rejected input without rejecting the outer promise.",
    keywords: ["async", "concurrency", "outcomes"]
  }),
  jsBuiltIn({
    typeName: "Promise",
    name: "race",
    signatureIncludes: "Iterable",
    displayName: "Use the first settled promise",
    description: "Returns a promise that mirrors the first input to settle.",
    definition: "Promise.race settles asynchronously with the first fulfillment or rejection. Other operations continue unless separately cancelled.",
    template: "void (async () => {\n  const result = await Promise.race({values});\n  console.log(result);\n})();",
    parameters: [parameter("values", "Values", "An iterable of values or promises.", "[Promise.resolve(42), new Promise((resolve) => setTimeout(() => resolve(7), 10))]", "Iterable expression")],
    exampleTitle: "Select the immediate result",
    exampleCode: "void (async () => {\n  const result = await Promise.race([Promise.resolve(42), new Promise((resolve) => setTimeout(() => resolve(7), 10))]);\n  console.log(result);\n})();",
    exampleExplanation: "The already-resolved promise wins the race and supplies 42.",
    keywords: ["async", "concurrency", "first"]
  }),
  jsBuiltIn({
    name: "parseInt",
    displayName: "Parse an integer",
    description: "Parses a string as an integer using a specified radix.",
    definition: "parseInt reads valid digits from the beginning of a string and stops at the first invalid character. Supplying the radix avoids ambiguous intent.",
    template: "console.log(parseInt({text}, {radix}));",
    parameters: [
      parameter("text", "Text", "The string expression to parse.", "\"2a\"", "String expression"),
      parameter("radix", "Radix", "The integer base from 2 through 36.", "16", "Integer expression")
    ],
    exampleTitle: "Parse a hexadecimal value",
    exampleCode: "console.log(parseInt(\"2a\", 16));",
    exampleExplanation: "2a in base 16 equals 42 in decimal.",
    keywords: ["integer", "radix", "conversion"]
  }),
  jsPattern({
    id: "javascript.core.optional-chaining",
    category: "Expressions",
    name: "optional chaining",
    displayName: "Safely follow an optional property path",
    description: "Stops a property or call chain when the current value is null or undefined.",
    definition: "The optional-chaining operator returns undefined instead of throwing when its left operand is nullish. It does not suppress errors from later non-optional operations.",
    documentationUrl: "https://tc39.es/ecma262/#sec-optional-chaining",
    template: "const result = {value}?.{property};\nconsole.log(result);",
    parameters: [
      parameter("value", "Value", "The object expression that may be nullish.", "{ profile: { name: \"Ada\" } }", "Object expression"),
      parameter("property", "Property path", "The property path followed after the optional operator.", "profile.name", "Property path")
    ],
    exampleTitle: "Read a missing nested value safely",
    exampleCode: "const user = null;\nconsole.log(user?.profile?.name);",
    exampleExplanation: "Evaluation stops at the null user and returns undefined.",
    keywords: ["null", "undefined", "property access"]
  }),
  jsPattern({
    id: "javascript.core.nullish-coalescing",
    category: "Expressions",
    name: "nullish coalescing",
    displayName: "Choose a fallback for nullish values",
    description: "Uses a fallback only when the left value is null or undefined.",
    definition: "The nullish-coalescing operator preserves valid falsy values such as zero, false, and an empty string, unlike logical OR.",
    documentationUrl: "https://tc39.es/ecma262/#sec-coalesce-expression",
    template: "const result = {value} ?? {fallback};\nconsole.log(result);",
    parameters: [
      parameter("value", "Value", "The expression checked for null or undefined.", "null"),
      parameter("fallback", "Fallback", "The expression evaluated only when the value is nullish.", "42")
    ],
    exampleTitle: "Preserve zero while defaulting null",
    exampleCode: "console.log(0 ?? 42, null ?? 42);",
    exampleExplanation: "Zero is retained, while null selects the fallback.",
    keywords: ["null", "undefined", "fallback"]
  }),
  jsPattern({
    id: "javascript.core.spread-syntax",
    category: "Expressions",
    name: "spread syntax",
    symbolKind: "Operator",
    displayName: "Spread values into a new collection",
    description: "Expands iterable values or enumerable object properties in a surrounding literal or call.",
    definition: "Spread syntax is context-sensitive: arrays and calls consume iterables, while object literals copy own enumerable properties into a new object.",
    documentationUrl: "https://tc39.es/ecma262/#sec-array-initializer",
    template: "const combined = [...{left}, ...{right}];\nconsole.log(combined);",
    parameters: [
      parameter("left", "First iterable", "Values placed first in the result.", "[1, 2]", "Iterable expression"),
      parameter("right", "Second iterable", "Values placed after the first iterable.", "[3, 42]", "Iterable expression")
    ],
    exampleTitle: "Combine two arrays",
    exampleCode: "console.log([... [1, 2], ... [3, 42]]);",
    exampleExplanation: "Both iterables are expanded into one new array.",
    keywords: ["array", "object", "copy"]
  }),
  jsPattern({
    id: "javascript.core.for-of",
    category: "Control Flow",
    name: "for...of",
    displayName: "Iterate values from an iterable",
    description: "Runs a loop body once for each value produced by an iterable.",
    definition: "for...of uses the iterable protocol and binds each produced value. It differs from for...in, which enumerates property keys.",
    documentationUrl: "https://tc39.es/ecma262/#sec-for-in-and-for-of-statements",
    template: "for (const {item} of {iterable}) {\n  console.log({item});\n}",
    parameters: [
      parameter("item", "Item name", "The binding for each yielded value.", "value", "Identifier"),
      parameter("iterable", "Iterable", "The iterable expression to consume.", "[12, 42]", "Iterable expression")
    ],
    exampleTitle: "Iterate array values",
    exampleCode: "for (const value of [12, 42]) {\n  console.log(value);\n}",
    exampleExplanation: "The loop receives each array value in order.",
    keywords: ["loop", "iterable", "values"]
  }),
  jsPattern({
    id: "javascript.core.async-await",
    category: "Functions",
    name: "async and await",
    displayName: "Write promise-based flow sequentially",
    description: "Declares a promise-returning function and pauses it until a promise settles.",
    definition: "An async function always returns a promise. await resumes the function after fulfillment or throws the rejection reason inside the async function.",
    documentationUrl: "https://tc39.es/ecma262/#sec-async-function-definitions",
    template: "async function {name}() {\n  const result = await {promise};\n  console.log(result);\n}\nvoid {name}();",
    parameters: [
      parameter("name", "Function name", "The async function identifier.", "main", "Identifier"),
      parameter("promise", "Promise", "The promise or value to await.", "Promise.resolve(42)", "Promise expression")
    ],
    exampleTitle: "Await a resolved value",
    exampleCode: "async function main() {\n  console.log(await Promise.resolve(42));\n}\nvoid main();",
    exampleExplanation: "The async function resumes with the fulfilled value and logs 42.",
    keywords: ["promise", "asynchronous", "function"]
  }),
  jsPattern({
    id: "javascript.core.generator-function",
    category: "Functions",
    name: "generator function",
    displayName: "Produce an iterable sequence lazily",
    description: "Defines a function that can pause at yield expressions and resume later.",
    definition: "Calling a generator function returns an iterator without executing the body immediately. Each next call resumes execution through the next yield or return.",
    documentationUrl: "https://tc39.es/ecma262/#sec-generator-function-definitions",
    template: "function* {name}() {\n  yield {first};\n  yield {second};\n}\nconsole.log([...{name}()]);",
    parameters: [
      parameter("name", "Function name", "The generator function identifier.", "values", "Identifier"),
      parameter("first", "First value", "The first yielded expression.", "12"),
      parameter("second", "Second value", "The second yielded expression.", "42")
    ],
    exampleTitle: "Yield two values",
    exampleCode: "function* values() {\n  yield 12;\n  yield 42;\n}\nconsole.log([...values()]);",
    exampleExplanation: "Spreading the generator consumes both yielded values.",
    keywords: ["iterator", "yield", "lazy"]
  })
];

const nodeGuides = [
  nodeApi({
    moduleName: "node:fs/promises",
    name: "writeFile",
    displayName: "Write a file asynchronously",
    description: "Writes data to a file and resolves when the operation completes.",
    definition: "fs/promises writeFile replaces an existing file by default and creates it when absent. Callers should await completion and avoid overlapping writes to the same path.",
    template: "void (async () => {\n  const { writeFile } = require(\"node:fs/promises\");\n  await writeFile({path}, {data}, {encoding});\n})();",
    parameters: [
      parameter("path", "Path", "A string, Buffer, URL, or file handle identifying the file.", "\"output.txt\"", "Path expression"),
      parameter("data", "Data", "The string or binary content to write.", "\"Hello from Hypershell\"", "String or binary expression"),
      parameter("encoding", "Encoding", "The text encoding option.", "\"utf8\"", "String expression")
    ],
    exampleTitle: "Write and verify a temporary file",
    exampleCode: "void (async () => {\n  const { mkdtemp, readFile, rm, writeFile } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-write-\"));\n  try {\n    const path = join(root, \"answer.txt\");\n    await writeFile(path, \"42\", \"utf8\");\n    console.log(await readFile(path, \"utf8\"));\n  } finally {\n    await rm(root, { recursive: true, force: true });\n  }\n})();",
    exampleExplanation: "The example writes 42, reads it back, and removes its isolated temporary directory.",
    keywords: ["file", "write", "async"]
  }),
  nodeApi({
    moduleName: "node:fs/promises",
    name: "readdir",
    displayName: "List directory entries asynchronously",
    description: "Returns the names or directory-entry objects contained by a directory.",
    definition: "fs/promises readdir resolves with entry names by default. withFileTypes returns Dirent objects, while recursive can traverse descendants on supported Node versions.",
    template: "void (async () => {\n  const { readdir } = require(\"node:fs/promises\");\n  const entries = await readdir({path}, {options});\n  console.log(entries);\n})();",
    parameters: [
      parameter("path", "Directory", "The directory path to inspect.", "\".\"", "Path expression"),
      parameter("options", "Options", "Encoding, withFileTypes, or recursive behavior.", "{ withFileTypes: true }", "Options object")
    ],
    exampleTitle: "List an isolated directory",
    exampleCode: "void (async () => {\n  const { mkdtemp, readdir, rm, writeFile } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-list-\"));\n  try {\n    await writeFile(join(root, \"answer.txt\"), \"42\");\n    console.log((await readdir(root)).sort());\n  } finally {\n    await rm(root, { recursive: true, force: true });\n  }\n})();",
    exampleExplanation: "The directory contains only answer.txt, then the temporary tree is removed.",
    keywords: ["directory", "list", "Dirent"]
  }),
  nodeApi({
    moduleName: "node:fs/promises",
    name: "mkdir",
    displayName: "Create a directory asynchronously",
    description: "Creates one directory or an entire missing directory chain.",
    definition: "fs/promises mkdir rejects when the target already exists unless recursive is true. Recursive creation resolves after all missing parents have been created.",
    template: "void (async () => {\n  const { mkdir } = require(\"node:fs/promises\");\n  await mkdir({path}, {options});\n})();",
    parameters: [
      parameter("path", "Directory", "The directory path to create.", "\"output/reports\"", "Path expression"),
      parameter("options", "Options", "Recursive and permission-mode behavior.", "{ recursive: true }", "Options object")
    ],
    exampleTitle: "Create nested temporary directories",
    exampleCode: "void (async () => {\n  const { mkdtemp, mkdir, rm, stat } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-mkdir-\"));\n  try {\n    const nested = join(root, \"one\", \"two\");\n    await mkdir(nested, { recursive: true });\n    console.log((await stat(nested)).isDirectory());\n  } finally {\n    await rm(root, { recursive: true, force: true });\n  }\n})();",
    exampleExplanation: "Recursive creation builds both missing levels and stat confirms the final path is a directory.",
    keywords: ["directory", "create", "recursive"]
  }),
  nodeApi({
    moduleName: "node:fs/promises",
    name: "stat",
    displayName: "Inspect file metadata asynchronously",
    description: "Returns metadata for a file-system path.",
    definition: "fs/promises stat follows symbolic links and resolves with an fs.Stats object containing type checks, sizes, and timestamps. Use lstat to inspect a link itself.",
    template: "void (async () => {\n  const { stat } = require(\"node:fs/promises\");\n  const info = await stat({path});\n  console.log(info.isFile(), info.size);\n})();",
    parameters: [parameter("path", "Path", "The path whose metadata is requested.", "\"README.md\"", "Path expression")],
    exampleTitle: "Inspect a temporary file",
    exampleCode: "void (async () => {\n  const { mkdtemp, rm, stat, writeFile } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-stat-\"));\n  try {\n    const path = join(root, \"answer.txt\");\n    await writeFile(path, \"42\");\n    const info = await stat(path);\n    console.log(info.isFile(), info.size);\n  } finally {\n    await rm(root, { recursive: true, force: true });\n  }\n})();",
    exampleExplanation: "The Stats object identifies a file containing two UTF-8 bytes.",
    keywords: ["metadata", "file", "Stats"]
  }),
  nodeApi({
    moduleName: "node:fs/promises",
    name: "rm",
    displayName: "Remove a path asynchronously",
    description: "Removes a file or directory according to explicit options.",
    definition: "fs/promises rm can recursively remove directory trees and can ignore missing paths with force. Recursive removal should be scoped to a path the application has validated.",
    template: "void (async () => {\n  const { rm } = require(\"node:fs/promises\");\n  await rm({path}, {options});\n})();",
    parameters: [
      parameter("path", "Path", "The validated file or directory path to remove.", "\"output.tmp\"", "Path expression"),
      parameter("options", "Options", "Recursive, force, and retry behavior.", "{ force: true }", "Options object")
    ],
    exampleTitle: "Remove one temporary file",
    exampleCode: "void (async () => {\n  const { mkdtemp, rm, writeFile } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-rm-\"));\n  const path = join(root, \"temporary.txt\");\n  await writeFile(path, \"temporary\");\n  await rm(path);\n  console.log(\"removed\");\n  await rm(root, { recursive: true, force: true });\n})();",
    exampleExplanation: "The example removes only a file it created inside an isolated temporary directory.",
    keywords: ["remove", "file", "directory", "safety"]
  }),
  nodeApi({
    moduleName: "node:fs/promises",
    name: "rename",
    displayName: "Rename or move a path asynchronously",
    description: "Changes a file-system path name within the capabilities of the host file system.",
    definition: "fs/promises rename delegates to the operating system. Moving across file-system volumes can fail and may require an explicit copy-then-remove strategy.",
    template: "void (async () => {\n  const { rename } = require(\"node:fs/promises\");\n  await rename({oldPath}, {newPath});\n})();",
    parameters: [
      parameter("oldPath", "Current path", "The existing source path.", "\"draft.txt\"", "Path expression"),
      parameter("newPath", "New path", "The destination path.", "\"final.txt\"", "Path expression")
    ],
    exampleTitle: "Rename a temporary file",
    exampleCode: "void (async () => {\n  const { mkdtemp, readFile, rename, rm, writeFile } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-rename-\"));\n  try {\n    const draft = join(root, \"draft.txt\");\n    const final = join(root, \"final.txt\");\n    await writeFile(draft, \"42\");\n    await rename(draft, final);\n    console.log(await readFile(final, \"utf8\"));\n  } finally {\n    await rm(root, { recursive: true, force: true });\n  }\n})();",
    exampleExplanation: "The created file is renamed in place and remains readable under its new path.",
    keywords: ["rename", "move", "path"]
  }),
  nodeApi({
    moduleName: "node:fs/promises",
    name: "access",
    displayName: "Check path accessibility asynchronously",
    description: "Resolves when the current process can access a path using the requested mode.",
    definition: "fs/promises access is useful for a direct permission check, but checking before a later operation can introduce a race. Prefer performing the operation and handling its error when possible.",
    template: "void (async () => {\n  const { access } = require(\"node:fs/promises\");\n  await access({path});\n  console.log(\"accessible\");\n})();",
    parameters: [parameter("path", "Path", "The file-system path to test.", "\"README.md\"", "Path expression")],
    exampleTitle: "Check a temporary file",
    exampleCode: "void (async () => {\n  const { access, mkdtemp, rm, writeFile } = require(\"node:fs/promises\");\n  const { join } = require(\"node:path\");\n  const { tmpdir } = require(\"node:os\");\n  const root = await mkdtemp(join(tmpdir(), \"hypershell-access-\"));\n  try {\n    const path = join(root, \"answer.txt\");\n    await writeFile(path, \"42\");\n    await access(path);\n    console.log(\"accessible\");\n  } finally {\n    await rm(root, { recursive: true, force: true });\n  }\n})();",
    exampleExplanation: "access resolves for the file created by the example, after which the temporary directory is removed.",
    keywords: ["permissions", "path", "race condition"]
  }),
  nodeApi({
    moduleName: "node:path",
    name: "join",
    displayName: "Join path segments",
    description: "Combines path segments using the host platform's separator and normalization rules.",
    definition: "path.join concatenates segments, normalizes separators and dot segments, and returns a relative or absolute path according to the supplied parts.",
    template: "const { join } = require(\"node:path\");\nconsole.log(join({segments}));",
    parameters: [parameter("segments", "Segments", "Comma-separated string expressions passed as path segments.", "\"reports\", \"2026\", \"summary.txt\"", "Argument list")],
    exampleTitle: "Build a nested path",
    exampleCode: "const { join } = require(\"node:path\");\nconsole.log(join(\"reports\", \"2026\", \"summary.txt\"));",
    exampleExplanation: "The segments are combined with the correct separator for the running platform.",
    keywords: ["path", "segments", "cross-platform"]
  }),
  nodeApi({
    moduleName: "node:path",
    name: "resolve",
    displayName: "Resolve an absolute path",
    description: "Processes path segments from right to left until it forms an absolute path.",
    definition: "path.resolve normalizes the result and uses the current working directory when the supplied segments do not establish an absolute base.",
    template: "const { resolve } = require(\"node:path\");\nconsole.log(resolve({segments}));",
    parameters: [parameter("segments", "Segments", "Comma-separated path-segment expressions.", "\"reports\", \"summary.txt\"", "Argument list")],
    exampleTitle: "Resolve a path beneath the working directory",
    exampleCode: "const { isAbsolute, resolve } = require(\"node:path\");\nconsole.log(isAbsolute(resolve(\"reports\", \"summary.txt\")));",
    exampleExplanation: "resolve produces an absolute path, confirmed without relying on a platform-specific spelling.",
    keywords: ["path", "absolute", "normalize"]
  }),
  nodeApi({
    moduleName: "node:path",
    name: "relative",
    displayName: "Calculate a relative path",
    description: "Returns the path needed to navigate from one location to another.",
    definition: "path.relative resolves both inputs before comparing them and returns an empty string when they identify the same location.",
    template: "const { relative } = require(\"node:path\");\nconsole.log(relative({from}, {to}));",
    parameters: [
      parameter("from", "From", "The starting path expression.", "\"/projects/app\"", "Path expression"),
      parameter("to", "To", "The destination path expression.", "\"/projects/app/src/index.js\"", "Path expression")
    ],
    exampleTitle: "Find a child path relative to its root",
    exampleCode: "const { join, relative } = require(\"node:path\");\nconst root = process.cwd();\nconsole.log(relative(root, join(root, \"src\", \"index.js\")));",
    exampleExplanation: "The result identifies the nested file without embedding an operating-system-specific root.",
    keywords: ["path", "relative", "navigation"]
  }),
  nodeApi({
    moduleName: "node:path",
    name: "extname",
    displayName: "Read a path extension",
    description: "Returns the suffix from the final period in the last path segment.",
    definition: "path.extname includes the leading period and returns an empty string when the basename has no extension under Node's path rules.",
    template: "const { extname } = require(\"node:path\");\nconsole.log(extname({path}));",
    parameters: [parameter("path", "Path", "The path string to inspect.", "\"report.pdf\"", "Path expression")],
    exampleTitle: "Read a PDF extension",
    exampleCode: "const { extname } = require(\"node:path\");\nconsole.log(extname(\"report.pdf\"));",
    exampleExplanation: "The returned extension includes the leading period.",
    keywords: ["path", "extension", "filename"]
  }),
  nodeApi({
    moduleName: "node:path",
    name: "basename",
    displayName: "Read the final path segment",
    description: "Returns the last portion of a path with an optional suffix removed.",
    definition: "path.basename performs lexical path processing and does not access the file system. Case sensitivity of suffix removal follows normal string comparison.",
    template: "const { basename } = require(\"node:path\");\nconsole.log(basename({path}, {suffix}));",
    parameters: [
      parameter("path", "Path", "The path string to inspect.", "\"reports/summary.pdf\"", "Path expression"),
      parameter("suffix", "Suffix", "An optional exact suffix removed from the result.", "\".pdf\"", "String expression")
    ],
    exampleTitle: "Read a filename without its extension",
    exampleCode: "const { basename } = require(\"node:path\");\nconsole.log(basename(\"reports/summary.pdf\", \".pdf\"));",
    exampleExplanation: "basename selects summary.pdf and removes the exact .pdf suffix.",
    keywords: ["path", "filename", "suffix"]
  }),
  nodeApi({
    moduleName: "node:path",
    name: "dirname",
    displayName: "Read a path's parent portion",
    description: "Returns every path segment except the final basename.",
    definition: "path.dirname lexically normalizes trailing separators and does not require the path to exist.",
    template: "const { dirname } = require(\"node:path\");\nconsole.log(dirname({path}));",
    parameters: [parameter("path", "Path", "The path string to inspect.", "\"reports/summary.pdf\"", "Path expression")],
    exampleTitle: "Read the containing directory",
    exampleCode: "const { dirname } = require(\"node:path\");\nconsole.log(dirname(\"reports/summary.pdf\"));",
    exampleExplanation: "The final filename is removed, leaving reports.",
    keywords: ["path", "directory", "parent"]
  }),
  nodeApi({
    moduleName: "node:url",
    name: "fileURLToPath",
    displayName: "Convert a file URL to a native path",
    description: "Converts a file URL into a correctly decoded operating-system path.",
    definition: "url.fileURLToPath handles percent encoding, drive letters, and network paths according to Node's platform rules; reading URL.pathname directly does not.",
    template: "const { fileURLToPath } = require(\"node:url\");\nconsole.log(fileURLToPath({url}));",
    parameters: [parameter("url", "File URL", "A file URL object or string.", "new URL(\"file:///tmp/answer.txt\")", "URL expression")],
    exampleTitle: "Round-trip the working directory",
    exampleCode: "const { fileURLToPath, pathToFileURL } = require(\"node:url\");\nconst path = fileURLToPath(pathToFileURL(process.cwd()));\nconsole.log(typeof path === \"string\" && path.length > 0);",
    exampleExplanation: "Node converts the working directory to a file URL and back using platform-correct rules.",
    keywords: ["URL", "file", "path", "conversion"]
  }),
  nodeApi({
    moduleName: "node:url",
    name: "pathToFileURL",
    displayName: "Convert a native path to a file URL",
    description: "Creates an encoded file URL from an operating-system path.",
    definition: "url.pathToFileURL resolves relative paths and correctly escapes characters that would have special meaning in a URL.",
    template: "const { pathToFileURL } = require(\"node:url\");\nconsole.log(pathToFileURL({path}).href);",
    parameters: [parameter("path", "Path", "The native path string to convert.", "process.cwd()", "Path expression")],
    exampleTitle: "Create a file URL for the working directory",
    exampleCode: "const { pathToFileURL } = require(\"node:url\");\nconsole.log(pathToFileURL(process.cwd()).protocol);",
    exampleExplanation: "The resulting URL uses the file protocol on every supported platform.",
    keywords: ["URL", "file", "path", "conversion"]
  }),
  nodeApi({
    moduleName: "node:os",
    name: "platform",
    displayName: "Identify the operating-system platform",
    description: "Returns Node's operating-system platform identifier.",
    definition: "os.platform returns values such as win32, linux, or darwin and matches process.platform. Feature detection is usually preferable when behavior can be tested directly.",
    template: "const { platform } = require(\"node:os\");\nconsole.log(platform() === {expected});",
    parameters: [parameter("expected", "Expected platform", "A platform identifier to compare.", "process.platform", "String expression")],
    exampleTitle: "Compare the two platform surfaces",
    exampleCode: "const { platform } = require(\"node:os\");\nconsole.log(platform() === process.platform);",
    exampleExplanation: "os.platform and process.platform expose the same platform identifier.",
    keywords: ["operating system", "platform", "cross-platform"]
  }),
  nodeApi({
    moduleName: "node:os",
    name: "homedir",
    displayName: "Locate the current user's home directory",
    description: "Returns the effective user's home-directory path.",
    definition: "os.homedir uses operating-system account information and environment fallbacks. Applications should join child paths rather than concatenate separators manually.",
    template: "const { homedir } = require(\"node:os\");\nconsole.log(homedir());",
    parameters: [parameter("label", "Output label", "A label printed before the returned path.", "\"home\"", "String expression")],
    exampleTitle: "Confirm a home path is available",
    exampleCode: "const { homedir } = require(\"node:os\");\nconsole.log(typeof homedir() === \"string\" && homedir().length > 0);",
    exampleExplanation: "The API supplies a non-empty path string for the current user.",
    keywords: ["home", "user", "path"]
  }),
  nodeApi({
    moduleName: "node:os",
    name: "tmpdir",
    displayName: "Locate the temporary directory",
    description: "Returns the operating system's default directory for temporary files.",
    definition: "os.tmpdir chooses the platform's configured temporary location. Use fs.mkdtemp to create an application-owned unique directory beneath it.",
    template: "const { tmpdir } = require(\"node:os\");\nconsole.log({label}, tmpdir());",
    parameters: [parameter("label", "Output label", "A label printed before the path.", "\"temp\"", "String expression")],
    exampleTitle: "Read the temporary directory",
    exampleCode: "const { tmpdir } = require(\"node:os\");\nconsole.log(typeof tmpdir() === \"string\" && tmpdir().length > 0);",
    exampleExplanation: "The platform exposes a non-empty temporary-directory path.",
    keywords: ["temporary", "directory", "path"]
  }),
  nodeApi({
    moduleName: "node:util",
    name: "parseArgs",
    displayName: "Parse command-line arguments",
    description: "Parses command-line tokens against an explicit option configuration.",
    definition: "util.parseArgs returns option values, positional arguments, and optionally token metadata. Strict mode rejects unknown options and invalid values.",
    template: "const { parseArgs } = require(\"node:util\");\nconst parsed = parseArgs({ config });\nconsole.log(parsed.values, parsed.positionals);",
    parameters: [parameter("config", "Configuration", "The args, options, strict, and allowPositionals settings.", "{ args: [\"--count\", \"42\"], options: { count: { type: \"string\" } } }", "Options object")],
    exampleTitle: "Parse one typed option",
    exampleCode: "const { parseArgs } = require(\"node:util\");\nconst parsed = parseArgs({ args: [\"--count\", \"42\"], options: { count: { type: \"string\" } } });\nconsole.log(parsed.values.count);",
    exampleExplanation: "The declared string option consumes the following token and exposes 42 under values.count.",
    keywords: ["CLI", "arguments", "options"]
  }),
  nodeApi({
    moduleName: "node:util",
    name: "inspect",
    displayName: "Format a value for debugging",
    description: "Returns a configurable diagnostic string representation of a JavaScript value.",
    definition: "util.inspect understands circular references, collection types, depth limits, colors, and custom inspectors. Its output is intended for diagnostics rather than stable serialization.",
    template: "const { inspect } = require(\"node:util\");\nconsole.log(inspect({value}, {options}));",
    parameters: [
      parameter("value", "Value", "The value to inspect.", "{ answer: 42 }"),
      parameter("options", "Options", "Depth, colors, sorting, and formatting settings.", "{ sorted: true, colors: false }", "Options object")
    ],
    exampleTitle: "Inspect a nested object",
    exampleCode: "const { inspect } = require(\"node:util\");\nconsole.log(inspect({ answer: 42 }, { sorted: true, colors: false }));",
    exampleExplanation: "inspect produces a readable diagnostic form without ANSI color codes.",
    keywords: ["debug", "format", "diagnostic"]
  }),
  nodeApi({
    moduleName: "node:util",
    name: "promisify",
    displayName: "Convert a callback API to a promise API",
    description: "Wraps a conventional error-first callback function in a promise-returning function.",
    definition: "util.promisify expects the final argument to be a callback whose first value is an error. APIs with unusual callback contracts can provide a custom promisified implementation.",
    template: "const { promisify } = require(\"node:util\");\nconst promised = promisify({callbackFunction});",
    parameters: [parameter("callbackFunction", "Callback function", "An error-first callback function to wrap.", "(value, callback) => callback(null, value)", "Function expression")],
    exampleTitle: "Promisify a small callback function",
    exampleCode: "void (async () => {\n  const { promisify } = require(\"node:util\");\n  const double = promisify((value, callback) => callback(null, value * 2));\n  console.log(await double(21));\n})();",
    exampleExplanation: "The callback result becomes the fulfillment value of the returned promise.",
    keywords: ["promise", "callback", "async"]
  }),
  nodeApi({
    moduleName: "node:crypto",
    name: "createHash",
    displayName: "Create a streaming hash",
    description: "Creates a hash object for incrementally digesting data.",
    definition: "crypto.createHash selects a supported digest algorithm. After digest is called, the Hash object is finalized and cannot accept more data.",
    template: "const { createHash } = require(\"node:crypto\");\nconst digest = createHash({algorithm}).update({data}).digest({encoding});\nconsole.log(digest);",
    parameters: [
      parameter("algorithm", "Algorithm", "A supported digest algorithm name.", "\"sha256\"", "String expression"),
      parameter("data", "Data", "The string or binary data to hash.", "\"Hypershell\"", "String or binary expression"),
      parameter("encoding", "Output encoding", "The digest encoding.", "\"hex\"", "String expression")
    ],
    exampleTitle: "Hash a deterministic string",
    exampleCode: "const { createHash } = require(\"node:crypto\");\nconst digest = createHash(\"sha256\").update(\"Hypershell\").digest(\"hex\");\nconsole.log(digest.length);",
    exampleExplanation: "A SHA-256 digest rendered in hexadecimal contains 64 characters.",
    keywords: ["hash", "digest", "sha256"]
  }),
  nodeApi({
    moduleName: "node:crypto",
    name: "randomUUID",
    displayName: "Generate a random UUID",
    description: "Returns a cryptographically random RFC 4122 version 4 UUID string.",
    definition: "crypto.randomUUID uses the platform cryptographic random-number generator and may cache random data for performance unless disabled in its options.",
    template: "const { randomUUID } = require(\"node:crypto\");\nconsole.log(randomUUID({options}));",
    parameters: [parameter("options", "Options", "Controls entropy caching.", "{ disableEntropyCache: false }", "Options object")],
    exampleTitle: "Generate and validate a UUID shape",
    exampleCode: "const { randomUUID } = require(\"node:crypto\");\nconsole.log(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(randomUUID()));",
    exampleExplanation: "The generated identifier matches the hexadecimal version-4 UUID structure.",
    keywords: ["UUID", "random", "identifier"]
  }),
  nodeApi({
    moduleName: "node:crypto",
    name: "timingSafeEqual",
    displayName: "Compare equal-length secrets safely",
    description: "Compares two byte sequences using a constant-time algorithm.",
    definition: "crypto.timingSafeEqual reduces timing leakage from the comparison itself. Inputs must have equal byte lengths, and surrounding code must also avoid timing-dependent behavior.",
    template: "const { timingSafeEqual } = require(\"node:crypto\");\nconsole.log(timingSafeEqual({left}, {right}));",
    parameters: [
      parameter("left", "Left bytes", "The first Buffer or typed-array expression.", "Buffer.from(\"secret\")", "Binary expression"),
      parameter("right", "Right bytes", "An equal-length Buffer or typed-array expression.", "Buffer.from(\"secret\")", "Binary expression")
    ],
    exampleTitle: "Compare equal buffers",
    exampleCode: "const { timingSafeEqual } = require(\"node:crypto\");\nconsole.log(timingSafeEqual(Buffer.from(\"secret\"), Buffer.from(\"secret\")));",
    exampleExplanation: "Equal byte sequences produce true without an early-exit byte comparison.",
    keywords: ["security", "comparison", "constant time"]
  }),
  nodeApi({
    moduleName: "node:events",
    name: "EventEmitter",
    displayName: "Create an event emitter",
    description: "Creates an object that registers listeners and emits named events synchronously.",
    definition: "EventEmitter calls listeners in registration order when emit runs. An unhandled error event throws, so error handling should be explicit for emitters that can fail.",
    template: "const { EventEmitter } = require(\"node:events\");\nconst emitter = new EventEmitter();\nemitter.on({eventName}, {listener});\nemitter.emit({eventName}, {value});",
    parameters: [
      parameter("eventName", "Event name", "The string or symbol identifying the event.", "\"answer\"", "String or symbol expression"),
      parameter("listener", "Listener", "The function called for emitted values.", "(value) => console.log(value)", "Function expression"),
      parameter("value", "Value", "The value passed to the listener.", "42")
    ],
    exampleTitle: "Emit one named event",
    exampleCode: "const { EventEmitter } = require(\"node:events\");\nconst emitter = new EventEmitter();\nemitter.on(\"answer\", (value) => console.log(value));\nemitter.emit(\"answer\", 42);",
    exampleExplanation: "emit synchronously invokes the registered answer listener with 42.",
    keywords: ["event", "listener", "emit"]
  }),
  nodeApi({
    moduleName: "node:events",
    name: "once",
    displayName: "Await one event",
    description: "Returns a promise for the next occurrence of an event.",
    definition: "events.once resolves with an array of listener arguments and then removes its listeners. For EventEmitter instances, an error event rejects the promise while waiting for another event.",
    template: "void (async () => {\n  const { once } = require(\"node:events\");\n  const values = await once({emitter}, {eventName});\n  console.log(values);\n})();",
    parameters: [
      parameter("emitter", "Emitter", "An EventEmitter or EventTarget expression.", "emitter", "Event source expression"),
      parameter("eventName", "Event name", "The event to await.", "\"answer\"", "String or symbol expression")
    ],
    exampleTitle: "Wait for a queued event",
    exampleCode: "void (async () => {\n  const { EventEmitter, once } = require(\"node:events\");\n  const emitter = new EventEmitter();\n  const pending = once(emitter, \"answer\");\n  queueMicrotask(() => emitter.emit(\"answer\", 42));\n  console.log((await pending)[0]);\n})();",
    exampleExplanation: "The promise is registered before the microtask emits the event, then resolves with the emitted argument.",
    keywords: ["event", "promise", "once"]
  }),
  nodeApi({
    moduleName: "node:events",
    name: "on",
    displayName: "Iterate repeated events asynchronously",
    description: "Returns an async iterator that yields argument arrays for emitted events.",
    definition: "events.on queues matching events until consumed and removes its listeners when iteration ends. AbortSignal can cancel a long-running iterator.",
    template: "void (async () => {\n  for await (const values of require(\"node:events\").on({emitter}, {eventName})) {\n    console.log(values);\n    break;\n  }\n})();",
    parameters: [
      parameter("emitter", "Emitter", "An EventEmitter expression.", "emitter", "Event source expression"),
      parameter("eventName", "Event name", "The event to iterate.", "\"data\"", "String or symbol expression")
    ],
    exampleTitle: "Consume one event through an async iterator",
    exampleCode: "void (async () => {\n  const { EventEmitter, on } = require(\"node:events\");\n  const emitter = new EventEmitter();\n  const events = on(emitter, \"data\");\n  queueMicrotask(() => emitter.emit(\"data\", 42));\n  for await (const [value] of events) {\n    console.log(value);\n    break;\n  }\n})();",
    exampleExplanation: "The iterator yields the emitted argument array and closes when the loop breaks.",
    keywords: ["event", "async iterator", "stream"]
  }),
  nodeApi({
    moduleName: "node:assert",
    name: "deepStrictEqual",
    displayName: "Assert deep strict equality",
    description: "Throws an assertion error unless two values are deeply and strictly equal.",
    definition: "assert.deepStrictEqual compares prototypes, enumerable properties, collection contents, and primitive values using strict semantics appropriate for tests.",
    template: "const { deepStrictEqual } = require(\"node:assert\");\ndeepStrictEqual({actual}, {expected});",
    parameters: [
      parameter("actual", "Actual", "The value produced by the code under test.", "{ answer: 42 }"),
      parameter("expected", "Expected", "The value the result must deeply equal.", "{ answer: 42 }")
    ],
    exampleTitle: "Compare nested values",
    exampleCode: "const { deepStrictEqual } = require(\"node:assert\");\ndeepStrictEqual({ nested: [42] }, { nested: [42] });\nconsole.log(\"equal\");",
    exampleExplanation: "The two independently created structures contain equivalent prototypes, keys, arrays, and values.",
    keywords: ["test", "assertion", "equality"]
  }),
  nodeApi({
    moduleName: "node:assert",
    name: "throws",
    displayName: "Assert that a function throws",
    description: "Throws an assertion error unless a synchronous function throws an expected error.",
    definition: "assert.throws can validate an error class, regular-expression message, property object, or predicate. Use assert.rejects for promise rejections.",
    template: "const { throws } = require(\"node:assert\");\nthrows(() => {operation}, {expected});",
    parameters: [
      parameter("operation", "Operation", "The synchronous statement expected to throw.", "{ throw new TypeError(\"invalid\"); }", "Statement block"),
      parameter("expected", "Expected error", "An error constructor, regular expression, object, or predicate.", "TypeError", "Error matcher expression")
    ],
    exampleTitle: "Require a TypeError",
    exampleCode: "const { throws } = require(\"node:assert\");\nthrows(() => { throw new TypeError(\"invalid\"); }, TypeError);\nconsole.log(\"matched\");",
    exampleExplanation: "The assertion succeeds because the callback throws the expected error class.",
    keywords: ["test", "assertion", "error"]
  }),
  nodeApi({
    moduleName: "node:timers/promises",
    name: "setTimeout",
    displayName: "Await a delay",
    description: "Returns a promise that fulfills with a value after a delay.",
    definition: "timers/promises setTimeout integrates delays with async functions and supports AbortSignal cancellation and ref control through its options.",
    template: "void (async () => {\n  const { setTimeout } = require(\"node:timers/promises\");\n  console.log(await setTimeout({delay}, {value}));\n})();",
    parameters: [
      parameter("delay", "Delay", "The approximate delay in milliseconds.", "1", "Millisecond expression"),
      parameter("value", "Value", "The fulfillment value.", "42")
    ],
    exampleTitle: "Resolve after a short delay",
    exampleCode: "void (async () => {\n  const { setTimeout } = require(\"node:timers/promises\");\n  console.log(await setTimeout(1, 42));\n})();",
    exampleExplanation: "The promise fulfills with 42 after the timer becomes eligible to run.",
    keywords: ["timer", "promise", "delay"]
  }),
  nodeApi({
    moduleName: "node:timers/promises",
    name: "setImmediate",
    displayName: "Await the check phase",
    description: "Returns a promise that fulfills during the event loop's check phase.",
    definition: "timers/promises setImmediate schedules work after I/O callbacks for the current event-loop iteration and supports AbortSignal cancellation.",
    template: "void (async () => {\n  const { setImmediate } = require(\"node:timers/promises\");\n  console.log(await setImmediate({value}));\n})();",
    parameters: [parameter("value", "Value", "The fulfillment value.", "42")],
    exampleTitle: "Resolve on the next check phase",
    exampleCode: "void (async () => {\n  const { setImmediate } = require(\"node:timers/promises\");\n  console.log(await setImmediate(42));\n})();",
    exampleExplanation: "The promise fulfills with the supplied value on the scheduled check phase.",
    keywords: ["timer", "promise", "event loop"]
  }),
  nodeApi({
    moduleName: "node:zlib",
    name: "gzipSync",
    displayName: "Compress data with gzip synchronously",
    description: "Returns a Buffer containing gzip-compressed input data.",
    definition: "zlib.gzipSync blocks the event loop while compressing, making it suitable for small bounded inputs or tooling. Servers should normally use asynchronous or streaming compression.",
    template: "const { gzipSync } = require(\"node:zlib\");\nconst compressed = gzipSync({data});\nconsole.log(compressed.length);",
    parameters: [parameter("data", "Data", "The string, Buffer, typed array, DataView, or ArrayBuffer to compress.", "\"Hypershell\"", "String or binary expression")],
    exampleTitle: "Compress and restore text",
    exampleCode: "const { gunzipSync, gzipSync } = require(\"node:zlib\");\nconst compressed = gzipSync(\"Hypershell\");\nconsole.log(gunzipSync(compressed).toString(\"utf8\"));",
    exampleExplanation: "The gzip output round-trips through gunzip to the original text.",
    keywords: ["compression", "gzip", "Buffer"]
  }),
  nodeApi({
    moduleName: "node:child_process",
    name: "spawnSync",
    displayName: "Run a child process synchronously",
    description: "Starts a child process and blocks until it exits or reaches a configured limit.",
    definition: "child_process.spawnSync avoids shell parsing unless shell is explicitly enabled. Callers should inspect error, status, signal, stdout, and stderr and should set bounds for untrusted workloads.",
    template: "const { spawnSync } = require(\"node:child_process\");\nconst result = spawnSync({command}, {args}, {options});\nconsole.log(result.stdout);",
    parameters: [
      parameter("command", "Command", "The executable path or name.", "process.execPath", "Executable expression"),
      parameter("args", "Arguments", "An array of literal process arguments.", "[\"-e\", \"process.stdout.write('42')\"]", "Array expression"),
      parameter("options", "Options", "Encoding, working directory, environment, timeout, and shell behavior.", "{ encoding: \"utf8\", shell: false, timeout: 5000 }", "Options object")
    ],
    exampleTitle: "Run an isolated Node child",
    exampleCode: "const { spawnSync } = require(\"node:child_process\");\nconst result = spawnSync(process.execPath, [\"-e\", \"process.stdout.write('42')\"], { encoding: \"utf8\", shell: false, timeout: 5000 });\nif (result.error) throw result.error;\nconsole.log(result.stdout);",
    exampleExplanation: "The current Node executable runs with a literal argument array, no shell, and a timeout.",
    keywords: ["process", "execution", "synchronous", "shell"]
  }),
  nodeExplicit({
    id: "node.stdlib.process-cwd.guide",
    moduleName: "node:process",
    name: "cwd",
    symbolKind: "Function",
    displayName: "Read the current working directory",
    description: "Returns the absolute path Node currently treats as its working directory.",
    definition: "process.cwd reflects the process-wide working directory used to resolve relative file-system paths. process.chdir can change it for the entire process.",
    documentationUrl: "https://nodejs.org/docs/v24.14.0/api/process.html#processcwd",
    template: "console.log({label}, process.cwd());",
    parameters: [parameter("label", "Output label", "A label printed before the path.", "\"cwd\"", "String expression")],
    exampleTitle: "Confirm the working directory is absolute",
    exampleCode: "const { isAbsolute } = require(\"node:path\");\nconsole.log(isAbsolute(process.cwd()));",
    exampleExplanation: "Node exposes the current working directory as an absolute path.",
    keywords: ["working directory", "path"]
  }),
  nodeExplicit({
    id: "node.stdlib.process-argv.guide",
    moduleName: "node:process",
    name: "argv",
    symbolKind: "Property",
    displayName: "Read process arguments",
    description: "Exposes the executable path, script path, and arguments used to launch the current process.",
    definition: "process.argv is an array. The first item is the Node executable, the second is normally the script path, and later items are application arguments.",
    documentationUrl: "https://nodejs.org/docs/v24.14.0/api/process.html#processargv",
    template: "console.log(process.argv[{index}]);",
    parameters: [parameter("index", "Index", "The zero-based argument index to read.", "0", "Integer expression")],
    exampleTitle: "Inspect the executable argument",
    exampleCode: "console.log(typeof process.argv[0] === \"string\" && process.argv[0].length > 0);",
    exampleExplanation: "The first argument always identifies the executable used to start Node.",
    keywords: ["arguments", "CLI", "array"]
  }),
  nodeExplicit({
    id: "node.stdlib.process-env.guide",
    moduleName: "node:process",
    name: "env",
    symbolKind: "Property",
    displayName: "Read or set process environment variables",
    description: "Exposes the current process environment as a string-keyed object.",
    definition: "process.env values are strings when present. Assigning affects the current process and inherited child environments but does not persist system-wide settings.",
    documentationUrl: "https://nodejs.org/docs/v24.14.0/api/process.html#processenv",
    template: "console.log(process.env[{key}]);",
    parameters: [parameter("key", "Variable name", "A string expression containing the environment-variable name.", "\"PATH\"", "String expression")],
    exampleTitle: "Set and remove a process-local value",
    exampleCode: "process.env.HYPERSHELL_GUIDE_VALUE = \"42\";\nconsole.log(process.env.HYPERSHELL_GUIDE_VALUE);\ndelete process.env.HYPERSHELL_GUIDE_VALUE;",
    exampleExplanation: "The value exists only in this isolated Node process and is removed before exit.",
    keywords: ["environment", "variables", "child process"]
  }),
  nodeExplicit({
    id: "node.stdlib.buffer-from.guide",
    moduleName: "node:buffer",
    typeName: "Buffer",
    name: "from",
    symbolKind: "Function",
    displayName: "Create a Buffer from existing data",
    description: "Creates a Buffer containing bytes decoded or copied from an input value.",
    definition: "Buffer.from supports strings, arrays, array buffers, and other buffers. String decoding uses UTF-8 unless another supported encoding is supplied.",
    documentationUrl: "https://nodejs.org/docs/v24.14.0/api/buffer.html#static-method-bufferfromstring-encoding",
    template: "const { Buffer } = require(\"node:buffer\");\nconst bytes = Buffer.from({value}, {encoding});\nconsole.log(bytes);",
    parameters: [
      parameter("value", "Value", "The string or byte source.", "\"Hypershell\"", "String or binary expression"),
      parameter("encoding", "Encoding", "The string encoding used for text input.", "\"utf8\"", "String expression")
    ],
    exampleTitle: "Encode and decode UTF-8 text",
    exampleCode: "const { Buffer } = require(\"node:buffer\");\nconsole.log(Buffer.from(\"Hypershell\", \"utf8\").toString(\"utf8\"));",
    exampleExplanation: "Buffer.from encodes the string and toString decodes the same bytes back to text.",
    keywords: ["bytes", "encoding", "binary"]
  }),
  nodeExplicit({
    id: "node.stdlib.buffer-alloc.guide",
    moduleName: "node:buffer",
    typeName: "Buffer",
    name: "alloc",
    symbolKind: "Function",
    displayName: "Allocate an initialized Buffer",
    description: "Creates a fixed-size Buffer whose bytes are initialized.",
    definition: "Buffer.alloc zero-fills by default or repeats a supplied fill value. It is safer for general use than allocUnsafe when every byte will not be overwritten immediately.",
    documentationUrl: "https://nodejs.org/docs/v24.14.0/api/buffer.html#static-method-bufferallocsize-fill-encoding",
    template: "const { Buffer } = require(\"node:buffer\");\nconst bytes = Buffer.alloc({size}, {fill});\nconsole.log(bytes);",
    parameters: [
      parameter("size", "Size", "The number of bytes to allocate.", "4", "Integer expression"),
      parameter("fill", "Fill", "The byte, string, or Buffer repeated through the allocation.", "42", "Fill expression")
    ],
    exampleTitle: "Allocate four initialized bytes",
    exampleCode: "const { Buffer } = require(\"node:buffer\");\nconsole.log([...Buffer.alloc(4, 42)]);",
    exampleExplanation: "Every allocated byte is initialized to decimal value 42.",
    keywords: ["bytes", "allocation", "initialize"]
  }),
  nodeExplicit({
    id: "node.stdlib.buffer-byte-length.guide",
    moduleName: "node:buffer",
    typeName: "Buffer",
    name: "byteLength",
    symbolKind: "Function",
    displayName: "Measure encoded byte length",
    description: "Returns the number of bytes required to encode a string or contained by a binary value.",
    definition: "Buffer.byteLength measures encoded storage rather than JavaScript UTF-16 code units, so multibyte text can occupy more bytes than string.length reports.",
    documentationUrl: "https://nodejs.org/docs/v24.14.0/api/buffer.html#static-method-bufferbytelengthstring-encoding",
    template: "const { Buffer } = require(\"node:buffer\");\nconsole.log(Buffer.byteLength({value}, {encoding}));",
    parameters: [
      parameter("value", "Value", "The string or binary value to measure.", "\"✓\"", "String or binary expression"),
      parameter("encoding", "Encoding", "The encoding used to measure a string.", "\"utf8\"", "String expression")
    ],
    exampleTitle: "Measure a multibyte character",
    exampleCode: "const { Buffer } = require(\"node:buffer\");\nconsole.log(Buffer.byteLength(\"✓\", \"utf8\"));",
    exampleExplanation: "The check mark occupies three bytes in UTF-8 even though it is one JavaScript character.",
    keywords: ["bytes", "encoding", "length"]
  }),
  nodeExplicit({
    id: "node.stdlib.stream-promises-pipeline.guide",
    moduleName: "node:stream/promises",
    name: "pipeline",
    symbolKind: "Function",
    displayName: "Run a stream pipeline as a promise",
    description: "Connects streams, propagates errors, and resolves after the pipeline finishes.",
    definition: "stream/promises pipeline manages backpressure, forwards failures, and destroys participating streams as needed. It is safer than manually chaining pipe calls for multi-stage work.",
    documentationUrl: "https://nodejs.org/docs/v24.14.0/api/stream.html#streampipelinesource-transforms-destination-options",
    template: "void (async () => {\n  const { pipeline } = require(\"node:stream/promises\");\n  await pipeline({source}, {destination});\n})();",
    parameters: [
      parameter("source", "Source", "A readable stream, iterable, or async iterable.", "require(\"node:stream\").Readable.from([\"42\"])", "Stream expression"),
      parameter("destination", "Destination", "A writable stream or compatible transform.", "process.stdout", "Stream expression")
    ],
    exampleTitle: "Transform iterable text through a pipeline",
    exampleCode: "void (async () => {\n  const { Readable, Transform, Writable } = require(\"node:stream\");\n  const { pipeline } = require(\"node:stream/promises\");\n  let output = \"\";\n  const upper = new Transform({ transform(chunk, encoding, callback) { callback(null, chunk.toString().toUpperCase()); } });\n  const sink = new Writable({ write(chunk, encoding, callback) { output += chunk.toString(); callback(); } });\n  await pipeline(Readable.from([\"hyper\", \"shell\"]), upper, sink);\n  console.log(output);\n})();",
    exampleExplanation: "pipeline applies backpressure, waits for completion, and yields the transformed HYPERSHELL text.",
    keywords: ["stream", "promise", "backpressure", "pipeline"]
  })
];

export const javascriptNodeGuideExpansion = [
  ...javascriptGuides,
  ...nodeGuides
];
