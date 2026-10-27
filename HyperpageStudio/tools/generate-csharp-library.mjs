import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { csharpCoordinates } from "./csharp/catalog-utils.mjs";

const commands = [];

const parameter = (
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

const add = ({
  name,
  description,
  definition,
  moduleName,
  action,
  object,
  template,
  parameters = [],
  title,
  code,
  explanation,
  documentationUrl = "",
  keywords = [],
  namespaceName,
  typeName,
  memberName,
  memberKind,
  overloadSignature,
}) => {
  commands.push({
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
    keywords,
    parameters,
    examples: [{ title, code, explanation }],
  });
};

const actionDescriptions = {
  Add: "Adds a new value to an existing collection without replacing the collection itself.",
  Append: "Writes new data after the content that is already present.",
  Calculate: "Computes and returns a numerical result from the supplied values.",
  Clear: "Removes existing content or entries while leaving the container available for reuse.",
  Combine: "Builds one value from two or more separate values.",
  Convert: "Changes a value into another representation or data type.",
  Copy: "Creates another copy of existing data at a new destination.",
  Create: "Constructs a new object or resource that did not exist before.",
  Delete: "Permanently removes the selected resource.",
  Deserialize: "Reconstructs a typed in-memory object from stored or transmitted data.",
  Download: "Requests data from a remote HTTP address and returns the received content.",
  Filter: "Keeps only the values that satisfy a condition.",
  Find: "Searches for a value or position and reports what was found.",
  Format: "Produces a specific text representation or layout without changing the underlying value.",
  Get: "Retrieves an existing value, resource, or collection without intentionally changing it.",
  Group: "Organizes values into buckets that share the same key.",
  Join: "Combines several values into one sequence or string.",
  Match: "Applies a pattern and returns the matching text or match details.",
  Move: "Relocates a resource from one path to another.",
  Order: "Returns values arranged by a selected key.",
  Parse: "Interprets text as a specific typed value and reports or throws when the text is invalid.",
  Read: "Takes input from a user, stream, file, or response and returns it to the program.",
  Remove: "Takes a value out of a collection or deletes a selected element.",
  Replace: "Produces content in which selected values or matching text are substituted.",
  Run: "Schedules or launches executable work.",
  Select: "Projects or extracts a particular part of a value or sequence.",
  Serialize: "Turns an in-memory object into data that can be stored or transmitted.",
  Sort: "Rearranges a mutable collection into ascending or custom order.",
  Split: "Separates one value into multiple parts using a delimiter or pattern.",
  Start: "Launches a process or initiates an operation.",
  Sum: "Adds numeric values in a sequence and returns the total.",
  Test: "Evaluates a condition and returns true or false without intentionally changing the target.",
  Wait: "Asynchronously pauses until time passes or one or more operations finish.",
  Write: "Sends text or data to an output destination.",
};

const objectDescriptions = {
  Base64: "Base64 is a text-safe encoding of binary bytes. It is useful for transport, but it is not encryption.",
  Console: "The console is the application's standard text input and output interface, normally connected to a terminal window.",
  DateTime: "DateTime is a value containing a calendar date and clock time, with optional local or UTC context.",
  Dictionary: "Dictionary<TKey,TValue> stores unique keys paired with values and provides fast lookup by key.",
  Directory: "A directory is a file-system container that can hold files and other directories.",
  Environment: "Environment exposes information and settings associated with the current process and operating-system session.",
  File: "A file is a named sequence of bytes stored by a file system; text helpers encode and decode those bytes as characters.",
  Guid: "Guid is a 128-bit identifier designed to be unique across machines and time without a central counter.",
  HashSet: "HashSet<T> stores unique values and supports mathematical set operations such as union and intersection.",
  HttpClient: "HttpClient is a reusable client for sending HTTP requests and receiving HTTP responses.",
  HttpResponse: "HttpResponseMessage represents an HTTP server's status code, headers, and response body.",
  Json: "JSON is a text format for objects, arrays, strings, numbers, Boolean values, and null.",
  List: "List<T> is an ordered, index-based collection whose size can grow or shrink.",
  Number: "A number is a typed numeric value on which mathematical, comparison, and conversion operations can be performed.",
  Path: "A path is text that identifies a file-system location; Path helpers manipulate the text without opening the resource.",
  Process: "A process is a running instance of a program with its own identifier, memory, handles, and execution state.",
  Random: "Random is a pseudo-random number generator for simulations and ordinary selection. It is not suitable for secrets or security tokens.",
  Regex: "A regular expression is a pattern language for finding, validating, splitting, and replacing text.",
  Sequence: "An IEnumerable<T> sequence is a stream of typed values. Many LINQ operations are lazy and run only when the sequence is enumerated.",
  String: "A string is an immutable sequence of Unicode characters used to represent text.",
  Task: "A Task represents work that may still be running and will eventually complete, fail, or be canceled.",
};

// Console I/O
add({
  name: "Console.WriteLine",
  description: "Write a value followed by a line break.",
  definition: "Writes the text representation of a value to standard output and then writes the current platform's line terminator. The next console output begins on a new line.",
  moduleName: "System.Console",
  action: "Write",
  object: "Console",
  template: "Console.WriteLine({message});",
  parameters: [parameter("message", "message", "The value or string expression to display.", "object")],
  title: "Report a calculated result",
  code: String.raw`var filesFound = 12;
Console.WriteLine($"Found {filesFound} files.");`,
  explanation: "The interpolated expression inserts the current value of filesFound, writes the completed message, and moves the cursor to the next line.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.console.writeline",
});

add({
  name: "Console.Write",
  description: "Write a value without adding a line break.",
  definition: "Writes a value to standard output but leaves the cursor immediately after the text. This is useful for prompts or for building one line in several pieces.",
  moduleName: "System.Console",
  action: "Write",
  object: "Console",
  template: "Console.Write({message});",
  parameters: [parameter("message", "message", "The value or string expression to display.", "object")],
  title: "Prompt on the current line",
  code: String.raw`Console.Write("Project name: ");
var projectName = Console.ReadLine();`,
  explanation: "Because Write does not add a newline, the user's response appears on the same line as the prompt.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.console.write",
});

add({
  name: "Console.ReadLine",
  description: "Read one complete line of console input.",
  definition: "Waits for the user to enter text and press Enter, then returns the entered characters without the line terminator. It returns null when the input stream has ended.",
  moduleName: "System.Console",
  action: "Read",
  object: "Console",
  template: "var input = Console.ReadLine();",
  title: "Read and validate a name",
  code: String.raw`Console.Write("Your name: ");
var name = Console.ReadLine();
Console.WriteLine($"Hello, {name ?? "guest"}!");`,
  explanation: "ReadLine waits for Enter. The null-coalescing expression uses guest only if standard input has already closed.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.console.readline",
});

add({
  name: "Console.ReadKey",
  description: "Read the next key pressed in the console.",
  definition: "Waits for a key press and returns a ConsoleKeyInfo containing the character and modifier keys. The intercept argument decides whether the pressed character is echoed to the terminal.",
  moduleName: "System.Console",
  action: "Read",
  object: "Console",
  template: "var key = Console.ReadKey({intercept});",
  parameters: [parameter("intercept", "intercept", "Use true to keep the pressed key from being displayed.", "bool")],
  title: "Wait for confirmation without echoing",
  code: String.raw`Console.Write("Press Y to continue: ");
var key = Console.ReadKey(intercept: true);
Console.WriteLine(key.Key == ConsoleKey.Y ? "Continuing" : "Canceled");`,
  explanation: "ReadKey captures one key immediately. With intercept set to true, the original key is not printed before the result message.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.console.readkey",
});

// Strings
add({
  name: "string.Contains",
  description: "Test whether text contains a specified value.",
  definition: "Returns true when the target string contains the supplied character sequence. An overload accepts StringComparison so the caller can choose ordinal or culture-aware, case-sensitive or case-insensitive matching.",
  moduleName: "System.String",
  action: "Test",
  object: "String",
  template: "var contains = {text}.Contains({value}, StringComparison.OrdinalIgnoreCase);",
  parameters: [
    parameter("text", "text", "The string expression to search."),
    parameter("value", "value", "The character sequence to locate."),
  ],
  title: "Perform a case-insensitive search",
  code: String.raw`var fileName = "Quarterly-REPORT.pdf";
var isReport = fileName.Contains("report", StringComparison.OrdinalIgnoreCase);
Console.WriteLine(isReport);`,
  explanation: "OrdinalIgnoreCase makes REPORT match report without applying language-specific sorting rules.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.contains",
});

add({
  name: "string.StartsWith",
  description: "Test whether text begins with a specified value.",
  definition: "Returns true when the beginning of the target string matches the supplied text. Supplying StringComparison makes the intended case and culture rules explicit.",
  moduleName: "System.String",
  action: "Test",
  object: "String",
  template: "var startsWith = {text}.StartsWith({value}, StringComparison.OrdinalIgnoreCase);",
  parameters: [
    parameter("text", "text", "The string expression to inspect."),
    parameter("value", "value", "The expected prefix."),
  ],
  title: "Recognize an HTTPS address",
  code: String.raw`var address = "HTTPS://example.com/api";
var isSecure = address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
Console.WriteLine(isSecure);`,
  explanation: "Only the prefix is tested; the remainder of the address does not affect the result.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.startswith",
});

add({
  name: "string.EndsWith",
  description: "Test whether text ends with a specified value.",
  definition: "Returns true when the final characters of the target string match the supplied suffix. It is commonly used for extensions and naming conventions, but Path.GetExtension is safer for full file paths.",
  moduleName: "System.String",
  action: "Test",
  object: "String",
  template: "var endsWith = {text}.EndsWith({value}, StringComparison.OrdinalIgnoreCase);",
  parameters: [
    parameter("text", "text", "The string expression to inspect."),
    parameter("value", "value", "The expected suffix."),
  ],
  title: "Filter Markdown names",
  code: String.raw`var names = new[] { "README.md", "photo.png", "notes.MD" };
var markdown = names.Where(name =>
    name.EndsWith(".md", StringComparison.OrdinalIgnoreCase));`,
  explanation: "The predicate keeps names ending in .md regardless of letter case.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.endswith",
});

add({
  name: "string.Replace",
  description: "Create text with matching content replaced.",
  definition: "Returns a new string in which every occurrence of the specified old text is replaced with new text. Strings are immutable, so the original string is unchanged.",
  moduleName: "System.String",
  action: "Replace",
  object: "String",
  template: "var result = {text}.Replace({oldValue}, {newValue});",
  parameters: [
    parameter("text", "text", "The original string expression."),
    parameter("oldValue", "oldValue", "The text to find."),
    parameter("newValue", "newValue", "The replacement text."),
  ],
  title: "Normalize a path separator",
  code: String.raw`var portablePath = "logs\\2026\\today.txt";
var normalized = portablePath.Replace('\\', '/');
Console.WriteLine(normalized);`,
  explanation: "Every backslash character is replaced in the returned string; portablePath keeps its original value.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.replace",
});

add({
  name: "string.Split",
  description: "Separate text into an array of substrings.",
  definition: "Breaks a string wherever a delimiter occurs and returns the resulting pieces. Options can remove empty entries and trim whitespace during the split.",
  moduleName: "System.String",
  action: "Split",
  object: "String",
  template: "var parts = {text}.Split({separator}, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);",
  parameters: [
    parameter("text", "text", "The string expression to divide."),
    parameter("separator", "separator", "The delimiter character or string."),
  ],
  title: "Parse a comma-separated list",
  code: String.raw`var input = "alpha, beta, , gamma";
var names = input.Split(
    ',',
    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
Console.WriteLine(names.Length);`,
  explanation: "The options trim each piece and discard the empty field, producing alpha, beta, and gamma.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.split",
});

add({
  name: "string.Join",
  description: "Combine values into one delimited string.",
  definition: "Concatenates the text representation of each supplied value and inserts the separator between adjacent values. It does not place the separator before the first or after the last value.",
  moduleName: "System.String",
  action: "Join",
  object: "String",
  template: "var joined = string.Join({separator}, {values});",
  parameters: [
    parameter("separator", "separator", "Text to place between values."),
    parameter("values", "values", "The sequence or array to combine.", "IEnumerable<string>"),
  ],
  title: "Build a readable label list",
  code: String.raw`var labels = new[] { "bug", "urgent", "backend" };
var display = string.Join(", ", labels);
Console.WriteLine(display);`,
  explanation: "The three labels become one string: bug, urgent, backend.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.join",
});

add({
  name: "string.Trim",
  description: "Remove leading and trailing whitespace.",
  definition: "Returns a new string with whitespace removed from both ends. Characters inside the string are preserved, and an overload can trim a caller-supplied set of characters instead.",
  moduleName: "System.String",
  action: "Format",
  object: "String",
  template: "var trimmed = {text}.Trim();",
  parameters: [parameter("text", "text", "The string expression to trim.")],
  title: "Clean console input",
  code: String.raw`var raw = "   project-alpha   ";
var project = raw.Trim();
Console.WriteLine($"[{project}]");`,
  explanation: "The spaces at both edges disappear; the hyphen and internal characters remain.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.trim",
});

add({
  name: "string.ToUpperInvariant",
  description: "Create an uppercase string using culture-independent rules.",
  definition: "Returns an uppercase copy using the invariant culture. This is useful for machine-oriented identifiers; user-facing text often needs a specific CultureInfo instead.",
  moduleName: "System.String",
  action: "Convert",
  object: "String",
  template: "var upper = {text}.ToUpperInvariant();",
  parameters: [parameter("text", "text", "The string expression to convert.")],
  title: "Normalize a command token",
  code: String.raw`var token = "deploy";
switch (token.ToUpperInvariant())
{
    case "DEPLOY": Console.WriteLine("Starting deployment"); break;
}`,
  explanation: "Normalization lets the switch compare one predictable representation of the token.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.toupperinvariant",
});

add({
  name: "string.ToLowerInvariant",
  description: "Create a lowercase string using culture-independent rules.",
  definition: "Returns a lowercase copy using invariant-culture casing rules. It is appropriate for normalized protocol or identifier text, not for every language-sensitive display task.",
  moduleName: "System.String",
  action: "Convert",
  object: "String",
  template: "var lower = {text}.ToLowerInvariant();",
  parameters: [parameter("text", "text", "The string expression to convert.")],
  title: "Normalize a file extension",
  code: String.raw`var extension = Path.GetExtension("REPORT.PDF").ToLowerInvariant();
if (extension == ".pdf")
{
    Console.WriteLine("PDF document");
}`,
  explanation: "The uppercase extension is converted to .pdf before comparison.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.tolowerinvariant",
});

add({
  name: "string.IsNullOrWhiteSpace",
  description: "Test whether text is missing or contains only whitespace.",
  definition: "Returns true for null, the empty string, or a string made entirely of whitespace characters. It is a concise guard for required text input.",
  moduleName: "System.String",
  action: "Test",
  object: "String",
  template: "var isMissing = string.IsNullOrWhiteSpace({text});",
  parameters: [parameter("text", "text", "The nullable string expression to validate.", "string?")],
  title: "Reject an empty project name",
  code: String.raw`Console.Write("Project name: ");
var name = Console.ReadLine();
if (string.IsNullOrWhiteSpace(name))
{
    Console.WriteLine("A project name is required.");
}`,
  explanation: "The guard catches end-of-input, an empty response, and a response containing only spaces or tabs.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.isnullorwhitespace",
});

add({
  name: "string.IndexOf",
  description: "Find the zero-based position of text.",
  definition: "Searches the target string and returns the index where the requested character sequence first begins. It returns -1 when no match exists.",
  moduleName: "System.String",
  action: "Find",
  object: "String",
  template: "var index = {text}.IndexOf({value}, StringComparison.OrdinalIgnoreCase);",
  parameters: [
    parameter("text", "text", "The string expression to search."),
    parameter("value", "value", "The text to locate."),
  ],
  title: "Locate a separator",
  code: String.raw`var assignment = "timeout=30";
var separator = assignment.IndexOf('=');
var key = assignment[..separator];
var value = assignment[(separator + 1)..];`,
  explanation: "IndexOf finds the equals sign, and range expressions use that position to split the key from its value.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.indexof",
});

add({
  name: "string.Substring",
  description: "Extract part of a string by position.",
  definition: "Returns a new string beginning at a zero-based index and optionally limited to a specified length. Invalid indexes or lengths throw ArgumentOutOfRangeException.",
  moduleName: "System.String",
  action: "Select",
  object: "String",
  template: "var part = {text}.Substring({startIndex}, {length});",
  parameters: [
    parameter("text", "text", "The original string expression."),
    parameter("startIndex", "startIndex", "The zero-based first character to include.", "int"),
    parameter("length", "length", "The number of characters to return.", "int"),
  ],
  title: "Read a date prefix",
  code: String.raw`var fileName = "2026-07-27-report.txt";
var dateText = fileName.Substring(0, 10);
Console.WriteLine(dateText);`,
  explanation: "Starting at index 0 and taking 10 characters returns 2026-07-27.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.string.substring",
});

// File-system APIs
add({
  name: "File.ReadAllText",
  description: "Read an entire text file into one string.",
  definition: "Opens a text file, decodes all of its content, returns it as one string, and closes the file. Use a streaming API when the file may be too large to hold comfortably in memory.",
  moduleName: "System.IO.File",
  action: "Read",
  object: "File",
  template: "var content = File.ReadAllText({path});",
  parameters: [parameter("path", "path", "The path of the text file to read.")],
  title: "Read and display a configuration file",
  code: String.raw`var path = Path.Combine("config", "appsettings.json");
var json = File.ReadAllText(path);
Console.WriteLine(json);`,
  explanation: "Path.Combine builds the location, ReadAllText loads the complete file, and Console.WriteLine displays the returned string.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.readalltext",
});

add({
  name: "File.ReadAllTextAsync",
  description: "Asynchronously read an entire text file.",
  definition: "Reads all text without blocking the calling thread while operating-system I/O is pending. Await returns the complete string, and a CancellationToken overload can cancel the operation.",
  moduleName: "System.IO.File",
  action: "Read",
  object: "File",
  template: "var content = await File.ReadAllTextAsync({path});",
  parameters: [parameter("path", "path", "The path of the text file to read.")],
  title: "Load text without blocking",
  code: String.raw`var notesPath = Path.Combine("data", "notes.txt");
var notes = await File.ReadAllTextAsync(notesPath);
Console.WriteLine($"Loaded {notes.Length} characters.");`,
  explanation: "await yields control while the file read is pending, then resumes with the complete text.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.readalltextasync",
});

add({
  name: "File.ReadAllLines",
  description: "Read a text file into an array of lines.",
  definition: "Reads every line from a text file, closes the file, and returns a string array with one element per line. Line terminator characters are not included in the returned strings.",
  moduleName: "System.IO.File",
  action: "Read",
  object: "File",
  template: "var lines = File.ReadAllLines({path});",
  parameters: [parameter("path", "path", "The path of the text file to read.")],
  title: "Process a file line by line",
  code: String.raw`var lines = File.ReadAllLines("servers.txt");
foreach (var line in lines)
{
    Console.WriteLine($"Checking {line.Trim()}");
}`,
  explanation: "Each file line becomes a separate array element that the loop can process independently.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.readalllines",
});

add({
  name: "File.WriteAllText",
  description: "Create or replace a text file.",
  definition: "Writes the complete string to a file, creating the file if necessary and replacing its prior contents if it already exists. The method closes the file before returning.",
  moduleName: "System.IO.File",
  action: "Write",
  object: "File",
  template: "File.WriteAllText({path}, {contents});",
  parameters: [
    parameter("path", "path", "The destination file path."),
    parameter("contents", "contents", "The text to write."),
  ],
  title: "Save application state",
  code: String.raw`var state = "{\"lastProject\":\"Hypershell\"}";
Directory.CreateDirectory("data");
File.WriteAllText(Path.Combine("data", "state.json"), state);`,
  explanation: "The directory is ensured first, then WriteAllText creates or fully replaces state.json.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.writealltext",
});

add({
  name: "File.WriteAllTextAsync",
  description: "Asynchronously create or replace a text file.",
  definition: "Writes a complete string while allowing the caller to remain responsive during pending I/O. Await completes only after the content has been written and the file has been closed.",
  moduleName: "System.IO.File",
  action: "Write",
  object: "File",
  template: "await File.WriteAllTextAsync({path}, {contents});",
  parameters: [
    parameter("path", "path", "The destination file path."),
    parameter("contents", "contents", "The text to write."),
  ],
  title: "Save a report asynchronously",
  code: String.raw`var report = $"Build report{Environment.NewLine}Status: passed";
await File.WriteAllTextAsync("report.txt", report);
Console.WriteLine("Report saved.");`,
  explanation: "The success message is reached after the asynchronous write has finished.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.writealltextasync",
});

add({
  name: "File.AppendAllText",
  description: "Append text to the end of a file.",
  definition: "Opens or creates a text file, writes the supplied content after any existing content, and closes it. Add your own newline when each append should begin on a new line.",
  moduleName: "System.IO.File",
  action: "Append",
  object: "File",
  template: "File.AppendAllText({path}, {contents});",
  parameters: [
    parameter("path", "path", "The file to append to."),
    parameter("contents", "contents", "The text to add."),
  ],
  title: "Append a timestamped log entry",
  code: String.raw`var entry = $"{DateTimeOffset.Now:O} Application started{Environment.NewLine}";
File.AppendAllText("activity.log", entry);`,
  explanation: "The ISO timestamp and newline are added after earlier log entries; the prior file content is preserved.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.appendalltext",
});

add({
  name: "File.Exists",
  description: "Test whether a file path currently exists.",
  definition: "Returns true when the path identifies an existing file. It returns false for missing paths, directories, invalid paths, and many access-related failures rather than throwing those failures.",
  moduleName: "System.IO.File",
  action: "Test",
  object: "File",
  template: "var exists = File.Exists({path});",
  parameters: [parameter("path", "path", "The file path to check.")],
  title: "Load an optional settings file",
  code: String.raw`const string path = "settings.json";
var json = File.Exists(path)
    ? File.ReadAllText(path)
    : "{}";`,
  explanation: "The file is read only when it exists; otherwise the program uses an empty JSON object.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.exists",
});

add({
  name: "File.Copy",
  description: "Copy a file to another path.",
  definition: "Creates a second file containing the source file's data. The overwrite argument controls whether an existing destination file may be replaced.",
  moduleName: "System.IO.File",
  action: "Copy",
  object: "File",
  template: "File.Copy({sourcePath}, {destinationPath}, overwrite: {overwrite});",
  parameters: [
    parameter("sourcePath", "sourcePath", "The existing file to copy."),
    parameter("destinationPath", "destinationPath", "The new file path."),
    parameter("overwrite", "overwrite", "Whether an existing destination may be replaced.", "bool"),
  ],
  title: "Create a replaceable backup",
  code: String.raw`var source = "settings.json";
var backup = "settings.backup.json";
File.Copy(source, backup, overwrite: true);`,
  explanation: "The source remains in place, while the backup path receives a fresh copy even if an older backup exists.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.copy",
});

add({
  name: "File.Move",
  description: "Move or rename a file.",
  definition: "Relocates a file to another path, which can also change its name. On the same volume this is usually a metadata operation; overloads can optionally replace an existing destination.",
  moduleName: "System.IO.File",
  action: "Move",
  object: "File",
  template: "File.Move({sourcePath}, {destinationPath}, overwrite: {overwrite});",
  parameters: [
    parameter("sourcePath", "sourcePath", "The current file path."),
    parameter("destinationPath", "destinationPath", "The new file path."),
    parameter("overwrite", "overwrite", "Whether an existing destination may be replaced.", "bool"),
  ],
  title: "Archive a completed download",
  code: String.raw`Directory.CreateDirectory("archive");
File.Move(
    "download.tmp",
    Path.Combine("archive", "package.zip"),
    overwrite: true);`,
  explanation: "The temporary file is relocated and renamed package.zip inside the archive directory.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.move",
});

add({
  name: "File.Delete",
  description: "Delete a file.",
  definition: "Permanently removes the file at the supplied path. Calling it for a missing file does nothing, but invalid paths, access failures, and in-use files can throw exceptions.",
  moduleName: "System.IO.File",
  action: "Delete",
  object: "File",
  template: "File.Delete({path});",
  parameters: [parameter("path", "path", "The file to remove.")],
  title: "Remove an expired temporary file",
  code: String.raw`var tempFile = Path.Combine(Path.GetTempPath(), "hypershell.tmp");
if (File.Exists(tempFile))
{
    File.Delete(tempFile);
}`,
  explanation: "The existence check makes the intent explicit; Delete then removes the file rather than sending it to a recycle bin.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.file.delete",
});

add({
  name: "Directory.CreateDirectory",
  description: "Create a directory and any missing parents.",
  definition: "Ensures that the specified directory path exists, creating every missing directory in the path. It succeeds without replacing content when the directory already exists.",
  moduleName: "System.IO.Directory",
  action: "Create",
  object: "Directory",
  template: "var directory = Directory.CreateDirectory({path});",
  parameters: [parameter("path", "path", "The directory path to ensure.")],
  title: "Prepare a nested output folder",
  code: String.raw`var output = Path.Combine("build", "reports", "daily");
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "summary.txt"), "Ready");`,
  explanation: "CreateDirectory makes build, reports, and daily as needed before the file is written.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.directory.createdirectory",
});

add({
  name: "Directory.GetFiles",
  description: "Return file paths from a directory.",
  definition: "Immediately enumerates matching files and returns their paths as a string array. A search pattern limits names, and an overload can include subdirectories.",
  moduleName: "System.IO.Directory",
  action: "Get",
  object: "Directory",
  template: "var files = Directory.GetFiles({path}, {searchPattern});",
  parameters: [
    parameter("path", "path", "The directory to search."),
    parameter("searchPattern", "searchPattern", "A file-name pattern such as *.log."),
  ],
  title: "List log files",
  code: String.raw`var files = Directory.GetFiles("logs", "*.log");
foreach (var file in files)
{
    Console.WriteLine(Path.GetFileName(file));
}`,
  explanation: "GetFiles returns the matching paths as an array, and the loop prints just each file name.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.directory.getfiles",
});

add({
  name: "Directory.EnumerateFiles",
  description: "Lazily enumerate file paths from a directory.",
  definition: "Returns an IEnumerable<string> that produces matching file paths as they are requested. Unlike GetFiles, processing can begin before every match has been collected in memory.",
  moduleName: "System.IO.Directory",
  action: "Get",
  object: "Directory",
  template: "var files = Directory.EnumerateFiles({path}, {searchPattern}, SearchOption.AllDirectories);",
  parameters: [
    parameter("path", "path", "The directory to search."),
    parameter("searchPattern", "searchPattern", "A file-name pattern such as *.cs."),
  ],
  title: "Stop after the first ten source files",
  code: String.raw`var sourceFiles = Directory
    .EnumerateFiles(".", "*.cs", SearchOption.AllDirectories)
    .Take(10);

foreach (var file in sourceFiles)
{
    Console.WriteLine(file);
}`,
  explanation: "Lazy enumeration lets Take stop the directory walk after ten matching paths have been requested.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.directory.enumeratefiles",
});

add({
  name: "Directory.GetDirectories",
  description: "Return child directory paths.",
  definition: "Returns a string array containing directories that match the selected path and optional search pattern. SearchOption can limit the operation to direct children or recurse through the tree.",
  moduleName: "System.IO.Directory",
  action: "Get",
  object: "Directory",
  template: "var directories = Directory.GetDirectories({path});",
  parameters: [parameter("path", "path", "The parent directory to inspect.")],
  title: "Show immediate project folders",
  code: String.raw`foreach (var directory in Directory.GetDirectories("projects"))
{
    Console.WriteLine(Path.GetFileName(directory));
}`,
  explanation: "Only the direct child directories are returned by this overload.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.directory.getdirectories",
});

add({
  name: "Directory.Exists",
  description: "Test whether a directory path currently exists.",
  definition: "Returns true when the path identifies an existing directory. Missing, invalid, and inaccessible paths normally produce false rather than an exception.",
  moduleName: "System.IO.Directory",
  action: "Test",
  object: "Directory",
  template: "var exists = Directory.Exists({path});",
  parameters: [parameter("path", "path", "The directory path to check.")],
  title: "Choose an available workspace",
  code: String.raw`var workspace = Directory.Exists(@"D:\Work")
    ? @"D:\Work"
    : Environment.CurrentDirectory;
Console.WriteLine(workspace);`,
  explanation: "The preferred directory is used when available; otherwise the current process directory becomes the fallback.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.directory.exists",
});

add({
  name: "Directory.Delete",
  description: "Delete a directory.",
  definition: "Removes a directory. The recursive argument must be true to delete a non-empty tree, so callers should resolve and validate the exact target before using it.",
  moduleName: "System.IO.Directory",
  action: "Delete",
  object: "Directory",
  template: "Directory.Delete({path}, recursive: {recursive});",
  parameters: [
    parameter("path", "path", "The directory to remove."),
    parameter("recursive", "recursive", "Whether to remove all descendants.", "bool"),
  ],
  title: "Remove a known build-output folder",
  code: String.raw`var buildOutput = Path.GetFullPath(Path.Combine("artifacts", "temp"));
if (Directory.Exists(buildOutput))
{
    Directory.Delete(buildOutput, recursive: true);
}`,
  explanation: "The code resolves one explicit target, checks it, and removes the tree. Recursive deletion is permanent and should never use an unvalidated broad path.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.directory.delete",
});

add({
  name: "Path.Combine",
  description: "Combine path segments with the proper separator.",
  definition: "Joins path segments using the current platform's directory separator rules. If a later segment is rooted, earlier segments may be discarded, so untrusted rooted input should be validated.",
  moduleName: "System.IO.Path",
  action: "Combine",
  object: "Path",
  template: "var path = Path.Combine({path1}, {path2});",
  parameters: [
    parameter("path1", "path1", "The first path segment."),
    parameter("path2", "path2", "The next path segment."),
  ],
  title: "Build a settings path",
  code: String.raw`var settingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Hypershell",
    "settings.json");`,
  explanation: "Each segment is joined with the separator appropriate for the operating system.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.combine",
});

add({
  name: "Path.GetFileName",
  description: "Extract the file-name portion of a path.",
  definition: "Returns the characters after the final directory separator. The result can include an extension and is empty when the path ends in a separator.",
  moduleName: "System.IO.Path",
  action: "Select",
  object: "Path",
  template: "var fileName = Path.GetFileName({path});",
  parameters: [parameter("path", "path", "The path to inspect.")],
  title: "Display only a file name",
  code: String.raw`var fullPath = @"C:\logs\app-2026.log";
Console.WriteLine(Path.GetFileName(fullPath));`,
  explanation: "The directory portion is removed and app-2026.log is returned.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.getfilename",
});

add({
  name: "Path.GetExtension",
  description: "Extract the extension from a path.",
  definition: "Returns the final period and the characters that follow it in the last path segment. It returns an empty string when no extension is present.",
  moduleName: "System.IO.Path",
  action: "Select",
  object: "Path",
  template: "var extension = Path.GetExtension({path});",
  parameters: [parameter("path", "path", "The path to inspect.")],
  title: "Route files by extension",
  code: String.raw`var extension = Path.GetExtension("archive.tar.gz");
if (extension.Equals(".gz", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("GZip content");
}`,
  explanation: "GetExtension returns only the final extension, .gz, not the earlier .tar portion.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.getextension",
});

add({
  name: "Path.GetDirectoryName",
  description: "Extract the directory portion of a path.",
  definition: "Returns the path text before the final directory separator, or null when the input has no directory information. It manipulates path text and does not check whether the directory exists.",
  moduleName: "System.IO.Path",
  action: "Select",
  object: "Path",
  template: "var directory = Path.GetDirectoryName({path});",
  parameters: [parameter("path", "path", "The path to inspect.")],
  title: "Ensure a destination parent exists",
  code: String.raw`var destination = Path.GetFullPath(@"exports\daily\report.csv");
var parent = Path.GetDirectoryName(destination);
if (parent is not null)
{
    Directory.CreateDirectory(parent);
}`,
  explanation: "The parent path is extracted from the full destination and created before a later file write.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.getdirectoryname",
});

add({
  name: "Path.GetFullPath",
  description: "Resolve a path to an absolute normalized path.",
  definition: "Converts relative path text into a fully qualified path using a base directory and normalizes components such as dot segments. It does not require the resulting file or directory to exist.",
  moduleName: "System.IO.Path",
  action: "Get",
  object: "Path",
  template: "var fullPath = Path.GetFullPath({path});",
  parameters: [parameter("path", "path", "The relative or absolute path to resolve.")],
  title: "Resolve a user-supplied relative path",
  code: String.raw`var fullPath = Path.GetFullPath(
    Path.Combine(Environment.CurrentDirectory, @"data\..\config"));
Console.WriteLine(fullPath);`,
  explanation: "The result is absolute and the data\\.. pair is normalized away.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.getfullpath",
});

add({
  name: "Path.ChangeExtension",
  description: "Return a path with a different extension.",
  definition: "Produces new path text whose final extension is replaced, added, or removed. It does not rename or create a file on disk.",
  moduleName: "System.IO.Path",
  action: "Replace",
  object: "Path",
  template: "var changedPath = Path.ChangeExtension({path}, {extension});",
  parameters: [
    parameter("path", "path", "The original path."),
    parameter("extension", "extension", "The new extension, with or without a leading period."),
  ],
  title: "Build a sidecar metadata name",
  code: String.raw`var imagePath = @"photos\sunset.jpg";
var metadataPath = Path.ChangeExtension(imagePath, ".json");
Console.WriteLine(metadataPath);`,
  explanation: "The returned text is photos\\sunset.json; the original JPG file is untouched.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.changeextension",
});

add({
  name: "Path.GetTempPath",
  description: "Get the current user's temporary-file directory.",
  definition: "Returns the path the operating system designates for temporary files for the current process or user. The method returns a directory path but does not create a unique file within it.",
  moduleName: "System.IO.Path",
  action: "Get",
  object: "Path",
  template: "var tempDirectory = Path.GetTempPath();",
  title: "Build a unique temporary file path",
  code: String.raw`var tempFile = Path.Combine(
    Path.GetTempPath(),
    Path.GetRandomFileName());
await File.WriteAllTextAsync(tempFile, "temporary data");`,
  explanation: "GetTempPath chooses the temporary directory and GetRandomFileName supplies a low-collision name inside it.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.gettemppath",
});

add({
  name: "Path.GetRandomFileName",
  description: "Generate a random file name without creating a file.",
  definition: "Returns a cryptographically strong random name suitable for use as a file or directory name. It does not create anything, so the caller still chooses a directory and handles the small possibility of a collision.",
  moduleName: "System.IO.Path",
  action: "Create",
  object: "Path",
  template: "var randomName = Path.GetRandomFileName();",
  title: "Create an isolated temporary directory",
  code: String.raw`var tempDirectory = Path.Combine(
    Path.GetTempPath(),
    Path.GetRandomFileName());
Directory.CreateDirectory(tempDirectory);`,
  explanation: "The random name becomes one child of the operating system's temporary directory, then CreateDirectory makes it real.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.io.path.getrandomfilename",
});

// Generic collections
add({
  name: "List<T>.Add",
  description: "Add one item to the end of a list.",
  definition: "Appends one value to a mutable List<T>, increasing Count by one. The value must be compatible with the list's element type.",
  moduleName: "System.Collections.Generic.List<T>",
  action: "Add",
  object: "List",
  template: "{list}.Add({item});",
  parameters: [
    parameter("list", "list", "The List<T> instance to change.", "List<T>"),
    parameter("item", "item", "The value to append.", "T"),
  ],
  title: "Collect validation errors",
  code: String.raw`var projectName = "   ";
var errors = new List<string>();
if (string.IsNullOrWhiteSpace(projectName))
{
    errors.Add("Project name is required.");
}`,
  explanation: "The message is appended only when validation fails, and the list can later display all collected errors.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.add",
});

add({
  name: "List<T>.AddRange",
  description: "Append every item from another sequence.",
  definition: "Enumerates the supplied collection and appends each value to the list in order. The existing list entries remain before the newly added range.",
  moduleName: "System.Collections.Generic.List<T>",
  action: "Add",
  object: "List",
  template: "{list}.AddRange({items});",
  parameters: [
    parameter("list", "list", "The List<T> instance to change.", "List<T>"),
    parameter("items", "items", "The sequence of values to append.", "IEnumerable<T>"),
  ],
  title: "Merge discovered files into a work list",
  code: String.raw`var work = new List<string> { "README.md" };
var sourceFiles = Directory.EnumerateFiles("src", "*.cs");
work.AddRange(sourceFiles);
Console.WriteLine(work.Count);`,
  explanation: "README.md stays first, followed by every source-file path produced by the enumeration.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.addrange",
});

add({
  name: "List<T>.Remove",
  description: "Remove the first matching value from a list.",
  definition: "Searches from the beginning of the list, removes the first equal item, and returns true. It returns false and leaves the list unchanged when no equal item exists.",
  moduleName: "System.Collections.Generic.List<T>",
  action: "Remove",
  object: "List",
  template: "var removed = {list}.Remove({item});",
  parameters: [
    parameter("list", "list", "The List<T> instance to change.", "List<T>"),
    parameter("item", "item", "The value to remove.", "T"),
  ],
  title: "Remove a completed task",
  code: String.raw`var tasks = new List<string> { "Build", "Test", "Deploy" };
if (tasks.Remove("Test"))
{
    Console.WriteLine("Test task removed.");
}`,
  explanation: "Remove returns true because Test was found, and the remaining list contains Build and Deploy.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.remove",
});

add({
  name: "List<T>.RemoveAt",
  description: "Remove the item at a zero-based index.",
  definition: "Deletes exactly one element by position and shifts all later elements down by one index. An index outside the current list range throws ArgumentOutOfRangeException.",
  moduleName: "System.Collections.Generic.List<T>",
  action: "Remove",
  object: "List",
  template: "{list}.RemoveAt({index});",
  parameters: [
    parameter("list", "list", "The List<T> instance to change.", "List<T>"),
    parameter("index", "index", "The zero-based position to delete.", "int"),
  ],
  title: "Remove the oldest queue entry",
  code: String.raw`var queue = new List<string> { "oldest", "middle", "newest" };
if (queue.Count > 0)
{
    queue.RemoveAt(0);
}`,
  explanation: "After the count guard, index 0 is safe to remove and middle becomes the first entry.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.removeat",
});

add({
  name: "List<T>.Contains",
  description: "Test whether a list contains an equal value.",
  definition: "Searches the list using its element equality comparer and returns true when an equal value is present. For strings, the default comparison is case-sensitive.",
  moduleName: "System.Collections.Generic.List<T>",
  action: "Test",
  object: "List",
  template: "var contains = {list}.Contains({item});",
  parameters: [
    parameter("list", "list", "The List<T> instance to search.", "List<T>"),
    parameter("item", "item", "The value to locate.", "T"),
  ],
  title: "Avoid adding a duplicate ID",
  code: String.raw`var ids = new List<int> { 101, 203, 305 };
if (!ids.Contains(203))
{
    ids.Add(203);
}`,
  explanation: "Contains finds 203, so the guarded Add does not create a duplicate.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.contains",
});

add({
  name: "List<T>.Find",
  description: "Find the first list item that satisfies a predicate.",
  definition: "Runs a Boolean predicate against list elements in order and returns the first match. When nothing matches it returns default(T), which can be ambiguous for value types.",
  moduleName: "System.Collections.Generic.List<T>",
  action: "Find",
  object: "List",
  template: "var match = {list}.Find({predicate});",
  parameters: [
    parameter("list", "list", "The List<T> instance to search.", "List<T>"),
    parameter("predicate", "predicate", "A function returning true for the desired item.", "Predicate<T>"),
  ],
  title: "Find a user by ID",
  code: String.raw`var users = new[]
{
    new { Id = 7, Name = "Ada" },
    new { Id = 12, Name = "Grace" }
}.ToList();
var user = users.Find(candidate => candidate.Id == 12);
Console.WriteLine(user?.Name ?? "Not found");`,
  explanation: "The predicate selects the first User whose Id is 12, so Grace is printed.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.find",
});

add({
  name: "List<T>.Sort",
  description: "Sort a list in place.",
  definition: "Reorders the existing List<T> rather than returning a second sequence. With no comparer it uses the element type's default ordering; overloads accept comparison logic.",
  moduleName: "System.Collections.Generic.List<T>",
  action: "Sort",
  object: "List",
  template: "{list}.Sort();",
  parameters: [parameter("list", "list", "The List<T> instance to reorder.", "List<T>")],
  title: "Sort version labels with a comparison",
  code: String.raw`var versions = new List<Version>
{
    new(2, 0),
    new(1, 5),
    new(1, 2)
};
versions.Sort();
Console.WriteLine(string.Join(", ", versions));`,
  explanation: "The list itself becomes 1.2, 1.5, 2.0 using Version's default comparison.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.sort",
});

add({
  name: "Dictionary<TKey,TValue>.Add",
  description: "Add a new key and value to a dictionary.",
  definition: "Stores a key-value pair and requires the key to be absent. Adding an equal key twice throws ArgumentException; use TryAdd or assignment when duplicate keys are expected.",
  moduleName: "System.Collections.Generic.Dictionary<TKey,TValue>",
  action: "Add",
  object: "Dictionary",
  template: "{dictionary}.Add({key}, {value});",
  parameters: [
    parameter("dictionary", "dictionary", "The dictionary instance to change.", "Dictionary<TKey,TValue>"),
    parameter("key", "key", "The unique lookup key.", "TKey"),
    parameter("value", "value", "The value associated with the key.", "TValue"),
  ],
  title: "Register a command handler",
  code: String.raw`var handlers = new Dictionary<string, Action>(
    StringComparer.OrdinalIgnoreCase);
handlers.Add("build", () => Console.WriteLine("Building"));
handlers["build"]();`,
  explanation: "The comparer makes key lookup case-insensitive, and the stored Action is invoked by its build key.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2.add",
});

add({
  name: "Dictionary<TKey,TValue>.TryGetValue",
  description: "Look up a value without throwing when the key is absent.",
  definition: "Attempts one dictionary lookup, places the found value in an out variable, and returns true on success. On failure it returns false and assigns default(TValue).",
  moduleName: "System.Collections.Generic.Dictionary<TKey,TValue>",
  action: "Get",
  object: "Dictionary",
  template: "var found = {dictionary}.TryGetValue({key}, out var value);",
  parameters: [
    parameter("dictionary", "dictionary", "The dictionary instance to search.", "Dictionary<TKey,TValue>"),
    parameter("key", "key", "The lookup key.", "TKey"),
  ],
  title: "Read an optional timeout",
  code: String.raw`var settings = new Dictionary<string, int>
{
    ["timeout"] = 30
};
var timeout = settings.TryGetValue("timeout", out var seconds)
    ? seconds
    : 10;`,
  explanation: "The existing value 30 is returned; if the key were missing, the conditional would use 10.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2.trygetvalue",
});

add({
  name: "Dictionary<TKey,TValue>.ContainsKey",
  description: "Test whether a dictionary contains a key.",
  definition: "Returns true when the dictionary contains an equal key according to its key comparer. When the value is also needed, TryGetValue avoids performing a second lookup.",
  moduleName: "System.Collections.Generic.Dictionary<TKey,TValue>",
  action: "Test",
  object: "Dictionary",
  template: "var contains = {dictionary}.ContainsKey({key});",
  parameters: [
    parameter("dictionary", "dictionary", "The dictionary instance to inspect.", "Dictionary<TKey,TValue>"),
    parameter("key", "key", "The key to test.", "TKey"),
  ],
  title: "Add a default only when missing",
  code: String.raw`var ports = new Dictionary<string, int>();
if (!ports.ContainsKey("https"))
{
    ports["https"] = 443;
}`,
  explanation: "The assignment runs only when no https key exists.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2.containskey",
});

add({
  name: "Dictionary<TKey,TValue>.Remove",
  description: "Remove an entry by key.",
  definition: "Deletes the key and its associated value, returning true when an entry was present. An overload also returns the removed value through an out parameter.",
  moduleName: "System.Collections.Generic.Dictionary<TKey,TValue>",
  action: "Remove",
  object: "Dictionary",
  template: "var removed = {dictionary}.Remove({key});",
  parameters: [
    parameter("dictionary", "dictionary", "The dictionary instance to change.", "Dictionary<TKey,TValue>"),
    parameter("key", "key", "The key of the entry to remove.", "TKey"),
  ],
  title: "Remove and inspect a cached value",
  code: String.raw`var cache = new Dictionary<string, string>
{
    ["token"] = "temporary"
};
if (cache.Remove("token", out var removedValue))
{
    Console.WriteLine($"Removed {removedValue.Length} characters.");
}`,
  explanation: "The entry disappears from the dictionary while its former value remains available in removedValue.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2.remove",
});

add({
  name: "Dictionary<TKey,TValue>.Clear",
  description: "Remove every entry from a dictionary.",
  definition: "Deletes all key-value pairs and sets Count to zero while retaining the same dictionary instance, comparer, and reusable allocated storage.",
  moduleName: "System.Collections.Generic.Dictionary<TKey,TValue>",
  action: "Clear",
  object: "Dictionary",
  template: "{dictionary}.Clear();",
  parameters: [parameter("dictionary", "dictionary", "The dictionary instance to empty.", "Dictionary<TKey,TValue>")],
  title: "Reset an in-memory cache",
  code: String.raw`var cache = new Dictionary<string, object>();
cache["profile"] = new { Name = "Developer", Theme = "Dark" };
cache.Clear();
Console.WriteLine(cache.Count);`,
  explanation: "The same cache object remains usable, but it contains zero entries.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2.clear",
});

add({
  name: "HashSet<T>.Add",
  description: "Add a value only if it is not already in the set.",
  definition: "Inserts a unique value and returns true. When an equal value is already present, it returns false and the set remains unchanged.",
  moduleName: "System.Collections.Generic.HashSet<T>",
  action: "Add",
  object: "HashSet",
  template: "var added = {set}.Add({item});",
  parameters: [
    parameter("set", "set", "The HashSet<T> instance to change.", "HashSet<T>"),
    parameter("item", "item", "The value to add.", "T"),
  ],
  title: "Collect unique file extensions",
  code: String.raw`var extensions = new HashSet<string>(
    StringComparer.OrdinalIgnoreCase);
foreach (var file in Directory.EnumerateFiles("."))
{
    extensions.Add(Path.GetExtension(file));
}`,
  explanation: "Repeated extensions are ignored, and the comparer treats .TXT and .txt as the same value.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.hashset-1.add",
});

add({
  name: "HashSet<T>.Contains",
  description: "Test whether a set contains an equal value.",
  definition: "Uses the set's equality comparer to perform a fast membership test. It returns a Boolean and does not change the set.",
  moduleName: "System.Collections.Generic.HashSet<T>",
  action: "Test",
  object: "HashSet",
  template: "var contains = {set}.Contains({item});",
  parameters: [
    parameter("set", "set", "The HashSet<T> instance to inspect.", "HashSet<T>"),
    parameter("item", "item", "The value to test.", "T"),
  ],
  title: "Authorize a role",
  code: String.raw`var allowedRoles = new HashSet<string>(
    new[] { "Admin", "Operator" },
    StringComparer.OrdinalIgnoreCase);
var mayDeploy = allowedRoles.Contains("operator");`,
  explanation: "The case-insensitive set recognizes operator as the existing Operator role.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.hashset-1.contains",
});

add({
  name: "HashSet<T>.UnionWith",
  description: "Add every distinct value from another sequence.",
  definition: "Mutates the set so it contains values that appeared in either the original set or the supplied sequence. Duplicates remain collapsed according to the set's comparer.",
  moduleName: "System.Collections.Generic.HashSet<T>",
  action: "Combine",
  object: "HashSet",
  template: "{set}.UnionWith({other});",
  parameters: [
    parameter("set", "set", "The HashSet<T> instance to change.", "HashSet<T>"),
    parameter("other", "other", "The values to merge into the set.", "IEnumerable<T>"),
  ],
  title: "Merge permission sets",
  code: String.raw`var permissions = new HashSet<string> { "read", "write" };
var rolePermissions = new[] { "write", "deploy" };
permissions.UnionWith(rolePermissions);
Console.WriteLine(string.Join(", ", permissions));`,
  explanation: "The result contains read, write, and deploy; the repeated write value appears only once.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.hashset-1.unionwith",
});

add({
  name: "HashSet<T>.IntersectWith",
  description: "Keep only values that also occur in another sequence.",
  definition: "Mutates the set to its mathematical intersection with the supplied values. Every value not present in both collections is removed.",
  moduleName: "System.Collections.Generic.HashSet<T>",
  action: "Filter",
  object: "HashSet",
  template: "{set}.IntersectWith({other});",
  parameters: [
    parameter("set", "set", "The HashSet<T> instance to filter.", "HashSet<T>"),
    parameter("other", "other", "The values allowed to remain.", "IEnumerable<T>"),
  ],
  title: "Restrict requested features to supported features",
  code: String.raw`var requested = new HashSet<string> { "logs", "metrics", "tracing" };
var supported = new[] { "logs", "metrics" };
requested.IntersectWith(supported);`,
  explanation: "tracing is removed because it is not supported; logs and metrics remain.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.collections.generic.hashset-1.intersectwith",
});

// LINQ sequence operations
add({
  name: "Enumerable.Where",
  description: "Keep sequence elements that satisfy a condition.",
  definition: "Returns a lazy sequence that yields only source elements for which the predicate returns true. The source is evaluated when the result is enumerated, not when Where is called.",
  moduleName: "System.Linq",
  action: "Filter",
  object: "Sequence",
  template: "var filtered = {source}.Where({predicate});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("predicate", "predicate", "A function that returns true for values to keep.", "Func<T,bool>"),
  ],
  title: "Keep only large files",
  code: String.raw`var largeFiles = Directory
    .EnumerateFiles("logs", "*.log")
    .Where(path => new FileInfo(path).Length > 1_000_000);

foreach (var file in largeFiles)
{
    Console.WriteLine(file);
}`,
  explanation: "Each path is tested during enumeration, and only files larger than one million bytes reach the loop.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.where",
});

