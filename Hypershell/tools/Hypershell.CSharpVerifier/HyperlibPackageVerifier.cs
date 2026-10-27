using Hypershell.Services;
using System.IO.Compression;
using System.Security.Cryptography;

internal static class HyperlibPackageVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "hyperlib-package-lifecycle");
        var source = Path.Combine(root, "source");
        var extensions = Path.Combine(root, "extensions");
        var staging = Path.Combine(root, "staging");
        var recovery = Path.Combine(root, "recovery");
        var archives = Path.Combine(root, "archives");
        Directory.CreateDirectory(source);
        WritePackage(source, "1.0.0", "Initial guide text");

        var service = new HyperlibPackageService(
            extensions,
            staging,
            recovery);
        var folderPreview = service.InspectInstallSource(source);
        Require(
            folderPreview.Disposition
            == HyperlibPackageInstallDisposition.Install,
            "A new source folder was not classified as an install.");

        var firstArchive = Path.Combine(archives, "sample.hyperlib");
        var firstPack = service.Pack(source, firstArchive);
        var duplicateArchive = Path.Combine(
            archives,
            "sample-copy.hyperlib");
        var duplicatePack = service.Pack(source, duplicateArchive);
        Require(
            firstPack.PackageSha256 == folderPreview.PackageSha256
            && firstPack.PackageSha256 == duplicatePack.PackageSha256,
            "Folder inspection and packed archives did not retain one package-content hash.");
        Require(
            ComputeFileSha256(firstArchive)
            == ComputeFileSha256(duplicateArchive),
            "Packing identical content did not produce deterministic archive bytes.");

        var archivePreview = service.InspectInstallSource(firstArchive);
        Require(
            archivePreview.Disposition
            == HyperlibPackageInstallDisposition.Install
            && archivePreview.PackageSha256 == firstPack.PackageSha256,
            "A valid archive did not inspect as the original package content.");
        var installed = service.Install(firstArchive, allowUpdate: false);
        Require(
            installed.Disposition
            == HyperlibPackageInstallDisposition.Install
            && Directory.Exists(installed.InstallDirectory),
            "The initial Hyperlib archive was not installed.");
        var installedSnapshot = new ExtensionDiscoveryService(extensions)
            .Discover();
        Require(
            installedSnapshot.ValidatedPackageCount == 1
            && installedSnapshot.LibraryCandidates.Count == 1
            && installedSnapshot.HyperlibEntryCount == 1,
            "Installed content did not become one validated Hyperlib entry.");
        Require(
            service.InspectInstallSource(firstArchive).Disposition
            == HyperlibPackageInstallDisposition.AlreadyInstalled,
            "Identical installed content was not recognized by hash.");

        WritePackage(source, "1.1.0", "Updated guide text");
        var updateArchive = Path.Combine(archives, "sample-1.1.0.hyperlib");
        service.Pack(source, updateArchive);
        var updatePreview = service.InspectInstallSource(updateArchive);
        Require(
            updatePreview.Disposition
            == HyperlibPackageInstallDisposition.Update
            && updatePreview.ExistingVersion == "1.0.0"
            && updatePreview.Version == "1.1.0",
            "Changed package content was not classified as a versioned update.");
        ExpectFailure(
            () => service.Install(updateArchive, allowUpdate: false),
            "An update installed without explicit update authorization.");
        var updated = service.Install(updateArchive, allowUpdate: true);
        Require(
            updated.Disposition
            == HyperlibPackageInstallDisposition.Update
            && Directory.Exists(updated.RecoveryDirectory),
            "The update did not preserve its previous package in recovery.");
        var updatedPackage = new ExtensionDiscoveryService(extensions)
            .Discover()
            .Packages
            .Single();
        Require(
            updatedPackage.Manifest?.Version == "1.1.0",
            "The active package did not advance to the update version.");

        var maliciousArchive = Path.Combine(
            archives,
            "path-escape.hyperlib");
        using (var stream = File.Create(maliciousArchive))
        using (var archive = new ZipArchive(
                   stream,
                   ZipArchiveMode.Create,
                   leaveOpen: false))
        {
            using var writer = new StreamWriter(
                archive.CreateEntry("../escape.txt").Open());
            writer.Write("escape");
        }

        ExpectFailure(
            () => service.InspectInstallSource(maliciousArchive),
            "An archive path traversal entry was not rejected.");
        Require(
            !File.Exists(Path.Combine(root, "escape.txt")),
            "Archive inspection wrote outside its staging root.");

        var removal = service.Remove(updatedPackage);
        Require(
            !Directory.EnumerateDirectories(extensions).Any()
            && Directory.Exists(removal.RecoveryDirectory),
            "Removal did not unload the package into recoverable storage.");
        Require(
            !Directory.Exists(staging)
            || !Directory.EnumerateFileSystemEntries(staging).Any(),
            "Package lifecycle operations left staging content behind.");

        VerifyExternalLanguagePolicies(root);
        await VerifySchemaTwoPackageAsync(root);

        Console.WriteLine(
            $"HYPERLIB_PACKAGE_LIFECYCLE_OK|{firstPack.FileCount}|{firstPack.UncompressedBytes}|{firstPack.PackageSha256}");
    }

    private static void VerifyExternalLanguagePolicies(string root)
    {
        var extensions = Path.Combine(root, "external-language-policy");
        WriteExternalPackage(
            Path.Combine(extensions, "python-valid"),
            "verifier.python-valid",
            "pythonGuides",
            "Python",
            "Python");
        WriteExternalPackage(
            Path.Combine(extensions, "ruby-unsupported"),
            "verifier.ruby-unsupported",
            "rubyGuides",
            "Ruby",
            "External");
        WriteExternalPackage(
            Path.Combine(extensions, "typescript-valid"),
            "verifier.typescript-valid",
            "typescriptGuides",
            "TypeScript",
            "TypeScript");
        WriteExternalPackage(
            Path.Combine(extensions, "java-valid"),
            "verifier.java-valid",
            "javaGuides",
            "Java",
            "Java");
        WriteExternalPackage(
            Path.Combine(extensions, "cpp-valid"),
            "verifier.cpp-valid",
            "cppGuides",
            "C++",
            "Cpp");
        WriteExternalPackage(
            Path.Combine(extensions, "python-wrong-runner"),
            "verifier.python-wrong-runner",
            "pythonWrongRunner",
            "Python",
            "NodeJs");
        WriteExternalPackage(
            Path.Combine(extensions, "typescript-wrong-runner"),
            "verifier.typescript-wrong-runner",
            "typescriptWrongRunner",
            "TypeScript",
            "NodeJs");
        WriteExternalPackage(
            Path.Combine(extensions, "html-guidance-valid"),
            "verifier.html-guidance-valid",
            "htmlGuides",
            "HTML",
            "External",
            isExecutionSupported: false);
        WriteExternalPackage(
            Path.Combine(extensions, "web-guidance-valid"),
            "verifier.web-guidance-valid",
            "webGuides",
            "Web APIs",
            "External",
            isExecutionSupported: false);
        WriteExternalPackage(
            Path.Combine(extensions, "html-executable-invalid"),
            "verifier.html-executable-invalid",
            "htmlExecutable",
            "HTML",
            "External");

        var snapshot = new ExtensionDiscoveryService(extensions).Discover();
        var validPython = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.python-valid");
        var unsupportedRuby = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.ruby-unsupported");
        var validTypeScript = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.typescript-valid");
        var validJava = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.java-valid");
        var validCpp = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.cpp-valid");
        var wrongPythonRunner = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.python-wrong-runner");
        var wrongTypeScriptRunner = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.typescript-wrong-runner");
        var validHtmlGuidance = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.html-guidance-valid");
        var validWebGuidance = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.web-guidance-valid");
        var invalidExecutableHtml = snapshot.Packages.Single(package =>
            package.Manifest?.Id == "verifier.html-executable-invalid");
        Require(
            validPython.Status == ExtensionPackageStatus.Validated
            && snapshot.LibraryCandidates.Any(candidate =>
                candidate.Package.Manifest?.Id == "verifier.python-valid"),
            "A Python Hyperlib package could not use the registered Python runner.");
        Require(
            validTypeScript.Status == ExtensionPackageStatus.Validated
            && snapshot.LibraryCandidates.Any(candidate =>
                candidate.Package.Manifest?.Id == "verifier.typescript-valid"),
            "A TypeScript Hyperlib package could not use the registered TypeScript runner.");
        Require(
            validJava.Status == ExtensionPackageStatus.Validated
            && snapshot.LibraryCandidates.Any(candidate =>
                candidate.Package.Manifest?.Id == "verifier.java-valid"),
            "A Java Hyperlib package could not use the registered compiled JDK runner.");
        Require(
            validCpp.Status == ExtensionPackageStatus.Validated
            && snapshot.LibraryCandidates.Any(candidate =>
                candidate.Package.Manifest?.Id == "verifier.cpp-valid"),
            "A C++ Hyperlib package could not use the registered compiled-language runner.");
        Require(
            unsupportedRuby.Status == ExtensionPackageStatus.Invalid
            && unsupportedRuby.Diagnostics.Any(diagnostic =>
                diagnostic.Code.EndsWith(
                    ".EXTERNAL_EXECUTION_UNAVAILABLE",
                    StringComparison.Ordinal)),
            "An executable package for an unregistered external language was accepted.");
        Require(
            wrongPythonRunner.Status == ExtensionPackageStatus.Invalid
            && wrongPythonRunner.Diagnostics.Any(diagnostic =>
                diagnostic.Code.EndsWith(
                    ".RUNTIME_TARGET_MISMATCH",
                    StringComparison.Ordinal)),
            "A Python Hyperlib package targeting the Node.js runner was accepted.");
        Require(
            wrongTypeScriptRunner.Status == ExtensionPackageStatus.Invalid
            && wrongTypeScriptRunner.Diagnostics.Any(diagnostic =>
                diagnostic.Code.EndsWith(
                    ".RUNTIME_TARGET_MISMATCH",
                    StringComparison.Ordinal)),
            "A TypeScript Hyperlib package targeting the JavaScript runner was accepted.");
        Require(
            validHtmlGuidance.Status == ExtensionPackageStatus.Validated
            && snapshot.LibraryCandidates.Any(candidate =>
                candidate.Package.Manifest?.Id == "verifier.html-guidance-valid"),
            "A non-executable HTML guidance package was not accepted.");
        Require(
            validWebGuidance.Status == ExtensionPackageStatus.Validated
            && snapshot.LibraryCandidates.Any(candidate =>
                candidate.Package.Manifest?.Id == "verifier.web-guidance-valid"),
            "A non-executable Web API guidance package was not accepted.");
        Require(
            invalidExecutableHtml.Status == ExtensionPackageStatus.Invalid
            && invalidExecutableHtml.Diagnostics.Any(diagnostic =>
                diagnostic.Code.EndsWith(
                    ".EXTERNAL_EXECUTION_UNAVAILABLE",
                    StringComparison.Ordinal)),
            "An HTML guidance package was allowed to claim terminal execution.");
    }

    private static async Task VerifySchemaTwoPackageAsync(string root)
    {
        var extensions = Path.Combine(root, "schema-two");
        var packageDirectory = Path.Combine(
            extensions,
            "verifier.schema-two");
        var contentDirectory = Path.Combine(packageDirectory, "content");
        var entryDirectory = Path.Combine(contentDirectory, "entries");
        var guideDirectory = Path.Combine(contentDirectory, "guides");
        Directory.CreateDirectory(entryDirectory);
        Directory.CreateDirectory(guideDirectory);
        File.WriteAllText(
            Path.Combine(packageDirectory, "hypershell.extension.json"),
            """
            {
              "schemaVersion": 1,
              "id": "verifier.schema-two",
              "displayName": "Schema two verifier",
              "publisher": "Hypershell Verifier",
              "version": "2.0.0",
              "description": "Schema-v2 sharded catalog verification.",
              "runtime": "ContentOnly",
              "requestedCapabilities": ["LibraryContent"],
              "contributions": {
                "libraries": [
                  {
                    "id": "pythonCatalog",
                    "displayName": "Python schema two catalog",
                    "contentPath": "content/hyperlib.json",
                    "languageIds": ["python"],
                    "contentOrigin": "HumanAuthored",
                    "reviewState": "Reviewed",
                    "provenance": "Synthetic schema-v2 verifier content.",
                    "reviewedBy": "Hypershell verifier"
                  }
                ]
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(contentDirectory, "hyperlib.json"),
            """
            {
              "schemaVersion": 2,
              "contentVersion": "2.0.0",
              "id": "pythonCatalog",
              "displayName": "Python schema two catalog",
              "description": "One lazily loaded schema-v2 Python catalog entry.",
              "language": "Python",
              "kind": "Package",
              "canonicalName": "Verifier Python Package",
              "ecosystem": "Python",
              "targetVersion": "Python 3",
              "sourceVersion": "Verifier 2.0.0",
              "sourceUrl": "https://docs.python.org/3/",
              "license": "Verifier test content",
              "isExecutionSupported": true,
              "createStandaloneProfile": true,
              "executionTarget": "External",
              "declaredEntryCount": 1,
              "entryFiles": ["entries/python.entries.json"],
              "declaredGuideCount": 1,
              "guideFiles": ["guides/python.guides.json"]
            }
            """);
        File.WriteAllText(
            Path.Combine(entryDirectory, "python.entries.json"),
            """
            {
              "schemaVersion": 2,
              "libraryId": "pythonCatalog",
              "entries": [
                {
                  "id": "verifier.print",
                  "name": "print",
                  "displayName": "Print a verifier value",
                  "description": "Writes one verifier value.",
                  "definition": "Writes one verifier value.",
                  "moduleName": "Built-ins",
                  "commandType": "Function",
                  "preferredShellKind": "Python",
                  "documentationUrl": "https://docs.python.org/3/library/functions.html#print",
                  "symbolKind": "BuiltIn",
                  "coverageTier": "Guided",
                  "browsePath": [
                    { "kind": "Built-in", "name": "print" }
                  ],
                  "guideFile": "guides/python.guides.json",
                  "template": "print({value})",
                  "compositionMode": "Statement",
                  "parameters": [],
                  "examples": []
                }
              ]
            }
            """);
        File.WriteAllText(
            Path.Combine(guideDirectory, "python.guides.json"),
            """
            {
              "schemaVersion": 2,
              "libraryId": "pythonCatalog",
              "guides": [
                {
                  "entryId": "verifier.print",
                  "definition": "Python's print built-in writes the supplied value to standard output.",
                  "parameters": [
                    {
                      "name": "value",
                      "displayName": "Value",
                      "placeholder": "value",
                      "description": "A Python expression to display.",
                      "typeName": "Python expression",
                      "defaultValue": "42",
                      "isRequired": true,
                      "includeByDefault": true
                    }
                  ],
                  "examples": [
                    {
                      "title": "Print 42",
                      "code": "print(42)",
                      "explanation": "The integer is written to standard output."
                    }
                  ]
                }
              ]
            }
            """);

        var snapshot = new ExtensionDiscoveryService(extensions).Discover();
        Require(
            snapshot.ValidatedPackageCount == 1
            && snapshot.LibraryCandidates.Count == 1,
            "A valid sharded schema-v2 Hyperlib package did not validate.");
        var settingsService = new SettingsService(Path.Combine(
            root,
            "schema-two-settings"));
        var catalogService = new CommandCatalogService(
            settingsService,
            snapshot);
        var library = (await catalogService.LoadAsync()).Single(candidate =>
            candidate.Id.EndsWith(
                ".pythonCatalog",
                StringComparison.OrdinalIgnoreCase));
        Require(
            library.SchemaVersion == 2
            && library.HasDeferredEntries
            && library.Commands.Count == 0,
            "The validated schema-v2 package eagerly loaded its entry shard.");
        var commands = await catalogService.LoadLibraryEntriesAsync(library);
        var command = commands.Single();
        Require(
            command.HasDeferredGuide
            && command.Parameters.Count == 0
            && command.Examples.Count == 0,
            "The schema-v2 package eagerly loaded its guide shard.");
        await catalogService.LoadCommandGuideAsync(command);
        Require(
            command.IsGuideLoaded
            && command.Parameters.Count == 1
            && command.Examples.Count == 1,
            "The schema-v2 package guide did not hydrate on demand.");
    }

    private static void WriteExternalPackage(
        string packageDirectory,
        string extensionId,
        string contributionId,
        string language,
        string preferredShellKind,
        bool isExecutionSupported = true)
    {
        var executionSupportedJson = isExecutionSupported
            ? "true"
            : "false";
        var contentDirectory = Path.Combine(packageDirectory, "content");
        Directory.CreateDirectory(contentDirectory);
        File.WriteAllText(
            Path.Combine(packageDirectory, "hypershell.extension.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{extensionId}}",
              "displayName": "External language verifier",
              "publisher": "Hypershell Verifier",
              "version": "1.0.0",
              "description": "External language execution policy verification.",
              "runtime": "ContentOnly",
              "requestedCapabilities": ["LibraryContent"],
              "contributions": {
                "libraries": [
                  {
                    "id": "{{contributionId}}",
                    "displayName": "{{language}} Guides",
                    "contentPath": "content",
                    "languageIds": ["{{language.ToLowerInvariant()}}"],
                    "contentOrigin": "HumanAuthored",
                    "reviewState": "Reviewed",
                    "provenance": "Synthetic host-verifier content.",
                    "reviewedBy": "Hypershell verifier"
                  }
                ]
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(contentDirectory, "hyperlib.json"),
            $$"""
            {
              "schemaVersion": 1,
              "contentVersion": "1.0.0",
              "id": "{{contributionId}}",
              "displayName": "{{language}} Guides",
              "description": "One external language policy verification entry.",
              "language": "{{language}}",
              "kind": "Content",
              "isExecutionSupported": {{executionSupportedJson}},
              "createStandaloneProfile": true,
              "executionTarget": "External",
              "commands": [
                {
                  "id": "sample",
                  "name": "sample",
                  "displayName": "Sample",
                  "description": "A sample language statement.",
                  "definition": "A sample language statement for policy verification.",
                  "preferredShellKind": "{{preferredShellKind}}",
                  "executionTarget": "External",
                  "compositionMode": "Statement",
                  "template": "print({value})",
                  "parameters": [
                    {
                      "name": "value",
                      "displayName": "Value",
                      "description": "A sample value.",
                      "defaultValue": "42"
                    }
                  ],
                  "examples": [
                    {
                      "title": "Sample",
                      "code": "print(42)",
                      "explanation": "Displays a sample value."
                    }
                  ]
                }
              ]
            }
            """);
    }

    private static void WritePackage(
        string packageDirectory,
        string version,
        string guideText)
    {
        var contentDirectory = Path.Combine(packageDirectory, "content");
        Directory.CreateDirectory(contentDirectory);
        File.WriteAllText(
            Path.Combine(packageDirectory, "hypershell.extension.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "verifier.hyperlib-sample",
              "displayName": "Verifier Hyperlib Sample",
              "publisher": "Hypershell Verifier",
              "version": "{{version}}",
              "description": "Static package lifecycle verification content.",
              "runtime": "ContentOnly",
              "requestedCapabilities": ["LibraryContent"],
              "contributions": {
                "libraries": [
                  {
                    "id": "sampleGuides",
                    "displayName": "Sample Guides",
                    "contentPath": "content",
                    "languageIds": ["powershell"],
                    "contentOrigin": "HumanAuthored",
                    "reviewState": "Reviewed",
                    "provenance": "Synthetic host-verifier content.",
                    "reviewedBy": "Hypershell verifier"
                  }
                ]
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(contentDirectory, "hyperlib.json"),
            $$"""
            {
              "schemaVersion": 1,
              "contentVersion": "{{version}}",
              "id": "sampleGuides",
              "displayName": "Sample Guides",
              "description": "One package lifecycle verification guide.",
              "language": "PowerShell",
              "kind": "Content",
              "isExecutionSupported": true,
              "createStandaloneProfile": true,
              "executionTarget": "PowerShell",
              "commands": [
                {
                  "id": "getDate",
                  "name": "Get-Date",
                  "displayName": "Get-Date",
                  "description": "Read the current date and time.",
                  "definition": "{{guideText}}",
                  "template": "Get-Date",
                  "examples": [
                    {
                      "title": "Show the current date",
                      "code": "Get-Date",
                      "explanation": "Returns the current date and time."
                    }
                  ]
                }
              ]
            }
            """);
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
