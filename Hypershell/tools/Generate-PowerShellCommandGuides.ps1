[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $DocsRoot,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [string] $LegacyDocsRoot = '',

    [string] $SourceCommit = '19d6548860b75da2db261ebc8bd71855de44ecbd'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$moduleNames = @(
    'CimCmdlets'
    'Microsoft.PowerShell.Core'
    'Microsoft.PowerShell.Diagnostics'
    'Microsoft.PowerShell.Host'
    'Microsoft.PowerShell.Management'
    'Microsoft.PowerShell.Security'
    'Microsoft.PowerShell.Utility'
    'Microsoft.WSMan.Management'
    'PSDiagnostics'
)

# Get-Verb supplies Microsoft's approved-verb meanings. These custom entries
# cover familiar PowerShell command names that intentionally use other verbs.
$actionDescriptions = [ordered]@{}
Get-Verb | ForEach-Object {
    $actionDescriptions[$_.Verb] = "$($_.Description.Trim()). In a PowerShell command name, this verb tells you the operation the command performs."
}

$customActions = [ordered]@{
    Checkpoint = 'Creates a recoverable snapshot of the current state so it can be restored later.'
    ForEach = 'Runs the same script block once for every object that arrives through the pipeline.'
    Limit = 'Restricts a resource so it cannot grow beyond the size or count you specify.'
    Pop = 'Returns to the location most recently saved on PowerShell''s location stack.'
    Protect = 'Applies encryption or another safeguard so only an authorized recipient can read the content.'
    Push = 'Saves the current location on a stack and then optionally moves to another location.'
    Tee = 'Splits one stream of pipeline objects into two directions: it saves a copy in a variable or file while passing the same objects onward.'
    Where = 'Keeps only the pipeline objects that satisfy a condition and discards the rest from that pipeline.'
}
foreach ($entry in $customActions.GetEnumerator()) {
    $actionDescriptions[$entry.Key] = $entry.Value
}

# PowerShell nouns are concepts, not always literal .NET types. These definitions
# explain what each noun means in the command names shipped in Hypershell.
$objectDescriptions = [ordered]@{
    Acl = 'An access control list (ACL) is the set of permission rules attached to a securable resource, such as a file, folder, registry key, or certificate.'
    Alias = 'An alias is an alternate, usually shorter name that PowerShell resolves to another command. For example, dir is an alias for Get-ChildItem.'
    ArgumentCompleter = 'An argument completer is code that proposes valid parameter values when a user presses Tab while typing a command.'
    AuthenticodeSignature = 'An Authenticode signature is a digital signature attached to a script or file so its publisher and integrity can be verified.'
    BinaryMiLog = 'A binary MI log is a file containing serialized Common Information Model (CIM) instances, suitable for saving and replaying management data.'
    ChildItem = 'A child item is something contained by another provider item—for example, a file inside a folder or a registry value beneath a registry key.'
    CimAssociatedInstance = 'A CIM associated instance is a management object related to another CIM instance through an association defined by its CIM schema.'
    CimClass = 'A CIM class is a management schema that defines a kind of system resource, including the properties and methods its instances expose.'
    CimIndicationEvent = 'A CIM indication event is a notification produced when a managed system condition occurs, such as a process being created.'
    CimInstance = 'A CIM instance is a structured management object representing a real system resource such as an operating system, process, disk, or service.'
    CimMethod = 'A CIM method is an operation defined by a CIM class that can be invoked on a managed resource.'
    CimSession = 'A CIM session is a reusable connection to a local or remote computer for running CIM management operations.'
    CimSessionOption = 'CIM session options describe connection behavior such as protocol, encoding, certificate checks, and timeouts.'
    Clipboard = 'The clipboard is the operating system''s temporary shared storage for text or other content copied between applications.'
    CliXml = 'CLIXML is PowerShell''s XML serialization format for preserving objects and their property data outside the current session.'
    CmsMessage = 'A CMS message is content protected with Cryptographic Message Syntax, normally encrypted for one or more certificate holders.'
    Command = 'A command is something PowerShell can invoke, such as a cmdlet, function, script, alias, or native executable.'
    Computer = 'Computer refers to a Windows computer''s system identity or operating state, including domain membership, name, restart, shutdown, and restore operations.'
    ComputerInfo = 'Computer information is a consolidated snapshot of operating-system, hardware, firmware, locale, network, and PowerShell-related details.'
    ComputerMachinePassword = 'A computer machine password is the secret a domain-joined computer uses to authenticate its account to the domain.'
    ComputerRestore = 'Computer Restore is the Windows System Restore feature that records and restores protected system state.'
    ComputerRestorePoint = 'A restore point is a dated snapshot of protected Windows system files and settings that System Restore can return to.'
    ComputerSecureChannel = 'A computer secure channel is the authenticated trust connection between a domain member and its Active Directory domain.'
    Connection = 'A connection is a network communication path to another computer; Test-Connection checks reachability with echo requests similar to ping.'
    Content = 'Content is the data stored inside a provider item—most commonly the text or bytes inside a file, rather than the file object itself.'
    ControlPanelItem = 'A Control Panel item is a Windows settings applet, such as Programs and Features or Windows Defender Firewall.'
    Counter = 'A performance counter is a numeric measurement exposed by Windows for monitoring activity such as CPU, memory, disk, or application usage.'
    Credential = 'A credential packages a user name with a protected password so a command can authenticate as that identity.'
    Csv = 'CSV (comma-separated values) is a text table format in which each row is a record and each column represents a named field.'
    Culture = 'Culture describes regional formatting rules such as language, decimal separators, date formats, and sorting conventions.'
    Custom = 'Custom formatting is a developer-oriented view that displays selected object properties using a format definition rather than a normal table or list.'
    Date = 'A date is a .NET DateTime value containing a calendar date and time, which PowerShell can display, compare, and calculate with.'
    Debug = 'The debug stream carries diagnostic messages intended for investigating script behavior; it is separate from normal success output.'
    Debugger = 'The PowerShell debugger pauses script execution so you can inspect variables, call stacks, and the next statement to run.'
    Default = 'Default output is PowerShell''s normal formatting and display path when no more specific output command has handled an object.'
    EngineEvent = 'An engine event is a notification raised by PowerShell itself, such as a session exiting or a runspace changing state.'
    Error = 'An error is a structured record describing a failed operation, including its message, exception, category, target, and call location.'
    Event = 'A PowerShell event is a queued notification from an object, the engine, WMI/CIM, or another registered event source.'
    EventLog = 'An event log is a classic Windows log that stores timestamped diagnostic, security, and application records.'
    EventSubscriber = 'An event subscriber is the registration that tells PowerShell which event source to watch and what action to take when it fires.'
    ExecutionPolicy = 'Execution policy is PowerShell''s safety setting for deciding when scripts and configuration files may load; it is not a security boundary.'
    ExperimentalFeature = 'An experimental feature is an opt-in PowerShell capability that is still being evaluated and can be enabled or disabled by name.'
    Expression = 'An expression is PowerShell source text that produces a value or performs an operation when the engine evaluates it.'
    File = 'A file is a named sequence of data stored by a file system. These commands write pipeline output to a file or remove a downloaded-file security block.'
    FileCatalog = 'A file catalog is a signed catalog of cryptographic hashes used to detect whether files in a directory have changed.'
    FileHash = 'A file hash is a fixed-length cryptographic fingerprint calculated from file contents; matching hashes strongly indicate matching content.'
    FormatData = 'Format data is the collection of rules PowerShell uses to decide which properties and layouts represent particular object types on screen.'
    GridView = 'A grid view is an interactive table window for viewing, filtering, sorting, and optionally selecting pipeline objects.'
    Guid = 'A GUID is a 128-bit globally unique identifier commonly used as a durable ID for objects, records, and resources.'
    Help = 'Help is PowerShell''s documentation system for commands and conceptual topics, including descriptions, syntax, parameters, notes, and examples.'
    Hex = 'Hex output is a byte-oriented hexadecimal representation that helps inspect the raw contents of strings, files, and other input.'
    History = 'History is the session''s record of commands that were entered, allowing them to be reviewed or invoked again.'
    Host = 'The PowerShell host is the application providing the user interface around the PowerShell engine, including input, output, prompts, and window capabilities.'
    HotFix = 'A hotfix is an installed Windows update identified through the operating system''s Quick Fix Engineering records.'
    Html = 'HTML is the markup language used to structure web pages; PowerShell can turn objects into an HTML table or list fragment.'
    Information = 'The information stream carries structured messages meant for users or logs without mixing them into the pipeline''s success output.'
    Item = 'An item is a resource exposed through a PowerShell provider, such as a file, directory, registry key, certificate, variable, or alias.'
    ItemProperty = 'An item property is named metadata stored on a provider item, such as a registry value or a file attribute.'
    ItemPropertyValue = 'An item property value is the raw value contained by one named provider property, without the surrounding property object.'
    Job = 'A PowerShell job represents work running asynchronously or in the background, together with its state and buffered output.'
    Json = 'JSON is a text format for nested objects, arrays, strings, numbers, Boolean values, and null, widely used by APIs and configuration files.'
    List = 'A list view displays each selected property on its own labeled line; Update-List instead edits add/remove-capable collection properties on an object.'
    LocalizedData = 'Localized data is text or other culture-specific content loaded from a language-specific PowerShell data file.'
    Location = 'A location is PowerShell''s current position within a provider drive, usually the working directory but also potentially a registry or certificate path.'
    LogProperties = 'Log properties are settings that control a Windows event log, such as maximum size, retention, overflow behavior, and enabled state.'
    MailMessage = 'A mail message is an email assembled from sender, recipient, subject, body, attachment, and SMTP delivery information.'
    Markdown = 'Markdown is a lightweight plain-text markup format for headings, emphasis, links, lists, and code blocks.'
    MarkdownOption = 'Markdown options control how PowerShell renders Markdown text, including colors and terminal escape sequences.'
    Member = 'A member is a named property, method, event, or other capability exposed by a PowerShell/.NET object.'
    Module = 'A PowerShell module is a reusable package of commands, providers, variables, types, and related resources that can be loaded into a session.'
    ModuleManifest = 'A module manifest is a .psd1 data file declaring a module''s identity, version, dependencies, exports, compatibility, and other metadata.'
    ModuleMember = 'A module member is a command, variable, or alias that a module intentionally exposes to users after it is imported.'
    Null = 'Null means the absence of a value. Out-Null consumes pipeline output so it is neither displayed nor passed to another command.'
    Object = 'A PowerShell object is a value with a type and named properties and methods. Pipelines pass these structured objects between commands rather than passing only display text.'
    ObjectEvent = 'An object event is a .NET event raised by a particular object instance, such as a timer elapsing or a file-system watcher detecting a change.'
    Output = 'Output is the stream of objects a command returns to the success pipeline, where another command can consume them or the host can display them.'
    Path = 'A path identifies a location in a PowerShell provider, such as a file-system path, registry path, certificate path, or variable path.'
    PfxCertificate = 'A PFX certificate is a certificate read from a PKCS #12 file, often including the public certificate and an associated private key.'
    PowerShellDataFile = 'A PowerShell data file is a restricted .psd1 file containing a hashtable of configuration or localized data.'
    Printer = 'A printer is a print destination known to the operating system; Out-Printer formats pipeline output and sends it to that device.'
    Process = 'A process is a running instance of a program, identified by properties such as its name, process ID, memory use, and start time.'
    Progress = 'Progress is a transient status display showing that a longer operation is advancing, usually with activity text, percent complete, and remaining work.'
    PSBreakpoint = 'A PowerShell breakpoint is a rule that pauses script execution at a line, command, or variable access so the debugger can inspect state.'
    PSCallStack = 'The PowerShell call stack is the ordered chain of scripts, functions, and scopes that led to the debugger''s current execution point.'
    PSDebug = 'PowerShell debug settings control script tracing, strict stepping behavior, and debugger-related execution diagnostics.'
    PSDrive = 'A PowerShell drive is a named entry point into a provider-backed data store, such as C: for files, HKLM: for the registry, or Env: for environment variables.'
    PSHostProcess = 'A PowerShell host process is another process containing a PowerShell runspace that the current session can enter for inspection or debugging.'
    PSHostProcessInfo = 'PowerShell host process information identifies processes that currently host accessible PowerShell runspaces.'
    PSProvider = 'A PowerShell provider adapts a data store—such as the file system, registry, certificates, variables, or aliases—to a common drive-and-item interface.'
    PSRemoting = 'PowerShell remoting is the infrastructure for running commands in PowerShell sessions on other computers or in separate processes.'
    PSRoleCapabilityFile = 'A PowerShell role capability file defines which commands, providers, scripts, and language features a constrained remoting role may use.'
    PSSession = 'A PowerShell session (PSSession) is a persistent connection to a local or remote PowerShell runspace, preserving remote state across commands.'
    PSSessionCapability = 'A session capability describes the protocol versions, serialization version, and other features supported by a remote endpoint.'
    PSSessionConfiguration = 'A session configuration is a registered remoting endpoint that controls who may connect and what environment or capabilities they receive.'
    PSSessionConfigurationFile = 'A session configuration file (.pssc) declares the language mode, visible commands, role definitions, startup scripts, and other endpoint restrictions.'
    PSSessionOption = 'Session options control a remoting connection''s timeouts, buffering, proxy use, certificate checks, and related transport behavior.'
    PSTrace = 'PowerShell tracing records detailed engine and provider activity to help diagnose how a command or script is being processed.'
    PSTransportOption = 'Transport options define endpoint-level quotas and timeouts for remoting communications.'
    PSWSManCombinedTrace = 'A combined WSMan trace collects PowerShell remoting and WS-Management diagnostic events together for troubleshooting.'
    Random = 'Random output is a pseudo-random number or a randomly selected item from input supplied to Get-Random.'
    RecycleBin = 'The Recycle Bin is Windows'' recoverable holding area for deleted file-system items.'
    RestMethod = 'A REST method is an HTTP request to a REST-style web API whose response PowerShell converts into usable objects when possible.'
    Runspace = 'A runspace is an independent PowerShell execution environment containing session state, commands, variables, and pipelines.'
    RunspaceDebug = 'Runspace debugging is the ability to attach the debugger to scripts executing in another PowerShell runspace.'
    SddlString = 'An SDDL string is a compact text representation of a Windows security descriptor, including owner, group, permissions, and auditing rules.'
    SecureRandom = 'A secure random value is generated with a cryptographically strong source suitable for secrets and security-sensitive selection.'
    SecureString = 'A SecureString is an in-memory representation intended to reduce casual exposure of sensitive text; it does not by itself make a secret universally safe.'
    Service = 'A service is a long-running background program managed by the operating system, with properties such as name, status, startup type, and dependencies.'
    Sleep = 'Sleep is a deliberate pause in script execution for a specified duration.'
    StrictMode = 'Strict mode makes PowerShell report otherwise tolerated scripting mistakes, such as reading an uninitialized variable or missing property.'
    String = 'A string is an ordered sequence of text characters. These commands convert, combine, search, format, or display text.'
    StringData = 'String data is a simple key-value text format that PowerShell converts into a hashtable, commonly for localized messages.'
    Table = 'A table view arranges selected object properties into named columns with one object per row.'
    TemporaryFile = 'A temporary file is a newly created, uniquely named file intended for short-lived intermediate data.'
    TimeSpan = 'A TimeSpan is a duration—the elapsed amount of time between two points or a directly specified number of days, hours, minutes, and seconds.'
    TimeZone = 'A time zone defines a region''s offset from Coordinated Universal Time and its daylight-saving rules.'
    Trace = 'A trace is a diagnostic recording of low-level system or application activity collected over time.'
    TraceSource = 'A trace source is a named PowerShell component that emits diagnostic messages at configurable levels.'
    Transaction = 'A transaction groups supported changes into one unit that can be completed together or undone together.'
    Transcript = 'A transcript is a text record of a PowerShell session''s commands and console output.'
    Type = 'A type is a .NET class, struct, enum, interface, or other definition that determines what values and behavior objects can have.'
    TypeData = 'PowerShell type data adds or changes displayed properties, methods, serialization rules, and default behavior for existing .NET types.'
    UICulture = 'UI culture identifies the language PowerShell and applications use for user-facing text and localized resources.'
    Unique = 'Unique output contains only one representative from each adjacent duplicate value in already sorted input.'
    Uptime = 'Uptime is the amount of time elapsed since the operating system last started.'
    Variable = 'A variable is a named slot in session state that stores a value so scripts and commands can reuse it.'
    Verb = 'A PowerShell verb is the standardized action word before the hyphen in a Verb-Noun command name, such as Get, Set, or Remove.'
    Verbose = 'The verbose stream carries optional, detailed progress or diagnostic messages that appear when verbose output is enabled.'
    Warning = 'The warning stream carries non-terminating caution messages separately from normal success output and errors.'
    WebRequest = 'A web request is an HTTP or HTTPS request that returns a response containing status, headers, links, forms, and response content.'
    WebServiceProxy = 'A web-service proxy is a generated .NET client object for calling operations described by a SOAP/WSDL service.'
    Wide = 'A wide view displays one selected property from each object in multiple columns, optimized for fitting many names on screen.'
    WinEvent = 'A Windows event is a structured record read from modern Windows Event Log channels or event trace log files.'
    WmiEvent = 'A WMI event is a notification from Windows Management Instrumentation when a monitored system condition occurs.'
    WmiInstance = 'A WMI instance is a management object representing a Windows resource through the older Windows Management Instrumentation API.'
    WmiMethod = 'A WMI method is an operation exposed by a WMI class or instance for managing a Windows resource.'
    WmiObject = 'A WMI object is a Windows management object returned through the legacy WMI API, with properties and callable methods.'
    WSMan = 'WS-Management (WSMan) is the web-services protocol and client configuration used by traditional PowerShell remoting on Windows.'
    WSManAction = 'A WSMan action is an operation invoked directly against a resource exposed by the WS-Management protocol.'
    WSManCredSSP = 'WSMan CredSSP is a credential-delegation authentication option that can pass user credentials to a remote computer and therefore requires careful trust.'
    WSManInstance = 'A WSMan instance is a management resource addressed and manipulated directly through a WS-Management resource URI.'
    WSManQuickConfig = 'WSMan quick configuration is the standard setup that starts WinRM, creates a listener, and enables the firewall exception needed for remoting.'
    WSManSessionOption = 'WSMan session options control low-level WS-Management connection behavior such as authentication, proxy settings, certificates, and timeouts.'
    WSManTrace = 'A WSMan trace is a diagnostic log of Windows Remote Management communications and configuration activity.'
    Xml = 'XML is a hierarchical text markup format made of elements, attributes, and text nodes; PowerShell can create or query XML documents.'
}

function Get-MarkdownSection {
    param(
        [Parameter(Mandatory)] [string] $Markdown,
        [Parameter(Mandatory)] [string] $Heading
    )

    $escapedHeading = [regex]::Escape($Heading)
    $match = [regex]::Match(
        $Markdown,
        "(?ms)^##\s+$escapedHeading\s*\r?\n(?<Body>.*?)(?=^##\s+|\z)")
    if ($match.Success) {
        return $match.Groups['Body'].Value.Trim()
    }

    return ''
}

function ConvertFrom-MarkdownInline {
    param([AllowEmptyString()] [string] $Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ''
    }

    $value = [regex]::Replace($Text, '(?ms)<!--.*?-->', '')
    $value = [regex]::Replace($value, '(?m)^\s*>\s*\[![A-Z]+\]\s*', '')
    $value = [regex]::Replace($value, '(?m)^\s*>\s?', '')
    $value = [regex]::Replace($value, '\[([^\]]+)\]\([^)]+\)', '$1')
    $value = [regex]::Replace($value, '<([^>]+)>', '$1')
    $value = $value.Replace('`', '').Replace('**', '').Replace('__', '')
    $value = [regex]::Replace($value, '(?m)^\s*[-*]\s+', '• ')

    $paragraphs = [regex]::Split($value.Trim(), '\r?\n\s*\r?\n')
    $normalized = foreach ($paragraph in $paragraphs) {
        $lines = @(
            $paragraph -split '\r?\n' |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_ }
        )
        if ($lines.Count -gt 0) {
            ($lines -join ' ')
        }
    }

    return ($normalized -join [Environment]::NewLine + [Environment]::NewLine).Trim()
}