add({
  name: "Enumerable.Select",
  description: "Transform every sequence element into a result value.",
  definition: "Returns a lazy projection in which the selector function converts each source element to another value or shape. It does not mutate the source objects unless the selector itself does so.",
  moduleName: "System.Linq",
  action: "Select",
  object: "Sequence",
  template: "var projected = {source}.Select({selector});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<TSource>"),
    parameter("selector", "selector", "A function that creates one result per source value.", "Func<TSource,TResult>"),
  ],
  title: "Project file paths into names",
  code: String.raw`var names = Directory
    .EnumerateFiles("reports", "*.csv")
    .Select(Path.GetFileName);

Console.WriteLine(string.Join(Environment.NewLine, names));`,
  explanation: "Each full path is replaced in the result sequence by its file-name portion.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.select",
});

add({
  name: "Enumerable.OrderBy",
  description: "Order sequence elements by an ascending key.",
  definition: "Returns a new ordered sequence using the key produced for each element. The source collection is not rearranged, and enumeration performs the ordering work.",
  moduleName: "System.Linq",
  action: "Order",
  object: "Sequence",
  template: "var ordered = {source}.OrderBy({keySelector});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<TSource>"),
    parameter("keySelector", "keySelector", "A function that returns the sort key.", "Func<TSource,TKey>"),
  ],
  title: "Order processes by memory use",
  code: String.raw`var processes = Process.GetProcesses()
    .OrderBy(process => process.WorkingSet64)
    .Take(5);

foreach (var process in processes)
{
    Console.WriteLine($"{process.ProcessName}: {process.WorkingSet64:N0}");
}`,
  explanation: "The five processes with the smallest working sets are enumerated first.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.orderby",
});

