import {
  command,
  createLibrary,
  example,
  parameter,
} from "../catalog-utils.mjs";

const commands = [
  command({
    name: "Uri.TryCreate",
    description: "Try to parse and validate a URI.",
    definition:
      "Attempts to create a Uri with the requested relative or absolute form and returns false instead of throwing when the text is not valid for that form.",
    moduleName: "System.Uri",
    action: "Parse",
    object: "URI",
    template: "var valid = Uri.TryCreate({text}, {uriKind}, out var uri);",
    parameters: [
      parameter("text", "text", "The candidate URI text.", "string?"),
      parameter("uriKind", "uriKind", "UriKind.Absolute, Relative, or RelativeOrAbsolute.", "UriKind"),
    ],
    examples: [
      example(
        "Require an HTTPS address",
        String.raw`var input = "https://example.com/api";
var valid = Uri.TryCreate(input, UriKind.Absolute, out var uri)
    && uri.Scheme == Uri.UriSchemeHttps;
Console.WriteLine(valid);`,
        "Parsing and the explicit scheme check together reject relative and non-HTTPS values.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.uri.trycreate",
    keywords: ["URI", "URL", "parse", "address"],
  }),
  command({
    name: "UriBuilder.Create",
    description: "Build or modify a URI by components.",
    definition:
      "UriBuilder exposes scheme, host, port, path, query, and fragment as mutable components and produces a correctly assembled Uri.",
    moduleName: "System.UriBuilder",
    action: "Build",
    object: "URI",
    template:
      "var builder = new UriBuilder({baseUri})\n{\n    Path = {path},\n    Query = {query}\n};\nvar uri = builder.Uri;",
    parameters: [
      parameter("baseUri", "baseUri", "An absolute base URI."),
      parameter("path", "path", "The path component."),
      parameter("query", "query", "The query text without a leading question mark."),
    ],
    examples: [
      example(
        "Compose an API endpoint",
        String.raw`var builder = new UriBuilder("https://example.com")
{
    Path = "api/projects",
    Query = "status=active&page=2"
};
Console.WriteLine(builder.Uri);`,
        "UriBuilder adds separators between the scheme, host, path, and query components.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.uribuilder",
    keywords: ["URI", "URL", "builder", "query", "endpoint"],
  }),
  command({
    name: "WebUtility.UrlEncode",
    description: "Percent-encode text for use in a URL component.",
    definition:
      "Converts characters that are unsafe in a URL component into percent-encoded UTF-8 bytes. It does not by itself assemble or validate a complete URI.",
    moduleName: "System.Net.WebUtility",
    action: "Encode",
    object: "URL component",
    template: "var encoded = WebUtility.UrlEncode({value});",
    parameters: [
      parameter("value", "value", "The component text to encode.", "string?"),
    ],
    examples: [
      example(
        "Encode a query value",
        String.raw`var value = WebUtility.UrlEncode("C# streams & files");
var uri = new Uri($"https://example.com/search?q={value}");
Console.WriteLine(uri);`,
        "Spaces, the hash sign, and ampersand are encoded so they remain part of the q value.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.webutility.urlencode",
    keywords: ["URL", "percent encoding", "query", "escape"],
  }),
  command({
    name: "WebUtility.UrlDecode",
    description: "Decode percent-encoded URL component text.",
    definition:
      "Replaces percent-encoded byte sequences with their decoded characters and converts plus signs according to form-style URL encoding rules.",
    moduleName: "System.Net.WebUtility",
    action: "Decode",
    object: "URL component",
    template: "var decoded = WebUtility.UrlDecode({value});",
    parameters: [
      parameter("value", "value", "The encoded component text.", "string?"),
    ],
    examples: [
      example(
        "Decode a query value",
        String.raw`var decoded = WebUtility.UrlDecode("C%23+streams+%26+files");
Console.WriteLine(decoded);`,
        "The encoded sequence becomes readable C# streams & files text.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.webutility.urldecode",
    keywords: ["URL", "percent encoding", "decode", "unescape"],
  }),
  command({
    name: "WebUtility.HtmlEncode",
    description: "Encode text for safe placement in HTML text content.",
    definition:
      "Replaces characters with special HTML meaning with entity references. Context-specific encoding is still required for attributes, scripts, styles, and URLs.",
    moduleName: "System.Net.WebUtility",
    action: "Encode",
    object: "HTML text",
    template: "var encoded = WebUtility.HtmlEncode({value});",
    parameters: [
      parameter("value", "value", "Untrusted or literal text.", "string?"),
    ],
    examples: [
      example(
        "Encode a display label",
        String.raw`var encoded = WebUtility.HtmlEncode("<admin> & guests");
Console.WriteLine($"<p>{encoded}</p>");`,
        "Angle brackets and the ampersand are represented as text rather than being interpreted as markup.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.webutility.htmlencode",
    keywords: ["HTML", "encode", "escape", "XSS"],
  }),
  command({
    name: "WebUtility.HtmlDecode",
    description: "Decode HTML entity references into text.",
    definition:
      "Converts recognized named and numeric HTML entities back to their character values. Decoding does not sanitize untrusted markup.",
    moduleName: "System.Net.WebUtility",
    action: "Decode",
    object: "HTML text",
    template: "var decoded = WebUtility.HtmlDecode({value});",
    parameters: [
      parameter("value", "value", "HTML-encoded text.", "string?"),
    ],
    examples: [
      example(
        "Restore encoded display text",
        String.raw`var decoded = WebUtility.HtmlDecode(
    "&lt;admin&gt; &amp; guests");
Console.WriteLine(decoded);`,
        "Entity references become literal characters in the returned .NET string.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.webutility.htmldecode",
    keywords: ["HTML", "decode", "entities", "unescape"],
  }),
  command({
    name: "Dns.GetHostAddressesAsync",
    description: "Resolve a DNS host name to IP addresses asynchronously.",
    definition:
      "Queries configured name-resolution services and returns the IPv4 and/or IPv6 addresses associated with a host name.",
    moduleName: "System.Net.Dns",
    action: "Resolve",
    object: "Host name",
    template: "var addresses = await Dns.GetHostAddressesAsync({hostName});",
    parameters: [
      parameter("hostName", "hostName", "The DNS host name to resolve."),
    ],
    examples: [
      example(
        "Resolve a service host",
        String.raw`var addresses = await Dns.GetHostAddressesAsync("example.com");
foreach (var address in addresses)
{
    Console.WriteLine($"{address.AddressFamily}: {address}");
}`,
        "The result may contain several IPv4 and IPv6 addresses and can change across networks or calls.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.dns.gethostaddressesasync",
    keywords: ["DNS", "host", "IP address", "resolve"],
  }),
  command({
    name: "Dns.GetHostEntryAsync",
    description: "Resolve a host and retrieve canonical name information.",
    definition:
      "Returns an IPHostEntry containing the canonical host name, aliases, and resolved IP addresses available from the configured resolver.",
    moduleName: "System.Net.Dns",
    action: "Resolve",
    object: "Host name",
    template: "var entry = await Dns.GetHostEntryAsync({hostName});",
    parameters: [
      parameter("hostName", "hostName", "The DNS name or textual IP address."),
    ],
    examples: [
      example(
        "Inspect a host entry",
        String.raw`var entry = await Dns.GetHostEntryAsync("localhost");
Console.WriteLine(entry.HostName);
Console.WriteLine(string.Join(", ", entry.AddressList.Select(address => address.ToString())));`,
        "The resolver returns canonical naming and every address it associates with localhost.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.dns.gethostentryasync",
    keywords: ["DNS", "host entry", "canonical name", "resolve"],
  }),
  command({
    name: "IPAddress.TryParse",
    description: "Try to parse IPv4 or IPv6 address text.",
    definition:
      "Attempts to interpret text as an IPAddress and returns false instead of throwing when the representation is invalid.",
    moduleName: "System.Net.IPAddress",
    action: "Parse",
    object: "IP address",
    template: "var valid = IPAddress.TryParse({text}, out var address);",
    parameters: [
      parameter("text", "text", "The candidate IPv4 or IPv6 text.", "string?"),
    ],
    examples: [
      example(
        "Validate a literal address",
        String.raw`if (IPAddress.TryParse("2001:db8::1", out var address))
{
    Console.WriteLine($"{address.AddressFamily}: {address}");
}`,
        "The parsed value reports its address family without requiring DNS.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.ipaddress.tryparse",
    keywords: ["IP", "IPv4", "IPv6", "parse"],
  }),
  command({
    name: "HttpClient.GetStreamAsync",
    description: "Open an HTTP response body as a readable stream.",
    definition:
      "Sends a GET request, ensures a successful status, and returns the response body stream without first buffering the entire content.",
    moduleName: "System.Net.Http",
    action: "Read",
    object: "HTTP stream",
    template: "await using var stream = await {client}.GetStreamAsync({requestUri});",
    parameters: [
      parameter("client", "client", "A reusable HttpClient.", "HttpClient"),
      parameter("requestUri", "requestUri", "The absolute or base-relative request URI.", "string"),
    ],
    examples: [
      example(
        "Stream an HTTP body into a file",
        String.raw`using var client = new HttpClient();
await using var responseStream =
    await client.GetStreamAsync("https://example.com/");
await using var file = File.Create("example.html");
await responseStream.CopyToAsync(file);`,
        "The body is copied incrementally instead of being materialized as one string or byte array.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.getstreamasync",
    keywords: ["HTTP", "GET", "stream", "download"],
  }),
  command({
    name: "HttpClient.PutAsJsonAsync",
    description: "Send a JSON-encoded HTTP PUT request.",
    definition:
      "Serializes a value as JSON, sends it in an HTTP PUT request, and returns the response. The caller remains responsible for status handling and HttpClient lifetime.",
    moduleName: "System.Net.Http.Json",
    action: "Send",
    object: "HTTP JSON request",
    template: "using var response = await {client}.PutAsJsonAsync({requestUri}, {value});",
    parameters: [
      parameter("client", "client", "A reusable HttpClient.", "HttpClient"),
      parameter("requestUri", "requestUri", "The target URI.", "string"),
      parameter("value", "value", "The value to serialize as JSON.", "object"),
    ],
    examples: [
      example(
        "Replace an API resource",
        String.raw`using var client = new HttpClient();
var project = new { name = "Hypershell", status = "active" };
using var response = await client.PutAsJsonAsync(
    "https://api.example.com/projects/42",
    project);
Console.WriteLine(response.StatusCode);`,
        "The anonymous object becomes a JSON request body; production code should validate the returned status.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.json.httpclientjsonextensions.putasjsonasync",
    keywords: ["HTTP", "PUT", "JSON", "REST"],
  }),
  command({
    name: "HttpClient.DeleteAsync",
    description: "Send an HTTP DELETE request.",
    definition:
      "Sends a DELETE request to the selected URI and returns the complete response metadata and content handle.",
    moduleName: "System.Net.Http",
    action: "Send",
    object: "HTTP request",
    template: "using var response = await {client}.DeleteAsync({requestUri});",
    parameters: [
      parameter("client", "client", "A reusable HttpClient.", "HttpClient"),
      parameter("requestUri", "requestUri", "The target URI.", "string"),
    ],
    examples: [
      example(
        "Delete an API resource",
        String.raw`using var client = new HttpClient();
using var response = await client.DeleteAsync(
    "https://api.example.com/projects/42");
response.EnsureSuccessStatusCode();`,
        "EnsureSuccessStatusCode turns a non-success response into an exception after the request completes.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.deleteasync",
    keywords: ["HTTP", "DELETE", "REST"],
  }),
  command({
    name: "HttpContent.ReadAsStringAsync",
    description: "Read an HTTP content body as text.",
    definition:
      "Buffers the content bytes, selects an encoding from the content headers when available, and returns the decoded string.",
    moduleName: "System.Net.Http",
    action: "Read",
    object: "HTTP content",
    template: "var text = await {content}.ReadAsStringAsync();",
    parameters: [
      parameter("content", "content", "An HttpContent response or request body.", "HttpContent"),
    ],
    examples: [
      example(
        "Inspect an unsuccessful response body",
        String.raw`using var response = new HttpResponseMessage(
    HttpStatusCode.BadRequest)
{
    Content = new StringContent("Project name is required.")
};
var details = await response.Content.ReadAsStringAsync();
Console.WriteLine($"{response.StatusCode}: {details}");`,
        "The content is decoded independently of whether the HTTP status represents success.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.httpcontent.readasstringasync",
    keywords: ["HTTP", "content", "body", "text"],
  }),
  command({
    name: "HttpContent.ReadFromJsonAsync<T>",
    description: "Deserialize an HTTP content body from JSON.",
    definition:
      "Reads content as a JSON document and deserializes it into the requested .NET type using web-oriented serializer defaults unless options are supplied.",
    moduleName: "System.Net.Http.Json",
    action: "Read",
    object: "HTTP JSON content",
    template: "var value = await {content}.ReadFromJsonAsync<{type}>();",
    parameters: [
      parameter("content", "content", "The JSON HttpContent body.", "HttpContent"),
      parameter("type", "type", "The target .NET type.", "type name"),
    ],
    examples: [
      example(
        "Read a JSON dictionary",
        String.raw`using var content = JsonContent.Create(
    new Dictionary<string, string>
    {
        ["name"] = "Hypershell",
        ["status"] = "active"
    });
var value = await content.ReadFromJsonAsync<Dictionary<string, string>>();
Console.WriteLine(value?["name"]);`,
        "The in-memory JSON content is deserialized into a strongly typed dictionary.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.json.httpcontentjsonextensions.readfromjsonasync",
    keywords: ["HTTP", "JSON", "deserialize", "content"],
  }),
  command({
    name: "HttpRequestMessage.Create",
    description: "Create a configurable HTTP request message.",
    definition:
      "Combines an HTTP method, URI, headers, options, version policy, and optional content into a message that HttpClient.SendAsync can transmit.",
    moduleName: "System.Net.Http",
    action: "Create",
    object: "HTTP request",
    template:
      "using var request = new HttpRequestMessage({method}, {requestUri});",
    parameters: [
      parameter("method", "method", "An HttpMethod such as HttpMethod.Get.", "HttpMethod"),
      parameter("requestUri", "requestUri", "The target URI.", "string"),
    ],
    examples: [
      example(
        "Create a conditional GET request",
        String.raw`using var request = new HttpRequestMessage(
    HttpMethod.Get,
    "https://example.com/catalog.json");
request.Headers.IfNoneMatch.ParseAdd("\"catalog-v2\"");
using var client = new HttpClient();
using var response = await client.SendAsync(request);
Console.WriteLine(response.StatusCode);`,
        "A request object allows conditional headers to be configured before sending.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.httprequestmessage",
    keywords: ["HTTP", "request", "headers", "method"],
  }),
  command({
    name: "HttpHeaders.TryAddWithoutValidation",
    description: "Try to add a raw HTTP header value.",
    definition:
      "Adds a header without parsing the value against the known header grammar. It should be reserved for compatibility cases; untrusted values still require policy and injection safeguards.",
    moduleName: "System.Net.Http.Headers",
    action: "Add",
    object: "HTTP header",
    template: "var added = {headers}.TryAddWithoutValidation({name}, {value});",
    parameters: [
      parameter("headers", "headers", "An HttpHeaders collection.", "HttpHeaders"),
      parameter("name", "name", "The header field name."),
      parameter("value", "value", "The raw header field value."),
    ],
    examples: [
      example(
        "Add a vendor compatibility header",
        String.raw`using var request = new HttpRequestMessage(
    HttpMethod.Get,
    "https://example.com");
var added = request.Headers.TryAddWithoutValidation(
    "X-Vendor-Mode",
    "compat-v1");
Console.WriteLine(added);`,
        "The custom header is accepted without a registered parser; standard strongly typed header properties remain preferable.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.headers.httpheaders.tryaddwithoutvalidation",
    keywords: ["HTTP", "headers", "raw", "compatibility"],
  }),
  command({
    name: "FormUrlEncodedContent.Create",
    description: "Create an application/x-www-form-urlencoded HTTP body.",
    definition:
      "Encodes key-value pairs using HTML form URL-encoding rules and exposes them as HttpContent with the appropriate media type.",
    moduleName: "System.Net.Http",
    action: "Create",
    object: "HTTP form content",
    template: "using var content = new FormUrlEncodedContent({values});",
    parameters: [
      parameter("values", "values", "The form key-value pairs.", "IEnumerable<KeyValuePair<string,string>>"),
    ],
    examples: [
      example(
        "Build a form request body",
        String.raw`using var content = new FormUrlEncodedContent(
    new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["scope"] = "catalog.read"
    });
Console.WriteLine(await content.ReadAsStringAsync());`,
        "Keys and values are percent-encoded and joined into a standard form body.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.http.formurlencodedcontent",
    keywords: ["HTTP", "form", "urlencoded", "content"],
  }),
  command({
    name: "TcpClient.ConnectAsync",
    description: "Asynchronously connect a TCP client to a remote endpoint.",
    definition:
      "Resolves the host when necessary and attempts a TCP connection to the requested port, honoring an optional cancellation token.",
    moduleName: "System.Net.Sockets.TcpClient",
    action: "Send",
    object: "TCP connection",
    template: "await {client}.ConnectAsync({host}, {port});",
    parameters: [
      parameter("client", "client", "A TcpClient instance.", "TcpClient"),
      parameter("host", "host", "A DNS name or textual IP address.", "string"),
      parameter("port", "port", "The remote TCP port.", "int"),
    ],
    examples: [
      example(
        "Connect to a local ephemeral listener",
        String.raw`var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var client = new TcpClient();
var acceptedTask = listener.AcceptTcpClientAsync();
await client.ConnectAsync(IPAddress.Loopback, port);
using var accepted = await acceptedTask;
Console.WriteLine(client.Connected);
listener.Stop();`,
        "An ephemeral loopback listener verifies the client connection without contacting an external service.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.sockets.tcpclient.connectasync",
    keywords: ["TCP", "socket", "connect", "client"],
  }),
  command({
    name: "NetworkStream.WriteAsync",
    description: "Asynchronously write bytes to a connected network stream.",
    definition:
      "Writes bytes to the underlying connected socket and completes after the supplied buffer has been accepted for transmission or an error occurs.",
    moduleName: "System.Net.Sockets.NetworkStream",
    action: "Write",
    object: "TCP stream",
    template: "await {stream}.WriteAsync({buffer});",
    parameters: [
      parameter("stream", "stream", "A writable connected NetworkStream.", "NetworkStream"),
      parameter("buffer", "buffer", "The bytes to transmit.", "ReadOnlyMemory<byte>"),
    ],
    examples: [
      example(
        "Exchange a loopback message",
        String.raw`var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var endpoint = (IPEndPoint)listener.LocalEndpoint;
using var client = new TcpClient();
var acceptTask = listener.AcceptTcpClientAsync();
await client.ConnectAsync(endpoint.Address, endpoint.Port);
using var server = await acceptTask;
var message = Encoding.UTF8.GetBytes("ping");
await client.GetStream().WriteAsync(message);
var received = new byte[message.Length];
await server.GetStream().ReadExactlyAsync(received);
Console.WriteLine(Encoding.UTF8.GetString(received));
listener.Stop();`,
        "The local client writes four bytes and the accepted server connection reads exactly those bytes.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.sockets.networkstream.writeasync",
    keywords: ["TCP", "NetworkStream", "write", "socket"],
  }),
  command({
    name: "CookieContainer.Add",
    description: "Store a cookie for a URI.",
    definition:
      "Adds a Cookie under a URI-derived domain and path scope. CookieContainer enforces capacity and cookie-format rules.",
    moduleName: "System.Net.CookieContainer",
    action: "Add",
    object: "HTTP cookie",
    template: "{container}.Add({uri}, {cookie});",
    parameters: [
      parameter("container", "container", "A CookieContainer instance.", "CookieContainer"),
      parameter("uri", "uri", "The URI that supplies cookie scope.", "Uri"),
      parameter("cookie", "cookie", "The Cookie to store.", "Cookie"),
    ],
    examples: [
      example(
        "Store a scoped preference cookie",
        String.raw`var container = new CookieContainer();
var uri = new Uri("https://example.com/app/");
container.Add(uri, new Cookie("theme", "dark"));
Console.WriteLine(container.GetCookieHeader(uri));`,
        "The cookie is scoped to the URI's host and default path rules.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.cookiecontainer.add",
    keywords: ["cookie", "HTTP", "session", "state"],
  }),
  command({
    name: "CookieContainer.GetCookies",
    description: "Get cookies applicable to a URI.",
    definition:
      "Returns the non-expired cookies whose domain, path, port, and secure rules allow them to be sent to the selected URI.",
    moduleName: "System.Net.CookieContainer",
    action: "Get",
    object: "HTTP cookie",
    template: "var cookies = {container}.GetCookies({uri});",
    parameters: [
      parameter("container", "container", "A CookieContainer instance.", "CookieContainer"),
      parameter("uri", "uri", "The request URI used for cookie matching.", "Uri"),
    ],
    examples: [
      example(
        "Inspect cookies for one path",
        String.raw`var container = new CookieContainer();
var uri = new Uri("https://example.com/app/");
container.Add(uri, new Cookie("theme", "dark"));
foreach (Cookie cookie in container.GetCookies(uri))
{
    Console.WriteLine($"{cookie.Name}={cookie.Value}");
}`,
        "Only cookies applicable to the requested host, path, and scheme are returned.",
      ),
    ],
    documentationUrl:
      "https://learn.microsoft.com/dotnet/api/system.net.cookiecontainer.getcookies",
    keywords: ["cookie", "HTTP", "scope", "state"],
  }),
];

export default createLibrary({
  id: "csharp-networking",
  displayName: ".NET networking and HTTP",
  description:
    "Dependency-free .NET 8 APIs for URIs, encoding, DNS, IP addresses, HTTP messages and content, TCP streams, and cookies.",
  category: "Networking",
  keywords: ["network", "HTTP", "TCP", "DNS", "URI"],
  objectDescriptions: {
    "Host name":
      "A host name is a DNS label or fully qualified name resolved to one or more network addresses.",
    "HTML text":
      "HTML text is character data intended for a markup document; safe encoding depends on its exact HTML context.",
    "HTTP content":
      "HttpContent represents an HTTP request or response body plus its content headers.",
    "HTTP cookie":
      "An HTTP cookie is a small name-value record scoped by host, path, security, expiry, and related request rules.",
    "HTTP form content":
      "Form URL-encoded content represents key-value fields using the application/x-www-form-urlencoded media type.",
    "HTTP header":
      "An HTTP header is a named metadata field associated with a request, response, or content body.",
    "HTTP JSON content":
      "HTTP JSON content is an HTTP body encoded as JSON and described by content headers.",
    "HTTP JSON request":
      "An HTTP JSON request combines a method and URI with a JSON-serialized request body.",
    "HTTP request":
      "An HttpRequestMessage describes one outbound HTTP operation, including its method, URI, headers, content, and version policy.",
    "HTTP stream":
      "An HTTP stream exposes response-body bytes incrementally rather than buffering the entire body.",
    "IP address":
      "An IPAddress represents an IPv4 or IPv6 network address without DNS naming or transport-port information.",
    "TCP connection":
      "A TCP connection is a reliable ordered byte stream between a local and remote network endpoint.",
    "TCP stream":
      "A NetworkStream reads and writes bytes through a connected stream-oriented socket.",
    URI:
      "A URI is a structured identifier containing components such as scheme, authority, path, query, and fragment.",
    "URL component":
      "A URL component is one structural portion of a URL whose reserved characters require context-appropriate encoding.",
  },
  commands,
});
