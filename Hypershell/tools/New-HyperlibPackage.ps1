[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]+(?:[.-][a-z0-9]+)+$')]
    [string] $Id,

    [Parameter(Mandatory)]
    [ValidateLength(1, 80)]
    [string] $DisplayName,

    [Parameter(Mandatory)]
    [ValidateLength(1, 80)]
    [string] $Publisher,

    [ValidateLength(1, 80)]
    [string] $Language = 'PowerShell',

    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.1.0',

    [ValidatePattern('^[A-Za-z][A-Za-z0-9._-]+$')]
    [string] $LibraryId = 'starterGuides',

    [ValidateSet('HumanAuthored', 'AiAssisted', 'AiGenerated', 'Imported')]
    [string] $ContentOrigin = 'HumanAuthored',

    [string] $Destination = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$destinationRoot = [System.IO.Path]::GetFullPath($Destination)
$packageRoot = Join-Path $destinationRoot $Id
if (Test-Path -LiteralPath $packageRoot) {
    throw "The destination package already exists: $packageRoot"
}

$temporaryRoot = Join-Path $destinationRoot ".$Id.authoring-$([Guid]::NewGuid().ToString('N'))"
$contentRoot = Join-Path $temporaryRoot 'content'
$utf8 = [System.Text.UTF8Encoding]::new($false)

$normalizedLanguage = $Language.Trim().ToLowerInvariant()
$executionTarget = switch ($normalizedLanguage) {
    'powershell' { 'PowerShell' }
    'c#' { 'CSharp' }
    'csharp' { 'CSharp' }
    default { 'External' }
}
$executionSupported = $executionTarget -ne 'External'
$preferredShellKind = switch ($executionTarget) {
    'PowerShell' { 'PowerShell' }
    'CSharp' { 'CSharp' }
    default { 'External' }
}
$template = switch ($executionTarget) {
    'PowerShell' { 'Write-Output "Hello from Hyperlib"' }
    'CSharp' { 'Console.WriteLine("Hello from Hyperlib");' }
    default { 'Replace this text with a valid command or code pattern.' }
}

$manifest = [ordered]@{
    schemaVersion = 1
    id = $Id
    displayName = $DisplayName
    publisher = $Publisher
    version = $Version
    description = "A static $Language guide library for Hypershell."
    runtime = 'ContentOnly'
    requestedCapabilities = @('LibraryContent')
    contributions = [ordered]@{
        libraries = @(
            [ordered]@{
                id = $LibraryId
                displayName = $DisplayName
                contentPath = 'content'
                languageIds = @($normalizedLanguage)
                contentOrigin = $ContentOrigin
                reviewState = 'Draft'
                provenance = 'Starter package generated with tools/New-HyperlibPackage.ps1. Replace this text with the sources and review process used for the finished library.'
            }
        )
    }
}

$library = [ordered]@{
    schemaVersion = 2
    contentVersion = $Version
    id = $LibraryId
    displayName = $DisplayName
    description = "Searchable $Language guides and reusable examples."
    language = $Language
    kind = 'Package'
    canonicalName = $DisplayName
    ecosystem = $Language
    targetVersion = $Version
    sourceVersion = "Authoring package $Version"
    license = 'Author supplied; update before publication'
    isExecutionSupported = $executionSupported
    createStandaloneProfile = $true
    keywords = @($normalizedLanguage, 'starter', 'guide')
    executionTarget = $executionTarget
    declaredEntryCount = 1
    declaredIndexedCount = 0
    declaredGuidedCount = 1
    declaredVerifiedCount = 0
    legacyIds = @()
    commands = @(
        [ordered]@{
            id = 'helloWorld'
            name = 'Hello world'
            displayName = 'Hello world'
            description = "Create a minimal $Language output example."
            definition = 'Replace this starter definition with a complete explanation of the API, module, command, or language feature. Include behavior, important constraints, and when a user should choose it.'
            preferredShellKind = $preferredShellKind
            symbolKind = 'Other'
            coverageTier = 'Guided'
            browsePath = @(
                [ordered]@{
                    kind = 'Package'
                    name = $DisplayName
                },
                [ordered]@{
                    kind = 'Entry'
                    name = 'Hello world'
                }
            )
            legacyIds = @()
            template = $template
            keywords = @('hello', 'output', 'starter')
            compositionMode = if ($executionTarget -eq 'PowerShell') { 'Pipeline' } else { 'Statement' }
            parameters = @()
            examples = @(
                [ordered]@{
                    title = 'Run the starter example'
                    code = $template
                    explanation = 'Replace this explanation after verifying the example in the intended runtime.'
                }
            )
        }
    )
}

$readme = @"
# $DisplayName

This is a static Hypershell Hyperlib authoring folder for $Language.

1. Replace the schema-v2 starter entry in 'content/hyperlib.json' with
   correctly classified, canonical symbols and guides.
2. Keep every command ID stable across releases.
3. Record documentation URLs, provenance, and useful copyable examples.
4. Leave 'reviewState' as 'Draft' while content still needs review. To publish
   it as 'Reviewed', add a non-empty 'reviewedBy' value to the library
   contribution in 'hypershell.extension.json'.
5. In Hypershell, open **Settings > Extensions**, choose **Install folder** to
   validate locally, and choose **Pack folder** to create a distributable
   '.hyperlib' archive.

The package is ContentOnly. It cannot execute extension code or request file,
terminal, network, secret, workspace-panel, or AI capabilities.
"@

try {
    [System.IO.Directory]::CreateDirectory($contentRoot) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $temporaryRoot 'hypershell.extension.json'),
        ($manifest | ConvertTo-Json -Depth 20),
        $utf8)
    [System.IO.File]::WriteAllText(
        (Join-Path $contentRoot 'hyperlib.json'),
        ($library | ConvertTo-Json -Depth 20),
        $utf8)
    [System.IO.File]::WriteAllText(
        (Join-Path $temporaryRoot 'README.md'),
        $readme,
        $utf8)
    [System.IO.Directory]::Move($temporaryRoot, $packageRoot)
}
catch {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
    throw
}

[pscustomobject]@{
    PackageId = $Id
    Version = $Version
    Language = $Language
    PackageRoot = $packageRoot
    ManifestPath = Join-Path $packageRoot 'hypershell.extension.json'
    ContentPath = Join-Path $packageRoot 'content\hyperlib.json'
}