add({
  name: "Enumerable.OrderByDescending",
  description: "Order sequence elements by a descending key.",
  definition: "Returns an ordered sequence with larger or later key values first. The operation leaves the original collection unchanged.",
  moduleName: "System.Linq",
  action: "Order",
  object: "Sequence",
  template: "var ordered = {source}.OrderByDescending({keySelector});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<TSource>"),
    parameter("keySelector", "keySelector", "A function that returns the sort key.", "Func<TSource,TKey>"),
  ],
  title: "Show the newest files first",
  code: String.raw`var newest = Directory.EnumerateFiles("logs")
    .Select(path => new FileInfo(path))
    .OrderByDescending(file => file.LastWriteTimeUtc)
    .Take(3);`,
  explanation: "FileInfo objects are sorted by UTC modification time, newest first, and only the first three are kept.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.orderbydescending",
});

add({
  name: "Enumerable.GroupBy",
  description: "Group sequence elements that share a key.",
  definition: "Returns groups keyed by the value produced for each source element. Every group is itself an enumerable sequence, and all elements with equal keys appear together.",
  moduleName: "System.Linq",
  action: "Group",
  object: "Sequence",
  template: "var groups = {source}.GroupBy({keySelector});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<TSource>"),
    parameter("keySelector", "keySelector", "A function that chooses each grouping key.", "Func<TSource,TKey>"),
  ],
  title: "Count files by extension",
  code: String.raw`var groups = Directory.EnumerateFiles("assets")
    .GroupBy(path => Path.GetExtension(path).ToLowerInvariant());

foreach (var group in groups)
{
    Console.WriteLine($"{group.Key}: {group.Count()}");
}`,
  explanation: "Paths sharing an extension are collected into the same group, then each group's size is printed.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.groupby",
});