function Limit-Text {
    param(
        [AllowEmptyString()] [string] $Text,
        [int] $MaximumLength
    )

    if ($Text.Length -le $MaximumLength) {
        return $Text
    }

    $cut = $Text.LastIndexOf(' ', $MaximumLength)
    if ($cut -lt [math]::Floor($MaximumLength * 0.7)) {
        $cut = $MaximumLength
    }

    return "$($Text.Substring(0, $cut).TrimEnd())."
}

function Get-GuideSyntax {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $SyntaxSection)

    if ([string]::IsNullOrWhiteSpace($SyntaxSection)) {
        return ''
    }

    $sets = [regex]::Matches(
        $SyntaxSection,
        '(?ms)^###\s+(?<Name>.+?)\s*\r?\n.*?^```[^\r\n]*\r?\n(?<Code>.*?)^```')
    if ($sets.Count -gt 0) {
        $result = foreach ($set in $sets) {
            "# $($set.Groups['Name'].Value.Trim())`n$($set.Groups['Code'].Value.Trim())"
        }
        return ($result -join [Environment]::NewLine + [Environment]::NewLine).Trim()
    }

    $blocks = [regex]::Matches(
        $SyntaxSection,
        '(?ms)^```[^\r\n]*\r?\n(?<Code>.*?)^```')
    return (($blocks | ForEach-Object { $_.Groups['Code'].Value.Trim() }) -join [Environment]::NewLine + [Environment]::NewLine).Trim()
}

