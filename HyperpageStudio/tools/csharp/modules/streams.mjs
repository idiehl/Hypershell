import {
  command,
  createLibrary,
  example,
  parameter,
} from "../catalog-utils.mjs";

const commands = [
  command({
    name: "File.ReadAllBytesAsync",
    description: "Read an entire file into a byte array asynchronously.",
    definition:
      "Opens a file, asynchronously reads all of its bytes, closes the file, and returns the completed byte array. Large files are usually better processed as streams.",
    moduleName: "System.IO.File",
    action: "Read",
    object: "Binary file",
    template: "var bytes = await File.ReadAllBytesAsync({path});",
    parameters: [
      parameter("path", "path", "The file path to read."),
    ],
    examples: [
      example(
        "Calculate a binary file size",
        String.raw`var bytes = await File.ReadAllBytesAsync("icon.png");
Console.WriteLine($"Loaded {bytes.Length:N0} bytes.");`,
        "The complete file is available in memory after the awaited operation finishes.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.file.readallbytesasync",
    keywords: ["bytes", "binary", "file", "async"],
  }),
  command({
    name: "File.WriteAllBytesAsync",
    description: "Write a complete byte array to a file asynchronously.",
    definition:
      "Creates or overwrites a file, asynchronously writes the supplied bytes, and closes the file when the operation completes.",
    moduleName: "System.IO.File",
    action: "Write",
    object: "Binary file",
    template: "await File.WriteAllBytesAsync({path}, {bytes});",
    parameters: [
      parameter("path", "path", "The destination file path."),
      parameter("bytes", "bytes", "The byte array or compatible memory to write.", "byte[]"),
    ],
    examples: [
      example(
        "Persist decoded data",
        String.raw`var bytes = Convert.FromBase64String("SHlwZXJzaGVsbA==");
await File.WriteAllBytesAsync("payload.bin", bytes);`,
        "The decoded bytes replace payload.bin, creating it when necessary.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.file.writeallbytesasync",
    keywords: ["bytes", "binary", "file", "async"],
  }),
  command({
    name: "File.OpenRead",
    description: "Open an existing file as a read-only stream.",
    definition:
      "Opens a FileStream for reading with sharing that permits other readers. The returned stream must be disposed.",
    moduleName: "System.IO.File",
    action: "Read",
    object: "File stream",
    template: "using var stream = File.OpenRead({path});",
    parameters: [
      parameter("path", "path", "The existing file path to open."),
    ],
    examples: [
      example(
        "Read a file header",
        String.raw`using var stream = File.OpenRead("payload.bin");
var header = new byte[Math.Min(16, stream.Length)];
await stream.ReadExactlyAsync(header);
Console.WriteLine(Convert.ToHexString(header));`,
        "Only the first bytes are loaded, and the using declaration closes the file reliably.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.file.openread",
    keywords: ["FileStream", "stream", "open", "read"],
  }),
  command({
    name: "File.OpenWrite",
    description: "Open or create a file as a writable stream.",
    definition:
      "Opens an existing file or creates a new one for writing. The stream begins at position zero but does not automatically truncate existing trailing content.",
    moduleName: "System.IO.File",
    action: "Write",
    object: "File stream",
    template: "using var stream = File.OpenWrite({path});",
    parameters: [
      parameter("path", "path", "The file path to open or create."),
    ],
    examples: [
      example(
        "Write a compact binary header",
        String.raw`using var stream = File.OpenWrite("payload.bin");
stream.SetLength(0);
await stream.WriteAsync(new byte[] { 0x48, 0x59, 0x50, 0x45, 0x52 });`,
        "SetLength removes old trailing bytes before the new header is written.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.file.openwrite",
    keywords: ["FileStream", "stream", "open", "write"],
  }),
  command({
    name: "Stream.CopyToAsync",
    description: "Asynchronously copy all remaining bytes between streams.",
    definition:
      "Reads from the source stream's current position and writes to the destination until the source reaches its end. Neither stream is disposed by the operation.",
    moduleName: "System.IO.Stream",
    action: "Copy",
    object: "Stream",
    template: "await {source}.CopyToAsync({destination});",
    parameters: [
      parameter("source", "source", "A readable source stream.", "Stream"),
      parameter("destination", "destination", "A writable destination stream.", "Stream"),
    ],
    examples: [
      example(
        "Copy one file to another through streams",
        String.raw`await using var source = File.OpenRead("input.bin");
await using var destination = File.Create("copy.bin");
await source.CopyToAsync(destination);`,
        "The copy is streamed instead of loading the complete input into memory.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.stream.copytoasync",
    keywords: ["stream", "copy", "bytes", "async"],
  }),
  command({
    name: "Stream.ReadExactlyAsync",
    description: "Fill a buffer asynchronously or fail at end of stream.",
    definition:
      "Repeatedly reads until the requested buffer is full. It throws EndOfStreamException when the source ends before all requested bytes arrive.",
    moduleName: "System.IO.Stream",
    action: "Read",
    object: "Stream",
    template: "await {stream}.ReadExactlyAsync({buffer});",
    parameters: [
      parameter("stream", "stream", "A readable stream.", "Stream"),
      parameter("buffer", "buffer", "The buffer that must be completely filled.", "Memory<byte>"),
    ],
    examples: [
      example(
        "Read a fixed-size protocol header",
        String.raw`await using var stream = File.OpenRead("packet.bin");
var header = new byte[8];
await stream.ReadExactlyAsync(header);
Console.WriteLine(Convert.ToHexString(header));`,
        "The call either fills all eight bytes or reports a truncated input instead of returning a partial header.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.stream.readexactlyasync",
    keywords: ["stream", "buffer", "exact", "protocol"],
  }),
  command({
    name: "StreamReader.ReadLineAsync",
    description: "Asynchronously read the next text line.",
    definition:
      "Reads characters through the next line terminator and returns the line without that terminator. It returns null after the final line.",
    moduleName: "System.IO.StreamReader",
    action: "Read",
    object: "Text stream",
    template: "var line = await {reader}.ReadLineAsync();",
    parameters: [
      parameter("reader", "reader", "An open StreamReader.", "StreamReader"),
    ],
    examples: [
      example(
        "Process a text file line by line",
        String.raw`using var reader = File.OpenText("events.log");
while (await reader.ReadLineAsync() is { } line)
{
    if (!string.IsNullOrWhiteSpace(line))
    {
        Console.WriteLine(line);
    }
}`,
        "Only one line is retained at a time, and the null pattern ends the loop at end of file.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.streamreader.readlineasync",
    keywords: ["StreamReader", "line", "text", "async"],
  }),
  command({
    name: "StreamReader.ReadToEndAsync",
    description: "Asynchronously read all remaining text.",
    definition:
      "Reads from the current position through the end of the text stream and returns one string. Streaming is preferable when the remaining content may be large.",
    moduleName: "System.IO.StreamReader",
    action: "Read",
    object: "Text stream",
    template: "var text = await {reader}.ReadToEndAsync();",
    parameters: [
      parameter("reader", "reader", "An open StreamReader.", "StreamReader"),
    ],
    examples: [
      example(
        "Decode UTF-8 text from a stream",
        String.raw`await using var stream = File.OpenRead("notes.txt");
using var reader = new StreamReader(stream, Encoding.UTF8);
var text = await reader.ReadToEndAsync();
Console.WriteLine(text);`,
        "StreamReader handles byte-to-character decoding while the awaited call collects the remaining characters.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.streamreader.readtoendasync",
    keywords: ["StreamReader", "text", "async", "encoding"],
  }),
  command({
    name: "StreamWriter.WriteLineAsync",
    description: "Asynchronously write text followed by a line terminator.",
    definition:
      "Writes the supplied text and the writer's line terminator without synchronously blocking for the underlying destination.",
    moduleName: "System.IO.StreamWriter",
    action: "Write",
    object: "Text stream",
    template: "await {writer}.WriteLineAsync({value});",
    parameters: [
      parameter("writer", "writer", "An open StreamWriter.", "StreamWriter"),
      parameter("value", "value", "The text to write.", "string?"),
    ],
    examples: [
      example(
        "Append a timestamped log line",
        String.raw`await using var writer = new StreamWriter("events.log", append: true);
await writer.WriteLineAsync($"{DateTimeOffset.UtcNow:O} build completed");`,
        "Disposing the writer flushes its encoding buffer after the asynchronous write.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.streamwriter.writelineasync",
    keywords: ["StreamWriter", "line", "text", "async"],
  }),
  command({
    name: "MemoryStream.ToArray",
    description: "Copy a memory stream's contents into a byte array.",
    definition:
      "Returns a new byte array containing the bytes written to the stream, regardless of its current Position. The returned array is independent of the stream.",
    moduleName: "System.IO.MemoryStream",
    action: "Convert",
    object: "Memory stream",
    template: "var bytes = {stream}.ToArray();",
    parameters: [
      parameter("stream", "stream", "A MemoryStream instance.", "MemoryStream"),
    ],
    examples: [
      example(
        "Build a binary payload in memory",
        String.raw`using var stream = new MemoryStream();
await stream.WriteAsync(Encoding.UTF8.GetBytes("Hypershell"));
var payload = stream.ToArray();
Console.WriteLine(Convert.ToBase64String(payload));`,
        "ToArray returns every written byte even though the stream position is at its end.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.memorystream.toarray",
    keywords: ["MemoryStream", "buffer", "byte array", "in memory"],
  }),
  command({
    name: "BinaryReader.ReadInt32",
    description: "Read a little-endian 32-bit integer from a stream.",
    definition:
      "Reads four bytes from the underlying stream and interprets them as a signed Int32 using BinaryReader's fixed binary representation.",
    moduleName: "System.IO.BinaryReader",
    action: "Read",
    object: "Binary value",
    template: "var number = {reader}.ReadInt32();",
    parameters: [
      parameter("reader", "reader", "An open BinaryReader.", "BinaryReader"),
    ],
    examples: [
      example(
        "Read a binary record count",
        String.raw`using var stream = new MemoryStream(
    new byte[] { 0x03, 0x00, 0x00, 0x00 });
using var reader = new BinaryReader(stream);
Console.WriteLine(reader.ReadInt32());`,
        "The four little-endian bytes are interpreted as the integer 3.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.binaryreader.readint32",
    keywords: ["BinaryReader", "int", "binary", "little endian"],
  }),
  command({
    name: "BinaryWriter.Write",
    description: "Write a typed primitive or string in binary form.",
    definition:
      "Writes a supported primitive, byte sequence, character sequence, or string to the underlying stream using BinaryWriter's documented representation.",
    moduleName: "System.IO.BinaryWriter",
    action: "Write",
    object: "Binary value",
    template: "{writer}.Write({value});",
    parameters: [
      parameter("writer", "writer", "An open BinaryWriter.", "BinaryWriter"),
      parameter("value", "value", "The strongly typed value to encode.", "C# expression"),
    ],
    examples: [
      example(
        "Create a small binary record",
        String.raw`using var stream = new MemoryStream();
using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
{
    writer.Write(3);
    writer.Write("Hypershell");
}
Console.WriteLine(Convert.ToHexString(stream.ToArray()));`,
        "The writer encodes the integer and length-prefixed string into the same memory stream.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.binarywriter.write",
    keywords: ["BinaryWriter", "binary", "primitive", "serialize"],
  }),
  command({
    name: "ZipFile.CreateFromDirectory",
    description: "Create a ZIP archive from a directory tree.",
    definition:
      "Recursively compresses a source directory into a new ZIP archive. The destination archive must not already exist unless the caller removes or replaces it first.",
    moduleName: "System.IO.Compression.ZipFile",
    action: "Compress",
    object: "Directory archive",
    template: "ZipFile.CreateFromDirectory({sourceDirectory}, {archivePath});",
    parameters: [
      parameter("sourceDirectory", "sourceDirectory", "The directory whose contents will be archived."),
      parameter("archivePath", "archivePath", "The new ZIP file path."),
    ],
    examples: [
      example(
        "Archive a build output directory",
        String.raw`var archivePath = "release.zip";
if (File.Exists(archivePath)) File.Delete(archivePath);
ZipFile.CreateFromDirectory("bin/Release", archivePath);
Console.WriteLine(Path.GetFullPath(archivePath));`,
        "An existing archive is removed explicitly before the directory tree is compressed.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.compression.zipfile.createfromdirectory",
    keywords: ["zip", "archive", "directory", "compression"],
  }),
  command({
    name: "ZipFile.ExtractToDirectory",
    description: "Extract a ZIP archive into a directory.",
    definition:
      "Expands archive entries beneath a destination directory. An overload can overwrite existing files; untrusted archives require additional policy around paths, size, and content.",
    moduleName: "System.IO.Compression.ZipFile",
    action: "Decompress",
    object: "Directory archive",
    template: "ZipFile.ExtractToDirectory({archivePath}, {destinationDirectory});",
    parameters: [
      parameter("archivePath", "archivePath", "The ZIP file to extract."),
      parameter("destinationDirectory", "destinationDirectory", "The output directory."),
    ],
    examples: [
      example(
        "Extract into a fresh temporary directory",
        String.raw`var destination = Path.Combine(
    Path.GetTempPath(),
    $"hypershell-{Guid.NewGuid():N}");
ZipFile.ExtractToDirectory("release.zip", destination);
Console.WriteLine(destination);`,
        "A unique destination avoids collisions with files from an earlier extraction.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.compression.zipfile.extracttodirectory",
    keywords: ["zip", "archive", "extract", "decompression"],
  }),
  command({
    name: "GZipStream.Compress",
    description: "Compress one stream into GZip format.",
    definition:
      "A GZipStream in Compress mode writes a GZip-formatted byte stream to its destination. Disposing it finalizes the compression footer.",
    moduleName: "System.IO.Compression.GZipStream",
    action: "Compress",
    object: "Stream",
    template:
      "await using var gzip = new GZipStream({destination}, CompressionLevel.Optimal, leaveOpen: true);\nawait {source}.CopyToAsync(gzip);",
    parameters: [
      parameter("destination", "destination", "The writable destination stream.", "Stream"),
      parameter("source", "source", "The readable uncompressed stream.", "Stream"),
    ],
    examples: [
      example(
        "Compress a file",
        String.raw`await using var source = File.OpenRead("events.log");
await using var destination = File.Create("events.log.gz");
await using (var gzip = new GZipStream(
    destination,
    CompressionLevel.Optimal,
    leaveOpen: true))
{
    await source.CopyToAsync(gzip);
}`,
        "The nested using scope finalizes GZip metadata before the destination file is closed.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.compression.gzipstream",
    keywords: ["gzip", "compression", "stream"],
  }),
  command({
    name: "GZipStream.Decompress",
    description: "Decompress a GZip stream.",
    definition:
      "A GZipStream in Decompress mode exposes the uncompressed bytes read from an underlying GZip-formatted source.",
    moduleName: "System.IO.Compression.GZipStream",
    action: "Decompress",
    object: "Stream",
    template:
      "await using var gzip = new GZipStream({source}, CompressionMode.Decompress, leaveOpen: true);\nawait gzip.CopyToAsync({destination});",
    parameters: [
      parameter("source", "source", "The readable GZip source stream.", "Stream"),
      parameter("destination", "destination", "The writable uncompressed destination stream.", "Stream"),
    ],
    examples: [
      example(
        "Restore a compressed file",
        String.raw`await using var source = File.OpenRead("events.log.gz");
await using var gzip = new GZipStream(
    source,
    CompressionMode.Decompress);
await using var destination = File.Create("events-restored.log");
await gzip.CopyToAsync(destination);`,
        "Copying from the decompression stream writes restored bytes to the destination.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.compression.gzipstream",
    keywords: ["gzip", "decompression", "stream"],
  }),
  command({
    name: "FileSystemWatcher.Create",
    description: "Watch a directory for selected file-system changes.",
    definition:
      "FileSystemWatcher raises events for matching changes reported by the operating system. Event bursts, duplicate events, buffer overflow, and race conditions must be handled by production consumers.",
    moduleName: "System.IO.FileSystemWatcher",
    action: "Create",
    object: "Directory watcher",
    template:
      "using var watcher = new FileSystemWatcher({path}, {filter})\n{\n    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,\n    EnableRaisingEvents = true\n};",
    parameters: [
      parameter("path", "path", "The directory to monitor."),
      parameter("filter", "filter", "A file-name filter such as *.json."),
    ],
    examples: [
      example(
        "Observe JSON changes briefly",
        String.raw`using var watcher = new FileSystemWatcher(".", "*.json")
{
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
};
watcher.Changed += (_, change) =>
    Console.WriteLine($"{change.ChangeType}: {change.Name}");
watcher.EnableRaisingEvents = true;
await Task.Delay(TimeSpan.FromSeconds(10));`,
        "The watcher remains alive for ten seconds; real applications normally tie its lifetime to a longer-running service.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher",
    keywords: ["watch", "directory", "file changes", "events"],
  }),
  command({
    name: "DriveInfo.GetDrives",
    description: "Get the logical drives visible to the current machine.",
    definition:
      "Returns DriveInfo objects for mounted or logical drives. Some properties can throw when a drive is unavailable or not ready.",
    moduleName: "System.IO.DriveInfo",
    action: "Get",
    object: "Drive",
    template: "var drives = DriveInfo.GetDrives();",
    examples: [
      example(
        "Report ready drive space",
        String.raw`foreach (var drive in DriveInfo.GetDrives().Where(item => item.IsReady))
{
    Console.WriteLine(
        $"{drive.Name} {drive.DriveFormat} {drive.AvailableFreeSpace:N0} bytes free");
}`,
        "IsReady is checked before querying format and capacity properties.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.driveinfo.getdrives",
    keywords: ["drive", "disk", "capacity", "storage"],
  }),
  command({
    name: "Path.GetRelativePath",
    description: "Calculate a relative path between two absolute path contexts.",
    definition:
      "Resolves both inputs and returns a path that reaches the target from the base path according to platform path-comparison rules.",
    moduleName: "System.IO.Path",
    action: "Format",
    object: "Path",
    template: "var relative = Path.GetRelativePath({relativeTo}, {path});",
    parameters: [
      parameter("relativeTo", "relativeTo", "The base directory path."),
      parameter("path", "path", "The target path."),
    ],
    examples: [
      example(
        "Create a project-relative label",
        String.raw`var root = Path.GetFullPath(".");
var file = Path.Combine(root, "src", "Hypershell", "App.xaml");
Console.WriteLine(Path.GetRelativePath(root, file));`,
        "The result describes the target beneath the project root without retaining the absolute prefix.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.path.getrelativepath",
    keywords: ["path", "relative", "filesystem"],
  }),
  command({
    name: "File.GetAttributes",
    description: "Read a file or directory's attribute flags.",
    definition:
      "Returns a bitwise FileAttributes value describing flags such as Directory, Hidden, ReadOnly, Archive, and ReparsePoint.",
    moduleName: "System.IO.File",
    action: "Get",
    object: "File attributes",
    template: "var attributes = File.GetAttributes({path});",
    parameters: [
      parameter("path", "path", "The file or directory path."),
    ],
    examples: [
      example(
        "Test whether a path is hidden",
        String.raw`var attributes = File.GetAttributes("settings.json");
var isHidden = attributes.HasFlag(FileAttributes.Hidden);
Console.WriteLine(isHidden);`,
        "FileAttributes is a flags enum, so HasFlag tests one bit without discarding the others.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.io.file.getattributes",
    keywords: ["attributes", "hidden", "readonly", "flags"],
  }),
];

export default createLibrary({
  id: "csharp-streams",
  displayName: ".NET streams and archives",
  description:
    "Dependency-free .NET 8 APIs for binary files, streams, readers and writers, compression, storage, and file-system observation.",
  category: "Files and storage",
  keywords: ["io", "streams", "binary", "compression", "filesystem"],
  objectDescriptions: {
    "Binary file":
      "A binary file stores bytes whose interpretation is defined by an image, archive, executable, protocol, or application-specific format.",
    "Binary value":
      "A binary value is a typed primitive or string represented as bytes in a documented byte order and encoding.",
    Directory:
      "A directory is a file-system container that can contain files and nested directories.",
    "Directory archive":
      "A directory archive stores a directory tree and its file contents inside one compressed container.",
    "Directory watcher":
      "A directory watcher subscribes to operating-system notifications for matching file-system changes.",
    Drive:
      "A drive represents a mounted or logical storage volume visible to the operating system.",
    "File attributes":
      "File attributes are bit flags describing file-system traits such as hidden, read-only, directory, archive, or reparse-point state.",
    "File stream":
      "A FileStream provides seekable or sequential byte access backed by an operating-system file handle.",
    "Memory stream":
      "A MemoryStream provides seekable byte access backed by managed memory instead of a file or network resource.",
    Path:
      "A path is text identifying a file-system location; Path helpers transform that text without opening the target.",
    Stream:
      "A Stream is the .NET abstraction for reading, writing, and seeking through a sequence of bytes.",
    "Text stream":
      "A text stream decodes or encodes characters over an underlying byte stream with a selected text encoding.",
  },
  commands,
});