add({
  name: "Enumerable.Any",
  description: "Test whether a sequence has any matching element.",
  definition: "Returns true as soon as one element satisfies the predicate, so it need not enumerate the rest. Without a predicate it tests whether the sequence contains at least one element.",
  moduleName: "System.Linq",
  action: "Test",
  object: "Sequence",
  template: "var any = {source}.Any({predicate});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("predicate", "predicate", "The condition at least one element must satisfy.", "Func<T,bool>"),
  ],
  title: "Check for failed results",
  code: String.raw`var results = new[] { 0, 0, 2, 0 };
var hasFailure = results.Any(exitCode => exitCode != 0);
Console.WriteLine(hasFailure);`,
  explanation: "Enumeration stops when exit code 2 satisfies the predicate, and true is returned.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.any",
});

add({
  name: "Enumerable.All",
  description: "Test whether every sequence element satisfies a condition.",
  definition: "Returns false as soon as one element fails the predicate. It returns true for an empty sequence because no element violates the condition.",
  moduleName: "System.Linq",
  action: "Test",
  object: "Sequence",
  template: "var all = {source}.All({predicate});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("predicate", "predicate", "The condition every element must satisfy.", "Func<T,bool>"),
  ],
  title: "Validate all configured ports",
  code: String.raw`var ports = new[] { 80, 443, 8080 };
var valid = ports.All(port => port is > 0 and <= 65535);
Console.WriteLine(valid);`,
  explanation: "Every port lies inside the legal numeric range, so All returns true.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.all",
});