function Get-GuideExample {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $ExamplesSection)

    if ([string]::IsNullOrWhiteSpace($ExamplesSection)) {
        return @()
    }

    $matches = [regex]::Matches(
        $ExamplesSection,
        '(?ms)^###\s+(?<Title>.+?)\s*\r?\n(?<Body>.*?)(?=^###\s+|\z)')
    $candidates = for ($matchIndex = 0; $matchIndex -lt $matches.Count; $matchIndex++) {
        $match = $matches[$matchIndex]
        $body = $match.Groups['Body'].Value
        $block = $null
        $searchFrom = 0
        while ($searchFrom -lt $body.Length) {
            $open = $body.IndexOf('```', $searchFrom, [StringComparison]::Ordinal)
            if ($open -lt 0) {
                break
            }

            $headerEnd = $body.IndexOf("`n", $open)
            if ($headerEnd -lt 0) {
                break
            }

            $language = $body.Substring(
                $open + 3,
                $headerEnd - ($open + 3)).Trim()
            $contentStart = $headerEnd + 1
            $close = $body.IndexOf(
                '```',
                $contentStart,
                [StringComparison]::Ordinal)
            if ($close -lt 0) {
                break
            }

            if ([string]::IsNullOrWhiteSpace($language) -or
                $language -in @('powershell', 'PowerShell', 'ps1')) {
                $block = [pscustomobject]@{
                    Code = $body.Substring(
                        $contentStart,
                        $close - $contentStart).Trim()
                    Index = $open
                    Length = ($close + 3) - $open
                }
                break
            }

            $searchFrom = $close + 3
        }

        if ($null -eq $block) {
            continue
        }

        $code = $block.Code
        if ([string]::IsNullOrWhiteSpace($code)) {
            continue
        }

        $explanationMarkdown = $body.Remove(
            $block.Index,
            $block.Length)
        $explanationMarkdown = [regex]::Replace(
            $explanationMarkdown,
            '(?ms)^```(?:output|Output|text|console)?\s*\r?\n.*?^```',
            '')
        $explanation = Limit-Text `
            -Text (ConvertFrom-MarkdownInline $explanationMarkdown) `
            -MaximumLength 1100
        $lineCount = @($code -split '\r?\n' | Where-Object { $_.Trim() }).Count
        $score = 120 - ($matchIndex * 15)
        if ($lineCount -gt 1) { $score += 10 }
        if ($lineCount -gt 2) { $score += 5 }
        if ($lineCount -gt 18) { $score -= 60 }
        if ($code.Length -gt 1200) { $score -= 80 }
        if ($explanation.Length -gt 160) { $score += 15 }

        [pscustomobject]@{
            Title = (ConvertFrom-MarkdownInline $match.Groups['Title'].Value)
            Code = $code
            Explanation = $explanation
            Score = $score
        }
    }

    $selected = $candidates |
        Sort-Object -Property @{ Expression = 'Score'; Descending = $true } |
        Select-Object -First 1
    if ($null -eq $selected) {
        return @()
    }

    return @(
        [ordered]@{
            title = $selected.Title
            code = $selected.Code
            explanation = $selected.Explanation
        }
    )
}

