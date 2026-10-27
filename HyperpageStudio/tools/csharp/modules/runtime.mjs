import {
  command,
  createLibrary,
  example,
  parameter,
} from "../catalog-utils.mjs";

const commands = [
  command({
    name: "StringBuilder.Append",
    description: "Append text or a value to a mutable character buffer.",
    definition:
      "Adds the text representation of a value to the end of a StringBuilder and returns the same builder for fluent chaining.",
    moduleName: "System.Text.StringBuilder",
    action: "Add",
    object: "Text buffer",
    template: "{builder}.Append({value});",
    parameters: [
      parameter("builder", "builder", "A StringBuilder instance.", "StringBuilder"),
      parameter("value", "value", "The value or text to append.", "C# expression"),
    ],
    examples: [
      example(
        "Build a status line incrementally",
        String.raw`var builder = new StringBuilder();
builder.Append("Files: ").Append(12);
builder.Append("; Errors: ").Append(1);
Console.WriteLine(builder.ToString());`,
        "Each Append mutates and returns the same buffer, avoiding a chain of temporary strings.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.text.stringbuilder.append",
    keywords: ["StringBuilder", "text", "append", "buffer"],
  }),
  command({
    name: "StringBuilder.AppendLine",
    description: "Append text followed by a line terminator.",
    definition:
      "Adds text and the current environment's line terminator to a StringBuilder, returning the same builder.",
    moduleName: "System.Text.StringBuilder",
    action: "Add",
    object: "Text buffer",
    template: "{builder}.AppendLine({value});",
    parameters: [
      parameter("builder", "builder", "A StringBuilder instance.", "StringBuilder"),
      parameter("value", "value", "The line text.", "string?"),
    ],
    examples: [
      example(
        "Build a multiline report",
        String.raw`var report = new StringBuilder()
    .AppendLine("BUILD REPORT")
    .AppendLine("Status: passed")
    .AppendLine("Tests: 128");
Console.Write(report.ToString());`,
        "AppendLine uses the current platform's line ending for every report row.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.text.stringbuilder.appendline",
    keywords: ["StringBuilder", "line", "text", "buffer"],
  }),
  command({
    name: "Encoding.UTF8.GetBytes",
    description: "Encode Unicode text as UTF-8 bytes.",
    definition:
      "Converts UTF-16 .NET characters into the corresponding UTF-8 byte sequence. Invalid surrogate input is handled by the encoding's configured fallback.",
    moduleName: "System.Text.Encoding",
    action: "Encode",
    object: "UTF-8 text",
    template: "var bytes = Encoding.UTF8.GetBytes({text});",
    parameters: [
      parameter("text", "text", "The string to encode."),
    ],
    examples: [
      example(
        "Inspect a UTF-8 representation",
        String.raw`var bytes = Encoding.UTF8.GetBytes("turtle 🐢");
Console.WriteLine(Convert.ToHexString(bytes));`,
        "ASCII characters occupy one byte each while the turtle emoji uses a multibyte UTF-8 sequence.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.text.encoding.getbytes",
    keywords: ["UTF8", "encoding", "bytes", "Unicode"],
  }),
  command({
    name: "Encoding.UTF8.GetString",
    description: "Decode UTF-8 bytes into a .NET string.",
    definition:
      "Interprets the supplied bytes as UTF-8 and returns UTF-16 .NET text. Invalid byte sequences use the encoding's configured fallback behavior.",
    moduleName: "System.Text.Encoding",
    action: "Decode",
    object: "UTF-8 text",
    template: "var text = Encoding.UTF8.GetString({bytes});",
    parameters: [
      parameter("bytes", "bytes", "The UTF-8 encoded bytes.", "byte[]"),
    ],
    examples: [
      example(
        "Restore UTF-8 text",
        String.raw`var bytes = Convert.FromHexString("48797065727368656C6C");
Console.WriteLine(Encoding.UTF8.GetString(bytes));`,
        "The hexadecimal bytes decode to the original Hypershell text.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.text.encoding.getstring",
    keywords: ["UTF8", "decoding", "bytes", "Unicode"],
  }),
  command({
    name: "Convert.ToHexString",
    description: "Encode bytes as uppercase hexadecimal text.",
    definition:
      "Converts each byte to two hexadecimal characters and concatenates the result without separators.",
    moduleName: "System.Convert",
    action: "Encode",
    object: "Hexadecimal",
    template: "var hex = Convert.ToHexString({bytes});",
    parameters: [
      parameter("bytes", "bytes", "The binary data to encode.", "byte[]"),
    ],
    examples: [
      example(
        "Display a compact byte signature",
        String.raw`var signature = SHA256.HashData(
    Encoding.UTF8.GetBytes("Hypershell"));
Console.WriteLine(Convert.ToHexString(signature));`,
        "Every hash byte becomes two uppercase hexadecimal characters.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.convert.tohexstring",
    keywords: ["hex", "bytes", "encode", "base16"],
  }),
  command({
    name: "Convert.FromHexString",
    description: "Decode hexadecimal text into bytes.",
    definition:
      "Converts pairs of hexadecimal characters into their byte values. Invalid characters or an odd character count cause FormatException.",
    moduleName: "System.Convert",
    action: "Decode",
    object: "Hexadecimal",
    template: "var bytes = Convert.FromHexString({hex});",
    parameters: [
      parameter("hex", "hex", "An even-length hexadecimal string."),
    ],
    examples: [
      example(
        "Decode a protocol marker",
        String.raw`var marker = Convert.FromHexString("4859504552");
Console.WriteLine(Encoding.ASCII.GetString(marker));`,
        "Each character pair restores one byte, producing the ASCII text HYPER.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.convert.fromhexstring",
    keywords: ["hex", "bytes", "decode", "base16"],
  }),
  command({
    name: "SHA256.HashData",
    description: "Calculate a SHA-256 digest.",
    definition:
      "Computes the 256-bit SHA-2 digest of supplied bytes or a stream. A cryptographic hash detects changes but does not encrypt data or authenticate an unkeyed message.",
    moduleName: "System.Security.Cryptography",
    action: "Hash",
    object: "Data",
    template: "var hash = SHA256.HashData({data});",
    parameters: [
      parameter("data", "data", "The bytes or readable stream to hash.", "byte[]"),
    ],
    examples: [
      example(
        "Hash text deterministically",
        String.raw`var data = Encoding.UTF8.GetBytes("Hypershell");
var hash = SHA256.HashData(data);
Console.WriteLine(Convert.ToHexString(hash));`,
        "The same input bytes always produce the same 32-byte digest.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.security.cryptography.sha256.hashdata",
    keywords: ["SHA256", "hash", "digest", "integrity"],
  }),
  command({
    name: "HMACSHA256.HashData",
    description: "Calculate a keyed SHA-256 authentication code.",
    definition:
      "Computes an HMAC-SHA-256 value from a secret key and message. Unlike a plain hash, a valid HMAC demonstrates knowledge of the shared key.",
    moduleName: "System.Security.Cryptography",
    action: "Protect",
    object: "Message",
    template: "var tag = HMACSHA256.HashData({key}, {data});",
    parameters: [
      parameter("key", "key", "The secret HMAC key bytes.", "byte[]"),
      parameter("data", "data", "The message bytes.", "byte[]"),
    ],
    examples: [
      example(
        "Authenticate an in-memory message",
        String.raw`var key = RandomNumberGenerator.GetBytes(32);
var message = Encoding.UTF8.GetBytes("deploy:production");
var tag = HMACSHA256.HashData(key, message);
Console.WriteLine(Convert.ToHexString(tag));`,
        "The random key must be retained securely by any party that later verifies the message tag.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.security.cryptography.hmacsha256.hashdata",
    keywords: ["HMAC", "SHA256", "authentication", "keyed hash"],
  }),
  command({
    name: "RandomNumberGenerator.GetBytes",
    description: "Generate cryptographically strong random bytes.",
    definition:
      "Fills or returns bytes produced by the operating system's cryptographic random-number generator. It is appropriate for keys, salts, nonces, and security tokens.",
    moduleName: "System.Security.Cryptography",
    action: "Create",
    object: "Secure random data",
    template: "var bytes = RandomNumberGenerator.GetBytes({count});",
    parameters: [
      parameter("count", "count", "The number of random bytes to return.", "int"),
    ],
    examples: [
      example(
        "Create a random token",
        String.raw`var token = Convert.ToBase64String(
    RandomNumberGenerator.GetBytes(32));
Console.WriteLine(token);`,
        "Thirty-two unpredictable bytes are encoded for text-safe storage or transport.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.security.cryptography.randomnumbergenerator.getbytes",
    keywords: ["CSPRNG", "random", "token", "key", "nonce"],
  }),
  command({
    name: "RandomNumberGenerator.GetInt32",
    description: "Generate an unbiased secure random integer in a range.",
    definition:
      "Returns a cryptographically strong integer at least as large as the inclusive lower bound and smaller than the exclusive upper bound without modulo bias.",
    moduleName: "System.Security.Cryptography",
    action: "Create",
    object: "Secure random number",
    template: "var number = RandomNumberGenerator.GetInt32({minValue}, {maxValue});",
    parameters: [
      parameter("minValue", "minValue", "The inclusive lower bound.", "int"),
      parameter("maxValue", "maxValue", "The exclusive upper bound.", "int"),
    ],
    examples: [
      example(
        "Choose a secure six-digit code",
        String.raw`var code = RandomNumberGenerator.GetInt32(100000, 1000000);
Console.WriteLine(code);`,
        "The exclusive upper bound keeps every result at exactly six decimal digits.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.security.cryptography.randomnumbergenerator.getint32",
    keywords: ["CSPRNG", "random", "integer", "unbiased"],
  }),
  command({
    name: "CryptographicOperations.FixedTimeEquals",
    description: "Compare secret byte sequences without early-exit timing.",
    definition:
      "Tests equal-length byte sequences using work that does not depend on where their first mismatch occurs, reducing timing leakage during tag or token verification.",
    moduleName: "System.Security.Cryptography",
    action: "Compare",
    object: "Secret data",
    template: "var equal = CryptographicOperations.FixedTimeEquals({left}, {right});",
    parameters: [
      parameter("left", "left", "The first byte sequence.", "ReadOnlySpan<byte>"),
      parameter("right", "right", "The second byte sequence.", "ReadOnlySpan<byte>"),
    ],
    examples: [
      example(
        "Verify an HMAC tag",
        String.raw`var key = Encoding.UTF8.GetBytes("development-only-key");
var message = Encoding.UTF8.GetBytes("payload");
var expected = HMACSHA256.HashData(key, message);
var supplied = HMACSHA256.HashData(key, message);
Console.WriteLine(
    CryptographicOperations.FixedTimeEquals(expected, supplied));`,
        "FixedTimeEquals is suitable for comparing authentication tags after their lengths and encodings are validated.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals",
    keywords: ["constant time", "secret", "compare", "timing attack"],
  }),
  command({
    name: "Stopwatch.StartNew",
    description: "Start measuring elapsed time immediately.",
    definition:
      "Creates and starts a high-resolution Stopwatch using the best timing source available on the current platform.",
    moduleName: "System.Diagnostics.Stopwatch",
    action: "Create",
    object: "Elapsed timer",
    template: "var stopwatch = Stopwatch.StartNew();",
    examples: [
      example(
        "Measure an operation",
        String.raw`var stopwatch = Stopwatch.StartNew();
await Task.Delay(75);
stopwatch.Stop();
Console.WriteLine($"{stopwatch.Elapsed.TotalMilliseconds:N1} ms");`,
        "Elapsed time includes the asynchronous delay until Stop freezes the measurement.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.diagnostics.stopwatch.startnew",
    keywords: ["Stopwatch", "timing", "performance", "elapsed"],
  }),
  command({
    name: "Stopwatch.GetElapsedTime",
    description: "Calculate elapsed time from Stopwatch timestamps.",
    definition:
      "Converts one or two raw Stopwatch timestamps into a TimeSpan using the current platform's high-resolution timer frequency.",
    moduleName: "System.Diagnostics.Stopwatch",
    action: "Get",
    object: "Elapsed time",
    template: "var elapsed = Stopwatch.GetElapsedTime({startTimestamp});",
    parameters: [
      parameter("startTimestamp", "startTimestamp", "A value returned by Stopwatch.GetTimestamp.", "long"),
    ],
    examples: [
      example(
        "Measure without allocating a Stopwatch",
        String.raw`var started = Stopwatch.GetTimestamp();
var total = Enumerable.Range(1, 10000).Sum();
var elapsed = Stopwatch.GetElapsedTime(started);
Console.WriteLine($"{total}; {elapsed.TotalMicroseconds:N1} µs");`,
        "Raw timestamps provide a compact timing path for small measured regions.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.diagnostics.stopwatch.getelapsedtime",
    keywords: ["Stopwatch", "timestamp", "timing", "performance"],
  }),
  command({
    name: "Assembly.GetExecutingAssembly",
    description: "Get metadata for the assembly containing the running code.",
    definition:
      "Returns the Assembly whose code is currently executing, enabling inspection of its name, version, attributes, resources, and defined types.",
    moduleName: "System.Reflection.Assembly",
    action: "Inspect",
    object: "Assembly",
    template: "var assembly = Assembly.GetExecutingAssembly();",
    examples: [
      example(
        "Report the generated snippet assembly",
        String.raw`var assembly = Assembly.GetExecutingAssembly();
var name = assembly.GetName();
Console.WriteLine($"{name.Name} {name.Version}");`,
        "The Hypershell C# runner compiles snippets into a temporary HypershellSnippet assembly.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.reflection.assembly.getexecutingassembly",
    keywords: ["reflection", "assembly", "metadata", "runtime"],
  }),
  command({
    name: "Assembly.GetTypes",
    description: "Get every type defined in an assembly.",
    definition:
      "Returns Type objects for modules defined in the assembly. Type loading can fail partially when dependencies are missing, producing ReflectionTypeLoadException.",
    moduleName: "System.Reflection.Assembly",
    action: "Enumerate",
    object: "Runtime type",
    template: "var types = {assembly}.GetTypes();",
    parameters: [
      parameter("assembly", "assembly", "The Assembly to inspect.", "Assembly"),
    ],
    examples: [
      example(
        "List types declared by the snippet",
        String.raw`var types = Assembly.GetExecutingAssembly()
    .GetTypes()
    .OrderBy(type => type.FullName);
foreach (var type in types)
{
    Console.WriteLine(type.FullName);
}`,
        "The generated Program type and any types declared in the snippet appear in the assembly metadata.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.reflection.assembly.gettypes",
    keywords: ["reflection", "types", "assembly", "metadata"],
  }),
  command({
    name: "Type.GetProperties",
    description: "Inspect properties exposed by a runtime type.",
    definition:
      "Returns PropertyInfo metadata matching the requested public/non-public, static/instance, and declared/inherited binding rules.",
    moduleName: "System.Type",
    action: "Inspect",
    object: "Property metadata",
    template: "var properties = {type}.GetProperties();",
    parameters: [
      parameter("type", "type", "A System.Type instance.", "Type"),
    ],
    examples: [
      example(
        "List DateTime property types",
        String.raw`foreach (var property in typeof(DateTime).GetProperties())
{
    Console.WriteLine($"{property.PropertyType.Name} {property.Name}");
}`,
        "Reflection reports metadata rather than reading any particular DateTime instance.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.type.getproperties",
    keywords: ["reflection", "properties", "Type", "PropertyInfo"],
  }),
  command({
    name: "Type.GetMethods",
    description: "Inspect methods exposed by a runtime type.",
    definition:
      "Returns MethodInfo metadata for methods matching the requested binding rules, including overloads and inherited members.",
    moduleName: "System.Type",
    action: "Inspect",
    object: "Method metadata",
    template: "var methods = {type}.GetMethods();",
    parameters: [
      parameter("type", "type", "A System.Type instance.", "Type"),
    ],
    examples: [
      example(
        "Find string methods with one parameter",
        String.raw`var methods = typeof(string)
    .GetMethods()
    .Where(method => method.GetParameters().Length == 1)
    .Select(method => method.Name)
    .Distinct()
    .OrderBy(name => name);
Console.WriteLine(string.Join(", ", methods));`,
        "MethodInfo metadata can be filtered by signature before any method is invoked.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.type.getmethods",
    keywords: ["reflection", "methods", "Type", "MethodInfo"],
  }),
  command({
    name: "Activator.CreateInstance",
    description: "Create an object from its runtime Type.",
    definition:
      "Invokes a matching constructor for a Type selected at runtime. The result is object-typed and may be null for Nullable<T>; constructor and accessibility errors can throw.",
    moduleName: "System.Activator",
    action: "Create",
    object: "Runtime object",
    template: "var instance = Activator.CreateInstance({type}, {arguments});",
    parameters: [
      parameter("type", "type", "The runtime Type to construct.", "Type"),
      parameter("arguments", "arguments", "An object array containing constructor arguments.", "object?[]?"),
    ],
    examples: [
      example(
        "Construct a generic list dynamically",
        String.raw`var listType = typeof(List<>).MakeGenericType(typeof(string));
var instance = Activator.CreateInstance(listType)
    ?? throw new InvalidOperationException("Construction failed.");
Console.WriteLine(instance.GetType());`,
        "The concrete List<string> type is formed and instantiated entirely from runtime metadata.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.activator.createinstance",
    keywords: ["reflection", "constructor", "dynamic", "runtime type"],
  }),
  command({
    name: "RuntimeInformation.IsOSPlatform",
    description: "Test the current operating-system platform.",
    definition:
      "Compares the current runtime platform with a supplied OSPlatform value so platform-specific behavior can be guarded explicitly.",
    moduleName: "System.Runtime.InteropServices",
    action: "Test",
    object: "Operating system",
    template: "var matches = RuntimeInformation.IsOSPlatform({platform});",
    parameters: [
      parameter("platform", "platform", "An OSPlatform value such as OSPlatform.Windows.", "OSPlatform"),
    ],
    examples: [
      example(
        "Choose a platform-specific path separator",
        String.raw`var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? "Windows"
    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        ? "Linux"
        : "Other";
Console.WriteLine(platform);`,
        "RuntimeInformation identifies the platform without parsing an operating-system description string.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.runtimeinformation.isosplatform",
    keywords: ["OS", "platform", "Windows", "Linux", "macOS"],
  }),
  command({
    name: "ArrayPool<T>.Rent",
    description: "Rent a reusable array of at least the requested length.",
    definition:
      "Obtains an array from a shared or custom pool to reduce repeated large-array allocation. The returned array may be longer than requested and must be returned exactly once.",
    moduleName: "System.Buffers.ArrayPool<T>",
    action: "Allocate",
    object: "Pooled buffer",
    template: "var buffer = ArrayPool<{type}>.Shared.Rent({minimumLength});",
    parameters: [
      parameter("type", "type", "The pooled element type.", "type name"),
      parameter("minimumLength", "minimumLength", "The minimum acceptable array length.", "int"),
    ],
    examples: [
      example(
        "Use and return a temporary byte buffer",
        String.raw`var buffer = ArrayPool<byte>.Shared.Rent(4096);
try
{
    RandomNumberGenerator.Fill(buffer.AsSpan(0, 4096));
    Console.WriteLine(Convert.ToHexString(buffer.AsSpan(0, 8)));
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
}`,
        "The requested region is used and the array is returned in finally; clearing prevents residual bytes from being exposed to a later renter.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.rent",
    keywords: ["ArrayPool", "buffer", "allocation", "performance"],
  }),
  command({
    name: "ArrayPool<T>.Return",
    description: "Return a rented array to its pool.",
    definition:
      "Makes an array previously obtained from the same pool available for reuse. The caller must stop accessing the array after it is returned.",
    moduleName: "System.Buffers.ArrayPool<T>",
    action: "Release",
    object: "Pooled buffer",
    template: "ArrayPool<{type}>.Shared.Return({array}, clearArray: {clearArray});",
    parameters: [
      parameter("type", "type", "The pooled element type.", "type name"),
      parameter("array", "array", "The exact rented array to return.", "T[]"),
      parameter("clearArray", "clearArray", "Whether the pool should clear elements before reuse.", "bool"),
    ],
    examples: [
      example(
        "Return a sensitive character buffer",
        String.raw`var characters = ArrayPool<char>.Shared.Rent(256);
try
{
    "temporary-secret".AsSpan().CopyTo(characters);
}
finally
{
    ArrayPool<char>.Shared.Return(characters, clearArray: true);
}`,
        "clearArray trades extra work for reducing the chance that a later renter observes old character data.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.return",
    keywords: ["ArrayPool", "return", "buffer", "cleanup"],
  }),
  command({
    name: "Memory<T>.Slice",
    description: "Create a bounded memory view without copying elements.",
    definition:
      "Returns a Memory<T> view over part of the same underlying storage. Changes through either writable view affect the shared elements.",
    moduleName: "System.Memory<T>",
    action: "Slice",
    object: "Memory",
    template: "var slice = {memory}.Slice({start}, {length});",
    parameters: [
      parameter("memory", "memory", "The original Memory<T> value.", "Memory<T>"),
      parameter("start", "start", "The zero-based slice start.", "int"),
      parameter("length", "length", "The number of elements in the slice.", "int"),
    ],
    examples: [
      example(
        "Fill one region of an array",
        String.raw`var data = new byte[12];
Memory<byte> middle = data.AsMemory().Slice(4, 4);
middle.Span.Fill(0xFF);
Console.WriteLine(Convert.ToHexString(data));`,
        "The Memory<byte> view refers to indexes 4 through 7 of the original array without allocating another array.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.memory-1.slice",
    keywords: ["Memory", "slice", "buffer", "zero copy"],
  }),
  command({
    name: "Enum.TryParse<TEnum>",
    description: "Try to parse an enum name or numeric value.",
    definition:
      "Attempts to convert text into a specified enum type without throwing for invalid text. An overload controls case sensitivity.",
    moduleName: "System.Enum",
    action: "Parse",
    object: "Enum",
    template: "var valid = Enum.TryParse<{enumType}>({text}, ignoreCase: true, out var value);",
    parameters: [
      parameter("enumType", "enumType", "The enum type to parse.", "enum type"),
      parameter("text", "text", "The candidate enum name or number.", "string?"),
    ],
    examples: [
      example(
        "Parse a day name case-insensitively",
        String.raw`if (Enum.TryParse<DayOfWeek>(
        "friday",
        ignoreCase: true,
        out var day))
{
    Console.WriteLine(day);
}`,
        "The generic type fixes the allowed enum values while ignoreCase accepts lowercase input.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.enum.tryparse",
    keywords: ["enum", "parse", "TryParse", "generic"],
  }),
  command({
    name: "BitConverter.GetBytes",
    description: "Convert a primitive value into machine-endian bytes.",
    definition:
      "Returns the in-memory byte representation of a supported primitive. The byte order follows the current architecture and should be normalized explicitly for portable protocols.",
    moduleName: "System.BitConverter",
    action: "Convert",
    object: "Primitive bytes",
    template: "var bytes = BitConverter.GetBytes({value});",
    parameters: [
      parameter("value", "value", "A supported primitive value.", "primitive"),
    ],
    examples: [
      example(
        "Inspect an integer representation",
        String.raw`var bytes = BitConverter.GetBytes(0x12345678);
Console.WriteLine(Convert.ToHexString(bytes));
Console.WriteLine($"Little endian: {BitConverter.IsLittleEndian}");`,
        "The output order depends on the architecture, which IsLittleEndian reports explicitly.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.bitconverter.getbytes",
    keywords: ["BitConverter", "bytes", "endian", "primitive"],
  }),
  command({
    name: "GC.GetTotalAllocatedBytes",
    description: "Read the process-wide managed allocation counter.",
    definition:
      "Returns an estimate of the total bytes allocated by managed code since process start. It is a cumulative diagnostic counter, not the current live heap size.",
    moduleName: "System.GC",
    action: "Inspect",
    object: "Managed allocation",
    template: "var allocatedBytes = GC.GetTotalAllocatedBytes({precise});",
    parameters: [
      parameter("precise", "precise", "Whether to synchronize for a more precise value.", "bool"),
    ],
    examples: [
      example(
        "Measure allocations around a small operation",
        String.raw`var before = GC.GetTotalAllocatedBytes(precise: true);
var values = Enumerable.Range(1, 1000).Select(value => value.ToString()).ToArray();
var after = GC.GetTotalAllocatedBytes(precise: true);
Console.WriteLine($"Approximate allocated bytes: {after - before:N0}");`,
        "The difference includes managed allocations made by the measured code and relevant runtime activity.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.gc.gettotalallocatedbytes",
    keywords: ["GC", "allocation", "memory", "diagnostics"],
  }),
];

export default createLibrary({
  id: "csharp-runtime",
  displayName: ".NET text, runtime, and security",
  description:
    "Dependency-free .NET 8 APIs for encoding, buffers, cryptography, timing, reflection, platform inspection, and managed-memory diagnostics.",
  category: "Runtime and security",
  keywords: ["text", "encoding", "cryptography", "reflection", "buffers"],
  objectDescriptions: {
    Assembly:
      "An Assembly represents a deployed .NET code and metadata unit containing modules, types, resources, and identity information.",
    Data:
      "Data is an ordered byte sequence whose representation and interpretation are defined by the producing application or protocol.",
    "Elapsed time":
      "Elapsed time is a duration calculated from a monotonic high-resolution timer rather than the wall clock.",
    "Elapsed timer":
      "An elapsed timer records monotonic timestamps for performance measurement and duration tracking.",
    Enum:
      "An enum is a value type whose named constants represent a closed set of integral values.",
    Hexadecimal:
      "Hexadecimal is a base-16 text representation that uses two characters per byte.",
    "Managed allocation":
      "A managed allocation reserves garbage-collected heap storage for a new object, array, string, or runtime structure.",
    Memory:
      "Memory<T> is a heap-storable view over a contiguous region of typed elements.",
    Message:
      "A message is the byte sequence whose integrity and authenticity are being protected or verified.",
    "Method metadata":
      "MethodInfo describes a runtime method's name, signature, declaring type, attributes, and invocation behavior.",
    "Operating system":
      "The operating system is the platform hosting the current .NET process.",
    "Pooled buffer":
      "A pooled buffer is a reusable array temporarily leased from an ArrayPool<T> to reduce allocation pressure.",
    "Primitive bytes":
      "Primitive bytes are the machine representation of a numeric, Boolean, character, or related built-in value.",
    "Property metadata":
      "PropertyInfo describes a runtime property's type, accessors, declaring type, index parameters, and attributes.",
    "Runtime object":
      "A runtime object is an instance whose concrete Type may have been selected dynamically rather than through a compile-time constructor expression.",
    "Runtime type":
      "A System.Type describes a .NET type's identity, members, inheritance, generic shape, and runtime metadata.",
    "Secret data":
      "Secret data includes keys, authentication tags, tokens, and other bytes whose disclosure or comparison behavior affects security.",
    "Secure random data":
      "Secure random data is produced by a cryptographically strong operating-system random source suitable for security-sensitive values.",
    "Secure random number":
      "A secure random number is sampled without predictable state or range bias for security-sensitive selection.",
    "Text buffer":
      "A StringBuilder is a mutable buffer optimized for incrementally constructing text.",
    "UTF-8 text":
      "UTF-8 is a variable-width Unicode encoding compatible with ASCII for its first 128 code points.",
  },
  commands,
});