add({
  name: "Enumerable.FirstOrDefault",
  description: "Return the first matching element or a default value.",
  definition: "Returns the first source element, or the first one satisfying a predicate. When no element exists it returns default(T), commonly null for reference types and zero for int.",
  moduleName: "System.Linq",
  action: "Find",
  object: "Sequence",
  template: "var first = {source}.FirstOrDefault({predicate});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("predicate", "predicate", "The optional match condition.", "Func<T,bool>"),
  ],
  title: "Find the first JSON file",
  code: String.raw`var firstJson = Directory
    .EnumerateFiles("config")
    .FirstOrDefault(path =>
        Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase));

Console.WriteLine(firstJson ?? "No JSON file found");`,
  explanation: "Enumeration stops at the first matching path; null produces the fallback message.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.firstordefault",
});

add({
  name: "Enumerable.SingleOrDefault",
  description: "Return the only matching element, or a default when none exists.",
  definition: "Returns one element when exactly one match exists and default(T) when none exists. It throws InvalidOperationException if more than one element matches, making duplicate data visible.",
  moduleName: "System.Linq",
  action: "Find",
  object: "Sequence",
  template: "var single = {source}.SingleOrDefault({predicate});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("predicate", "predicate", "The match condition.", "Func<T,bool>"),
  ],
  title: "Require a unique user ID",
  code: String.raw`var requestedId = 12;
var users = new[]
{
    new { Id = 7, Name = "Ada" },
    new { Id = 12, Name = "Grace" }
};
var user = users.SingleOrDefault(candidate => candidate.Id == requestedId);
if (user is null)
{
    Console.WriteLine("User not found.");
}`,
  explanation: "No match yields null; duplicate IDs throw instead of silently choosing one record.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.singleordefault",
});

add({
  name: "Enumerable.Distinct",
  description: "Return one occurrence of each distinct value.",
  definition: "Produces a lazy sequence that omits later values considered equal by the selected comparer. It preserves the first-seen order in the current implementation, but callers should not depend on undocumented ordering guarantees.",
  moduleName: "System.Linq",
  action: "Filter",
  object: "Sequence",
  template: "var unique = {source}.Distinct();",
  parameters: [parameter("source", "source", "The input sequence.", "IEnumerable<T>")],
  title: "Remove duplicate tags",
  code: String.raw`var tags = new[] { "CSharp", "powershell", "csharp" };
var unique = tags.Distinct(StringComparer.OrdinalIgnoreCase);
Console.WriteLine(string.Join(", ", unique));`,
  explanation: "The case-insensitive comparer treats CSharp and csharp as duplicates.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.distinct",
});

add({
  name: "Enumerable.Count",
  description: "Count elements in a sequence.",
  definition: "Returns the number of source elements, or the number satisfying a predicate. It can use a collection's Count directly, but otherwise enumerates the sequence to calculate the result.",
  moduleName: "System.Linq",
  action: "Calculate",
  object: "Sequence",
  template: "var count = {source}.Count({predicate});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("predicate", "predicate", "The optional condition for values to count.", "Func<T,bool>", false),
  ],
  title: "Count enabled features",
  code: String.raw`var features = new[]
{
    (Name: "Search", Enabled: true),
    (Name: "Export", Enabled: false),
    (Name: "Preview", Enabled: true)
};
var enabledCount = features.Count(feature => feature.Enabled);`,
  explanation: "Only Search and Preview satisfy the predicate, so the result is 2.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.count",
});

add({
  name: "Enumerable.Sum",
  description: "Add numeric sequence values.",
  definition: "Enumerates numeric values and returns their total. Selector overloads first extract a numeric value from each object, and overflow behavior depends on the numeric type.",
  moduleName: "System.Linq",
  action: "Sum",
  object: "Sequence",
  template: "var total = {source}.Sum({selector});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("selector", "selector", "The optional function that supplies each number.", "Func<T,TNumber>", false),
  ],
  title: "Total file sizes",
  code: String.raw`var totalBytes = Directory
    .EnumerateFiles("downloads")
    .Select(path => new FileInfo(path))
    .Sum(file => file.Length);

Console.WriteLine($"{totalBytes:N0} bytes");`,
  explanation: "The selector reads each FileInfo.Length and Sum adds those long integer values.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.sum",
});

add({
  name: "Enumerable.Take",
  description: "Return at most the first requested number of elements.",
  definition: "Produces a lazy sequence containing the first count elements, or every element when the source is shorter. It stops requesting source values after the limit is reached.",
  moduleName: "System.Linq",
  action: "Select",
  object: "Sequence",
  template: "var first = {source}.Take({count});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("count", "count", "The maximum number of values to return.", "int"),
  ],
  title: "Display the five newest reports",
  code: String.raw`var reports = Directory.EnumerateFiles("reports")
    .OrderByDescending(File.GetLastWriteTimeUtc)
    .Take(5);

foreach (var report in reports)
{
    Console.WriteLine(report);
}`,
  explanation: "Ordering places newest paths first and Take limits the displayed result to five.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.take",
});