$commands = [ordered]@{}
$documentationRoots = @($DocsRoot)
if (-not [string]::IsNullOrWhiteSpace($LegacyDocsRoot)) {
    $documentationRoots += $LegacyDocsRoot
}

foreach ($documentationRoot in $documentationRoots) {
    foreach ($moduleName in $moduleNames) {
        $moduleDirectory = Join-Path $documentationRoot $moduleName
        if (-not (Test-Path -LiteralPath $moduleDirectory -PathType Container)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $moduleDirectory -File -Filter '*.md') {
        $markdown = Get-Content -LiteralPath $file.FullName -Raw
        $titleMatch = [regex]::Match($markdown, '(?m)^title:\s*(?<Title>.+?)\s*$')
        if (-not $titleMatch.Success) {
            continue
        }

        $name = $titleMatch.Groups['Title'].Value.Trim()
        if ($commands.Contains($name)) {
            continue
        }

        $synopsis = Limit-Text `
            -Text (ConvertFrom-MarkdownInline (Get-MarkdownSection $markdown 'SYNOPSIS')) `
            -MaximumLength 500
        $definition = Limit-Text `
            -Text (ConvertFrom-MarkdownInline (Get-MarkdownSection $markdown 'DESCRIPTION')) `
            -MaximumLength 1600
        $syntax = Get-GuideSyntax (Get-MarkdownSection $markdown 'SYNTAX')
        $examples = Get-GuideExample (Get-MarkdownSection $markdown 'EXAMPLES')
        $urlMatch = [regex]::Match(
            $markdown,
            '(?m)^online version:\s*(?<Url>\S+)\s*$')
        $sourceUrl = if ($urlMatch.Success) {
            $urlMatch.Groups['Url'].Value.Replace('&WT.mc_id=ps-gethelp', '')
        }
        else {
            ''
        }

        if ([string]::IsNullOrWhiteSpace($synopsis)) {
            continue
        }

            $commands[$name] = [ordered]@{
                summary = $synopsis
                definition = if ([string]::IsNullOrWhiteSpace($definition)) { $synopsis } else { $definition }
                syntax = $syntax
                sourceUrl = $sourceUrl
                examples = @($examples)
            }
        }
    }
}

