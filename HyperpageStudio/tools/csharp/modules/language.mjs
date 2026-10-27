import {
  command,
  createLibrary,
  example,
  parameter,
} from "../catalog-utils.mjs";

const commands = [
  command({
    name: "Language.Variable",
    description: "Declare a local variable whose type is inferred.",
    definition:
      "The var keyword asks the compiler to infer the local variable's static type from its initializer. The variable remains strongly typed; var is not a dynamic value.",
    moduleName: "C# language · Values",
    action: "Create",
    object: "Variable",
    template: "var {name} = {value};",
    parameters: [
      parameter("name", "name", "A valid local variable identifier.", "identifier"),
      parameter("value", "value", "The expression used to initialize the variable.", "C# expression"),
    ],
    examples: [
      example(
        "Infer a string variable",
        String.raw`var projectName = "Hypershell";
Console.WriteLine(projectName);`,
        "The compiler infers string from the initializer while retaining compile-time type checking.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/declarations",
    keywords: ["var", "local", "declaration", "inference"],
  }),
  command({
    name: "Language.Constant",
    description: "Declare a compile-time constant.",
    definition:
      "A const local or field must be initialized with a compile-time constant expression and cannot be assigned again. Constants are appropriate for values that are truly fixed by the program.",
    moduleName: "C# language · Values",
    action: "Create",
    object: "Constant",
    template: "const {type} {name} = {value};",
    parameters: [
      parameter("type", "type", "The constant's C# type.", "type name"),
      parameter("name", "name", "A valid constant identifier.", "identifier"),
      parameter("value", "value", "A compile-time constant expression.", "C# expression"),
    ],
    examples: [
      example(
        "Define a retry limit",
        String.raw`const int MaxAttempts = 3;
Console.WriteLine($"Retry limit: {MaxAttempts}");`,
        "MaxAttempts is embedded as a compile-time constant and cannot be reassigned.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/const",
    keywords: ["const", "readonly", "fixed value"],
  }),
  command({
    name: "Language.If",
    description: "Run a block only when a condition is true.",
    definition:
      "An if statement evaluates a Boolean expression and executes its body only when that expression is true.",
    moduleName: "C# language · Branching",
    action: "Select",
    object: "Branch",
    template: "if ({condition})\n{\n    {body}\n}",
    parameters: [
      parameter("condition", "condition", "A Boolean expression.", "bool expression"),
      parameter("body", "body", "One or more statements to run when the condition is true.", "C# statements"),
    ],
    examples: [
      example(
        "Validate a file before reading",
        String.raw`var path = "settings.json";
if (File.Exists(path))
{
    Console.WriteLine(File.ReadAllText(path));
}`,
        "The file is opened only when the existence check succeeds.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements",
    keywords: ["if", "condition", "branch", "boolean"],
  }),
  command({
    name: "Language.IfElse",
    description: "Choose between two statement blocks.",
    definition:
      "An if-else statement runs exactly one of two branches according to the result of a Boolean condition.",
    moduleName: "C# language · Branching",
    action: "Select",
    object: "Alternative",
    template:
      "if ({condition})\n{\n    {whenTrue}\n}\nelse\n{\n    {whenFalse}\n}",
    parameters: [
      parameter("condition", "condition", "A Boolean expression.", "bool expression"),
      parameter("whenTrue", "whenTrue", "Statements for the true branch.", "C# statements"),
      parameter("whenFalse", "whenFalse", "Statements for the false branch.", "C# statements"),
    ],
    examples: [
      example(
        "Report an input result",
        String.raw`var input = "42";
if (int.TryParse(input, out var number))
{
    Console.WriteLine($"Parsed {number}");
}
else
{
    Console.WriteLine("Input is not an integer.");
}`,
        "TryParse controls which branch runs without throwing for invalid input.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements",
    keywords: ["if", "else", "condition", "branch"],
  }),
  command({
    name: "Language.SwitchExpression",
    description: "Map one input value to one result expression.",
    definition:
      "A switch expression compares an input against ordered patterns and returns the expression associated with the first matching arm. A discard arm commonly supplies the fallback.",
    moduleName: "C# language · Branching",
    action: "Transform",
    object: "Switch expression",
    template:
      "var {resultName} = {value} switch\n{\n    {arms}\n};",
    parameters: [
      parameter("resultName", "resultName", "The result variable identifier.", "identifier"),
      parameter("value", "value", "The expression to match.", "C# expression"),
      parameter("arms", "arms", "Comma-separated pattern arms, including a fallback when needed.", "switch arms"),
    ],
    examples: [
      example(
        "Classify an HTTP status",
        String.raw`var statusCode = 404;
var category = statusCode switch
{
    >= 200 and < 300 => "Success",
    >= 400 and < 500 => "Client error",
    >= 500 => "Server error",
    _ => "Other"
};
Console.WriteLine(category);`,
        "Relational and logical patterns turn a numeric status into a descriptive category.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/switch-expression",
    keywords: ["switch", "expression", "pattern", "mapping"],
  }),
  command({
    name: "Language.ForEach",
    description: "Run a block once for each value in a sequence.",
    definition:
      "A foreach statement obtains an enumerator from a sequence and executes its body for each element. It is the usual loop for IEnumerable<T> values.",
    moduleName: "C# language · Iteration",
    action: "Enumerate",
    object: "Sequence",
    template:
      "foreach (var {itemName} in {sequence})\n{\n    {body}\n}",
    parameters: [
      parameter("itemName", "itemName", "The identifier for the current element.", "identifier"),
      parameter("sequence", "sequence", "An enumerable expression.", "IEnumerable<T>"),
      parameter("body", "body", "Statements to run for each element.", "C# statements"),
    ],
    examples: [
      example(
        "Print normalized names",
        String.raw`var names = new[] { " alpha ", " beta ", " gamma " };
foreach (var name in names)
{
    Console.WriteLine(name.Trim().ToUpperInvariant());
}`,
        "The loop visits each array element in order and works with one strongly typed value at a time.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/iteration-statements",
    keywords: ["foreach", "loop", "enumerable", "iteration"],
  }),
  command({
    name: "Language.For",
    description: "Repeat a block with explicit initialization, condition, and update.",
    definition:
      "A for statement combines loop initialization, a continuation condition, and an iterator expression. It is useful when an index or exact iteration count matters.",
    moduleName: "C# language · Iteration",
    action: "Run",
    object: "Counted loop",
    template:
      "for ({initializer}; {condition}; {iterator})\n{\n    {body}\n}",
    parameters: [
      parameter("initializer", "initializer", "The loop initializer, such as int index = 0.", "C# statement fragment"),
      parameter("condition", "condition", "The Boolean continuation condition.", "bool expression"),
      parameter("iterator", "iterator", "The update performed after each iteration.", "C# expression"),
      parameter("body", "body", "Statements to run in each iteration.", "C# statements"),
    ],
    examples: [
      example(
        "Print valid list indexes",
        String.raw`var colors = new[] { "blue", "orange", "green" };
for (var index = 0; index < colors.Length; index++)
{
    Console.WriteLine($"{index}: {colors[index]}");
}`,
        "The condition keeps every array access within the valid zero-based index range.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/iteration-statements",
    keywords: ["for", "loop", "index", "iteration"],
  }),
  command({
    name: "Language.While",
    description: "Repeat a block while a condition remains true.",
    definition:
      "A while statement checks its Boolean condition before each iteration. Its body may execute zero times when the initial condition is false.",
    moduleName: "C# language · Iteration",
    action: "Run",
    object: "Conditional loop",
    template: "while ({condition})\n{\n    {body}\n}",
    parameters: [
      parameter("condition", "condition", "The Boolean continuation condition.", "bool expression"),
      parameter("body", "body", "Statements that make progress toward ending the loop.", "C# statements"),
    ],
    examples: [
      example(
        "Process a queue",
        String.raw`var pending = new Queue<string>(new[] { "build", "test", "package" });
while (pending.TryDequeue(out var step))
{
    Console.WriteLine($"Running {step}");
}`,
        "TryDequeue both tests for an item and removes it, so the loop ends when the queue is empty.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/iteration-statements",
    keywords: ["while", "loop", "condition", "iteration"],
  }),
  command({
    name: "Language.TryCatch",
    description: "Handle a selected exception from a protected block.",
    definition:
      "A try statement runs protected code. A matching catch clause handles an exception, and an optional finally clause performs cleanup regardless of success or failure.",
    moduleName: "C# language · Errors",
    action: "Protect",
    object: "Operation",
    template:
      "try\n{\n    {body}\n}\ncatch ({exceptionType} exception)\n{\n    {handler}\n}",
    parameters: [
      parameter("body", "body", "Statements that may throw.", "C# statements"),
      parameter("exceptionType", "exceptionType", "The exception type to handle.", "exception type"),
      parameter("handler", "handler", "Statements that respond to the exception.", "C# statements"),
    ],
    examples: [
      example(
        "Handle malformed JSON",
        String.raw`try
{
    using var document = JsonDocument.Parse("{ invalid json }");
}
catch (JsonException exception)
{
    Console.WriteLine($"JSON error: {exception.Message}");
}`,
        "Only JSON parsing failures are caught; unrelated exceptions are not silently swallowed.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/exception-handling-statements",
    keywords: ["try", "catch", "finally", "exception", "error handling"],
  }),
  command({
    name: "Language.UsingDeclaration",
    description: "Dispose a resource automatically when its scope ends.",
    definition:
      "A using declaration registers an IDisposable or IAsyncDisposable value for deterministic cleanup at the end of the current scope, including when an exception leaves that scope.",
    moduleName: "C# language · Resources",
    action: "Protect",
    object: "Disposable resource",
    template: "using var {name} = {resource};",
    parameters: [
      parameter("name", "name", "The local resource identifier.", "identifier"),
      parameter("resource", "resource", "An expression returning an IDisposable value.", "IDisposable"),
    ],
    examples: [
      example(
        "Close a file stream reliably",
        String.raw`using var stream = File.OpenRead("input.bin");
Console.WriteLine($"Length: {stream.Length:N0} bytes");`,
        "The stream is disposed automatically when the enclosing top-level scope completes.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/statements/using",
    keywords: ["using", "dispose", "resource", "IDisposable"],
  }),
  command({
    name: "Language.LocalFunction",
    description: "Declare a named function inside the current member or top-level program.",
    definition:
      "A local function packages reusable logic within its containing scope. It can capture surrounding variables and can be generic, recursive, asynchronous, or an iterator.",
    moduleName: "C# language · Functions",
    action: "Create",
    object: "Local function",
    template:
      "{returnType} {name}({parameters})\n{\n    {body}\n}",
    parameters: [
      parameter("returnType", "returnType", "The function's return type.", "type name"),
      parameter("name", "name", "A valid function identifier.", "identifier"),
      parameter("parameters", "parameters", "A comma-separated parameter declaration list.", "parameter declarations"),
      parameter("body", "body", "The function body statements.", "C# statements"),
    ],
    examples: [
      example(
        "Normalize a reusable label",
        String.raw`string NormalizeLabel(string value)
{
    return value.Trim().ToLowerInvariant().Replace(' ', '-');
}

Console.WriteLine(NormalizeLabel("  Release Candidate  "));`,
        "The helper stays scoped to this program and can be called like any other strongly typed method.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/programming-guide/classes-and-structs/local-functions",
    keywords: ["function", "method", "local", "reusable"],
  }),
  command({
    name: "Language.Lambda",
    description: "Create an inline anonymous function.",
    definition:
      "A lambda expression creates a delegate or expression-tree value from parameters and an expression or statement body. Lambdas can capture variables from the enclosing scope.",
    moduleName: "C# language · Functions",
    action: "Create",
    object: "Lambda",
    template: "var {name} = ({parameters}) => {body};",
    parameters: [
      parameter("name", "name", "The delegate variable identifier.", "identifier"),
      parameter("parameters", "parameters", "Lambda parameter declarations or identifiers.", "lambda parameters"),
      parameter("body", "body", "The expression returned by the lambda.", "C# expression"),
    ],
    examples: [
      example(
        "Create a reusable predicate",
        String.raw`Func<string, bool> isMarkdown =
    name => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

Console.WriteLine(isMarkdown("README.MD"));`,
        "The lambda is converted to Func<string, bool> and can be passed to filtering APIs.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/lambda-expressions",
    keywords: ["lambda", "delegate", "anonymous function", "Func", "Action"],
  }),
  command({
    name: "Language.Tuple",
    description: "Group several named values without declaring a dedicated type.",
    definition:
      "A value tuple stores a fixed number of strongly typed elements. Element names improve readability but do not change the tuple's runtime type identity.",
    moduleName: "C# language · Values",
    action: "Create",
    object: "Tuple",
    template: "var {name} = ({elements});",
    parameters: [
      parameter("name", "name", "The tuple variable identifier.", "identifier"),
      parameter("elements", "elements", "Comma-separated named or unnamed values.", "tuple elements"),
    ],
    examples: [
      example(
        "Return a compact result",
        String.raw`var result = (Files: 12, Errors: 1);
Console.WriteLine($"Files={result.Files}; Errors={result.Errors}");`,
        "Named elements make the two integer values self-describing without a separate class.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/value-tuples",
    keywords: ["tuple", "value tuple", "deconstruct", "multiple values"],
  }),
  command({
    name: "Language.Record",
    description: "Declare an immutable-oriented data type with value equality.",
    definition:
      "A record is a reference or value type designed for data models. Positional records synthesize properties, value equality, deconstruction, and a readable string representation.",
    moduleName: "C# language · Types",
    action: "Create",
    object: "Record",
    template:
      "var {variable} = new {recordName}({arguments});\nConsole.WriteLine({variable});\n\nrecord {recordName}({parameters});",
    parameters: [
      parameter("variable", "variable", "The local instance identifier.", "identifier"),
      parameter("recordName", "recordName", "The record type name.", "type identifier"),
      parameter("arguments", "arguments", "Constructor argument expressions.", "argument list"),
      parameter("parameters", "parameters", "Positional property declarations.", "parameter declarations"),
    ],
    examples: [
      example(
        "Model an immutable project",
        String.raw`var project = new Project("Hypershell", "Active");
var archived = project with { Status = "Archived" };
Console.WriteLine(archived);

record Project(string Name, string Status);`,
        "The with expression creates a copy with one changed property while value-based formatting displays both fields.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record",
    keywords: ["record", "immutable", "value equality", "with expression"],
  }),
  command({
    name: "Language.PropertyPattern",
    description: "Test an object's shape and property values.",
    definition:
      "A property pattern recursively matches accessible properties and can introduce a typed variable when the input satisfies the requested shape.",
    moduleName: "C# language · Patterns",
    action: "Test",
    object: "Property pattern",
    template: "var {result} = {value} is {pattern};",
    parameters: [
      parameter("result", "result", "The Boolean result identifier.", "identifier"),
      parameter("value", "value", "The expression to test.", "C# expression"),
      parameter("pattern", "pattern", "A type and/or property pattern.", "C# pattern"),
    ],
    examples: [
      example(
        "Recognize a successful response shape",
        String.raw`var response = new { StatusCode = 204, Body = "" };
var succeeded = response is { StatusCode: >= 200 and < 300 };
Console.WriteLine(succeeded);`,
        "The property pattern tests the status range without separately extracting StatusCode.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/patterns",
    keywords: ["pattern", "property", "is", "matching"],
  }),
  command({
    name: "Language.CollectionExpression",
    description: "Create a collection using compact bracket syntax.",
    definition:
      "A collection expression uses bracket syntax and optional spread elements to initialize a compatible collection type. The target type determines the resulting representation.",
    moduleName: "C# language · Values",
    action: "Create",
    object: "Collection",
    template: "{type} {name} = [{items}];",
    parameters: [
      parameter("type", "type", "A collection-expression target type.", "type name"),
      parameter("name", "name", "The collection variable identifier.", "identifier"),
      parameter("items", "items", "Comma-separated elements and optional spread elements.", "collection elements"),
    ],
    examples: [
      example(
        "Combine fixed and spread elements",
        String.raw`string[] core = ["build", "test"];
string[] pipeline = ["clean", .. core, "package"];
Console.WriteLine(string.Join(" -> ", pipeline));`,
        "The spread element inserts every value from core between the fixed first and last entries.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/collection-expressions",
    keywords: ["collection expression", "spread", "array", "list"],
  }),
  command({
    name: "Language.NullCoalescing",
    description: "Use a fallback when a nullable value is null.",
    definition:
      "The null-coalescing operator returns its left operand when non-null and otherwise evaluates and returns its right operand. The right side is not evaluated unnecessarily.",
    moduleName: "C# language · Nullability",
    action: "Select",
    object: "Fallback value",
    template: "var {result} = {nullableValue} ?? {fallback};",
    parameters: [
      parameter("result", "result", "The result variable identifier.", "identifier"),
      parameter("nullableValue", "nullableValue", "A nullable expression.", "nullable value"),
      parameter("fallback", "fallback", "The value used only when the left side is null.", "C# expression"),
    ],
    examples: [
      example(
        "Default a missing environment value",
        String.raw`var mode =
    Environment.GetEnvironmentVariable("APP_MODE")
    ?? "Production";
Console.WriteLine(mode);`,
        "Production is used only when APP_MODE is absent, not when it contains an empty string.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/null-coalescing-operator",
    keywords: ["null", "coalescing", "fallback", "nullable", "??"],
  }),
  command({
    name: "Language.NullConditional",
    description: "Access a member only when its receiver is non-null.",
    definition:
      "The null-conditional operator short-circuits member or element access when the receiver is null and produces null instead of throwing NullReferenceException.",
    moduleName: "C# language · Nullability",
    action: "Access",
    object: "Nullable member",
    template: "var {result} = {value}?.{member};",
    parameters: [
      parameter("result", "result", "The nullable result variable identifier.", "identifier"),
      parameter("value", "value", "A possibly null receiver expression.", "nullable value"),
      parameter("member", "member", "The member access or invocation after ?..", "member expression"),
    ],
    examples: [
      example(
        "Read an optional string length",
        String.raw`string? label = null;
int? length = label?.Trim().Length;
Console.WriteLine(length?.ToString() ?? "No label");`,
        "The chained calls are skipped when label is null, and the nullable length remains null.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/member-access-operators",
    keywords: ["null conditional", "nullable", "safe navigation", "?.", "?[]"],
  }),
  command({
    name: "Language.RangeSlice",
    description: "Select a contiguous range by index.",
    definition:
      "Range and index expressions describe a start and end boundary. The end is exclusive, and the ^ operator counts backward from the end of the sequence.",
    moduleName: "C# language · Values",
    action: "Slice",
    object: "Range",
    template: "var {result} = {value}[{range}];",
    parameters: [
      parameter("result", "result", "The sliced value identifier.", "identifier"),
      parameter("value", "value", "A range-capable value such as a string or array.", "range-capable value"),
      parameter("range", "range", "A range expression such as 1..^1.", "Range"),
    ],
    examples: [
      example(
        "Remove the first and last characters",
        String.raw`var wrapped = "[Hypershell]";
var content = wrapped[1..^1];
Console.WriteLine(content);`,
        "Index 1 skips the opening bracket and ^1 identifies the position immediately before the final bracket.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/member-access-operators",
    keywords: ["range", "index", "slice", "..", "from end"],
  }),
  command({
    name: "Language.Await",
    description: "Asynchronously wait for an operation without blocking its thread.",
    definition:
      "The await operator suspends the current asynchronous flow until its awaitable completes, then retrieves the result or propagates its exception.",
    moduleName: "C# language · Asynchrony",
    action: "Wait",
    object: "Awaitable operation",
    template: "var {result} = await {operation};",
    parameters: [
      parameter("result", "result", "The completed result variable identifier.", "identifier"),
      parameter("operation", "operation", "A Task<T>, ValueTask<T>, or other awaitable expression.", "awaitable"),
    ],
    examples: [
      example(
        "Await asynchronous file I/O",
        String.raw`var text = await File.ReadAllTextAsync("README.md");
Console.WriteLine($"Characters: {text.Length:N0}");`,
        "The generated top-level C# program supports await directly and resumes with the file contents.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/csharp/language-reference/operators/await",
    keywords: ["await", "async", "Task", "ValueTask", "nonblocking"],
  }),
];

export default createLibrary({
  id: "csharp-language",
  displayName: "C# language patterns",
  description:
    "Runnable templates for C# values, branching, iteration, functions, types, patterns, nullability, and asynchronous flow.",
  category: "Language foundations",
  kind: "Language",
  parentLibraryId: "",
  keywords: ["syntax", "statements", "patterns", "learning"],
  objectDescriptions: {
    Alternative:
      "An alternative is one of two mutually exclusive statement branches selected by a Boolean condition.",
    "Awaitable operation":
      "An awaitable operation exposes a completion protocol that C# can suspend for without blocking the current thread.",
    Branch:
      "A branch is a block of statements whose execution depends on a condition.",
    Collection:
      "A collection stores a sequence or set of values under one strongly typed object.",
    Constant:
      "A constant is a named value fixed at compile time and unavailable for later assignment.",
    "Conditional loop":
      "A conditional loop repeats while a Boolean condition remains true.",
    "Counted loop":
      "A counted loop exposes explicit initialization, continuation, and update expressions.",
    "Disposable resource":
      "A disposable resource owns cleanup-sensitive state such as a file handle, stream, lock, or native object.",
    "Fallback value":
      "A fallback value substitutes for a nullable expression only when that expression is null.",
    Lambda:
      "A lambda is an anonymous function that can be converted to a delegate or expression tree.",
    "Local function":
      "A local function is a named function declared inside another member or a top-level program.",
    "Nullable member":
      "A nullable member access may produce no result when its receiver is null.",
    Operation:
      "An operation is a block of work that may complete normally or raise an exception.",
    "Property pattern":
      "A property pattern tests an object's type, shape, and nested property values.",
    Range:
      "A range identifies a contiguous, end-exclusive portion of a range-capable value.",
    Record:
      "A record is a data-focused C# type with synthesized value equality and nondestructive mutation support.",
    Sequence:
      "A sequence exposes values one at a time through C# enumeration.",
    "Switch expression":
      "A switch expression maps an input value to an output by testing ordered patterns.",
    Tuple:
      "A value tuple groups a fixed number of strongly typed elements without requiring a dedicated type declaration.",
    Variable:
      "A variable is a named, strongly typed storage location whose value can be read and usually reassigned.",
  },
  commands,
});