add({
  name: "Enumerable.Skip",
  description: "Ignore a requested number of leading elements.",
  definition: "Returns a lazy sequence that discards the first count values and yields the remainder. If the source has fewer elements, the result is empty.",
  moduleName: "System.Linq",
  action: "Select",
  object: "Sequence",
  template: "var remainder = {source}.Skip({count});",
  parameters: [
    parameter("source", "source", "The input sequence.", "IEnumerable<T>"),
    parameter("count", "count", "The number of leading values to ignore.", "int"),
  ],
  title: "Select the second page of results",
  code: String.raw`const int pageSize = 20;
const int pageNumber = 2;
var results = Enumerable.Range(1, 75);
var page = results
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize);`,
  explanation: "Skip ignores the first page's 20 values and Take returns at most 20 values for page two.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.skip",
});

add({
  name: "Enumerable.ToList",
  description: "Materialize a sequence into a new list.",
  definition: "Enumerates the source immediately and copies its current values into a new List<T>. Later source changes do not automatically update that list.",
  moduleName: "System.Linq",
  action: "Create",
  object: "Sequence",
  template: "var list = {source}.ToList();",
  parameters: [parameter("source", "source", "The sequence to enumerate.", "IEnumerable<T>")],
  title: "Capture the current matching files",
  code: String.raw`var snapshot = Directory
    .EnumerateFiles("inbox", "*.json")
    .ToList();

Console.WriteLine($"Captured {snapshot.Count} paths.");`,
  explanation: "ToList performs the directory enumeration now and stores the paths in a mutable list.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.linq.enumerable.tolist",
});

// JSON serialization
add({
  name: "JsonSerializer.Serialize",
  description: "Serialize a .NET value into JSON text.",
  definition: "Inspects the value's public data and produces a JSON string according to its .NET type and JsonSerializerOptions. The output is data, not executable C# source.",
  moduleName: "System.Text.Json",
  action: "Serialize",
  object: "Json",
  template: "var json = JsonSerializer.Serialize({value}, {options});",
  parameters: [
    parameter("value", "value", "The .NET object to serialize.", "object"),
    parameter("options", "options", "Formatting, naming, converter, and reference-handling settings.", "JsonSerializerOptions", false),
  ],
  title: "Write an indented settings document",
  code: String.raw`var settings = new
{
    ApplicationName = "Hypershell",
    DarkMode = true
};
var options = new JsonSerializerOptions { WriteIndented = true };
var json = JsonSerializer.Serialize(settings, options);
await File.WriteAllTextAsync("settings.json", json);`,
  explanation: "The record becomes indented JSON text, which is then saved as a file.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializer.serialize",
});

add({
  name: "JsonSerializer.Deserialize<T>",
  description: "Deserialize JSON text into a typed .NET value.",
  definition: "Parses JSON and creates an instance of T by mapping JSON properties to the type's serializable members. Invalid JSON can throw JsonException, and a literal JSON null can produce null.",
  moduleName: "System.Text.Json",
  action: "Deserialize",
  object: "Json",
  template: "var value = JsonSerializer.Deserialize<{type}>({json}, {options});",
  parameters: [
    parameter("type", "type", "The C# result type.", "Type"),
    parameter("json", "json", "The JSON string to parse."),
    parameter("options", "options", "Optional naming and converter settings.", "JsonSerializerOptions", false),
  ],
  title: "Load typed application settings",
  code: String.raw`var json = """
    { "applicationName": "Hypershell", "theme": "Dark" }
    """;
var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
    ?? throw new InvalidDataException("Settings were null.");
Console.WriteLine(settings["applicationName"]);`,
  explanation: "The JSON text is parsed into a typed dictionary, and the null guard rejects a JSON null document.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializer.deserialize",
});

add({
  name: "JsonSerializer.SerializeAsync",
  description: "Asynchronously write a .NET value as JSON to a stream.",
  definition: "Serializes a value directly into a Stream without first requiring one complete JSON string in memory. Await completes after the JSON bytes have been written.",
  moduleName: "System.Text.Json",
  action: "Serialize",
  object: "Json",
  template: "await JsonSerializer.SerializeAsync({stream}, {value}, {options});",
  parameters: [
    parameter("stream", "stream", "The writable destination stream.", "Stream"),
    parameter("value", "value", "The object to serialize.", "object"),
    parameter("options", "options", "Optional serialization settings.", "JsonSerializerOptions", false),
  ],
  title: "Stream a report into a file",
  code: String.raw`var report = new
{
    Status = "Passed",
    Tests = 128
};
await using var stream = File.Create("report.json");
await JsonSerializer.SerializeAsync(
    stream,
    report,
    new JsonSerializerOptions { WriteIndented = true });`,
  explanation: "File.Create supplies the writable stream, and await using ensures it is disposed after serialization finishes.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializer.serializeasync",
});

add({
  name: "JsonSerializer.DeserializeAsync<T>",
  description: "Asynchronously read a typed value from a JSON stream.",
  definition: "Reads UTF-8 JSON from a Stream and reconstructs an instance of T while allowing the caller to remain responsive during pending I/O. Malformed or incompatible content can throw JsonException.",
  moduleName: "System.Text.Json",
  action: "Deserialize",
  object: "Json",
  template: "var value = await JsonSerializer.DeserializeAsync<{type}>({stream}, {options});",
  parameters: [
    parameter("type", "type", "The C# result type.", "Type"),
    parameter("stream", "stream", "The readable JSON stream.", "Stream"),
    parameter("options", "options", "Optional deserialization settings.", "JsonSerializerOptions", false),
  ],
  title: "Read JSON from a stream",
  code: String.raw`var json = """
    { "name": "Hypershell", "commands": 211 }
    """;
await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
var catalog = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(stream)
    ?? throw new InvalidDataException("Catalog was null.");`,
  explanation: "The UTF-8 JSON is consumed from a stream and converted directly into a typed dictionary.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializer.deserializeasync",
});

// HTTP
add({
  name: "HttpClient.GetStringAsync",
  description: "Download an HTTP response body as text.",
  definition: "Sends an HTTP GET request and asynchronously returns the response content as a string. It throws for an unsuccessful status code and should normally use a reused HttpClient instance.",
  moduleName: "System.Net.Http",
  action: "Download",
  object: "HttpClient",
  template: "var response = await {client}.GetStringAsync({url});",
  parameters: [
    parameter("client", "client", "A reused HttpClient instance.", "HttpClient"),
    parameter("url", "url", "The request URI.", "string"),
  ],
  title: "Download and parse JSON",
  code: String.raw`using var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd("Hypershell-Catalog/1.0");
var json = await client.GetStringAsync(
    "https://api.github.com/repos/dotnet/runtime");
using var document = JsonDocument.Parse(json);
var repositoryName = document.RootElement.GetProperty("full_name").GetString();
Console.WriteLine(repositoryName);`,
  explanation: "GetStringAsync returns the response body, and JsonDocument reads the selected property without requiring an application-specific model type.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.getstringasync",
});

add({
  name: "HttpClient.GetAsync",
  description: "Send an HTTP GET request and keep the full response.",
  definition: "Asynchronously returns an HttpResponseMessage containing status, headers, and content. Unlike GetStringAsync, it lets the caller inspect non-success responses before deciding how to handle them.",
  moduleName: "System.Net.Http",
  action: "Download",
  object: "HttpClient",
  template: "using var response = await {client}.GetAsync({url});",
  parameters: [
    parameter("client", "client", "A reused HttpClient instance.", "HttpClient"),
    parameter("url", "url", "The request URI.", "string"),
  ],
  title: "Inspect status before reading content",
  code: String.raw`using var client = new HttpClient();
using var response = await client.GetAsync("https://example.com");
Console.WriteLine($"{(int)response.StatusCode} {response.ReasonPhrase}");
var html = await response.Content.ReadAsStringAsync();`,
  explanation: "The response object exposes status metadata and owns the content stream, so it is disposed after the body is read.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.getasync",
});

add({
  name: "HttpClient.PostAsJsonAsync",
  description: "Serialize a value as JSON and send it with HTTP POST.",
  definition: "Uses System.Net.Http.Json extensions to serialize a .NET value into a JSON request body, sets an application/json content type, and asynchronously returns the full response.",
  moduleName: "System.Net.Http.Json",
  action: "Write",
  object: "HttpClient",
  template: "using var response = await {client}.PostAsJsonAsync({url}, {value});",
  parameters: [
    parameter("client", "client", "A reused HttpClient instance.", "HttpClient"),
    parameter("url", "url", "The destination URI.", "string"),
    parameter("value", "value", "The object to send as JSON.", "object"),
  ],
  title: "Create an API record",
  code: String.raw`using var client = new HttpClient();
var request = new { Name = "Hypershell" };
using var response = await client.PostAsJsonAsync(
    "https://api.example.com/projects",
    request);
response.EnsureSuccessStatusCode();`,
  explanation: "The request record is serialized into the POST body, and the final call turns a non-success response into an exception.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.net.http.json.httpclientjsonextensions.postasjsonasync",
});

add({
  name: "HttpClient.SendAsync",
  description: "Send a fully customized HTTP request.",
  definition: "Asynchronously sends an HttpRequestMessage whose method, URI, headers, version, and content the caller controls. It returns the response without automatically requiring a successful status.",
  moduleName: "System.Net.Http",
  action: "Write",
  object: "HttpClient",
  template: "using var response = await {client}.SendAsync({request});",
  parameters: [
    parameter("client", "client", "A reused HttpClient instance.", "HttpClient"),
    parameter("request", "request", "The configured HTTP request.", "HttpRequestMessage"),
  ],
  title: "Send an authenticated PATCH request",
  code: String.raw`const string token = "development-example-token";
using var client = new HttpClient();
using var request = new HttpRequestMessage(
    HttpMethod.Patch,
    "https://api.example.com/projects/42");
request.Headers.Authorization =
    new AuthenticationHeaderValue("Bearer", token);
request.Content = JsonContent.Create(new { archived = true });

using var response = await client.SendAsync(request);
response.EnsureSuccessStatusCode();`,
  explanation: "HttpRequestMessage provides control over PATCH, the authorization header, and the JSON body before SendAsync transmits it.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.sendasync",
});

add({
  name: "HttpResponseMessage.EnsureSuccessStatusCode",
  description: "Require an HTTP success status.",
  definition: "Returns the same response when its status code is in the 200–299 range. Otherwise it throws HttpRequestException, preventing later code from silently treating an error response as successful data.",
  moduleName: "System.Net.Http",
  action: "Test",
  object: "HttpResponse",
  template: "{response}.EnsureSuccessStatusCode();",
  parameters: [parameter("response", "response", "The HTTP response to validate.", "HttpResponseMessage")],
  title: "Fail before parsing an error page",
  code: String.raw`using var client = new HttpClient();
var endpoint = "https://example.com/";
using var response = await client.GetAsync(endpoint);
response.EnsureSuccessStatusCode();
var payload = await response.Content.ReadAsStringAsync();
Console.WriteLine($"Received {payload.Length:N0} characters.");`,
  explanation: "Only a 2xx response reaches body processing; an error status throws with HTTP context.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.net.http.httpresponsemessage.ensuresuccessstatuscode",
});

// Dates and times
add({
  name: "DateTime.Now",
  description: "Get the current local date and time.",
  definition: "Returns a DateTime representing the computer's current local clock with Kind set to Local. Use UtcNow for storage, distributed systems, and comparisons that should not depend on a local time zone.",
  moduleName: "System.DateTime",
  action: "Get",
  object: "DateTime",
  template: "var now = DateTime.Now;",
  title: "Display a local timestamp",
  code: String.raw`var now = DateTime.Now;
Console.WriteLine(now.ToString("yyyy-MM-dd HH:mm:ss"));`,
  explanation: "Now reads the local system clock and ToString formats it for display.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.datetime.now",
});