if ($commands.Contains('Tee-Object')) {
    $commands['Tee-Object'].summary =
        'Saves a copy of pipeline output in a file or variable while passing the same objects to the next command.'
    $commands['Tee-Object'].definition =
        'Tee-Object splits one stream of PowerShell objects into two directions, like a T-shaped pipe fitting. One copy is stored in a file or variable, while the same objects continue through the pipeline for another command to process. If nothing follows Tee-Object, the continuing output is displayed in the console. This is useful when you need to record intermediate results without interrupting the rest of a pipeline.'
}

$catalog = [ordered]@{
    sourceLabel = 'Microsoft Learn · PowerShell 7.5 / Windows PowerShell 5.1 · CC BY 4.0'
    sourceRepository = 'https://github.com/MicrosoftDocs/PowerShell-Docs'
    sourceCommit = $SourceCommit
    contentLicense = 'CC BY 4.0'
    actions = $actionDescriptions
    objects = $objectDescriptions
    commands = $commands
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$catalog |
    ConvertTo-Json -Depth 10 |
    Set-Content -LiteralPath $resolvedOutput -Encoding utf8

Write-Host "Generated $($commands.Count) PowerShell command guides."
Write-Host "Actions: $($actionDescriptions.Count); objects: $($objectDescriptions.Count)"
Write-Host "Output: $resolvedOutput"
