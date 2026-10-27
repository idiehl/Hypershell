const parameter = (
  name,
  displayName,
  description,
  defaultValue,
  typeName = "TypeScript expression") => ({
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

const compilerApiPackageName = "@typescript/typescript6";
const useCompatibilityPackage = value => value?.replaceAll(
  'from "typescript"',
  `from "${compilerApiPackageName}"`);

const coreGuide = ({
  id,
  name,
  displayName,
  description,
  definition,
  category,
  documentationUrl,
  symbolKind = "Syntax",
  template,
  parameters,
  exampleTitle,
  exampleCode,
  exampleExplanation,
  keywords = []
}) => ({
  libraryId: "typescript-core",
  command: {
    id,
    name,
    displayName,
    description,
    definition,
    moduleName: category,
    commandType: "TypeScript language surface",
    preferredShellKind: "TypeScript",
    documentationUrl,
    keywords: ["typescript", "core", category, ...keywords],
    symbolKind,
    coverageTier: "Verified",
    browsePath: [
      { kind: "Category", name: category },
      { kind: symbolKind, name, displayName }
    ],
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

const compilerGuide = ({
  id,
  canonical,
  displayName,
  description,
  definition = description,
  documentationUrl =
    "https://github.com/microsoft/TypeScript/wiki/Using-the-Compiler-API",
  template,
  parameters,
  exampleTitle,
  exampleCode,
  exampleExplanation,
  keywords = []
}) => ({
  libraryId: "typescript-compiler-api",
  canonical,
  command: {
    id,
    displayName,
    description,
    definition,
    commandType: "TypeScript Compiler API",
    preferredShellKind: "TypeScript",
    documentationUrl,
    keywords: ["typescript", "compiler api", ...keywords],
    coverageTier: "Guided",
    template: useCompatibilityPackage(template),
    compositionMode: "Statement",
    parameters,
    examples: [{
      title: exampleTitle,
      code: useCompatibilityPackage(exampleCode),
      explanation: exampleExplanation
    }]
  }
});

const coreGuides = [
  coreGuide({
    id: "typescript.core.intersection-type",
    name: "intersection type",
    displayName: "Combine contracts with an intersection",
    description: "Requires a value to satisfy every member type in one combined contract.",
    definition: "An intersection joins types with ampersands. The resulting type contains the compatible requirements of every member and is useful for composing orthogonal capabilities.",
    category: "Type Composition",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/objects.html#intersection-types",
    template: "type {name} = {left} & {right};\nconst value: {name} = {sample};\nconsole.log(value);",
    parameters: [
      parameter("name", "Type name", "The name of the combined contract.", "Identified", "Identifier"),
      parameter("left", "Left type", "The first member type.", "{ id: number }", "Type expression"),
      parameter("right", "Right type", "The second member type.", "{ name: string }", "Type expression"),
      parameter("sample", "Sample value", "A value satisfying both types.", "{ id: 42, name: \"Ada\" }")
    ],
    exampleTitle: "Combine identity and display data",
    exampleCode: "type Identified = { id: number } & { name: string };\nconst value: Identified = { id: 42, name: \"Ada\" };\nconsole.log(value.name);",
    exampleExplanation: "The object contains the required members from both sides of the intersection.",
    keywords: ["intersection", "compose", "object type"]
  }),
  coreGuide({
    id: "typescript.core.literal-union",
    name: "literal union",
    displayName: "Restrict values with literal types",
    description: "Defines a finite set of permitted string, number, or boolean values.",
    definition: "Literal types preserve exact values in the type system. Joining them in a union creates an enumerable domain that editors can autocomplete and the checker can exhaustively analyze.",
    category: "Type Composition",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/everyday-types.html#literal-types",
    template: "type {name} = {members};\nconst value: {name} = {sample};\nconsole.log(value);",
    parameters: [
      parameter("name", "Type name", "The literal-union alias.", "Direction", "Identifier"),
      parameter("members", "Members", "Literal types joined with vertical bars.", "\"left\" | \"right\"", "Type expression"),
      parameter("sample", "Sample value", "One permitted literal value.", "\"left\"")
    ],
    exampleTitle: "Represent a direction",
    exampleCode: "type Direction = \"left\" | \"right\";\nconst value: Direction = \"left\";\nconsole.log(value);",
    exampleExplanation: "Direction accepts only its two declared string literals.",
    keywords: ["literal", "union", "finite set"]
  }),
  coreGuide({
    id: "typescript.core.tuple-type",
    name: "tuple type",
    displayName: "Model an ordered tuple",
    description: "Assigns a specific type and meaning to each position in an array-like value.",
    definition: "A tuple is an array type with a known sequence of element types. Labels improve readability without changing runtime behavior.",
    category: "Object Types",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/objects.html#tuple-types",
    template: "type {name} = [{firstLabel}: {firstType}, {secondLabel}: {secondType}];\nconst value: {name} = [{firstValue}, {secondValue}];\nconsole.log(value);",
    parameters: [
      parameter("name", "Tuple name", "The tuple alias.", "Coordinate", "Identifier"),
      parameter("firstLabel", "First label", "The first position label.", "x", "Identifier"),
      parameter("firstType", "First type", "The first position type.", "number", "Type expression"),
      parameter("secondLabel", "Second label", "The second position label.", "y", "Identifier"),
      parameter("secondType", "Second type", "The second position type.", "number", "Type expression"),
      parameter("firstValue", "First value", "A value for the first position.", "10"),
      parameter("secondValue", "Second value", "A value for the second position.", "20")
    ],
    exampleTitle: "Create a coordinate tuple",
    exampleCode: "type Coordinate = [x: number, y: number];\nconst point: Coordinate = [10, 20];\nconsole.log(point[0] + point[1]);",
    exampleExplanation: "Each tuple position is checked independently while the runtime value remains an array.",
    keywords: ["tuple", "array", "position"]
  }),
  coreGuide({
    id: "typescript.core.readonly-tuple",
    name: "readonly tuple",
    displayName: "Protect a tuple from mutation",
    description: "Declares an ordered tuple whose positions cannot be reassigned through that type.",
    definition: "The readonly modifier prevents mutating methods and indexed assignment during type checking. It does not freeze the runtime array.",
    category: "Object Types",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/objects.html#readonly-tuple-types",
    template: "type {name} = readonly [{firstType}, {secondType}];\nconst value: {name} = [{firstValue}, {secondValue}];\nconsole.log(value);",
    parameters: [
      parameter("name", "Tuple name", "The readonly tuple alias.", "Pair", "Identifier"),
      parameter("firstType", "First type", "The first position type.", "string", "Type expression"),
      parameter("secondType", "Second type", "The second position type.", "number", "Type expression"),
      parameter("firstValue", "First value", "A value for the first position.", "\"age\""),
      parameter("secondValue", "Second value", "A value for the second position.", "42")
    ],
    exampleTitle: "Declare a readonly pair",
    exampleCode: "type Pair = readonly [string, number];\nconst pair: Pair = [\"age\", 42];\nconsole.log(pair[1]);",
    exampleExplanation: "The pair can be read normally, but assignment to either indexed position would fail type checking.",
    keywords: ["readonly", "tuple", "immutability"]
  }),
  coreGuide({
    id: "typescript.core.generic-constraint",
    name: "generic constraint",
    displayName: "Constrain a generic type parameter",
    description: "Requires generic inputs to expose a known minimum shape while preserving their specific type.",
    definition: "An extends clause on a type parameter limits accepted types. Code inside the generic declaration may safely use members guaranteed by that constraint.",
    category: "Generics",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/generics.html#generic-constraints",
    template: "function {functionName}<T extends {constraint}>(value: T): number {\n  return value.{property};\n}\nconsole.log({functionName}({sample}));",
    parameters: [
      parameter("functionName", "Function name", "The generic function identifier.", "measure", "Identifier"),
      parameter("constraint", "Constraint", "The minimum object shape required of T.", "{ length: number }", "Type expression"),
      parameter("property", "Property", "A member guaranteed by the constraint.", "length", "Identifier"),
      parameter("sample", "Sample value", "A value satisfying the constraint.", "\"typed\"")
    ],
    exampleTitle: "Accept any value with a length",
    exampleCode: "function measure<T extends { length: number }>(value: T): number {\n  return value.length;\n}\nconsole.log(measure(\"typed\"));",
    exampleExplanation: "The constraint permits length access while retaining the caller's concrete string type for T.",
    keywords: ["generic", "constraint", "extends"]
  }),
  coreGuide({
    id: "typescript.core.default-type-parameter",
    name: "default type parameter",
    displayName: "Give a generic parameter a default",
    description: "Supplies a type argument when a caller does not choose one explicitly.",
    definition: "A default type parameter follows an equals sign and makes that type argument optional when inference cannot select a more specific type.",
    category: "Generics",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/generics.html#generic-parameter-defaults",
    template: "type {name}<T = {defaultType}> = {shape};\nconst value: {name} = {sample};\nconsole.log(value);",
    parameters: [
      parameter("name", "Generic type", "The generic alias name.", "Container", "Identifier"),
      parameter("defaultType", "Default type", "The type used when no argument is provided.", "string", "Type expression"),
      parameter("shape", "Type body", "A type expression using T.", "{ value: T }", "Type expression"),
      parameter("sample", "Sample value", "A value matching the default instantiation.", "{ value: \"ready\" }")
    ],
    exampleTitle: "Default a container to strings",
    exampleCode: "type Container<T = string> = { value: T };\nconst value: Container = { value: \"ready\" };\nconsole.log(value.value);",
    exampleExplanation: "Omitting the type argument makes this Container equivalent to Container<string>.",
    keywords: ["generic", "default", "type parameter"]
  }),
  coreGuide({
    id: "typescript.core.indexed-access-type",
    name: "indexed access type",
    displayName: "Read a property type by key",
    description: "Selects the type associated with a property key from another type.",
    definition: "Indexed access uses bracket notation in a type position. A union of keys produces the union of their corresponding property types.",
    category: "Type Operators",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/indexed-access-types.html",
    symbolKind: "Operator",
    template: "type {objectType} = {shape};\ntype {resultType} = {objectType}[{keyType}];\nconst value: {resultType} = {sample};\nconsole.log(value);",
    parameters: [
      parameter("objectType", "Object type", "The source object type name.", "User", "Identifier"),
      parameter("shape", "Object shape", "The properties available for selection.", "{ id: number; name: string }", "Type expression"),
      parameter("resultType", "Result type", "The alias for the selected property type.", "UserId", "Identifier"),
      parameter("keyType", "Key type", "A property-name literal or key union.", "\"id\"", "Type expression"),
      parameter("sample", "Sample value", "A value assignable to the selected type.", "42")
    ],
    exampleTitle: "Extract an identifier property type",
    exampleCode: "type User = { id: number; name: string };\ntype UserId = User[\"id\"];\nconst value: UserId = 42;\nconsole.log(value);",
    exampleExplanation: "User[\"id\"] resolves to number without duplicating the property's declaration.",
    keywords: ["indexed access", "property", "lookup"]
  }),
  coreGuide({
    id: "typescript.core.typeof-type-query",
    name: "typeof type query",
    displayName: "Derive a type from a value",
    description: "Captures the static type of an existing value declaration for reuse in type positions.",
    definition: "TypeScript's typeof type operator refers to the checker-known type of a value. It is distinct from JavaScript's runtime typeof expression.",
    category: "Type Operators",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/typeof-types.html",
    symbolKind: "Operator",
    template: "const {sourceName} = {sourceValue};\ntype {typeName} = typeof {sourceName};\nconst value: {typeName} = {sample};\nconsole.log(value);",
    parameters: [
      parameter("sourceName", "Source binding", "The value whose type is captured.", "defaults", "Identifier"),
      parameter("sourceValue", "Source value", "The value used for inference.", "{ retries: 3, mode: \"safe\" }"),
      parameter("typeName", "Type name", "The alias receiving the inferred type.", "Configuration", "Identifier"),
      parameter("sample", "Sample value", "A value matching the captured type.", "{ retries: 1, mode: \"fast\" }")
    ],
    exampleTitle: "Reuse an inferred configuration shape",
    exampleCode: "const defaults = { retries: 3, mode: \"safe\" };\ntype Configuration = typeof defaults;\nconst value: Configuration = { retries: 1, mode: \"fast\" };\nconsole.log(value.mode);",
    exampleExplanation: "Configuration stays synchronized with the shape inferred for defaults.",
    keywords: ["typeof", "type query", "inference"]
  }),
  coreGuide({
    id: "typescript.core.conditional-type",
    name: "conditional type",
    displayName: "Choose a type conditionally",
    description: "Selects one of two result types according to an assignability test.",
    definition: "A conditional type has the form T extends U ? X : Y. When T is a naked type parameter, unions may distribute through the condition member by member.",
    category: "Type Transformations",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/conditional-types.html",
    template: "type {name}<T> = T extends {condition} ? {trueType} : {falseType};\nconst value: {name}<{sampleInput}> = {sampleValue};\nconsole.log(value);",
    parameters: [
      parameter("name", "Type name", "The conditional type alias.", "Label", "Identifier"),
      parameter("condition", "Condition type", "The type T is tested against.", "string", "Type expression"),
      parameter("trueType", "True result", "The type selected when T is assignable.", "\"text\"", "Type expression"),
      parameter("falseType", "False result", "The alternate result type.", "\"other\"", "Type expression"),
      parameter("sampleInput", "Sample input type", "The type used to instantiate T.", "string", "Type expression"),
      parameter("sampleValue", "Sample value", "A value matching the selected result.", "\"text\"")
    ],
    exampleTitle: "Classify a type",
    exampleCode: "type Label<T> = T extends string ? \"text\" : \"other\";\nconst value: Label<string> = \"text\";\nconsole.log(value);",
    exampleExplanation: "Because string extends string, the conditional type selects the text literal.",
    keywords: ["conditional", "extends", "type transformation"]
  }),
  coreGuide({
    id: "typescript.core.infer-type",
    name: "infer type variable",
    displayName: "Infer part of a matched type",
    description: "Introduces a type variable inside a conditional type and returns the captured component.",
    definition: "The infer keyword declares a type variable in the true branch's match pattern. It is commonly used to extract function results, promise values, and tuple elements.",
    category: "Type Transformations",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/conditional-types.html#inferring-within-conditional-types",
    symbolKind: "Keyword",
    template: "type {name}<T> = T extends (...args: never[]) => infer R ? R : never;\ntype Result = {name}<typeof {functionName}>;\nconst value: Result = {functionName}();\nconsole.log(value);",
    parameters: [
      parameter("name", "Extractor name", "The conditional type alias.", "ResultOf", "Identifier"),
      parameter("functionName", "Function", "A declared function whose result is extracted.", "createValue", "Identifier")
    ],
    exampleTitle: "Extract a function return type",
    exampleCode: "function createValue() { return { id: 42 }; }\ntype ResultOf<T> = T extends (...args: never[]) => infer R ? R : never;\ntype Result = ResultOf<typeof createValue>;\nconst value: Result = createValue();\nconsole.log(value.id);",
    exampleExplanation: "infer captures the return component of the function type as R.",
    keywords: ["infer", "conditional", "extract"]
  }),
  coreGuide({
    id: "typescript.core.mapped-type",
    name: "mapped type",
    displayName: "Transform every property in a type",
    description: "Iterates over property keys to construct a related object type.",
    definition: "A mapped type uses an in clause over a key union, commonly keyof T. Modifiers can be added or removed while each property's value type is retained or transformed.",
    category: "Type Transformations",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/mapped-types.html",
    template: "type {name}<T> = { [K in keyof T]: {wrapper}<T[K]> };\ntype Result = {name}<{source}>;\nconst value: Result = {sample};\nconsole.log(value);",
    parameters: [
      parameter("name", "Mapped type", "The mapped type alias.", "Boxed", "Identifier"),
      parameter("wrapper", "Wrapper type", "A generic wrapper applied to each property.", "Promise", "Type identifier"),
      parameter("source", "Source type", "The object type to transform.", "{ id: number }", "Type expression"),
      parameter("sample", "Sample value", "A value matching the mapped result.", "{ id: Promise.resolve(42) }")
    ],
    exampleTitle: "Make every property a promise",
    exampleCode: "type Promised<T> = { [K in keyof T]: Promise<T[K]> };\ntype Result = Promised<{ id: number }>;\nconst value: Result = { id: Promise.resolve(42) };\nvoid value.id.then(result => console.log(result));",
    exampleExplanation: "The mapped type preserves the id key while wrapping its number value type in Promise.",
    keywords: ["mapped", "keyof", "properties"]
  }),
  coreGuide({
    id: "typescript.core.key-remapping",
    name: "mapped key remapping",
    displayName: "Rename keys in a mapped type",
    description: "Uses an as clause to generate different property names while mapping an object type.",
    definition: "Mapped key remapping combines keyof, template literal types, and an as clause. Mapping a key to never removes that property.",
    category: "Type Transformations",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/mapped-types.html#key-remapping-via-as",
    template: "type {name}<T> = { [K in keyof T as `{prefix}${Capitalize<string & K>}`]: T[K] };\ntype Result = {name}<{source}>;\nconst value: Result = {sample};\nconsole.log(value);",
    parameters: [
      parameter("name", "Mapped type", "The remapping type alias.", "Getters", "Identifier"),
      parameter("prefix", "Prefix", "Text prepended to each generated key.", "get", "Identifier fragment"),
      parameter("source", "Source type", "The object keys and values to remap.", "{ name: string }", "Type expression"),
      parameter("sample", "Sample value", "A value using the remapped key.", "{ getName: \"Ada\" }")
    ],
    exampleTitle: "Generate getter-style keys",
    exampleCode: "type Getters<T> = { [K in keyof T as `get${Capitalize<string & K>}`]: T[K] };\ntype Result = Getters<{ name: string }>;\nconst value: Result = { getName: \"Ada\" };\nconsole.log(value.getName);",
    exampleExplanation: "The name key is remapped to getName and retains its string value type.",
    keywords: ["mapped", "remap", "template literal"]
  }),
  coreGuide({
    id: "typescript.core.template-literal-type",
    name: "template literal type",
    displayName: "Compose string literal types",
    description: "Builds new string literal unions by interpolating literal types and unions.",
    definition: "Template literal types use the runtime template-literal shape in type positions. Union interpolation forms the cross-product of possible strings.",
    category: "Type Transformations",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/template-literal-types.html",
    template: "type {sourceName} = {members};\ntype {resultName} = `{prefix}${{sourceName}}{suffix}`;\nconst value: {resultName} = {sample};\nconsole.log(value);",
    parameters: [
      parameter("sourceName", "Source type", "The literal union to interpolate.", "Field", "Identifier"),
      parameter("members", "Literal members", "String literals joined in a union.", "\"name\" | \"age\"", "Type expression"),
      parameter("resultName", "Result type", "The generated string type alias.", "EventName", "Identifier"),
      parameter("prefix", "Prefix", "Text placed before each interpolated member.", "on", "String fragment"),
      parameter("suffix", "Suffix", "Text placed after each interpolated member.", "Changed", "String fragment"),
      parameter("sample", "Sample value", "One generated literal value.", "\"onNameChanged\"")
    ],
    exampleTitle: "Generate change event names",
    exampleCode: "type Field = \"name\" | \"age\";\ntype EventName = `on${Capitalize<Field>}Changed`;\nconst value: EventName = \"onNameChanged\";\nconsole.log(value);",
    exampleExplanation: "Interpolation and Capitalize generate one event-name literal for each Field member.",
    keywords: ["template literal", "string", "union"]
  }),
  coreGuide({
    id: "typescript.core.type-predicate",
    name: "type predicate",
    displayName: "Define a user type guard",
    description: "Lets a boolean function communicate a successful runtime type check to control-flow analysis.",
    definition: "A return type such as value is string is a type predicate. Callers narrow the tested value when the function returns true.",
    category: "Narrowing",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/narrowing.html#using-type-predicates",
    template: "function {functionName}(value: unknown): value is {targetType} {\n  return {condition};\n}\nconst value: unknown = {sample};\nif ({functionName}(value)) console.log({use});",
    parameters: [
      parameter("functionName", "Guard name", "The predicate function identifier.", "isString", "Identifier"),
      parameter("targetType", "Narrowed type", "The type proven by a true result.", "string", "Type expression"),
      parameter("condition", "Runtime check", "A boolean expression that validates the value.", "typeof value === \"string\"", "Boolean expression"),
      parameter("sample", "Sample value", "An unknown value to inspect.", "\"typed\""),
      parameter("use", "Narrowed use", "An expression valid for the narrowed type.", "value.toUpperCase()")
    ],
    exampleTitle: "Narrow an unknown value to string",
    exampleCode: "function isString(value: unknown): value is string {\n  return typeof value === \"string\";\n}\nconst value: unknown = \"typed\";\nif (isString(value)) console.log(value.toUpperCase());",
    exampleExplanation: "Inside the branch, the predicate narrows value from unknown to string.",
    keywords: ["predicate", "type guard", "narrowing"]
  }),
  coreGuide({
    id: "typescript.core.assertion-function",
    name: "assertion function",
    displayName: "Assert and narrow a value",
    description: "Throws when a condition fails and tells the checker what is true after the call returns.",
    definition: "An asserts return type models a function that cannot return normally unless its stated condition holds. Assertions are useful at validation boundaries.",
    category: "Narrowing",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/release-notes/typescript-3-7.html#assertion-functions",
    template: "function {functionName}(value: unknown): asserts value is {targetType} {\n  if (!({condition})) throw new TypeError({message});\n}\nconst value: unknown = {sample};\n{functionName}(value);\nconsole.log({use});",
    parameters: [
      parameter("functionName", "Assertion name", "The assertion function identifier.", "assertString", "Identifier"),
      parameter("targetType", "Asserted type", "The type established after a successful call.", "string", "Type expression"),
      parameter("condition", "Runtime check", "The condition that validates the value.", "typeof value === \"string\"", "Boolean expression"),
      parameter("message", "Error message", "The expression used for the thrown error.", "\"Expected text\"", "String expression"),
      parameter("sample", "Sample value", "A value to validate.", "\"typed\""),
      parameter("use", "Narrowed use", "An expression valid after the assertion.", "value.toUpperCase()")
    ],
    exampleTitle: "Assert that input is text",
    exampleCode: "function assertString(value: unknown): asserts value is string {\n  if (typeof value !== \"string\") throw new TypeError(\"Expected text\");\n}\nconst value: unknown = \"typed\";\nassertString(value);\nconsole.log(value.toUpperCase());",
    exampleExplanation: "A normal return proves value is string for all following statements in the scope.",
    keywords: ["asserts", "validation", "narrowing"]
  }),
  coreGuide({
    id: "typescript.core.in-narrowing",
    name: "in operator narrowing",
    displayName: "Narrow by checking for a property",
    description: "Uses the runtime in operator to distinguish union members by their available properties.",
    definition: "When the left operand is a property-name literal, TypeScript narrows the right operand to union members that may contain that property.",
    category: "Narrowing",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/narrowing.html#the-in-operator-narrowing",
    symbolKind: "Operator",
    template: "type Value = {leftType} | {rightType};\nfunction describe(value: Value): string {\n  if ({property} in value) return {leftUse};\n  return {rightUse};\n}\nconsole.log(describe({sample}));",
    parameters: [
      parameter("leftType", "First member", "The union member containing the tested property.", "{ path: string }", "Type expression"),
      parameter("rightType", "Second member", "The alternate union member.", "{ url: string }", "Type expression"),
      parameter("property", "Property literal", "The property name checked with in.", "\"path\"", "String literal"),
      parameter("leftUse", "First branch", "An expression valid for the first member.", "value.path"),
      parameter("rightUse", "Second branch", "An expression valid for the alternate member.", "value.url"),
      parameter("sample", "Sample value", "A value matching one union member.", "{ path: \"index.ts\" }")
    ],
    exampleTitle: "Distinguish a path from a URL",
    exampleCode: "type Value = { path: string } | { url: string };\nfunction describe(value: Value): string {\n  if (\"path\" in value) return value.path;\n  return value.url;\n}\nconsole.log(describe({ path: \"index.ts\" }));",
    exampleExplanation: "The property test narrows the first branch to the object containing path.",
    keywords: ["in", "property", "type guard"]
  }),
  coreGuide({
    id: "typescript.core.instanceof-narrowing",
    name: "instanceof narrowing",
    displayName: "Narrow by constructor identity",
    description: "Uses a runtime prototype check to narrow a value to a class instance.",
    definition: "An instanceof expression checks an object's prototype chain. TypeScript uses a successful check to narrow the value to the constructor's instance type.",
    category: "Narrowing",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/narrowing.html#instanceof-narrowing",
    symbolKind: "Operator",
    template: "const value: {leftType} | {rightType} = {sample};\nif (value instanceof {leftType}) console.log({leftUse});\nelse console.log({rightUse});",
    parameters: [
      parameter("leftType", "Checked class", "The constructor used by instanceof.", "Date", "Class identifier"),
      parameter("rightType", "Alternate type", "The other possible value type.", "string", "Type expression"),
      parameter("sample", "Sample value", "A value matching one branch.", "new Date(0)"),
      parameter("leftUse", "Class use", "An expression valid for the class instance.", "value.toISOString()"),
      parameter("rightUse", "Alternate use", "An expression valid for the other member.", "value.toUpperCase()")
    ],
    exampleTitle: "Distinguish a Date from text",
    exampleCode: "const value: Date | string = new Date(0);\nif (value instanceof Date) console.log(value.toISOString());\nelse console.log(value.toUpperCase());",
    exampleExplanation: "The true branch knows value is a Date and exposes Date methods.",
    keywords: ["instanceof", "class", "type guard"]
  }),
  coreGuide({
    id: "typescript.core.unknown-type",
    name: "unknown type",
    displayName: "Accept an unknown value safely",
    description: "Represents a value whose shape must be checked before type-specific use.",
    definition: "unknown accepts every value like any, but disallows arbitrary operations until control-flow analysis or an assertion establishes a narrower type.",
    category: "Special Types",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/functions.html#unknown",
    symbolKind: "Keyword",
    template: "const value: unknown = {sample};\nif ({condition}) console.log({use});",
    parameters: [
      parameter("sample", "Sample value", "The value entering an untrusted boundary.", "\"typed\""),
      parameter("condition", "Narrowing condition", "A runtime check proving a useful type.", "typeof value === \"string\"", "Boolean expression"),
      parameter("use", "Narrowed use", "An expression valid after the check.", "value.toUpperCase()")
    ],
    exampleTitle: "Safely inspect unknown input",
    exampleCode: "const value: unknown = \"typed\";\nif (typeof value === \"string\") console.log(value.toUpperCase());",
    exampleExplanation: "The runtime check is required before string behavior is available.",
    keywords: ["unknown", "safe", "boundary"]
  }),
  coreGuide({
    id: "typescript.core.never-exhaustive",
    name: "never exhaustiveness",
    displayName: "Check a union exhaustively",
    description: "Uses never to make an unhandled union member a type error.",
    definition: "never represents values that cannot occur. Assigning a switch remainder to never verifies that earlier cases handled every discriminant member.",
    category: "Special Types",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/narrowing.html#exhaustiveness-checking",
    symbolKind: "Keyword",
    template: "type State = {members};\nfunction describe(state: State): string {\n  switch (state) {\n    {cases}\n    default: { const unreachable: never = state; return unreachable; }\n  }\n}\nconsole.log(describe({sample}));",
    parameters: [
      parameter("members", "Union members", "Literal types handled by the switch.", "\"ready\" | \"done\"", "Type expression"),
      parameter("cases", "Cases", "A case for every declared member.", "case \"ready\": return \"Ready\";\n    case \"done\": return \"Done\";", "Switch cases"),
      parameter("sample", "Sample value", "One union member to describe.", "\"ready\"")
    ],
    exampleTitle: "Require every state case",
    exampleCode: "type State = \"ready\" | \"done\";\nfunction describe(state: State): string {\n  switch (state) {\n    case \"ready\": return \"Ready\";\n    case \"done\": return \"Done\";\n    default: { const unreachable: never = state; return unreachable; }\n  }\n}\nconsole.log(describe(\"ready\"));",
    exampleExplanation: "If State gains another member, the default assignment stops compiling until that member is handled.",
    keywords: ["never", "exhaustive", "switch"]
  }),
  coreGuide({
    id: "typescript.core.function-overloads",
    name: "function overloads",
    displayName: "Describe multiple call signatures",
    description: "Publishes several accepted call shapes above one compatible implementation.",
    definition: "Overload signatures are visible to callers but erased from output. The implementation signature must accommodate every overload and is not itself callable from the outside.",
    category: "Functions",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/functions.html#function-overloads",
    symbolKind: "Function",
    template: "function {name}(value: {firstType}): {firstResult};\nfunction {name}(value: {secondType}): {secondResult};\nfunction {name}(value: {firstType} | {secondType}): {firstResult} | {secondResult} {\n  return {implementation};\n}\nconsole.log({name}({sample}));",
    parameters: [
      parameter("name", "Function name", "The overloaded function identifier.", "normalize", "Identifier"),
      parameter("firstType", "First input", "The first overload's parameter type.", "string", "Type expression"),
      parameter("firstResult", "First result", "The first overload's result type.", "string", "Type expression"),
      parameter("secondType", "Second input", "The second overload's parameter type.", "number", "Type expression"),
      parameter("secondResult", "Second result", "The second overload's result type.", "number", "Type expression"),
      parameter("implementation", "Implementation", "An expression compatible with both results.", "value", "TypeScript expression"),
      parameter("sample", "Sample call", "An argument matching one overload.", "\"ready\"")
    ],
    exampleTitle: "Overload an identity normalizer",
    exampleCode: "function normalize(value: string): string;\nfunction normalize(value: number): number;\nfunction normalize(value: string | number): string | number { return value; }\nconsole.log(normalize(\"ready\"));",
    exampleExplanation: "Callers receive a precise result type for each accepted input shape.",
    keywords: ["overload", "signature", "function"]
  }),
  coreGuide({
    id: "typescript.core.call-signature",
    name: "call signature",
    displayName: "Describe a callable object",
    description: "Models a value that can be invoked and can also expose named properties.",
    definition: "A call signature is written without an arrow inside an object type. It is useful for JavaScript functions that carry configuration or metadata properties.",
    category: "Functions",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/functions.html#call-signatures",
    symbolKind: "Interface",
    template: "interface {name} {\n  ({parameterName}: {parameterType}): {returnType};\n  {property}: {propertyType};\n}\nconst callable = ((value: {parameterType}) => {expression}) as {name};\ncallable.{property} = {propertyValue};\nconsole.log(callable({sample}), callable.{property});",
    parameters: [
      parameter("name", "Interface name", "The callable contract name.", "Formatter", "Identifier"),
      parameter("parameterName", "Parameter", "The call parameter identifier.", "value", "Identifier"),
      parameter("parameterType", "Parameter type", "The accepted argument type.", "string", "Type expression"),
      parameter("returnType", "Return type", "The call result type.", "string", "Type expression"),
      parameter("property", "Property", "A property carried by the function value.", "label", "Identifier"),
      parameter("propertyType", "Property type", "The carried property's type.", "string", "Type expression"),
      parameter("expression", "Function body", "The expression returned by the function.", "value.toUpperCase()"),
      parameter("propertyValue", "Property value", "The metadata value assigned to the callable.", "\"upper\""),
      parameter("sample", "Sample argument", "A value accepted by the call signature.", "\"typed\"")
    ],
    exampleTitle: "Create a labeled formatter",
    exampleCode: "interface Formatter { (value: string): string; label: string; }\nconst formatter = ((value: string) => value.toUpperCase()) as Formatter;\nformatter.label = \"upper\";\nconsole.log(formatter(\"typed\"), formatter.label);",
    exampleExplanation: "The function satisfies both the callable signature and the label property requirement.",
    keywords: ["callable", "signature", "function object"]
  }),
  coreGuide({
    id: "typescript.core.construct-signature",
    name: "construct signature",
    displayName: "Describe a constructor value",
    description: "Models a value that creates instances when invoked with new.",
    definition: "A construct signature begins with new and describes constructor parameters plus the resulting instance type. It enables factories that accept class-like values.",
    category: "Functions",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/functions.html#construct-signatures",
    symbolKind: "Constructor",
    template: "interface {constructorType} { new ({parameter}: {parameterType}): {instanceType}; }\nfunction create(ctor: {constructorType}, value: {parameterType}): {instanceType} { return new ctor(value); }\nclass {className} { {parameter}: {parameterType}; constructor({parameter}: {parameterType}) { this.{parameter} = {parameter}; } }\nconsole.log(create({className}, {sample}).{parameter});",
    parameters: [
      parameter("constructorType", "Constructor contract", "The construct-signature interface.", "CreatesItem", "Identifier"),
      parameter("parameter", "Constructor parameter", "The instance value initialized by the constructor.", "name", "Identifier"),
      parameter("parameterType", "Parameter type", "The type accepted by the constructor.", "string", "Type expression"),
      parameter("instanceType", "Instance type", "The type returned by new.", "Item", "Type expression"),
      parameter("className", "Class name", "A concrete class satisfying the signature.", "Item", "Identifier"),
      parameter("sample", "Sample argument", "A value passed to the constructor.", "\"typed\"")
    ],
    exampleTitle: "Accept a class in a factory",
    exampleCode: "interface CreatesItem { new (name: string): Item; }\nclass Item { name: string; constructor(name: string) { this.name = name; } }\nfunction create(ctor: CreatesItem, value: string): Item { return new ctor(value); }\nconsole.log(create(Item, \"typed\").name);",
    exampleExplanation: "Item's constructor matches the construct signature and can be passed as a value to the factory.",
    keywords: ["construct", "new", "factory"]
  }),
  coreGuide({
    id: "typescript.core.index-signature",
    name: "index signature",
    displayName: "Describe dynamically named properties",
    description: "Defines the value type returned for keys that are not individually named in an object type.",
    definition: "An index signature constrains property values accessed by string, number, symbol, or a permitted template-pattern key type.",
    category: "Object Types",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/objects.html#index-signatures",
    symbolKind: "Interface",
    template: "interface {name} { [{keyName}: {keyType}]: {valueType}; }\nconst value: {name} = {sample};\nconsole.log(value[{sampleKey}]);",
    parameters: [
      parameter("name", "Interface name", "The indexed object contract.", "Scores", "Identifier"),
      parameter("keyName", "Key name", "The index parameter identifier.", "name", "Identifier"),
      parameter("keyType", "Key type", "The permitted key domain.", "string", "Type expression"),
      parameter("valueType", "Value type", "The value returned for each key.", "number", "Type expression"),
      parameter("sample", "Sample object", "An object satisfying the index signature.", "{ alpha: 10, beta: 20 }"),
      parameter("sampleKey", "Sample key", "A key used to read the object.", "\"beta\"")
    ],
    exampleTitle: "Map names to numeric scores",
    exampleCode: "interface Scores { [name: string]: number; }\nconst scores: Scores = { alpha: 10, beta: 20 };\nconsole.log(scores[\"beta\"]);",
    exampleExplanation: "Every string-named property in Scores must hold a number.",
    keywords: ["index", "dictionary", "dynamic property"]
  }),
  coreGuide({
    id: "typescript.core.interface-merging",
    name: "interface declaration merging",
    displayName: "Merge compatible interface declarations",
    description: "Combines members from repeated interface declarations that share a name.",
    definition: "Interface declarations participate in TypeScript declaration merging. Non-function members must be unique or have matching types, while overload groups are combined.",
    category: "Declarations",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/declaration-merging.html#merging-interfaces",
    symbolKind: "Interface",
    template: "interface {name} { {firstProperty}: {firstType}; }\ninterface {name} { {secondProperty}: {secondType}; }\nconst value: {name} = {sample};\nconsole.log(value);",
    parameters: [
      parameter("name", "Interface name", "The repeated declaration name.", "Dimensions", "Identifier"),
      parameter("firstProperty", "First property", "A member from the first declaration.", "width", "Identifier"),
      parameter("firstType", "First type", "The first member's type.", "number", "Type expression"),
      parameter("secondProperty", "Second property", "A member from the second declaration.", "height", "Identifier"),
      parameter("secondType", "Second type", "The second member's type.", "number", "Type expression"),
      parameter("sample", "Sample value", "An object satisfying the merged interface.", "{ width: 10, height: 20 }")
    ],
    exampleTitle: "Merge dimension members",
    exampleCode: "interface Dimensions { width: number; }\ninterface Dimensions { height: number; }\nconst value: Dimensions = { width: 10, height: 20 };\nconsole.log(value.width * value.height);",
    exampleExplanation: "The merged Dimensions contract requires both width and height.",
    keywords: ["declaration merging", "interface", "augmentation"]
  }),
  coreGuide({
    id: "typescript.core.generic-interface",
    name: "generic interface",
    displayName: "Parameterize an object contract",
    description: "Makes an interface reusable while preserving the relationship between its member types.",
    definition: "A generic interface declares type parameters after its name. Each instantiation substitutes concrete types throughout the interface's members.",
    category: "Generics",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/generics.html#generic-types",
    symbolKind: "Interface",
    template: "interface {name}<T> { value: T; map<U>(transform: (value: T) => U): {name}<U>; }\nconst value: {name}<{type}> = {sample};\nconsole.log(value.value);",
    parameters: [
      parameter("name", "Interface name", "The generic contract identifier.", "Box", "Identifier"),
      parameter("type", "Type argument", "The concrete value type for this instance.", "number", "Type expression"),
      parameter("sample", "Sample value", "An object satisfying the generic interface.", "{ value: 42, map(transform) { return { value: transform(this.value), map: this.map }; } }")
    ],
    exampleTitle: "Create a typed box",
    exampleCode: "interface Box<T> { value: T; }\nconst value: Box<number> = { value: 42 };\nconsole.log(value.value);",
    exampleExplanation: "Instantiating Box<number> makes its value member a number.",
    keywords: ["generic", "interface", "type parameter"]
  }),
  coreGuide({
    id: "typescript.core.this-parameter",
    name: "this parameter",
    displayName: "Type a function's this context",
    description: "Declares the required receiver type for a function without adding a runtime parameter.",
    definition: "A fake first parameter named this exists only for checking. It prevents a function from being called with an incompatible receiver.",
    category: "Functions",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/functions.html#declaring-this-in-a-function",
    symbolKind: "Function",
    template: "interface {contextType} { {property}: {propertyType}; }\nfunction {name}(this: {contextType}): {returnType} { return {expression}; }\nconsole.log({name}.call({sample}));",
    parameters: [
      parameter("contextType", "Context type", "The required receiver contract.", "Counter", "Identifier"),
      parameter("property", "Property", "A member used through this.", "value", "Identifier"),
      parameter("propertyType", "Property type", "The receiver member's type.", "number", "Type expression"),
      parameter("name", "Function name", "The receiver-aware function identifier.", "read", "Identifier"),
      parameter("returnType", "Return type", "The function's result type.", "number", "Type expression"),
      parameter("expression", "Return expression", "An expression using the typed receiver.", "this.value"),
      parameter("sample", "Sample receiver", "An object satisfying the receiver contract.", "{ value: 42 }")
    ],
    exampleTitle: "Require a counter receiver",
    exampleCode: "interface Counter { value: number; }\nfunction read(this: Counter): number { return this.value; }\nconsole.log(read.call({ value: 42 }));",
    exampleExplanation: "The this parameter is erased, but call must provide a receiver with a numeric value.",
    keywords: ["this", "receiver", "function"]
  }),
  coreGuide({
    id: "typescript.core.optional-parameter",
    name: "optional parameter",
    displayName: "Make a function argument optional",
    description: "Allows callers to omit an argument and requires the implementation to handle undefined.",
    definition: "A question mark after a parameter name adds undefined to its effective type. Optional parameters must follow required parameters unless a default handles omission.",
    category: "Functions",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/functions.html#optional-parameters",
    symbolKind: "Function",
    template: "function {name}({parameter}?: {type}): string {\n  return {expression};\n}\nconsole.log({name}({sample}));",
    parameters: [
      parameter("name", "Function name", "The function identifier.", "greet", "Identifier"),
      parameter("parameter", "Parameter", "The optional argument name.", "name", "Identifier"),
      parameter("type", "Parameter type", "The type accepted when supplied.", "string", "Type expression"),
      parameter("expression", "Return expression", "An expression that handles omission.", "`Hello, ${name ?? \"there\"}`"),
      parameter("sample", "Sample argument", "An optional call argument.", "\"Ada\"")
    ],
    exampleTitle: "Greet with an optional name",
    exampleCode: "function greet(name?: string): string {\n  return `Hello, ${name ?? \"there\"}`;\n}\nconsole.log(greet(\"Ada\"));",
    exampleExplanation: "The function accepts a string or omission and explicitly handles the undefined case.",
    keywords: ["optional", "parameter", "undefined"]
  }),
  coreGuide({
    id: "typescript.core.rest-tuple-parameters",
    name: "rest tuple parameters",
    displayName: "Type a variable argument list with a tuple",
    description: "Uses a tuple type to preserve the position and type of every rest argument.",
    definition: "A rest parameter may use a tuple type, including optional and named elements. Calls are checked as though the tuple elements were separate parameters.",
    category: "Functions",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/functions.html#rest-parameters-and-arguments",
    symbolKind: "Function",
    template: "function {name}(...args: [{firstLabel}: {firstType}, {secondLabel}: {secondType}]): string {\n  return {expression};\n}\nconsole.log({name}({firstValue}, {secondValue}));",
    parameters: [
      parameter("name", "Function name", "The variadic function identifier.", "format", "Identifier"),
      parameter("firstLabel", "First label", "The first rest-tuple parameter label.", "label", "Identifier"),
      parameter("firstType", "First type", "The first argument type.", "string", "Type expression"),
      parameter("secondLabel", "Second label", "The second rest-tuple parameter label.", "value", "Identifier"),
      parameter("secondType", "Second type", "The second argument type.", "number", "Type expression"),
      parameter("expression", "Return expression", "An expression using the tuple arguments.", "`${args[0]}: ${args[1]}`"),
      parameter("firstValue", "First value", "The first call argument.", "\"Answer\""),
      parameter("secondValue", "Second value", "The second call argument.", "42")
    ],
    exampleTitle: "Format a labeled value",
    exampleCode: "function format(...args: [label: string, value: number]): string {\n  return `${args[0]}: ${args[1]}`;\n}\nconsole.log(format(\"Answer\", 42));",
    exampleExplanation: "The rest array retains a fixed two-position tuple type at the call site and in the body.",
    keywords: ["rest", "tuple", "parameters"]
  }),
  coreGuide({
    id: "typescript.core.variadic-tuple",
    name: "variadic tuple type",
    displayName: "Compose tuple types with rest elements",
    description: "Spreads generic tuple types into a new ordered tuple without losing individual positions.",
    definition: "Variadic tuple types allow generic spreads in tuple positions. They model operations such as concatenation, partial application, and argument forwarding precisely.",
    category: "Type Composition",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/release-notes/typescript-4-0.html#variadic-tuple-types",
    template: "function concat<A extends unknown[], B extends unknown[]>(left: [...A], right: [...B]): [...A, ...B] {\n  return [...left, ...right];\n}\nconst value = concat({left}, {right});\nconsole.log(value);",
    parameters: [
      parameter("left", "Left tuple", "The first tuple expression.", "[\"answer\", 42]"),
      parameter("right", "Right tuple", "The tuple appended after the first.", "[true]")
    ],
    exampleTitle: "Concatenate two typed tuples",
    exampleCode: "function concat<A extends unknown[], B extends unknown[]>(left: [...A], right: [...B]): [...A, ...B] {\n  return [...left, ...right];\n}\nconst value = concat([\"answer\", 42], [true]);\nconsole.log(value);",
    exampleExplanation: "The result retains the ordered string, number, and boolean element types.",
    keywords: ["variadic", "tuple", "spread"]
  }),
  coreGuide({
    id: "typescript.core.branded-type",
    name: "branded structural type",
    displayName: "Distinguish structurally identical values",
    description: "Intersects a value type with a private-looking marker to prevent accidental interchange.",
    definition: "A brand is a TypeScript modeling convention rather than a runtime feature. A unique marker property makes otherwise identical structural types incompatible after validation or construction.",
    category: "Type Composition",
    documentationUrl: "https://www.typescriptlang.org/docs/handbook/2/objects.html#intersection-types",
    template: "type {name} = {baseType} & { readonly __brand: {brandLiteral} };\nfunction create(value: {baseType}): {name} { return value as {name}; }\nconst value = create({sample});\nconsole.log(value);",
    parameters: [
      parameter("name", "Branded type", "The distinct domain type name.", "UserId", "Identifier"),
      parameter("baseType", "Base type", "The runtime representation.", "number", "Type expression"),
      parameter("brandLiteral", "Brand", "A unique literal used only by the checker.", "\"UserId\"", "Literal type"),
      parameter("sample", "Sample value", "A validated runtime value.", "42")
    ],
    exampleTitle: "Brand a numeric user identifier",
    exampleCode: "type UserId = number & { readonly __brand: \"UserId\" };\nfunction createUserId(value: number): UserId { return value as UserId; }\nconst value = createUserId(42);\nconsole.log(value);",
    exampleExplanation: "The brand exists only in the type system while the runtime value remains a number.",
    keywords: ["brand", "nominal", "intersection"]
  })
];

const compilerGuides = [
  compilerGuide({
    id: "typescript.compiler-api.create-source-file",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createSourceFile" },
    displayName: "Parse source text into an AST",
    description: "Creates a SourceFile syntax tree from TypeScript or JavaScript text.",
    template: "import * as ts from \"typescript\";\nconst source = ts.createSourceFile({fileName}, {sourceText}, ts.ScriptTarget.{target}, {setParents});",
    parameters: [
      parameter("fileName", "File name", "The logical source-file name and extension.", "\"example.ts\"", "String expression"),
      parameter("sourceText", "Source text", "The text to parse.", "\"const answer: number = 42;\"", "String expression"),
      parameter("target", "Script target", "The ScriptTarget enum member used by the parser.", "Latest", "Enum member"),
      parameter("setParents", "Set parents", "Whether parsed nodes receive parent links.", "true", "Boolean expression")
    ],
    exampleTitle: "Parse a typed binding",
    exampleCode: "import * as ts from \"typescript\";\nconst source = ts.createSourceFile(\"example.ts\", \"const answer: number = 42;\", ts.ScriptTarget.Latest, true);\nconsole.log(ts.SyntaxKind[source.statements[0].kind]);",
    exampleExplanation: "The returned SourceFile owns the parsed statement tree and optional parent links.",
    keywords: ["parser", "source file", "ast"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.create-program",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createProgram", signatureIncludes: "rootNames: readonly string[]" },
    displayName: "Create a compiler program",
    description: "Builds the compiler's whole-project view from root files, options, and an optional host.",
    template: "import * as ts from \"typescript\";\nconst program = ts.createProgram({rootNames}, {options});",
    parameters: [
      parameter("rootNames", "Root files", "An array of source file paths.", "[\"index.ts\"]", "String array expression"),
      parameter("options", "Compiler options", "CompilerOptions controlling resolution, checking, and emit.", "{ noEmit: true, strict: true }", "CompilerOptions expression")
    ],
    exampleTitle: "Create a strict no-emit program",
    exampleCode: "import * as ts from \"typescript\";\nconst program = ts.createProgram([\"index.ts\"], { noEmit: true, strict: true });\nconsole.log(program.getRootFileNames());",
    exampleExplanation: "Program coordinates source loading, binding, type checking, module resolution, and emit state.",
    keywords: ["program", "project", "type checker"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.for-each-child",
    canonical: { namespacePath: ["ts"], typeName: null, name: "forEachChild" },
    displayName: "Walk direct AST children",
    description: "Visits a node's syntactic children in source order through the supported traversal API.",
    template: "import * as ts from \"typescript\";\nts.forEachChild({node}, child => { {statement} });",
    parameters: [
      parameter("node", "Node", "The AST node whose children are visited.", "source", "ts.Node expression"),
      parameter("statement", "Visitor body", "Work performed for each child.", "console.log(ts.SyntaxKind[child.kind]);", "TypeScript statement")
    ],
    exampleTitle: "List source-file child kinds",
    exampleCode: "import * as ts from \"typescript\";\nconst source = ts.createSourceFile(\"example.ts\", \"const answer = 42;\", ts.ScriptTarget.Latest);\nts.forEachChild(source, child => console.log(ts.SyntaxKind[child.kind]));",
    exampleExplanation: "forEachChild exposes the public syntactic tree without relying on internal node arrays.",
    keywords: ["traversal", "visitor", "ast"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.factory-identifier",
    canonical: { namespacePath: ["ts"], typeName: "NodeFactory", name: "createIdentifier" },
    displayName: "Create an identifier node",
    description: "Creates a synthesized Identifier through the supported node factory.",
    template: "import * as ts from \"typescript\";\nconst identifier = ts.factory.createIdentifier({text});",
    parameters: [
      parameter("text", "Identifier text", "The identifier's source spelling.", "\"answer\"", "String expression")
    ],
    exampleTitle: "Create an answer identifier",
    exampleCode: "import * as ts from \"typescript\";\nconst identifier = ts.factory.createIdentifier(\"answer\");\nconsole.log(identifier.text);",
    exampleExplanation: "Using ts.factory keeps synthesized nodes compatible with the active compiler version.",
    keywords: ["factory", "identifier", "ast"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.transpile-module",
    canonical: { namespacePath: ["ts"], typeName: null, name: "transpileModule" },
    displayName: "Transpile one isolated module",
    description: "Performs syntactic transformation and emit for one source string without creating a complete Program.",
    template: "import * as ts from \"typescript\";\nconst result = ts.transpileModule({sourceText}, { compilerOptions: { target: ts.ScriptTarget.{target}, module: ts.ModuleKind.{moduleKind} } });",
    parameters: [
      parameter("sourceText", "Source text", "The isolated TypeScript source to transpile.", "\"const answer: number = 42;\"", "String expression"),
      parameter("target", "Target", "The emitted JavaScript language level.", "ES2022", "ScriptTarget member"),
      parameter("moduleKind", "Module kind", "The emitted module format.", "ESNext", "ModuleKind member")
    ],
    exampleTitle: "Strip a number annotation",
    exampleCode: "import * as ts from \"typescript\";\nconst result = ts.transpileModule(\"const answer: number = 42;\", { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } });\nconsole.log(result.outputText);",
    exampleExplanation: "transpileModule emits JavaScript but does not perform whole-program semantic checking.",
    keywords: ["transpile", "emit", "isolated"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.create-printer",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createPrinter" },
    displayName: "Create an AST printer",
    description: "Creates a Printer that converts parsed or synthesized nodes back into source text.",
    template: "import * as ts from \"typescript\";\nconst printer = ts.createPrinter({options});",
    parameters: [
      parameter("options", "Printer options", "Formatting and newline settings for emitted text.", "{ newLine: ts.NewLineKind.LineFeed }", "ts.PrinterOptions expression")
    ],
    exampleTitle: "Create a line-feed printer",
    exampleCode: "import * as ts from \"typescript\";\nconst printer = ts.createPrinter({ newLine: ts.NewLineKind.LineFeed });\nconsole.log(typeof printer.printNode);",
    exampleExplanation: "The printer can render individual nodes, node lists, or complete files with consistent formatting options.",
    keywords: ["printer", "emit", "ast"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.printer-print-node",
    canonical: { namespacePath: ["ts"], typeName: "Printer", name: "printNode" },
    displayName: "Print one AST node",
    description: "Renders a node to source text using an emit hint and source-file context.",
    template: "import * as ts from \"typescript\";\nconst text = {printer}.printNode(ts.EmitHint.{hint}, {node}, {sourceFile});",
    parameters: [
      parameter("printer", "Printer", "A ts.Printer expression.", "ts.createPrinter()", "ts.Printer expression"),
      parameter("hint", "Emit hint", "How the printer should interpret the node's syntactic role.", "Unspecified", "EmitHint member"),
      parameter("node", "Node", "The AST node to render.", "ts.factory.createIdentifier(\"answer\")", "ts.Node expression"),
      parameter("sourceFile", "Source file", "The source context used by the printer.", "ts.createSourceFile(\"example.ts\", \"\", ts.ScriptTarget.Latest)", "ts.SourceFile expression")
    ],
    exampleTitle: "Print a synthesized identifier",
    exampleCode: "import * as ts from \"typescript\";\nconst printer = ts.createPrinter();\nconst source = ts.createSourceFile(\"example.ts\", \"\", ts.ScriptTarget.Latest);\nconsole.log(printer.printNode(ts.EmitHint.Unspecified, ts.factory.createIdentifier(\"answer\"), source));",
    exampleExplanation: "The printer renders the factory-created identifier as valid source text.",
    keywords: ["printer", "printNode", "source text"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.transform",
    canonical: { namespacePath: ["ts"], typeName: null, name: "transform" },
    displayName: "Run AST transformer factories",
    description: "Applies one or more transformer factories to a node or node array and returns transformed roots.",
    template: "import * as ts from \"typescript\";\nconst result = ts.transform({source}, [{transformer}]);\ntry { {statement} } finally { result.dispose(); }",
    parameters: [
      parameter("source", "Source", "The node or node array to transform.", "sourceFile", "ts.Node expression"),
      parameter("transformer", "Transformer", "A TransformerFactory compatible with the source node type.", "context => root => ts.visitEachChild(root, node => node, context)", "TransformerFactory expression"),
      parameter("statement", "Result use", "Work performed with result.transformed.", "console.log(result.transformed.length);", "TypeScript statement")
    ],
    exampleTitle: "Run an identity transform",
    exampleCode: "import * as ts from \"typescript\";\nconst source = ts.createSourceFile(\"example.ts\", \"const answer = 42;\", ts.ScriptTarget.Latest);\nconst result = ts.transform(source, [context => root => ts.visitEachChild(root, node => node, context)]);\ntry { console.log(result.transformed.length); } finally { result.dispose(); }",
    exampleExplanation: "TransformationResult owns disposable compiler resources and exposes the transformed roots.",
    keywords: ["transform", "transformer", "visitor"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.visit-each-child",
    canonical: { namespacePath: ["ts"], typeName: null, name: "visitEachChild", signatureIncludes: "node: T, visitor" },
    displayName: "Recursively update a node's children",
    description: "Visits child nodes through a transformer context and rebuilds parents only when descendants change.",
    template: "import * as ts from \"typescript\";\nconst visit = (node: ts.Node): ts.VisitResult<ts.Node> => ts.visitEachChild(node, visit, {context});",
    parameters: [
      parameter("context", "Context", "The active TransformationContext supplied to a transformer factory.", "context", "ts.TransformationContext expression")
    ],
    exampleTitle: "Define a recursive transformer visitor",
    exampleCode: "import * as ts from \"typescript\";\nconst transformer: ts.TransformerFactory<ts.SourceFile> = context => {\n  const visit = (node: ts.Node): ts.VisitResult<ts.Node> => ts.visitEachChild(node, visit, context);\n  return source => ts.visitNode(source, visit) as ts.SourceFile;\n};\nconsole.log(typeof transformer);",
    exampleExplanation: "visitEachChild preserves unchanged nodes and uses the transformation context for safe tree updates.",
    keywords: ["visitor", "transformer", "recursion"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.create-compiler-host",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createCompilerHost" },
    displayName: "Create the default compiler host",
    description: "Builds the file-system and output callbacks a Program uses for source loading and emit.",
    template: "import * as ts from \"typescript\";\nconst options: ts.CompilerOptions = {options};\nconst host = ts.createCompilerHost(options, {setParents});",
    parameters: [
      parameter("options", "Compiler options", "Options shared by the host and Program.", "{ strict: true, noEmit: true }", "ts.CompilerOptions expression"),
      parameter("setParents", "Set parents", "Whether host-created source files should include parent links.", "true", "Boolean expression")
    ],
    exampleTitle: "Create a strict analysis host",
    exampleCode: "import * as ts from \"typescript\";\nconst options: ts.CompilerOptions = { strict: true, noEmit: true };\nconst host = ts.createCompilerHost(options, true);\nconsole.log(host.getCurrentDirectory());",
    exampleExplanation: "The returned host can be wrapped to customize source reads, module resolution, or emitted outputs.",
    keywords: ["compiler host", "filesystem", "program"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.program-get-type-checker",
    canonical: { namespacePath: ["ts"], typeName: "Program", name: "getTypeChecker" },
    displayName: "Get a program's type checker",
    description: "Returns the semantic engine used to resolve symbols and inspect types within a Program.",
    template: "const checker = {program}.getTypeChecker();",
    parameters: [
      parameter("program", "Program", "The compiler Program that owns semantic state.", "program", "ts.Program expression")
    ],
    exampleTitle: "Access semantic analysis",
    exampleCode: "import * as ts from \"typescript\";\nconst program = ts.createProgram([\"index.ts\"], { noEmit: true });\nconst checker = program.getTypeChecker();\nconsole.log(typeof checker.getTypeAtLocation);",
    exampleExplanation: "A TypeChecker is tied to its Program and should be used with nodes from that same program.",
    keywords: ["type checker", "semantic", "program"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.checker-get-type-at-location",
    canonical: { namespacePath: ["ts"], typeName: "TypeChecker", name: "getTypeAtLocation" },
    displayName: "Resolve the type at an AST node",
    description: "Returns the semantic Type associated with a node in the current Program.",
    template: "const type = {checker}.getTypeAtLocation({node});",
    parameters: [
      parameter("checker", "Type checker", "The Program's TypeChecker.", "checker", "ts.TypeChecker expression"),
      parameter("node", "Node", "A node belonging to the checker's Program.", "node", "ts.Node expression")
    ],
    exampleTitle: "Inspect a declaration type",
    exampleCode: "const type = checker.getTypeAtLocation(node);\nconsole.log(checker.typeToString(type));",
    exampleExplanation: "The Type object represents the checker's semantic interpretation at that source location.",
    keywords: ["type", "node", "semantic"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.checker-get-symbol-at-location",
    canonical: { namespacePath: ["ts"], typeName: "TypeChecker", name: "getSymbolAtLocation" },
    displayName: "Resolve the symbol at an AST node",
    description: "Finds the declaration symbol referenced or introduced at a node when one exists.",
    template: "const symbol = {checker}.getSymbolAtLocation({node});\nif (symbol) { {statement} }",
    parameters: [
      parameter("checker", "Type checker", "The Program's TypeChecker.", "checker", "ts.TypeChecker expression"),
      parameter("node", "Node", "An identifier or other symbol-bearing node.", "node", "ts.Node expression"),
      parameter("statement", "Symbol use", "Work performed when resolution succeeds.", "console.log(symbol.getName());", "TypeScript statement")
    ],
    exampleTitle: "Read a resolved symbol name",
    exampleCode: "const symbol = checker.getSymbolAtLocation(node);\nif (symbol) console.log(symbol.getName());",
    exampleExplanation: "Symbols represent the type system's view of declarations and references, not JavaScript Symbol values.",
    keywords: ["symbol", "resolution", "semantic"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.checker-type-to-string",
    canonical: { namespacePath: ["ts"], typeName: "TypeChecker", name: "typeToString" },
    displayName: "Format a semantic type",
    description: "Converts a Type object into a readable TypeScript type expression.",
    template: "const text = {checker}.typeToString({type}, {enclosingNode}, {flags});",
    parameters: [
      parameter("checker", "Type checker", "The TypeChecker that owns the Type.", "checker", "ts.TypeChecker expression"),
      parameter("type", "Type", "The semantic Type to format.", "type", "ts.Type expression"),
      parameter("enclosingNode", "Enclosing node", "Optional context for accessible names.", "undefined", "ts.Node or undefined"),
      parameter("flags", "Format flags", "TypeFormatFlags controlling detail and spelling.", "ts.TypeFormatFlags.NoTruncation", "ts.TypeFormatFlags expression")
    ],
    exampleTitle: "Print a complete type",
    exampleCode: "const text = checker.typeToString(type, undefined, ts.TypeFormatFlags.NoTruncation);\nconsole.log(text);",
    exampleExplanation: "Formatting through the owning checker respects aliases, scope, and requested detail flags.",
    keywords: ["type", "format", "display"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.get-pre-emit-diagnostics",
    canonical: { namespacePath: ["ts"], typeName: null, name: "getPreEmitDiagnostics" },
    displayName: "Collect diagnostics before emit",
    description: "Combines configuration, syntactic, options, global, and semantic diagnostics for a Program.",
    template: "import * as ts from \"typescript\";\nconst diagnostics = ts.getPreEmitDiagnostics({program});",
    parameters: [
      parameter("program", "Program", "The Program to diagnose.", "program", "ts.Program expression")
    ],
    exampleTitle: "Collect project diagnostics",
    exampleCode: "import * as ts from \"typescript\";\nconst diagnostics = ts.getPreEmitDiagnostics(program);\nconsole.log(diagnostics.length);",
    exampleExplanation: "The helper gathers the standard diagnostic phases typically checked before calling emit.",
    keywords: ["diagnostic", "type check", "errors"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.flatten-diagnostic-message",
    canonical: { namespacePath: ["ts"], typeName: null, name: "flattenDiagnosticMessageText" },
    displayName: "Flatten a diagnostic message chain",
    description: "Converts nested diagnostic message chains into one readable string with a chosen newline.",
    template: "import * as ts from \"typescript\";\nconst message = ts.flattenDiagnosticMessageText({messageText}, {newLine});",
    parameters: [
      parameter("messageText", "Message text", "A diagnostic string or DiagnosticMessageChain.", "diagnostic.messageText", "Diagnostic message expression"),
      parameter("newLine", "Newline", "The separator used between nested messages.", "\"\\n\"", "String expression")
    ],
    exampleTitle: "Format a compiler diagnostic",
    exampleCode: "import * as ts from \"typescript\";\nconsole.log(ts.flattenDiagnosticMessageText(diagnostic.messageText, \"\\n\"));",
    exampleExplanation: "Nested explanatory branches become a stable multiline message suitable for a console or problem list.",
    keywords: ["diagnostic", "message", "format"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.get-line-character",
    canonical: { namespacePath: ["ts"], typeName: null, name: "getLineAndCharacterOfPosition" },
    displayName: "Convert an offset to line and column",
    description: "Maps a zero-based source offset to its zero-based line and character coordinates.",
    template: "import * as ts from \"typescript\";\nconst location = ts.getLineAndCharacterOfPosition({sourceFile}, {position});",
    parameters: [
      parameter("sourceFile", "Source file", "The SourceFileLike containing the position.", "sourceFile", "ts.SourceFileLike expression"),
      parameter("position", "Position", "A zero-based character offset.", "diagnostic.start ?? 0", "Number expression")
    ],
    exampleTitle: "Locate a diagnostic",
    exampleCode: "import * as ts from \"typescript\";\nconst location = ts.getLineAndCharacterOfPosition(sourceFile, diagnostic.start ?? 0);\nconsole.log(location.line + 1, location.character + 1);",
    exampleExplanation: "Adding one produces the one-based coordinates commonly shown to users.",
    keywords: ["position", "line", "diagnostic"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.program-emit",
    canonical: { namespacePath: ["ts"], typeName: "Program", name: "emit" },
    displayName: "Emit a program",
    description: "Writes JavaScript, declaration, and source-map outputs according to compiler options and callbacks.",
    template: "const emitResult = {program}.emit({sourceFile}, {writeFile});",
    parameters: [
      parameter("program", "Program", "The Program to emit.", "program", "ts.Program expression"),
      parameter("sourceFile", "Source file", "A specific SourceFile or undefined for the entire Program.", "undefined", "ts.SourceFile or undefined"),
      parameter("writeFile", "Write callback", "An optional callback that receives generated outputs.", "undefined", "ts.WriteFileCallback or undefined")
    ],
    exampleTitle: "Emit all program outputs",
    exampleCode: "const emitResult = program.emit();\nconsole.log(emitResult.emitSkipped, emitResult.diagnostics.length);",
    exampleExplanation: "EmitResult reports whether output was skipped and includes diagnostics produced during emit.",
    keywords: ["emit", "javascript", "output"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.find-config-file",
    canonical: { namespacePath: ["ts"], typeName: null, name: "findConfigFile" },
    displayName: "Find the nearest tsconfig file",
    description: "Walks upward from a search path until a named configuration file is found.",
    template: "import * as ts from \"typescript\";\nconst configPath = ts.findConfigFile({searchPath}, ts.sys.fileExists, {configName});",
    parameters: [
      parameter("searchPath", "Search path", "The file or directory where the upward search begins.", "process.cwd()", "String expression"),
      parameter("configName", "Config name", "The configuration filename to locate.", "\"tsconfig.json\"", "String expression")
    ],
    exampleTitle: "Locate tsconfig.json",
    exampleCode: "import * as ts from \"typescript\";\nconst configPath = ts.findConfigFile(process.cwd(), ts.sys.fileExists, \"tsconfig.json\");\nconsole.log(configPath);",
    exampleExplanation: "The search uses the compiler system's fileExists callback and returns undefined when no config is found.",
    keywords: ["tsconfig", "configuration", "filesystem"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.read-config-file",
    canonical: { namespacePath: ["ts"], typeName: null, name: "readConfigFile" },
    displayName: "Read and parse a config file",
    description: "Loads JSON configuration text and reports a structured config value or a diagnostic.",
    template: "import * as ts from \"typescript\";\nconst result = ts.readConfigFile({fileName}, ts.sys.readFile);",
    parameters: [
      parameter("fileName", "Config path", "The tsconfig or jsconfig file to read.", "\"tsconfig.json\"", "String expression")
    ],
    exampleTitle: "Read tsconfig.json",
    exampleCode: "import * as ts from \"typescript\";\nconst result = ts.readConfigFile(\"tsconfig.json\", ts.sys.readFile);\nif (result.error) console.error(ts.flattenDiagnosticMessageText(result.error.messageText, \"\\n\"));\nelse console.log(result.config);",
    exampleExplanation: "readConfigFile handles comments and compiler JSON syntax while preserving parse errors as diagnostics.",
    keywords: ["tsconfig", "json", "diagnostic"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.parse-json-config",
    canonical: { namespacePath: ["ts"], typeName: null, name: "parseJsonConfigFileContent" },
    displayName: "Resolve parsed compiler configuration",
    description: "Expands a config object into file names, normalized compiler options, project references, and diagnostics.",
    template: "import * as ts from \"typescript\";\nconst parsed = ts.parseJsonConfigFileContent({config}, ts.sys, {basePath});",
    parameters: [
      parameter("config", "Config object", "The value returned by readConfigFile or another JSON source.", "configResult.config", "Configuration object expression"),
      parameter("basePath", "Base path", "The directory used to resolve relative paths and include patterns.", "process.cwd()", "String expression")
    ],
    exampleTitle: "Resolve a configuration object",
    exampleCode: "import * as ts from \"typescript\";\nconst parsed = ts.parseJsonConfigFileContent({ compilerOptions: { strict: true }, files: [\"index.ts\"] }, ts.sys, process.cwd());\nconsole.log(parsed.fileNames, parsed.options.strict);",
    exampleExplanation: "The returned ParsedCommandLine contains normalized options and the resolved project file list.",
    keywords: ["tsconfig", "compiler options", "files"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.get-parsed-command-line",
    canonical: { namespacePath: ["ts"], typeName: null, name: "getParsedCommandLineOfConfigFile" },
    displayName: "Load a complete parsed command line",
    description: "Reads a config file and resolves its options and file graph through a ParseConfigFileHost.",
    template: "import * as ts from \"typescript\";\nconst parsed = ts.getParsedCommandLineOfConfigFile({configPath}, {options}, {host});",
    parameters: [
      parameter("configPath", "Config path", "The configuration file to load.", "\"tsconfig.json\"", "String expression"),
      parameter("options", "Extended options", "Compiler options that override or extend the config.", "{}", "ts.CompilerOptions expression"),
      parameter("host", "Config host", "A ParseConfigFileHost used for filesystem access and error reporting.", "{ ...ts.sys, onUnRecoverableConfigFileDiagnostic: diagnostic => console.error(diagnostic.messageText) }", "ts.ParseConfigFileHost expression")
    ],
    exampleTitle: "Load tsconfig through a host",
    exampleCode: "import * as ts from \"typescript\";\nconst host: ts.ParseConfigFileHost = { ...ts.sys, onUnRecoverableConfigFileDiagnostic: diagnostic => console.error(diagnostic.messageText) };\nconst parsed = ts.getParsedCommandLineOfConfigFile(\"tsconfig.json\", {}, host);\nconsole.log(parsed?.fileNames.length);",
    exampleExplanation: "This higher-level helper combines config reading, extends resolution, path expansion, and option conversion.",
    keywords: ["tsconfig", "parse", "host"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.resolve-module-name",
    canonical: { namespacePath: ["ts"], typeName: null, name: "resolveModuleName" },
    displayName: "Resolve one module specifier",
    description: "Runs TypeScript's module-resolution algorithm for a specifier and containing file.",
    template: "import * as ts from \"typescript\";\nconst resolution = ts.resolveModuleName({moduleName}, {containingFile}, {options}, ts.sys);",
    parameters: [
      parameter("moduleName", "Module name", "The import specifier to resolve.", "\"./dependency\"", "String expression"),
      parameter("containingFile", "Containing file", "The absolute or project-relative source file issuing the import.", "\"/project/index.ts\"", "String expression"),
      parameter("options", "Compiler options", "Module and path settings used by resolution.", "{ moduleResolution: ts.ModuleResolutionKind.NodeNext }", "ts.CompilerOptions expression")
    ],
    exampleTitle: "Resolve a relative dependency",
    exampleCode: "import * as ts from \"typescript\";\nconst resolution = ts.resolveModuleName(\"./dependency\", \"/project/index.ts\", { moduleResolution: ts.ModuleResolutionKind.NodeNext }, ts.sys);\nconsole.log(resolution.resolvedModule?.resolvedFileName);",
    exampleExplanation: "The result includes the resolved module when successful and failed lookup locations for diagnostics or tracing.",
    keywords: ["module resolution", "import", "path"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.create-incremental-program",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createIncrementalProgram" },
    displayName: "Create an incremental builder program",
    description: "Creates a builder that can reuse semantic and emit state between related compilations.",
    template: "import * as ts from \"typescript\";\nconst builder = ts.createIncrementalProgram({ rootNames: {rootNames}, options: {options} });",
    parameters: [
      parameter("rootNames", "Root files", "The source files entering the project graph.", "[\"index.ts\"]", "String array expression"),
      parameter("options", "Compiler options", "Incremental compiler options, commonly including incremental and tsBuildInfoFile.", "{ incremental: true, noEmit: true }", "ts.CompilerOptions expression")
    ],
    exampleTitle: "Create an incremental analysis build",
    exampleCode: "import * as ts from \"typescript\";\nconst builder = ts.createIncrementalProgram({ rootNames: [\"index.ts\"], options: { incremental: true, noEmit: true } });\nconsole.log(builder.getProgram().getRootFileNames());",
    exampleExplanation: "Builder programs expose the underlying Program and reuse prior work when supplied compatible state.",
    keywords: ["incremental", "builder", "performance"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.create-emit-builder",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createEmitAndSemanticDiagnosticsBuilderProgram", signatureIncludes: "rootNames: readonly string[]" },
    displayName: "Create an emit-aware builder program",
    description: "Tracks affected files so semantic diagnostics and emit can be updated incrementally.",
    template: "import * as ts from \"typescript\";\nconst builder = ts.createEmitAndSemanticDiagnosticsBuilderProgram({rootNames}, {options}, {host});",
    parameters: [
      parameter("rootNames", "Root files", "The source files entering the builder graph.", "[\"index.ts\"]", "String array expression"),
      parameter("options", "Compiler options", "Options shared across builder updates.", "{ incremental: true }", "ts.CompilerOptions expression"),
      parameter("host", "Compiler host", "An optional CompilerHost or BuilderProgramHost.", "ts.createIncrementalCompilerHost({ incremental: true })", "Compiler host expression")
    ],
    exampleTitle: "Create an affected-file builder",
    exampleCode: "import * as ts from \"typescript\";\nconst options: ts.CompilerOptions = { incremental: true, noEmit: true };\nconst host = ts.createIncrementalCompilerHost(options);\nconst builder = ts.createEmitAndSemanticDiagnosticsBuilderProgram([\"index.ts\"], options, host);\nconsole.log(builder.getProgram().getRootFileNames());",
    exampleExplanation: "The builder can report and emit only files affected by changes when reused by a watch or incremental workflow.",
    keywords: ["builder", "incremental", "emit"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.create-watch-host",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createWatchCompilerHost", signatureIncludes: "configFileName: string" },
    displayName: "Create a config-file watch host",
    description: "Creates the host callbacks and builder strategy used to monitor a TypeScript project configuration.",
    template: "import * as ts from \"typescript\";\nconst host = ts.createWatchCompilerHost({configPath}, {options}, ts.sys, ts.createSemanticDiagnosticsBuilderProgram);",
    parameters: [
      parameter("configPath", "Config path", "The tsconfig file to watch.", "\"tsconfig.json\"", "String expression"),
      parameter("options", "Extended options", "Compiler options layered over the configuration.", "{}", "ts.CompilerOptions expression")
    ],
    exampleTitle: "Prepare a semantic watch host",
    exampleCode: "import * as ts from \"typescript\";\nconst host = ts.createWatchCompilerHost(\"tsconfig.json\", {}, ts.sys, ts.createSemanticDiagnosticsBuilderProgram);\nconsole.log(typeof host.watchFile);",
    exampleExplanation: "The host can be customized before it is passed to createWatchProgram.",
    keywords: ["watch", "host", "builder"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.create-watch-program",
    canonical: { namespacePath: ["ts"], typeName: null, name: "createWatchProgram", signatureIncludes: "WatchCompilerHostOfConfigFile" },
    displayName: "Start a compiler watch program",
    description: "Starts file watching and rebuilds a project through the supplied watch host.",
    template: "import * as ts from \"typescript\";\nconst watch = ts.createWatchProgram({host});",
    parameters: [
      parameter("host", "Watch host", "A host created or customized for the watched project.", "host", "WatchCompilerHost expression")
    ],
    exampleTitle: "Start a config watch",
    exampleCode: "import * as ts from \"typescript\";\nconst host = ts.createWatchCompilerHost(\"tsconfig.json\", {}, ts.sys, ts.createSemanticDiagnosticsBuilderProgram);\nconst watch = ts.createWatchProgram(host);\nconsole.log(watch.getProgram().getProgram().getRootFileNames());",
    exampleExplanation: "The returned watch owns file watchers and the current builder program and should be closed when no longer needed.",
    keywords: ["watch", "rebuild", "files"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.is-identifier",
    canonical: { namespacePath: ["ts"], typeName: null, name: "isIdentifier" },
    displayName: "Narrow a node to Identifier",
    description: "Tests a Node's syntax kind and exposes Identifier-specific members to TypeScript.",
    template: "import * as ts from \"typescript\";\nif (ts.isIdentifier({node})) { {statement} }",
    parameters: [
      parameter("node", "Node", "The AST node to test.", "node", "ts.Node expression"),
      parameter("statement", "Identifier use", "Work performed with the narrowed Identifier.", "console.log(node.text);", "TypeScript statement")
    ],
    exampleTitle: "Read identifier text",
    exampleCode: "import * as ts from \"typescript\";\nif (ts.isIdentifier(node)) console.log(node.text);",
    exampleExplanation: "The predicate narrows node to ts.Identifier inside the branch.",
    keywords: ["node predicate", "identifier", "narrowing"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.is-function-declaration",
    canonical: { namespacePath: ["ts"], typeName: null, name: "isFunctionDeclaration" },
    displayName: "Narrow a node to FunctionDeclaration",
    description: "Tests whether a Node is a function declaration and enables access to its name, parameters, and body.",
    template: "import * as ts from \"typescript\";\nif (ts.isFunctionDeclaration({node})) { {statement} }",
    parameters: [
      parameter("node", "Node", "The AST node to test.", "node", "ts.Node expression"),
      parameter("statement", "Declaration use", "Work performed with the narrowed function declaration.", "console.log(node.name?.text);", "TypeScript statement")
    ],
    exampleTitle: "Read a function name",
    exampleCode: "import * as ts from \"typescript\";\nif (ts.isFunctionDeclaration(node)) console.log(node.name?.text);",
    exampleExplanation: "The supported predicate is safer than manually comparing SyntaxKind and casting.",
    keywords: ["node predicate", "function", "declaration"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.is-class-declaration",
    canonical: { namespacePath: ["ts"], typeName: null, name: "isClassDeclaration" },
    displayName: "Narrow a node to ClassDeclaration",
    description: "Tests whether a Node is a class declaration and exposes its class-specific syntax members.",
    template: "import * as ts from \"typescript\";\nif (ts.isClassDeclaration({node})) { {statement} }",
    parameters: [
      parameter("node", "Node", "The AST node to test.", "node", "ts.Node expression"),
      parameter("statement", "Declaration use", "Work performed with the narrowed class declaration.", "console.log(node.name?.text, node.members.length);", "TypeScript statement")
    ],
    exampleTitle: "Inspect a class declaration",
    exampleCode: "import * as ts from \"typescript\";\nif (ts.isClassDeclaration(node)) console.log(node.name?.text, node.members.length);",
    exampleExplanation: "The predicate narrows node to ts.ClassDeclaration for safe member access.",
    keywords: ["node predicate", "class", "declaration"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.get-combined-modifier-flags",
    canonical: { namespacePath: ["ts"], typeName: null, name: "getCombinedModifierFlags" },
    displayName: "Read effective declaration modifiers",
    description: "Returns a bitmask containing the modifier flags associated with a declaration.",
    template: "import * as ts from \"typescript\";\nconst flags = ts.getCombinedModifierFlags({declaration});\nconst isExported = (flags & ts.ModifierFlags.Export) !== 0;",
    parameters: [
      parameter("declaration", "Declaration", "The declaration whose modifiers are inspected.", "node", "ts.Declaration expression")
    ],
    exampleTitle: "Check whether a declaration is exported",
    exampleCode: "import * as ts from \"typescript\";\nconst flags = ts.getCombinedModifierFlags(node);\nconsole.log((flags & ts.ModifierFlags.Export) !== 0);",
    exampleExplanation: "Bitwise checks test individual ModifierFlags without depending on modifier-array layout.",
    keywords: ["modifier", "export", "flags"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.get-jsdoc-comments-tags",
    canonical: { namespacePath: ["ts"], typeName: null, name: "getJSDocCommentsAndTags" },
    displayName: "Read attached JSDoc nodes",
    description: "Returns JSDoc blocks and tags associated with a host syntax node.",
    template: "import * as ts from \"typescript\";\nconst jsDoc = ts.getJSDocCommentsAndTags({node});",
    parameters: [
      parameter("node", "Host node", "The syntax node whose documentation is requested.", "node", "ts.Node expression")
    ],
    exampleTitle: "Count documentation nodes",
    exampleCode: "import * as ts from \"typescript\";\nconst jsDoc = ts.getJSDocCommentsAndTags(node);\nconsole.log(jsDoc.length);",
    exampleExplanation: "The helper handles JSDoc attachment rules and returns syntax nodes for structured inspection.",
    keywords: ["jsdoc", "documentation", "ast"]
  }),
  compilerGuide({
    id: "typescript.compiler-api.get-default-lib-file-path",
    canonical: { namespacePath: ["ts"], typeName: null, name: "getDefaultLibFilePath" },
    displayName: "Locate the default library declaration file",
    description: "Returns the compiler installation path to the default lib declaration selected by compiler options.",
    template: "import * as ts from \"typescript\";\nconst libPath = ts.getDefaultLibFilePath({options});",
    parameters: [
      parameter("options", "Compiler options", "Options whose target influences the selected default library.", "{ target: ts.ScriptTarget.ES2022 }", "ts.CompilerOptions expression")
    ],
    exampleTitle: "Locate the ES2022 default library",
    exampleCode: "import * as ts from \"typescript\";\nconsole.log(ts.getDefaultLibFilePath({ target: ts.ScriptTarget.ES2022 }));",
    exampleExplanation: "The returned path points into the active TypeScript package rather than the user's project source tree.",
    keywords: ["lib.d.ts", "compiler installation", "path"]
  })
];

export const typescriptGuideExpansion = [
  ...coreGuides,
  ...compilerGuides
];