add({
  name: "DateTime.UtcNow",
  description: "Get the current Coordinated Universal Time.",
  definition: "Returns the current clock time in UTC with Kind set to Utc. UTC timestamps avoid ambiguity between computers in different time zones, though DateTimeOffset is often better when an offset must be preserved.",
  moduleName: "System.DateTime",
  action: "Get",
  object: "DateTime",
  template: "var utcNow = DateTime.UtcNow;",
  title: "Create an unambiguous log timestamp",
  code: String.raw`var line = $"{DateTime.UtcNow:O} deployment started";
File.AppendAllText("deploy.log", line + Environment.NewLine);`,
  explanation: "The round-trip O format includes full precision and a UTC designator suitable for logs.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.datetime.utcnow",
});

add({
  name: "DateTime.Parse",
  description: "Parse date-and-time text or throw when invalid.",
  definition: "Interprets text using a culture and style rules and returns a DateTime. It throws FormatException for invalid text, so TryParse is preferable for ordinary user input.",
  moduleName: "System.DateTime",
  action: "Parse",
  object: "DateTime",
  template: "var date = DateTime.Parse({text}, CultureInfo.InvariantCulture);",
  parameters: [parameter("text", "text", "The date-and-time text to interpret.")],
  title: "Parse a controlled ISO date",
  code: String.raw`var releaseDate = DateTime.Parse(
    "2026-07-27",
    CultureInfo.InvariantCulture,
    DateTimeStyles.AssumeUniversal);
Console.WriteLine(releaseDate.Year);`,
  explanation: "A known machine-generated format is parsed with explicit invariant-culture rules.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.datetime.parse",
});

add({
  name: "DateTime.TryParse",
  description: "Try to parse date-and-time text without throwing for bad input.",
  definition: "Returns true and assigns the parsed DateTime when the text is valid under the selected culture and styles. Invalid text returns false and assigns the default DateTime value.",
  moduleName: "System.DateTime",
  action: "Parse",
  object: "DateTime",
  template: "var valid = DateTime.TryParse({text}, out var date);",
  parameters: [parameter("text", "text", "The date-and-time text to test.")],
  title: "Validate a user-entered deadline",
  code: String.raw`Console.Write("Deadline: ");
var input = Console.ReadLine();
if (DateTime.TryParse(input, out var deadline))
{
    Console.WriteLine($"Stored {deadline:d}");
}
else
{
    Console.WriteLine("Enter a valid date.");
}`,
  explanation: "Bad input follows the else branch instead of producing a FormatException.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.datetime.tryparse",
});

add({
  name: "DateTime.AddDays",
  description: "Create a DateTime shifted by a number of days.",
  definition: "Returns a new DateTime offset by the supplied whole or fractional day count. DateTime is immutable, so the original value remains unchanged.",
  moduleName: "System.DateTime",
  action: "Calculate",
  object: "DateTime",
  template: "var shifted = {date}.AddDays({days});",
  parameters: [
    parameter("date", "date", "The original DateTime value.", "DateTime"),
    parameter("days", "days", "Positive or negative days to add.", "double"),
  ],
  title: "Calculate a seven-day expiration",
  code: String.raw`var createdUtc = DateTime.UtcNow;
var expiresUtc = createdUtc.AddDays(7);
Console.WriteLine($"Expires: {expiresUtc:O}");`,
  explanation: "expiresUtc is seven days later, while createdUtc still holds the original timestamp.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.datetime.adddays",
});

// Regular expressions
add({
  name: "Regex.IsMatch",
  description: "Test whether text contains a regular-expression match.",
  definition: "Returns true when the regex engine finds at least one match for the pattern. Invalid patterns throw ArgumentException, and timeouts should be considered for patterns applied to untrusted input.",
  moduleName: "System.Text.RegularExpressions",
  action: "Test",
  object: "Regex",
  template: "var matches = Regex.IsMatch({input}, {pattern});",
  parameters: [
    parameter("input", "input", "The text to inspect."),
    parameter("pattern", "pattern", "The regular-expression pattern."),
  ],
  title: "Validate a simple identifier",
  code: String.raw`var identifier = "project_alpha-2";
var valid = Regex.IsMatch(
    identifier,
    @"^[A-Za-z][A-Za-z0-9_-]*$");
Console.WriteLine(valid);`,
  explanation: "The anchors require the entire string to match, the first character must be a letter, and later characters may include digits, underscore, or hyphen.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.regularexpressions.regex.ismatch",
});

add({
  name: "Regex.Match",
  description: "Find the first regex match and capture its groups.",
  definition: "Searches input and returns a Match object describing the first occurrence, including its text, position, length, and named or numbered capture groups. Match.Success indicates whether anything matched.",
  moduleName: "System.Text.RegularExpressions",
  action: "Match",
  object: "Regex",
  template: "var match = Regex.Match({input}, {pattern});",
  parameters: [
    parameter("input", "input", "The text to search."),
    parameter("pattern", "pattern", "The regular-expression pattern."),
  ],
  title: "Extract a key and numeric value",
  code: String.raw`var match = Regex.Match(
    "timeout=30",
    @"^(?<key>\w+)=(?<value>\d+)$");
if (match.Success)
{
    Console.WriteLine($"{match.Groups["key"].Value}: {match.Groups["value"].Value}");
}`,
  explanation: "Named groups capture timeout separately from 30 after the complete input matches.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.regularexpressions.regex.match",
});

add({
  name: "Regex.Replace",
  description: "Replace every substring matching a pattern.",
  definition: "Returns new text in which regex matches are replaced by fixed text or by values computed in a MatchEvaluator. The original input string is unchanged.",
  moduleName: "System.Text.RegularExpressions",
  action: "Replace",
  object: "Regex",
  template: "var result = Regex.Replace({input}, {pattern}, {replacement});",
  parameters: [
    parameter("input", "input", "The original text."),
    parameter("pattern", "pattern", "The regular-expression pattern."),
    parameter("replacement", "replacement", "Replacement text or evaluator.", "string"),
  ],
  title: "Collapse repeated whitespace",
  code: String.raw`var raw = "alpha   beta\t\tgamma";
var normalized = Regex.Replace(raw, @"\s+", " ");
Console.WriteLine(normalized);`,
  explanation: "Every run of one or more whitespace characters becomes exactly one space.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.regularexpressions.regex.replace",
});

add({
  name: "Regex.Split",
  description: "Split text wherever a regex pattern matches.",
  definition: "Returns an array of text segments separated by pattern matches. Capturing groups in the pattern can also appear in the returned array, which differs from a simple string delimiter split.",
  moduleName: "System.Text.RegularExpressions",
  action: "Split",
  object: "Regex",
  template: "var parts = Regex.Split({input}, {pattern});",
  parameters: [
    parameter("input", "input", "The text to divide."),
    parameter("pattern", "pattern", "The delimiter pattern."),
  ],
  title: "Split on commas or semicolons",
  code: String.raw`var input = "alpha, beta;gamma";
var parts = Regex.Split(input, @"\s*[,;]\s*");
Console.WriteLine(string.Join("|", parts));`,
  explanation: "The pattern accepts either delimiter and consumes optional surrounding whitespace, producing alpha|beta|gamma.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.text.regularexpressions.regex.split",
});

// Tasks and asynchronous coordination
add({
  name: "Task.Delay",
  description: "Asynchronously wait for a duration.",
  definition: "Returns a Task that completes after the requested interval without blocking a thread during the wait. A CancellationToken overload can end the delay early.",
  moduleName: "System.Threading.Tasks",
  action: "Wait",
  object: "Task",
  template: "await Task.Delay({milliseconds});",
  parameters: [parameter("milliseconds", "milliseconds", "The delay duration in milliseconds.", "int")],
  title: "Retry after a short backoff",
  code: String.raw`async Task<bool> TryConnectAsync(int attempt)
{
    await Task.Delay(10);
    return attempt >= 2;
}

for (var attempt = 1; attempt <= 3; attempt++)
{
    if (await TryConnectAsync(attempt))
    {
        break;
    }

    await Task.Delay(TimeSpan.FromSeconds(attempt));
}`,
  explanation: "Each failed attempt awaits a progressively longer delay without tying up a worker thread.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.delay",
});

add({
  name: "Task.WhenAll",
  description: "Wait asynchronously for every supplied task.",
  definition: "Returns one task that completes when all input tasks finish. Await produces all results for generic tasks and propagates failures after the group has completed.",
  moduleName: "System.Threading.Tasks",
  action: "Wait",
  object: "Task",
  template: "var results = await Task.WhenAll({tasks});",
  parameters: [parameter("tasks", "tasks", "The operations to await together.", "IEnumerable<Task<T>>")],
  title: "Download several pages concurrently",
  code: String.raw`using var client = new HttpClient();
var urls = new[]
{
    "https://example.com/",
    "https://example.com/?page=2",
    "https://example.com/?page=3"
};
var downloads = urls.Select(client.GetStringAsync);
var pages = await Task.WhenAll(downloads);
Console.WriteLine($"Downloaded {pages.Length} pages.");`,
  explanation: "All GET requests are started before WhenAll is awaited, allowing their I/O waits to overlap.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.whenall",
});

add({
  name: "Task.WhenAny",
  description: "Wait asynchronously until one supplied task finishes.",
  definition: "Returns a task whose result is the first input Task to reach a terminal state. Await the returned inner task again to obtain its value or observe its exception.",
  moduleName: "System.Threading.Tasks",
  action: "Wait",
  object: "Task",
  template: "var completed = await Task.WhenAny({tasks});",
  parameters: [parameter("tasks", "tasks", "The competing operations.", "IEnumerable<Task<T>>")],
  title: "Apply an operation timeout",
  code: String.raw`var operation = Task.Run(async () =>
{
    await Task.Delay(25);
    return new[] { "Console", "File", "Task" };
});
var timeout = Task.Delay(TimeSpan.FromSeconds(5));
var completed = await Task.WhenAny(operation, timeout);
if (completed != operation)
{
    throw new TimeoutException("Catalog load timed out.");
}
var catalog = await operation;`,
  explanation: "WhenAny identifies which task completed first; the operation is awaited again to retrieve its result and propagate failures.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.whenany",
});

add({
  name: "Task.Run",
  description: "Schedule CPU-bound work on the thread pool.",
  definition: "Queues a delegate to run on a thread-pool thread and returns a Task representing that work. It can keep a UI responsive during CPU-bound work, but it does not make synchronous I/O truly asynchronous.",
  moduleName: "System.Threading.Tasks",
  action: "Run",
  object: "Task",
  template: "var result = await Task.Run({function});",
  parameters: [parameter("function", "function", "The CPU-bound delegate to execute.", "Func<T>")],
  title: "Calculate a hash away from the UI thread",
  code: String.raw`var data = Encoding.UTF8.GetBytes("Hypershell");
var hash = await Task.Run(() =>
{
    return Convert.ToHexString(SHA256.HashData(data));
});
var statusText = $"SHA-256: {hash}";
Console.WriteLine(statusText);`,
  explanation: "The hashing work runs on a thread-pool thread, and the awaiting UI code resumes with the result.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.run",
});

// Processes, environment, identifiers, and numeric utilities
add({
  name: "Process.Start",
  description: "Start a process or open a shell-associated resource.",
  definition: "Creates a new operating-system process from a ProcessStartInfo and returns a Process object when one is available. UseShellExecute controls whether the OS shell may open documents and URLs instead of directly launching an executable.",
  moduleName: "System.Diagnostics",
  action: "Start",
  object: "Process",
  template: "using var process = Process.Start({startInfo});",
  parameters: [parameter("startInfo", "startInfo", "The executable, arguments, shell behavior, and startup settings.", "ProcessStartInfo")],
  title: "Run an executable with a safe argument list",
  code: String.raw`var startInfo = new ProcessStartInfo
{
    FileName = "dotnet",
    UseShellExecute = false
};
startInfo.ArgumentList.Add("--info");

using var process = Process.Start(startInfo)
    ?? throw new InvalidOperationException("Process did not start.");
await process.WaitForExitAsync();`,
  explanation: "ArgumentList avoids hand-built command-line quoting, and the code asynchronously waits for the launched process to exit.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.diagnostics.process.start",
});

add({
  name: "Process.GetProcesses",
  description: "Get processes running on the local computer.",
  definition: "Returns Process components representing the processes visible to the caller at that moment. Process state can change immediately, and each returned component should be disposed after use.",
  moduleName: "System.Diagnostics",
  action: "Get",
  object: "Process",
  template: "var processes = Process.GetProcesses();",
  title: "Display the five largest working sets",
  code: String.raw`var processes = Process.GetProcesses();
try
{
    foreach (var process in processes
        .OrderByDescending(item => item.WorkingSet64)
        .Take(5))
    {
        Console.WriteLine($"{process.ProcessName}: {process.WorkingSet64:N0}");
    }
}
finally
{
    foreach (var process in processes) process.Dispose();
}`,
  explanation: "The snapshot is ordered by memory use, and finally disposes every Process wrapper even if reading one property fails.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.diagnostics.process.getprocesses",
});

add({
  name: "Process.GetCurrentProcess",
  description: "Get a Process component for the running application.",
  definition: "Returns a Process object representing the caller's own operating-system process. It exposes the process ID, memory metrics, start time, threads, and other available runtime information.",
  moduleName: "System.Diagnostics",
  action: "Get",
  object: "Process",
  template: "using var current = Process.GetCurrentProcess();",
  title: "Report this application's process ID",
  code: String.raw`using var current = Process.GetCurrentProcess();
Console.WriteLine(
    $"PID {current.Id}; working set {current.WorkingSet64:N0} bytes");`,
  explanation: "The Process wrapper reads information about the application that is executing this code, then is disposed.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.diagnostics.process.getcurrentprocess",
});

add({
  name: "Environment.GetEnvironmentVariable",
  description: "Read an environment-variable value.",
  definition: "Looks up a named variable in the current process environment and returns its string value. It returns null when the name is not defined; target overloads can inspect user or machine variables on supported systems.",
  moduleName: "System.Environment",
  action: "Get",
  object: "Environment",
  template: "var value = Environment.GetEnvironmentVariable({name});",
  parameters: [parameter("name", "name", "The environment-variable name.")],
  title: "Read an optional API endpoint",
  code: String.raw`var endpoint = Environment.GetEnvironmentVariable("APP_API_URL");
if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var apiUri))
{
    apiUri = new Uri("https://api.example.com");
}`,
  explanation: "A missing or invalid variable falls back to a known absolute URI.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.environment.getenvironmentvariable",
});

add({
  name: "Environment.SetEnvironmentVariable",
  description: "Set or remove an environment-variable value.",
  definition: "Changes an environment variable for the current process by default; user and machine targets are also available where supported and permitted. Passing null removes the variable from the selected target.",
  moduleName: "System.Environment",
  action: "Write",
  object: "Environment",
  template: "Environment.SetEnvironmentVariable({name}, {value});",
  parameters: [
    parameter("name", "name", "The environment-variable name."),
    parameter("value", "value", "The new value, or null to remove it.", "string?"),
  ],
  title: "Configure a child process through its own environment",
  code: String.raw`var startInfo = new ProcessStartInfo("worker.exe")
{
    UseShellExecute = false
};
startInfo.Environment["APP_MODE"] = "Development";
using var worker = Process.Start(startInfo);`,
  explanation: "For child-process configuration, editing ProcessStartInfo.Environment scopes the value to that child instead of changing the whole current process.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.environment.setenvironmentvariable",
});

add({
  name: "Guid.NewGuid",
  description: "Create a new random GUID.",
  definition: "Returns a new 128-bit identifier with an extremely low collision probability. It is useful for identifiers, not as proof of identity or as a secret.",
  moduleName: "System.Guid",
  action: "Create",
  object: "Guid",
  template: "var id = Guid.NewGuid();",
  title: "Assign an ID to a new record",
  code: String.raw`var record = new
{
    Id = Guid.NewGuid(),
    Name = "Hypershell",
    CreatedUtc = DateTime.UtcNow
};
Console.WriteLine(record.Id);`,
  explanation: "The record receives a new identifier independently of its name and creation time.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.guid.newguid",
});

add({
  name: "Guid.TryParse",
  description: "Try to parse GUID text without throwing for bad input.",
  definition: "Returns true and assigns the parsed Guid when the text has a recognized GUID format. Invalid or missing text returns false and assigns Guid.Empty.",
  moduleName: "System.Guid",
  action: "Parse",
  object: "Guid",
  template: "var valid = Guid.TryParse({text}, out var id);",
  parameters: [parameter("text", "text", "The candidate GUID text.", "string?")],
  title: "Validate an ID from a route",
  code: String.raw`var routeValue = "6f9619ff-8b86-d011-b42d-00cf4fc964ff";
if (!Guid.TryParse(routeValue, out var projectId))
{
    Console.WriteLine("Project ID is not a valid GUID.");
}
else
{
    Console.WriteLine($"Validated project ID: {projectId}");
}
`,
  explanation: "Invalid route text takes the error branch; only a successfully parsed Guid is displayed as a validated ID.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.guid.tryparse",
});

add({
  name: "Convert.ToInt32",
  description: "Convert a compatible value to a 32-bit integer.",
  definition: "Converts supported primitive or string values into Int32 using defined rounding and culture behavior. Invalid text, overflow, or unsupported values can throw; int.TryParse is safer for untrusted text.",
  moduleName: "System.Convert",
  action: "Convert",
  object: "Number",
  template: "var number = Convert.ToInt32({value});",
  parameters: [parameter("value", "value", "The compatible value to convert.", "object")],
  title: "Convert a Boolean setting to a numeric flag",
  code: String.raw`var enabled = true;
var databaseFlag = Convert.ToInt32(enabled);
Console.WriteLine(databaseFlag);`,
  explanation: "Convert maps true to 1 and false to 0. User-entered numeric text should normally use int.TryParse instead.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.convert.toint32",
});

add({
  name: "Convert.ToBase64String",
  description: "Encode bytes as Base64 text.",
  definition: "Converts binary bytes into a Base64 string made from a restricted text alphabet. The encoding is reversible and transport-friendly but provides no secrecy.",
  moduleName: "System.Convert",
  action: "Convert",
  object: "Base64",
  template: "var encoded = Convert.ToBase64String({bytes});",
  parameters: [parameter("bytes", "bytes", "The binary data to encode.", "byte[]")],
  title: "Embed a small binary value in JSON",
  code: String.raw`var bytes = await File.ReadAllBytesAsync("icon.png");
var payload = new
{
    fileName = "icon.png",
    base64 = Convert.ToBase64String(bytes)
};
var json = JsonSerializer.Serialize(payload);`,
  explanation: "The image bytes become ordinary text that JSON can carry; a receiver must decode them to recover the image.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.convert.tobase64string",
});

add({
  name: "Convert.FromBase64String",
  description: "Decode Base64 text into bytes.",
  definition: "Reconstructs the original byte array from valid Base64 text, ignoring permitted whitespace. Invalid alphabet, padding, or length produces FormatException.",
  moduleName: "System.Convert",
  action: "Convert",
  object: "Base64",
  template: "var bytes = Convert.FromBase64String({encoded});",
  parameters: [parameter("encoded", "encoded", "The Base64 text to decode.")],
  title: "Restore an encoded attachment",
  code: String.raw`var payload = new
{
    Base64 = "SHlwZXJzaGVsbA==",
    FileName = Path.Combine(Path.GetTempPath(), "hypershell-attachment.txt")
};
try
{
    var bytes = Convert.FromBase64String(payload.Base64);
    await File.WriteAllBytesAsync(payload.FileName, bytes);
}
catch (FormatException)
{
    Console.WriteLine("Attachment data is not valid Base64.");
}`,
  explanation: "Valid text is decoded and written as bytes; malformed input is handled explicitly.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.convert.frombase64string",
});

add({
  name: "Math.Round",
  description: "Round a floating-point or decimal value.",
  definition: "Returns the nearest value at the requested number of fractional digits. The overload's MidpointRounding choice determines how exact halfway values are handled.",
  moduleName: "System.Math",
  action: "Calculate",
  object: "Number",
  template: "var rounded = Math.Round({value}, {digits}, MidpointRounding.AwayFromZero);",
  parameters: [
    parameter("value", "value", "The value to round.", "decimal"),
    parameter("digits", "digits", "The number of fractional digits to retain.", "int"),
  ],
  title: "Round currency with an explicit midpoint rule",
  code: String.raw`decimal unitPrice = 10.125m;
var displayPrice = Math.Round(
    unitPrice,
    2,
    MidpointRounding.AwayFromZero);
Console.WriteLine(displayPrice);`,
  explanation: "The explicit rule turns the midpoint into 10.13 instead of relying on the default banker's rounding behavior.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.math.round",
});

add({
  name: "Math.Clamp",
  description: "Restrict a comparable value to a minimum and maximum.",
  definition: "Returns the minimum when the value is too small, the maximum when it is too large, and the value itself when it is inside the range. It throws when the maximum is less than the minimum.",
  moduleName: "System.Math",
  action: "Calculate",
  object: "Number",
  template: "var clamped = Math.Clamp({value}, {min}, {max});",
  parameters: [
    parameter("value", "value", "The value to constrain.", "int"),
    parameter("min", "min", "The inclusive lower limit.", "int"),
    parameter("max", "max", "The inclusive upper limit.", "int"),
  ],
  title: "Constrain an opacity percentage",
  code: String.raw`var requestedOpacity = 135;
var opacityPercent = Math.Clamp(requestedOpacity, 0, 100);
var opacity = opacityPercent / 100.0;`,
  explanation: "The out-of-range request becomes 100, then converts to an opacity value of 1.0.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.math.clamp",
});

add({
  name: "Random.Next",
  description: "Generate a pseudo-random integer in a range.",
  definition: "Returns an integer greater than or equal to the minimum and strictly less than the maximum. Random.Shared is convenient and thread-safe for ordinary use; security-sensitive values require RandomNumberGenerator.",
  moduleName: "System.Random",
  action: "Create",
  object: "Random",
  template: "var number = Random.Shared.Next({minValue}, {maxValue});",
  parameters: [
    parameter("minValue", "minValue", "The inclusive lower bound.", "int"),
    parameter("maxValue", "maxValue", "The exclusive upper bound.", "int"),
  ],
  title: "Choose a random list item",
  code: String.raw`var colors = new[] { "blue", "orange", "green" };
var index = Random.Shared.Next(0, colors.Length);
Console.WriteLine(colors[index]);`,
  explanation: "The exclusive upper bound guarantees the returned index is valid for the array.",
  documentationUrl: "https://learn.microsoft.com/dotnet/api/system.random.next",
});

// __MORE_COMMANDS__

const library = {
  schemaVersion: 1,
  contentVersion: "1.1.0",
  id: "csharp-basics",
  displayName: "C# and .NET essentials",
  description:
    "Curated, example-driven C# and .NET APIs for everyday application, automation, data, and systems work. No additional packages are required.",
  language: "C#",
  category: "Essentials",
  kind: "Extension",
  parentLibraryId: "csharp-dotnet-8",
  frameworkName: ".NET",
  frameworkVersion: "8",
  targetFramework: "net8.0",
  isExecutionSupported: true,
  executionSupportMessage:
    "Compatible with the built-in .NET 10 C# runner; authored against .NET 8.",
  isStarterLibrary: true,
  createStandaloneProfile: true,
  keywords: ["csharp", "dotnet", "bcl", "standard library", "essentials"],
  executionTarget: "CSharp",
  actionDescriptions,
  objectDescriptions,
  commands,
};

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const outputPath = path.resolve(
  scriptDirectory,
  "..",
  "src",
  "Hypershell",
  "Assets",
  "CommandLibraries",
  "csharp.json",
);
fs.writeFileSync(outputPath, `${JSON.stringify(library, null, 2)}\n`, "utf8");
console.log(
  `Generated ${commands.length} C# commands across ${new Set(commands.map((command) => command.moduleName)).size} API groups.`,
);
