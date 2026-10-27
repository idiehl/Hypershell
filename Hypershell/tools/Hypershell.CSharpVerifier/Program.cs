using Hypershell.Extensibility;
using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

var fakeFormatterIndex = Array.FindIndex(
    args,
    argument => string.Equals(
        argument,
        "--fake-formatter",
        StringComparison.OrdinalIgnoreCase));
if (args.Contains("--fake-mynet-host", StringComparer.OrdinalIgnoreCase))
{
    await MyNetDesktopProcessVerifier.RunFakeHostAsync(args);
    return;
}

if (args.Contains("--mynet-desktop-ui-only", StringComparer.OrdinalIgnoreCase))
{
    var previewRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "mynet-desktop-qa"));
    Directory.CreateDirectory(previewRoot);
    await MyNetDesktopWindowVerifier.VerifyAsync(previewRoot);
    return;
}

if (args.Contains("--meridian-browser-only", StringComparer.OrdinalIgnoreCase))
{
    var browserRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "meridian-browser-qa"));
    Directory.CreateDirectory(browserRoot);
    await MyNetAppWindowVerifier.VerifyAsync(browserRoot, websiteOnly: true);
    return;
}

if (args.Contains("--mynet-desktop-only", StringComparer.OrdinalIgnoreCase))
{
    var desktopRoot = Path.Combine(Path.GetTempPath(), "Hypershell", "mynet-desktop-verifier", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(desktopRoot);
    try
    {
        MyNetDesktopStoreVerifier.Verify(desktopRoot);
        await MyNetDesktopHostVerifier.VerifyAsync(desktopRoot);
        await MyNetDesktopProcessVerifier.VerifyAsync(desktopRoot);
        await MyNetAppWindowVerifier.VerifyAsync(desktopRoot);
    }
    finally
    {
        // WebView2 can release its data files shortly after the window closes.
        try { Directory.Delete(desktopRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    return;
}

if (fakeFormatterIndex >= 0)
{
    var mode = fakeFormatterIndex + 1 < args.Length
        ? args[fakeFormatterIndex + 1]
        : "format";
    var input = await Console.In.ReadToEndAsync();
    switch (mode.ToLowerInvariant())
    {
        case "format":
            Console.Write(input.Replace("  ", " ", StringComparison.Ordinal));
            break;
        case "delay":
            await Task.Delay(TimeSpan.FromSeconds(5));
            Console.Write(input);
            break;
        case "flood":
            Console.Write(new string('x', 4096));
            break;
        case "fail":
            Console.Error.Write("synthetic formatter failure");
            Environment.ExitCode = 17;
            break;
        default:
            Console.Error.Write("unknown fake formatter mode");
            Environment.ExitCode = 18;
            break;
    }

    return;
}

if (args.Contains("--fake-native-mynet-window", StringComparer.OrdinalIgnoreCase))
{
    NativeMyNetAppVerifier.RunFakeWindow(args);
    return;
}
if (args.Contains("--native-mynet-only", StringComparer.OrdinalIgnoreCase))
{
    var root = Path.Combine(Path.GetTempPath(), "Hypershell", "native-mynet-verifier", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    await NativeMyNetAppVerifier.VerifyAsync(root);
    return;
}
if (args.Contains("--fake-java-compiler", StringComparer.OrdinalIgnoreCase))
{
    var outputIndex = Array.FindIndex(args, argument => argument == "-d");
    var sourcePath = args.LastOrDefault(argument =>
        string.Equals(
            Path.GetExtension(argument),
            ".java",
            StringComparison.OrdinalIgnoreCase));
    if (outputIndex < 0
        || outputIndex + 1 >= args.Length
        || string.IsNullOrWhiteSpace(sourcePath)
        || !File.Exists(sourcePath))
    {
        Console.Error.Write("synthetic javac received an invalid argument list");
        Environment.ExitCode = 30;
        return;
    }

    var outputDirectory = Path.GetFullPath(args[outputIndex + 1]);
    var source = await File.ReadAllTextAsync(sourcePath);
    if (source.Contains("COMPILE_ERROR", StringComparison.Ordinal))
    {
        Console.Error.Write($"{sourcePath}:1: error: synthetic compilation failure");
        Environment.ExitCode = 31;
        return;
    }

    Directory.CreateDirectory(outputDirectory);
    await File.WriteAllTextAsync(
        Path.Combine(outputDirectory, "hypershell.fake.class"),
        source,
        new UTF8Encoding(false));
    Console.Error.Write("synthetic javac warning");
    return;
}

if (args.Contains("--fake-java-runtime", StringComparer.OrdinalIgnoreCase))
{
    var classPathIndex = Array.FindIndex(args, argument => argument == "-cp");
    var entryPoint = args.LastOrDefault() ?? string.Empty;
    if (classPathIndex < 0
        || classPathIndex + 1 >= args.Length
        || string.IsNullOrWhiteSpace(entryPoint))
    {
        Console.Error.Write("synthetic java received an invalid argument list");
        Environment.ExitCode = 32;
        return;
    }

    var markerPath = Path.Combine(
        Path.GetFullPath(args[classPathIndex + 1]),
        "hypershell.fake.class");
    if (!File.Exists(markerPath))
    {
        Console.Error.Write("synthetic java could not find compiled output");
        Environment.ExitCode = 33;
        return;
    }

    var source = await File.ReadAllTextAsync(markerPath);
    if (source.Contains("RUNTIME_ERROR", StringComparison.Ordinal))
    {
        Console.Error.Write("synthetic Java runtime failure");
        Environment.ExitCode = 34;
        return;
    }

    Console.Write($"JAVA_FAKE_OK|{entryPoint}");
    return;
}

if (args.Contains("--fake-language-server", StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("fake language server ready");
    await Console.In.ReadToEndAsync();
    return;
}

if (args.Contains(
        "--network-observatory-only",
        StringComparer.OrdinalIgnoreCase))
{
    var networkObservatoryRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "network-observatory-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(networkObservatoryRoot);
    try
    {
        await NetworkObservatoryVerifier.VerifyAsync(
            networkObservatoryRoot);
    }
    finally
    {
        Directory.Delete(networkObservatoryRoot, recursive: true);
    }

    return;
}

if (args.Contains("--atlas-mynet-only", StringComparer.OrdinalIgnoreCase))
{
    var atlasMyNetRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "atlas-mynet-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(atlasMyNetRoot);
    try
    {
        await AtlasMyNetVerifier.VerifyAsync(atlasMyNetRoot);
    }
    finally
    {
        Directory.Delete(atlasMyNetRoot, recursive: true);
    }

    return;
}

if (args.Contains("--project-work-items-only", StringComparer.OrdinalIgnoreCase))
{
    var workItemsRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "project-work-items-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workItemsRoot);
    try
    {
        ProjectWorkItemVerifier.Verify(workItemsRoot);
    }
    finally
    {
        Directory.Delete(workItemsRoot, recursive: true);
    }

    return;
}

if (args.Contains("--workspace-chrome-only", StringComparer.OrdinalIgnoreCase))
{
    WorkspaceChromeVerifier.Verify();
    return;
}

if (args.Contains("--profile-avatar-only", StringComparer.OrdinalIgnoreCase))
{
    var avatarRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "profile-avatar-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(avatarRoot);
    try
    {
        ProfileAvatarVerifier.Verify(avatarRoot);
    }
    finally
    {
        Directory.Delete(avatarRoot, recursive: true);
    }

    return;
}

if (args.Contains("--explorer-projects-only", StringComparer.OrdinalIgnoreCase))
{
    var explorerProjectsRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "explorer-projects-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(explorerProjectsRoot);
    try
    {
        await ExplorerProjectVerifier.VerifyAsync(explorerProjectsRoot);
    }
    finally
    {
        Directory.Delete(explorerProjectsRoot, recursive: true);
    }

    return;
}

var hyperlibInspectIndex = Array.FindIndex(
    args,
    argument => string.Equals(
        argument,
        "--hyperlib-inspect",
        StringComparison.OrdinalIgnoreCase));
if (hyperlibInspectIndex >= 0)
{
    if (hyperlibInspectIndex + 1 >= args.Length)
    {
        throw new InvalidOperationException(
            "Pass a .hyperlib archive or unpacked package folder after --hyperlib-inspect.");
    }

    var inspectRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "hyperlib-inspect",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(inspectRoot);
    try
    {
        var service = new HyperlibPackageService(
            Path.Combine(inspectRoot, "extensions"),
            Path.Combine(inspectRoot, "staging"),
            Path.Combine(inspectRoot, "recovery"));
        var preview = service.InspectInstallSource(
            args[hyperlibInspectIndex + 1]);
        Console.WriteLine(
            $"HYPERLIB_PACKAGE_VALID|{preview.ExtensionId}|{preview.Version}|{preview.PackageSha256}|{preview.Disposition}");
    }
    finally
    {
        Directory.Delete(inspectRoot, recursive: true);
    }

    return;
}

if (args.Contains("--profile-portability-only", StringComparer.OrdinalIgnoreCase))
{
    var profileRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "profile-portability-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(profileRoot);
    try
    {
        await ProfilePortabilityVerifier.VerifyAsync(profileRoot);
    }
    finally
    {
        Directory.Delete(profileRoot, recursive: true);
    }

    return;
}

if (args.Contains("--workspace-ai-only", StringComparer.OrdinalIgnoreCase))
{
    var workspaceAiRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "workspace-ai-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspaceAiRoot);
    try
    {
        await WorkspaceAiPermissionVerifier.VerifyAsync(workspaceAiRoot);
    }
    finally
    {
        Directory.Delete(workspaceAiRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--workspace-ai-context-only",
        StringComparer.OrdinalIgnoreCase))
{
    var contextRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "workspace-ai-context-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(contextRoot);
    try
    {
        await WorkspaceAiContextVerifier.VerifyAsync(contextRoot);
    }
    finally
    {
        Directory.Delete(contextRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--workspace-ai-provider-only",
        StringComparer.OrdinalIgnoreCase))
{
    var workspaceAiProviderRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "workspace-ai-provider-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspaceAiProviderRoot);
    try
    {
        await WorkspaceAiProviderVerifier.VerifyAsync(
            workspaceAiProviderRoot);
    }
    finally
    {
        Directory.Delete(workspaceAiProviderRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--workspace-ai-execution-only",
        StringComparer.OrdinalIgnoreCase))
{
    var executionRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "workspace-ai-execution-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(executionRoot);
    try
    {
        WorkspaceAiExecutionVerifier.Verify(executionRoot);
    }
    finally
    {
        Directory.Delete(executionRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--workspace-ai-local-runtime-only",
        StringComparer.OrdinalIgnoreCase))
{
    await WorkspaceAiProviderVerifier.VerifyLocalRuntimeAsync();
    return;
}

if (args.Contains(
        "--workspace-ai-embedding-runtime-only",
        StringComparer.OrdinalIgnoreCase))
{
    await WorkspaceAiProviderVerifier.VerifyEmbeddingRuntimeAsync();
    return;
}

if (args.Contains(
        "--hyperlib-semantic-runtime-only",
        StringComparer.OrdinalIgnoreCase))
{
    var semanticRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "hyperlib-semantic-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(semanticRoot);
    try
    {
        await WorkspaceAiProviderVerifier.VerifyHyperlibSemanticAsync(
            semanticRoot);
    }
    finally
    {
        Directory.Delete(semanticRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--remote-workspace-ai-only",
        StringComparer.OrdinalIgnoreCase))
{
    var remoteAiRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "remote-workspace-ai-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(remoteAiRoot);
    try
    {
        await RemoteWorkspaceAiVerifier.VerifyAsync(remoteAiRoot);
    }
    finally
    {
        Directory.Delete(remoteAiRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--workspace-ai-git-only",
        StringComparer.OrdinalIgnoreCase))
{
    var workspaceAiGitRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "workspace-ai-git-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspaceAiGitRoot);
    try
    {
        await WorkspaceAiGitVerifier.VerifyAsync(workspaceAiGitRoot);
    }
    finally
    {
        GitRepositoryVerifier.DeleteVerifierRoot(workspaceAiGitRoot);
    }

    return;
}

if (args.Contains("--git-only", StringComparer.OrdinalIgnoreCase))
{
    var gitRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "git-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(gitRoot);
    try
    {
        await GitRepositoryVerifier.VerifyAsync(gitRoot);
    }
    finally
    {
        GitRepositoryVerifier.DeleteVerifierRoot(gitRoot);
    }

    return;
}

if (args.Contains("--hardening-only", StringComparer.OrdinalIgnoreCase))
{
    var hardeningRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "hardening-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(hardeningRoot);
    try
    {
        await ProfileHardeningVerifier.VerifyAsync(hardeningRoot);
    }
    finally
    {
        Directory.Delete(hardeningRoot, recursive: true);
    }

    return;
}

if (args.Contains("--profile-sync-only", StringComparer.OrdinalIgnoreCase))
{
    var profileSyncRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "profile-sync-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(profileSyncRoot);
    try
    {
        await ProfileSyncVerifier.VerifyAsync(profileSyncRoot);
    }
    finally
    {
        Directory.Delete(profileSyncRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--profile-production-smoke",
        StringComparer.OrdinalIgnoreCase))
{
    var endpoint = Environment.GetEnvironmentVariable(
        "HYPERSHELL_PROFILE_SMOKE_ENDPOINT");
    var accountId = Environment.GetEnvironmentVariable(
        "HYPERSHELL_PROFILE_SMOKE_ACCOUNT_ID");
    var accessKey = Environment.GetEnvironmentVariable(
        "HYPERSHELL_PROFILE_SMOKE_ACCESS_KEY");
    if (string.IsNullOrWhiteSpace(endpoint)
        || string.IsNullOrWhiteSpace(accountId)
        || string.IsNullOrWhiteSpace(accessKey))
    {
        throw new InvalidOperationException(
            "Set HYPERSHELL_PROFILE_SMOKE_ENDPOINT, HYPERSHELL_PROFILE_SMOKE_ACCOUNT_ID, and HYPERSHELL_PROFILE_SMOKE_ACCESS_KEY for the read-only production smoke test.");
    }

    var productionSmokeRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "profile-production-smoke-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(productionSmokeRoot);
    try
    {
        await ProfileProductionSmokeVerifier.VerifyAsync(
            productionSmokeRoot,
            endpoint,
            accountId,
            accessKey);
    }
    finally
    {
        Directory.Delete(productionSmokeRoot, recursive: true);
    }

    return;
}

if (args.Contains("--profile-server-only", StringComparer.OrdinalIgnoreCase))
{
    var profileServerRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "profile-server-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(profileServerRoot);
    try
    {
        await ProfileServerVerifier.VerifyAsync(profileServerRoot);
    }
    finally
    {
        Directory.Delete(profileServerRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--workspace-filesystems-only",
        StringComparer.OrdinalIgnoreCase))
{
    var workspaceRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "workspace-filesystem-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspaceRoot);
    try
    {
        await WorkspaceFileSystemVerifier.VerifyAsync(workspaceRoot);
    }
    finally
    {
        Directory.Delete(workspaceRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--workspace-documents-only",
        StringComparer.OrdinalIgnoreCase))
{
    var workspaceRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "workspace-document-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspaceRoot);
    try
    {
        await WorkspaceDocumentVerifier.VerifyAsync(workspaceRoot);
    }
    finally
    {
        Directory.Delete(workspaceRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--remote-terminals-only",
        StringComparer.OrdinalIgnoreCase))
{
    await RemoteTerminalVerifier.VerifyAsync();
    return;
}

if (args.Contains("--document-providers-only", StringComparer.OrdinalIgnoreCase))
{
    var documentProviderRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "document-provider-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(documentProviderRoot);
    try
    {
        await VerifyDocumentProvidersAsync(documentProviderRoot);
    }
    finally
    {
        Directory.Delete(documentProviderRoot, recursive: true);
    }

    return;
}

if (args.Contains("--language-tooling-only", StringComparer.OrdinalIgnoreCase))
{
    var languageToolingRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "language-tooling-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(languageToolingRoot);
    try
    {
        await VerifyLanguageToolingAsync(languageToolingRoot);
    }
    finally
    {
        Directory.Delete(languageToolingRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--official-language-packs-only",
        StringComparer.OrdinalIgnoreCase))
{
    var languagePackRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "official-language-pack-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(languagePackRoot);
    try
    {
        await OfficialLanguagePackVerifier.VerifyAsync(languagePackRoot);
    }
    finally
    {
        Directory.Delete(languagePackRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--hyperlib-packages-only",
        StringComparer.OrdinalIgnoreCase))
{
    var hyperlibPackageRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "hyperlib-package-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(hyperlibPackageRoot);
    try
    {
        await HyperlibPackageVerifier.VerifyAsync(hyperlibPackageRoot);
    }
    finally
    {
        Directory.Delete(hyperlibPackageRoot, recursive: true);
    }

    return;
}

if (args.Contains("--editor-only", StringComparer.OrdinalIgnoreCase))
{
    var editorRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "editor-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(editorRoot);
    try
    {
        await VerifyEditorReliabilityAsync(editorRoot);
    }
    finally
    {
        Directory.Delete(editorRoot, recursive: true);
    }

    return;
}

if (args.Contains("--composer-only", StringComparer.OrdinalIgnoreCase))
{
    var composerRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "csharp-composer-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(composerRoot);
    try
    {
        await VerifyCSharpComposerAsync(composerRoot);
    }
    finally
    {
        Directory.Delete(composerRoot, recursive: true);
    }

    return;
}

if (args.Contains(
        "--command-builder-language-only",
        StringComparer.OrdinalIgnoreCase))
{
    var builderRoot = Path.Combine(
        Path.GetTempPath(),
        "Hypershell",
        "command-builder-language-verifier",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(builderRoot);
    try
    {
        await VerifyCatalogSearchAsync(builderRoot, languageOnly: true);
    }
    finally
    {
        Directory.Delete(builderRoot, recursive: true);
    }

    return;
}

if (args.Contains("--api-index-only", StringComparer.OrdinalIgnoreCase))
{
    await VerifyDotNetApiIndexAsync();
    return;
}

if (args.Contains(
        "--official-frameworks-only",
        StringComparer.OrdinalIgnoreCase))
{
    await VerifyOfficialFrameworkCatalogAsync();
    return;
}

if (args.Contains(
        "--powershell-native-help-only",
        StringComparer.OrdinalIgnoreCase))
{
    using var cancellation =
        new CancellationTokenSource(TimeSpan.FromSeconds(15));
    using var session = new PowerShellTerminalSession();
    var result = await session.ExecuteAsync(
        "dotnet build --help",
        Environment.CurrentDirectory,
        cancellation.Token);
    Console.WriteLine(
        $"POWERSHELL_NATIVE_HELP_OK|{result.Output.Count}|" +
        $"{result.Errors.Count}");
    return;
}

if (args.Contains(
        "--toolchain-list",
        StringComparer.OrdinalIgnoreCase))
{
    foreach (var library in new DotNetToolchainCatalogService().Discover())
    {
        Console.WriteLine(
            $"{library.DisplayName}|{library.FrameworkVersion}|" +
            $"{library.Commands.Count}");
        foreach (var command in library.Commands)
        {
            Console.WriteLine(
                $"  {command.Action}|{command.Name}|" +
                $"{command.Description}");
        }
    }

    return;
}

var verifierRoot = Path.Combine(
    Path.GetTempPath(),
    "Hypershell",
    "csharp-verifier",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(verifierRoot);

try
{
    Console.WriteLine("VERIFY_STAGE|workspace-chrome");
    WorkspaceChromeVerifier.Verify();
    Console.WriteLine("VERIFY_STAGE|profile-avatar");
    ProfileAvatarVerifier.Verify(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|atlas-mynet");
    await AtlasMyNetVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|editor-reliability");
    await VerifyEditorReliabilityAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|document-providers");
    await VerifyDocumentProvidersAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|language-tooling");
    await VerifyLanguageToolingAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|official-language-packs");
    await OfficialLanguagePackVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|profile-portability");
    await ProfilePortabilityVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|profile-sync");
    await ProfileSyncVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|profile-server");
    await ProfileServerVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|profile-hardening");
    await ProfileHardeningVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|workspace-filesystems");
    await WorkspaceFileSystemVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|workspace-documents");
    await WorkspaceDocumentVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|remote-terminals");
    await RemoteTerminalVerifier.VerifyAsync();
    Console.WriteLine("VERIFY_STAGE|workspace-ai");
    await WorkspaceAiPermissionVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|workspace-ai-contexts");
    await WorkspaceAiContextVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|workspace-ai-provider");
    await WorkspaceAiProviderVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|workspace-ai-execution");
    WorkspaceAiExecutionVerifier.Verify(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|remote-workspace-ai");
    await RemoteWorkspaceAiVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|git-repository");
    await GitRepositoryVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|workspace-ai-git");
    await WorkspaceAiGitVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|panel-messaging");
    await VerifyPanelMessagingAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|hyperlib-package-lifecycle");
    await HyperlibPackageVerifier.VerifyAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|library-boundary");
    await VerifyLibraryBoundaryAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|csharp-composer");
    await VerifyCSharpComposerAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|terminal-tabs");
    await VerifyTerminalTabsAsync(verifierRoot);
    Console.WriteLine("VERIFY_STAGE|csharp-runner");
    await VerifyCSharpRunnerAsync(verifierRoot);
    Console.WriteLine("Editor reliability, document providers, language tooling, profile portability and sync, workspace AI permissions, panel messaging, C# catalog, composer, terminal, and runner verification passed.");
}
finally
{
    try
    {
        GitRepositoryVerifier.DeleteVerifierRoot(verifierRoot);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

static async Task VerifyEditorReliabilityAsync(string verifierRoot)
{
    var editorRoot = Path.Combine(verifierRoot, "editor-reliability");
    var documentsRoot = Path.Combine(editorRoot, "documents");
    var appDataRoot = Path.Combine(editorRoot, "app-data");
    Directory.CreateDirectory(documentsRoot);

    var documentService = new TextDocumentService();
    var utf8BomPath = Path.Combine(documentsRoot, "utf8-bom.txt");
    await File.WriteAllBytesAsync(
        utf8BomPath,
        EncodeWithPreamble(
            new UTF8Encoding(true, true),
            "alpha\r\nbeta\r\n"));
    var utf8Bom = await documentService.LoadAsync(utf8BomPath);
    Require(
        utf8Bom.EncodingKind == DocumentEncodingKind.Utf8WithBom
        && utf8Bom.LineEndingKind == DocumentLineEndingKind.CrLf,
        "UTF-8 BOM or CRLF detection failed.");

    var utf16Path = Path.Combine(documentsRoot, "utf16-be.txt");
    await File.WriteAllBytesAsync(
        utf16Path,
        EncodeWithPreamble(
            new UnicodeEncoding(true, true, true),
            "alpha\nbeta\n"));
    var utf16 = await documentService.LoadAsync(utf16Path);
    Require(
        utf16.EncodingKind == DocumentEncodingKind.Utf16BigEndian
        && utf16.LineEndingKind == DocumentLineEndingKind.Lf,
        "UTF-16 BE or LF detection failed.");

    var mixedPath = Path.Combine(documentsRoot, "mixed.txt");
    await File.WriteAllTextAsync(
        mixedPath,
        "alpha\r\nbeta\ngamma\r",
        new UTF8Encoding(false));
    var mixed = await documentService.LoadAsync(mixedPath);
    Require(
        mixed.LineEndingKind == DocumentLineEndingKind.Preserve,
        "Mixed line endings were not marked for preservation.");
    var mixedDocument = await DocumentTabViewModel.OpenAsync(
        mixedPath,
        documentService);
    mixedDocument.Content = "alpha edited\nbeta\ngamma\n";
    Require(
        mixedDocument.Content == "alpha edited\r\nbeta\ngamma\r",
        "The host did not reapply the mixed-EOL map after Monaco normalization.");
    await mixedDocument.SaveAsync(mixedPath);
    Require(
        (await documentService.LoadAsync(mixedPath)).Text ==
        "alpha edited\r\nbeta\ngamma\r",
        "Preserve mixed normalized line endings unexpectedly on save.");

    var editorSettings = new DocumentEditorSettings
    {
        LanguageId = "csharp",
        TrimTrailingWhitespaceOnSave = true,
        InsertFinalNewlineOnSave = true
    };
    var document = DocumentTabViewModel.CreateUntitled(
        new NewDocumentRequest(
            "sample.cs",
            editorSettings,
            DocumentEncodingKind.Utf16BigEndian,
            DocumentLineEndingKind.Lf),
        documentService);
    document.Content = "alpha  \r\nbeta\t";
    document.EditorViewState = "{\"position\":{\"lineNumber\":2,\"column\":3}}";
    var savedPath = Path.Combine(documentsRoot, "sample.cs");
    await document.SaveAsync(savedPath);
    var normalized = await documentService.LoadAsync(savedPath);
    Require(
        normalized.EncodingKind == DocumentEncodingKind.Utf16BigEndian
        && normalized.LineEndingKind == DocumentLineEndingKind.Lf
        && normalized.Text == "alpha\nbeta\n"
        && !document.HasUnsavedChanges,
        "Explicit encoding/EOL save normalization failed.");

    document.EncodingKind = DocumentEncodingKind.Utf8WithBom;
    document.LineEndingKind = DocumentLineEndingKind.CrLf;
    Require(
        document.HasUnsavedChanges,
        "Changing document encoding/EOL did not mark the document dirty.");
    await document.SaveAsync(savedPath);
    var converted = await documentService.LoadAsync(savedPath);
    Require(
        converted.EncodingKind == DocumentEncodingKind.Utf8WithBom
        && converted.LineEndingKind == DocumentLineEndingKind.CrLf
        && converted.Text == "alpha\r\nbeta\r\n",
        "Encoding/EOL conversion did not persist.");

    document.Content += "unsaved\r\n";
    var settingsService = new SettingsService(appDataRoot);
    var sessionService = new EditorSessionService(
        settingsService.EditorSessionPath,
        settingsService.EditorRecoveryDirectory);
    var entry = document.CreateSessionEntry();
    var recovery = document.CreateRecoverySnapshot();
    var session = new EditorSessionSnapshot
    {
        ActiveDocumentSessionId = document.SessionId,
        Documents = [entry]
    };
    var saveResult = await sessionService.SaveAsync(session, [recovery]);
    Require(
        saveResult.RecoveryCount == 1
        && saveResult.SkippedRecoverySessionIds.Count == 0,
        "Dirty recovery snapshot was not persisted.");

    var loadedSession = await sessionService.LoadSessionAsync();
    var loadedRecovery = await sessionService.LoadRecoveryAsync(document.SessionId);
    Require(
        loadedSession.Documents.Count == 1
        && loadedSession.Documents[0].SessionId == document.SessionId
        && loadedSession.ActiveDocumentSessionId == document.SessionId
        && loadedRecovery?.Content == document.Content,
        "Session order, active document, or recovery content did not round-trip.");
    var restored = await DocumentTabViewModel.RestoreAsync(
        loadedSession.Documents[0],
        loadedRecovery,
        documentService);
    Require(
        restored is
        {
            HasUnsavedChanges: true,
            EncodingKind: DocumentEncodingKind.Utf8WithBom,
            LineEndingKind: DocumentLineEndingKind.CrLf
        }
        && restored.Content == document.Content
        && restored.StatusText.StartsWith(
            "Recovered unsaved changes",
            StringComparison.Ordinal),
        "A dirty named document was not restored from recovery.");

    var missingSessionId = Guid.NewGuid().ToString("N");
    var missingPath = Path.Combine(documentsRoot, "deleted-after-crash.cs");
    var missingEntry = new EditorDocumentSessionEntry
    {
        SessionId = missingSessionId,
        Title = "deleted-after-crash.cs",
        FilePath = missingPath,
        HadUnsavedChanges = true,
        EditorSettings = editorSettings,
        EncodingKind = DocumentEncodingKind.Utf8,
        LineEndingKind = DocumentLineEndingKind.Lf
    };
    var missingRecovery = new EditorRecoverySnapshot
    {
        SessionId = missingSessionId,
        Title = missingEntry.Title,
        FilePath = missingPath,
        Content = "recovered despite missing file\n",
        EditorSettings = editorSettings,
        EncodingKind = DocumentEncodingKind.Utf8,
        LineEndingKind = DocumentLineEndingKind.Lf
    };
    await sessionService.SaveAsync(
        new EditorSessionSnapshot
        {
            ActiveDocumentSessionId = missingSessionId,
            Documents = [missingEntry]
        },
        [missingRecovery]);
    var restoredMissing = await DocumentTabViewModel.RestoreAsync(
        missingEntry,
        await sessionService.LoadRecoveryAsync(missingSessionId),
        documentService);
    Require(
        restoredMissing is { HasUnsavedChanges: true }
        && restoredMissing.FilePath == missingPath
        && restoredMissing.Content == missingRecovery.Content,
        "Recovery failed when the original file was missing.");

    var integrationAppDataRoot = Path.Combine(editorRoot, "view-model-app-data");
    var integrationSettingsService = new SettingsService(integrationAppDataRoot);
    var integrationSettings = integrationSettingsService.Load();
    integrationSettings.Application.CreateTerminalOnStartup = false;
    integrationSettings.Application.NewTabPlacement = NewTabPlacement.AfterCurrent;
    integrationSettingsService.Save(integrationSettings);
    var firstRestoredPath = Path.Combine(documentsRoot, "restore-first.cs");
    var secondRestoredPath = Path.Combine(documentsRoot, "restore-second.cs");
    await File.WriteAllTextAsync(firstRestoredPath, "first\r\n");
    await File.WriteAllTextAsync(secondRestoredPath, "second on disk\r\n");
    var firstRestoredId = Guid.NewGuid().ToString("N");
    var secondRestoredId = Guid.NewGuid().ToString("N");
    var firstRestoredEntry = new EditorDocumentSessionEntry
    {
        SessionId = firstRestoredId,
        Title = Path.GetFileName(firstRestoredPath),
        FilePath = firstRestoredPath,
        EditorSettings = editorSettings,
        EncodingKind = DocumentEncodingKind.Utf8,
        LineEndingKind = DocumentLineEndingKind.CrLf
    };
    var secondRestoredEntry = new EditorDocumentSessionEntry
    {
        SessionId = secondRestoredId,
        Title = Path.GetFileName(secondRestoredPath),
        FilePath = secondRestoredPath,
        HadUnsavedChanges = true,
        EditorSettings = editorSettings,
        EncodingKind = DocumentEncodingKind.Utf8,
        LineEndingKind = DocumentLineEndingKind.CrLf
    };
    var integrationSessionService = new EditorSessionService(
        integrationSettingsService.EditorSessionPath,
        integrationSettingsService.EditorRecoveryDirectory);
    await integrationSessionService.SaveAsync(
        new EditorSessionSnapshot
        {
            ActiveDocumentSessionId = secondRestoredId,
            Documents = [firstRestoredEntry, secondRestoredEntry]
        },
        [
            new EditorRecoverySnapshot
            {
                SessionId = secondRestoredId,
                Title = secondRestoredEntry.Title,
                FilePath = secondRestoredPath,
                Content = "second recovered\r\n",
                EditorSettings = editorSettings,
                EncodingKind = DocumentEncodingKind.Utf8,
                LineEndingKind = DocumentLineEndingKind.CrLf
            }
        ]);
    using (var viewModel = new MainWindowViewModel(integrationSettingsService))
    {
        await viewModel.InitializeAsync();
        var restoredTabs = viewModel.WorkspaceTabs
            .OfType<DocumentTabViewModel>()
            .ToList();
        Require(
            restoredTabs.Select(tab => tab.SessionId).SequenceEqual(
                [firstRestoredId, secondRestoredId])
            && viewModel.ActiveDocument?.SessionId == secondRestoredId
            && restoredTabs[1].Content == "second recovered\r\n"
            && restoredTabs[1].HasUnsavedChanges
            && viewModel.IsProfileLoginGateVisible
            && viewModel.CanBeginProfileLogin
            && viewModel.CanContinueWithoutProfileLogin,
            "View-model startup did not preserve editor recovery or expose the signed-out login gate.");
        viewModel.ContinueWithoutProfileLogin();
        Require(
            !viewModel.IsProfileLoginGateVisible,
            "Continuing offline did not release the startup login gate for the current session.");
        await viewModel.PrepareForShutdownAsync();
    }

    var unrecoverableUntitled = await DocumentTabViewModel.RestoreAsync(
        new EditorDocumentSessionEntry
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Title = "Untitled.cs",
            HadUnsavedChanges = true,
            EditorSettings = editorSettings
        },
        recovery: null,
        documentService);
    Require(
        unrecoverableUntitled is null,
        "An untitled dirty session without recovery was restored as misleading blank content.");

    var oversizedSessionId = Guid.NewGuid().ToString("N");
    var oversizedEntry = missingEntry with
    {
        SessionId = oversizedSessionId,
        Title = "oversized.txt",
        FilePath = string.Empty
    };
    var oversizedRecovery = missingRecovery with
    {
        SessionId = oversizedSessionId,
        Title = oversizedEntry.Title,
        FilePath = string.Empty,
        Content = new string(
            'x',
            EditorSessionService.MaximumRecoveryContentBytes + 1)
    };
    var oversizedResult = await sessionService.SaveAsync(
        new EditorSessionSnapshot { Documents = [oversizedEntry] },
        [oversizedRecovery]);
    Require(
        oversizedResult.SkippedRecoverySessionIds.SequenceEqual(
            [oversizedSessionId])
        && await sessionService.LoadRecoveryAsync(oversizedSessionId) is null,
        "Oversized recovery content was not safely rejected.");

    await File.WriteAllTextAsync(
        settingsService.EditorSessionPath,
        "{not valid json",
        new UTF8Encoding(false));
    Require(
        (await sessionService.LoadSessionAsync()).Documents.Count == 0,
        "A corrupt editor session did not fail closed.");

    document.Dispose();
    mixedDocument.Dispose();
    restored?.Dispose();
    restoredMissing?.Dispose();
    Console.WriteLine(
        "EDITOR_RELIABILITY_OK|encoding|eol|format|recovery|restore|limits");
}

static byte[] EncodeWithPreamble(Encoding encoding, string text)
{
    var preamble = encoding.GetPreamble();
    var content = encoding.GetBytes(text);
    var bytes = new byte[preamble.Length + content.Length];
    preamble.CopyTo(bytes, 0);
    content.CopyTo(bytes, preamble.Length);
    return bytes;
}

static async Task VerifyDocumentProvidersAsync(string verifierRoot)
{
    var providerRoot = Path.Combine(verifierRoot, "document-providers");
    var exportRoot = Path.Combine(providerRoot, "exports");
    var extensionRoot = Path.Combine(providerRoot, "extensions");
    Directory.CreateDirectory(exportRoot);
    Directory.CreateDirectory(extensionRoot);

    var textProvider = new TextDocumentProvider();
    var markdownProvider = new MarkdownDocumentProvider();
    var jsonProvider = new JsonDocumentProvider();
    var requiredOperations = DocumentProviderOperation.Read
                             | DocumentProviderOperation.Replace
                             | DocumentProviderOperation.Insert
                             | DocumentProviderOperation.Export;
    foreach (var provider in new IDocumentProvider[]
             {
                 textProvider,
                 markdownProvider,
                 jsonProvider
             })
    {
        Require(
            (provider.Definition.Operations & requiredOperations)
            == requiredOperations,
            $"{provider.Definition.DisplayName} did not declare the complete typed operation surface.");
        Require(
            provider.Definition.ExportFormats.Select(format => format.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(["html", "docx"]),
            $"{provider.Definition.DisplayName} did not declare both standard export formats.");
    }

    const string markdown = """
        # Hypershell Guide

        Intro with a [safe link](https://example.com) and an [unsafe link](javascript:alert).

        ## Workflow

        - Inspect the outline
        - Export a copy

        1. First step
        2. Second step

        > Keep the source editable.

        ```csharp
        Console.WriteLine("hello");
        ```

        ![Remote diagram](https://example.invalid/pixel.png)

        <script>alert('blocked')</script>
        """;
    var markdownRead = markdownProvider.Read(new DocumentReadRequest(
        markdown,
        "markdown",
        "guide.md"));
    Require(
        markdownRead.Succeeded
        && markdownRead.Outline.Count == 2
        && markdownRead.Outline[0].DisplayName == "Hypershell Guide"
        && markdownRead.Outline[0].Level == 1
        && markdownRead.Outline[1].DisplayName == "Workflow"
        && markdownRead.Outline[1].Level == 2,
        "The Markdown provider did not return a stable heading outline.");

    var guideOffset = markdown.IndexOf("Guide", StringComparison.Ordinal);
    var markdownMutation = markdownProvider.Replace(new DocumentReplaceRequest(
        markdown,
        guideOffset,
        "Guide".Length,
        "Handbook",
        "markdown",
        "guide.md"));
    Require(
        markdownMutation.Succeeded
        && markdownMutation.Content.Contains(
            "Hypershell Handbook",
            StringComparison.Ordinal),
        "The Markdown provider did not apply a valid typed replacement.");
    var rejectedRange = textProvider.Replace(new DocumentReplaceRequest(
        "source",
        99,
        1,
        "replacement",
        "plaintext"));
    Require(
        !rejectedRange.Succeeded
        && rejectedRange.Content == "source",
        "An invalid typed replacement range changed the source document.");

    const string json = """
        {
          "project": {
            "name": "Hyper"
          },
          "steps": [
            "inspect",
            "export"
          ]
        }
        """;
    var jsonRead = jsonProvider.Read(new DocumentReadRequest(
        json,
        "json",
        "project.json"));
    Require(
        jsonRead.Succeeded
        && jsonRead.Outline.Any(item => item.Id == "$.project.name")
        && jsonRead.Outline.Any(item => item.Id == "$.steps[1]"),
        "The JSON provider did not expose its bounded structural outline.");
    var invalidJsonMutation = jsonProvider.Insert(new DocumentInsertRequest(
        json,
        0,
        "{",
        "json",
        "project.json"));
    Require(
        !invalidJsonMutation.Succeeded
        && invalidJsonMutation.Content == json
        && invalidJsonMutation.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "JSON_INVALID"),
        "The JSON provider accepted an invalid mutation or failed to preserve the original source.");
    var jsonValueOffset = json.IndexOf("Hyper", StringComparison.Ordinal);
    var validJsonMutation = jsonProvider.Replace(new DocumentReplaceRequest(
        json,
        jsonValueOffset,
        "Hyper".Length,
        "Hypershell",
        "json",
        "project.json"));
    Require(
        validJsonMutation.Succeeded
        && jsonProvider.Read(new DocumentReadRequest(
            validJsonMutation.Content,
            "json",
            "project.json")).Succeeded,
        "The JSON provider rejected a mutation that preserved valid JSON.");

    var exportService = new DocumentExportService();
    var invalidJsonExportRejected = false;
    try
    {
        await exportService.CreateArtifactAsync(
            jsonProvider,
            new DocumentExportRequest
            {
                Content = "{",
                LanguageId = "json",
                Title = "invalid.json",
                FormatId = DocumentExportFormatCatalog.Html.Id
            });
    }
    catch (InvalidDataException)
    {
        invalidJsonExportRejected = true;
    }

    Require(
        invalidJsonExportRejected,
        "The JSON provider did not reject export of an invalid source document as a validation failure.");
    var htmlArtifact = await exportService.CreateArtifactAsync(
        markdownProvider,
        new DocumentExportRequest
        {
            Content = markdown,
            LanguageId = "markdown",
            Title = "guide.md",
            FilePath = "guide.md",
            FormatId = DocumentExportFormatCatalog.Html.Id
        });
    var html = Encoding.UTF8.GetString(htmlArtifact.Content);
    Require(
        html.Contains("Content-Security-Policy", StringComparison.Ordinal)
        && html.Contains("script-src 'none'", StringComparison.Ordinal)
        && html.Contains("<h1", StringComparison.Ordinal)
        && html.Contains("Hypershell Guide", StringComparison.Ordinal)
        && html.Contains("href=\"#\"", StringComparison.Ordinal)
        && html.Contains("rel=\"noreferrer noopener\"", StringComparison.Ordinal)
        && html.Contains("[Image: Remote diagram]", StringComparison.Ordinal)
        && !html.Contains("<script", StringComparison.OrdinalIgnoreCase)
        && !html.Contains(
            "https://example.invalid/pixel.png",
            StringComparison.OrdinalIgnoreCase),
        "The standalone HTML export did not enforce its sanitization and offline-content policy.");

    var htmlPath = Path.Combine(exportRoot, "guide.html");
    var writtenHtmlPath = await exportService.WriteArtifactAsync(
        htmlArtifact,
        htmlPath,
        Path.Combine(exportRoot, "guide.md"));
    Require(
        writtenHtmlPath == Path.GetFullPath(htmlPath)
        && (await File.ReadAllBytesAsync(htmlPath)).SequenceEqual(
            htmlArtifact.Content)
        && !Directory.EnumerateFiles(
                exportRoot,
                "*.hypershell-export",
                SearchOption.TopDirectoryOnly)
            .Any(),
        "The HTML artifact was not written atomically or left a temporary sibling behind.");

    var sourceOverwriteRejected = false;
    try
    {
        await exportService.WriteArtifactAsync(
            htmlArtifact,
            htmlPath,
            htmlPath);
    }
    catch (InvalidOperationException)
    {
        sourceOverwriteRejected = true;
    }

    Require(
        sourceOverwriteRejected,
        "The export service allowed an artifact to overwrite its active source path.");
    var wrongExtensionRejected = false;
    try
    {
        await exportService.WriteArtifactAsync(
            htmlArtifact,
            Path.Combine(exportRoot, "guide.docx"));
    }
    catch (InvalidDataException)
    {
        wrongExtensionRejected = true;
    }

    Require(
        wrongExtensionRejected,
        "The export service accepted a path whose extension did not match the artifact.");

    var oversizedInputRejected = false;
    try
    {
        await exportService.CreateArtifactAsync(
            textProvider,
            new DocumentExportRequest
            {
                Content = new string(
                    'x',
                    DocumentExportService.MaximumInputCharacters + 1),
                LanguageId = "plaintext",
                Title = "oversized.txt",
                FormatId = DocumentExportFormatCatalog.Html.Id
            });
    }
    catch (InvalidDataException)
    {
        oversizedInputRejected = true;
    }

    Require(
        oversizedInputRejected,
        "The export service accepted input beyond its documented in-memory limit.");

    var wordArtifact = await exportService.CreateArtifactAsync(
        markdownProvider,
        new DocumentExportRequest
        {
            Content = markdown,
            LanguageId = "markdown",
            Title = "guide.md",
            FilePath = "guide.md",
            FormatId = DocumentExportFormatCatalog.WordDocument.Id
        });
    Require(
        wordArtifact.Content.Length > 0
        && wordArtifact.SuggestedFileName == "guide.docx",
        "The Word provider returned an empty or incorrectly named artifact.");
    using (var wordStream = new MemoryStream(wordArtifact.Content))
    using (var wordDocument = WordprocessingDocument.Open(
               wordStream,
               isEditable: false))
    {
        var validationErrors = new OpenXmlValidator()
            .Validate(wordDocument)
            .ToList();
        var body = wordDocument.MainDocumentPart?.Document?.Body;
        var text = string.Join(
            " ",
            body?.Descendants<Text>().Select(node => node.Text) ?? []);
        var paragraphStyleIds = body?.Descendants<ParagraphStyleId>()
            .Select(style => style.Val?.Value ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
        var styleIds = wordDocument.MainDocumentPart?.StyleDefinitionsPart
                ?.Styles?.Elements<Style>()
                .Select(style => style.StyleId?.Value ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
        Require(
            validationErrors.Count == 0
            && text.Contains("Hypershell Guide", StringComparison.Ordinal)
            && text.Contains("Console.WriteLine", StringComparison.Ordinal)
            && paragraphStyleIds.Contains("Heading1")
            && paragraphStyleIds.Contains("CodeBlock")
            && styleIds.Contains("Normal")
            && styleIds.Contains("Heading1")
            && wordDocument.MainDocumentPart?.NumberingDefinitionsPart
                ?.Numbering is not null,
            "The Word artifact was not a valid structured Open XML document.");
    }

    var samplePackageRoot = Path.Combine(
        FindWorkspaceRoot(),
        "samples",
        "extensions",
        "markdown-notes-alias");
    CopyDirectory(
        samplePackageRoot,
        Path.Combine(extensionRoot, "markdown-notes-alias"));
    await WriteDocumentPackageAsync(
        "core-conflict",
        "verifier.document.core-conflict",
        [
            new DocumentFormatContribution
            {
                Id = "core-conflict",
                DisplayName = "Verifier JSON conflict",
                Kind = DocumentContributionKind.TextEditor,
                FileExtensions = [".json"],
                LanguageId = "json"
            }
        ]);
    await WriteDocumentPackageAsync(
        "duplicate-one",
        "verifier.document.duplicate-one",
        [
            new DocumentFormatContribution
            {
                Id = "duplicate-one-format",
                DisplayName = "Verifier duplicate one",
                Kind = DocumentContributionKind.TextEditor,
                FileExtensions = [".dupe"],
                LanguageId = "plaintext"
            }
        ]);
    await WriteDocumentPackageAsync(
        "duplicate-two",
        "verifier.document.duplicate-two",
        [
            new DocumentFormatContribution
            {
                Id = "duplicate-two-format",
                DisplayName = "Verifier duplicate two",
                Kind = DocumentContributionKind.TextEditor,
                FileExtensions = [".dupe"],
                LanguageId = "json"
            }
        ]);

    var extensionSnapshot = new ExtensionDiscoveryService(extensionRoot).Discover();
    Require(
        extensionSnapshot.Packages.Count == 4
        && extensionSnapshot.Packages.All(package =>
            package.Status == ExtensionPackageStatus.Validated),
        "The static document-mapping packages did not pass extension discovery.");
    var registry = new DocumentProviderRegistry(extensionSnapshot);
    Require(
        registry.Catalog.ActiveBuiltInCount == EditorLanguageCatalog.Languages.Count
        && registry.Catalog.ActiveExtensionCount == 1
        && registry.Catalog.DormantExtensionCount == 3,
        "The document registry did not apply its core-owner and duplicate-claim conflict policy.");
    var aliasResolution = registry.Resolve("guide.hnote");
    var coreJsonResolution = registry.Resolve("settings.json");
    var duplicateResolution = registry.Resolve("ambiguous.dupe");
    Require(
        aliasResolution.Provider.Definition.Id == MarkdownDocumentProvider.ProviderId
        && aliasResolution.LanguageId == "markdown"
        && aliasResolution.Registration.OwnerId == "community.markdown-notes-alias"
        && !aliasResolution.Registration.IsBuiltIn
        && coreJsonResolution.Provider.Definition.Id == JsonDocumentProvider.ProviderId
        && coreJsonResolution.Registration.IsBuiltIn
        && duplicateResolution.Provider.Definition.Id == TextDocumentProvider.ProviderId
        && duplicateResolution.Registration.IsBuiltIn,
        "Document provider resolution did not preserve host ownership or activate the safe alias.");
    var openFilter = registry.CreateOpenDialogFilter();
    Require(
        openFilter.Contains("*.hnote", StringComparison.OrdinalIgnoreCase)
        && !openFilter.Contains("*.dupe", StringComparison.OrdinalIgnoreCase),
        "The open-document filter exposed dormant extension mappings.");

    var documentService = new TextDocumentService();
    using var tab = DocumentTabViewModel.CreateUntitled(
        new NewDocumentRequest(
            "notes.hnote",
            EditorLanguageCatalog.CreateDefaultSettings("plaintext"),
            DocumentEncodingKind.Utf8,
            DocumentLineEndingKind.Lf),
        documentService);
    tab.ApplyDocumentProvider(registry.Resolve(tab.Title, tab.LanguageId));
    tab.Content = "# Tab heading\n\nBody";
    var tabRead = tab.ReadThroughProvider();
    var tabInsert = tab.InsertThroughProvider(tab.Content.Length, "\n");
    var tabSourceAfterInsert = tab.Content;
    var tabRejectedMutation = tab.ReplaceThroughProvider(
        tab.Content.Length + 1,
        0,
        "invalid");
    Require(
        tab.DocumentProviderId == MarkdownDocumentProvider.ProviderId
        && tab.LanguageId == "markdown"
        && tab.ProviderFileExtensions.Contains(
            ".hnote",
            StringComparer.OrdinalIgnoreCase)
        && tab.CanExport
        && tabRead.Outline.Single().DisplayName == "Tab heading"
        && tabInsert.Succeeded
        && !tabRejectedMutation.Succeeded
        && tab.Content == tabSourceAfterInsert,
        "The document tab did not route typed operations through its bound provider safely.");

    var integrationSettings = new SettingsService(
        Path.Combine(providerRoot, "main-window-app-data"));
    CopyDirectory(
        samplePackageRoot,
        Path.Combine(
            integrationSettings.ExtensionDirectory,
            "markdown-notes-alias"));
    using (var mainWindow = new MainWindowViewModel(integrationSettings))
    {
        var integratedDocument = mainWindow.CreateUntitledDocument(
            new NewDocumentRequest(
                "integrated.hnote",
                EditorLanguageCatalog.CreateDefaultSettings("plaintext"),
                DocumentEncodingKind.Utf8,
                DocumentLineEndingKind.Lf));
        integratedDocument.Content = "# Integrated export\n\nHost-routed content.";
        var integratedExportPath = Path.Combine(
            exportRoot,
            "integrated.html");
        var integratedResult = await mainWindow.ExportDocumentAsync(
            integratedDocument,
            DocumentExportFormatCatalog.Html.Id,
            integratedExportPath);
        Require(
            integratedDocument.DocumentProviderId
                == MarkdownDocumentProvider.ProviderId
            && integratedDocument.LanguageId == "markdown"
            && integratedResult.Succeeded
            && integratedResult.OutputPath
                == Path.GetFullPath(integratedExportPath)
            && File.Exists(integratedExportPath),
            "The main workspace did not bind and export through the resolved document provider.");
    }

    Console.WriteLine(
        $"DOCUMENT_PROVIDERS_OK|providers:3|exports:2|extension-active:{registry.Catalog.ActiveExtensionCount}|extension-dormant:{registry.Catalog.DormantExtensionCount}|docx-valid");

    async Task WriteDocumentPackageAsync(
        string folderName,
        string extensionId,
        IReadOnlyList<DocumentFormatContribution> formats)
    {
        var packageRoot = Path.Combine(extensionRoot, folderName);
        Directory.CreateDirectory(packageRoot);
        var manifest = new ExtensionManifest
        {
            SchemaVersion = ExtensionContract.CurrentManifestSchemaVersion,
            Id = extensionId,
            DisplayName = extensionId,
            Publisher = "Hypershell Verifier",
            Version = "1.0.0",
            Description = "Static document mapping verification package.",
            Runtime = ExtensionRuntimeKind.ContentOnly,
            RequestedCapabilities = [ExtensionCapability.DocumentProvider],
            Contributions = new ExtensionContributions
            {
                DocumentFormats = formats
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(
                packageRoot,
                ExtensionDiscoveryService.ManifestFileName),
            JsonSerializer.Serialize(
                manifest,
                ExtensionContract.CreateJsonOptions()),
            new UTF8Encoding(false));
    }
}

static async Task VerifyLanguageToolingAsync(string verifierRoot)
{
    var toolingRoot = Path.Combine(verifierRoot, "language-tooling");
    Directory.CreateDirectory(toolingRoot);
    var helperPath = Path.Combine(
        AppContext.BaseDirectory,
        "Hypershell.CSharpVerifier.exe");
    Require(File.Exists(helperPath), "The language-tooling helper executable was not found.");
    var helperDirectory = Path.GetDirectoryName(helperPath)!;
    var helperCandidate = Path.GetFileName(helperPath);
    var shimPath = Path.Combine(toolingRoot, "shim-only.cmd");
    await File.WriteAllTextAsync(shimPath, "@echo off\r\nexit /b 0\r\n");
    var discoveryPath = string.Join(
        Path.PathSeparator,
        helperDirectory,
        toolingRoot);

    var nativeFormatterDefinition = new LanguageToolDefinition
    {
        Id = "formatter.verifier",
        DisplayName = "Verifier formatter",
        Kind = LanguageToolKind.Formatter,
        LanguageIds = ["plaintext"],
        ExecutableCandidates = [helperCandidate],
        Arguments = ["--fake-formatter", "format", "--metadata={filePath}"],
        AcceptsDocumentOnStandardInput = true,
        Priority = 1
    };
    var definitions = new List<LanguageToolDefinition>
    {
        new()
        {
            Id = "runtime.builtin.verifier",
            DisplayName = "Built-in verifier runtime",
            Kind = LanguageToolKind.Runtime,
            IsHostBuiltIn = true
        },
        nativeFormatterDefinition,
        new()
        {
            Id = "formatter.shim.verifier",
            DisplayName = "Shim-only formatter",
            Kind = LanguageToolKind.Formatter,
            LanguageIds = ["plaintext"],
            ExecutableCandidates = ["shim-only"],
            AcceptsDocumentOnStandardInput = true
        },
        new()
        {
            Id = "lsp.missing.verifier",
            DisplayName = "Missing verifier language server",
            Kind = LanguageToolKind.LanguageServer,
            LanguageIds = ["plaintext"],
            ExecutableCandidates = ["hypershell-certainly-missing-lsp"]
        }
    };
    var snapshot = new LanguageToolDiscoveryService(definitions)
        .Discover(discoveryPath);
    var nativeFormatter = snapshot.Tools.Single(tool =>
        tool.Definition.Id == nativeFormatterDefinition.Id);
    var shimFormatter = snapshot.Tools.Single(tool =>
        tool.Definition.Id == "formatter.shim.verifier");
    Require(
        snapshot.Tools.Single(tool =>
                tool.Definition.Id == "runtime.builtin.verifier")
            .IsAvailable
        && nativeFormatter.IsAvailable
        && string.Equals(
            nativeFormatter.ExecutablePath,
            helperPath,
            StringComparison.OrdinalIgnoreCase)
        && shimFormatter.Availability == LanguageToolAvailability.UnsupportedCommandShim
        && snapshot.Tools.Single(tool =>
                tool.Definition.Id == "lsp.missing.verifier")
            .Availability == LanguageToolAvailability.Missing,
        "Language-tool discovery did not preserve built-in, native, shim, and missing states.");

    var registryIds = LanguageToolRegistry.Definitions
        .Select(definition => definition.Id)
        .ToList();
    var editorLanguageIds = EditorLanguageCatalog.Languages
        .Select(language => language.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    Require(
        registryIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == registryIds.Count
        && LanguageToolRegistry.Definitions
            .SelectMany(definition => definition.LanguageIds)
            .All(editorLanguageIds.Contains)
        && LanguageToolRegistry.Definitions.Any(definition =>
            definition.Id == "formatter.monaco"
            && definition.IsHostBuiltIn)
        && LanguageToolRegistry.Definitions.Any(definition =>
            definition.Id == "compiler.javac"
            && definition.Kind == LanguageToolKind.Compiler
            && definition.LanguageIds.Contains("java"))
        && LanguageToolRegistry.Definitions.Any(definition =>
            definition.Id == "shell.jshell"
            && definition.Kind == LanguageToolKind.InteractiveShell
            && definition.LanguageIds.Contains("java"))
        && LanguageToolRegistry.Definitions
            .Where(definition => definition.Kind == LanguageToolKind.Formatter
                                 && !definition.IsHostBuiltIn)
            .All(definition => definition.AcceptsDocumentOnStandardInput),
        "The host language-tool registry contains duplicate IDs or an invalid adapter contract.");

    var formatterCatalog = new LanguageToolCatalogSnapshot
    {
        Tools = [nativeFormatter]
    };
    var formatter = new ExternalDocumentFormatterService();
    var formattingRequest = new DocumentFormattingRequest
    {
        LanguageId = "plaintext",
        Content = "alpha  beta\n",
        FileName = "document with spaces.txt",
        WorkingDirectory = toolingRoot
    };
    var applied = await formatter.FormatAsync(formattingRequest, formatterCatalog);
    Require(
        applied.Outcome == DocumentFormattingOutcome.Applied
        && applied.FormattedText == "alpha beta\n"
        && applied.FormatterId == nativeFormatterDefinition.Id,
        $"The external formatter adapter failed: {applied.Message}");
    var unchanged = await formatter.FormatAsync(
        formattingRequest with { Content = applied.FormattedText },
        formatterCatalog);
    Require(
        unchanged.Outcome == DocumentFormattingOutcome.Unchanged,
        "An unchanged formatter result was not distinguished from an edit.");

    var failedTool = nativeFormatter with
    {
        Definition = nativeFormatterDefinition with
        {
            Id = "formatter.verifier.fail",
            Arguments = ["--fake-formatter", "fail"]
        }
    };
    var failed = await formatter.FormatAsync(
        formattingRequest,
        new LanguageToolCatalogSnapshot { Tools = [failedTool] });
    Require(
        failed.Outcome == DocumentFormattingOutcome.Failed
        && failed.ShouldUseEditorFallback
        && failed.Message.Contains("synthetic formatter failure", StringComparison.Ordinal),
        "A formatter failure did not produce a non-destructive editor fallback result.");

    var delayedTool = nativeFormatter with
    {
        Definition = nativeFormatterDefinition with
        {
            Id = "formatter.verifier.delay",
            Arguments = ["--fake-formatter", "delay"]
        }
    };
    var timedOut = await new ExternalDocumentFormatterService(
            timeout: TimeSpan.FromMilliseconds(250))
        .FormatAsync(
            formattingRequest,
            new LanguageToolCatalogSnapshot { Tools = [delayedTool] });
    Require(
        timedOut.Outcome == DocumentFormattingOutcome.TimedOut
        && timedOut.ShouldUseEditorFallback,
        "A formatter timeout did not terminate and fall back safely.");

    var floodTool = nativeFormatter with
    {
        Definition = nativeFormatterDefinition with
        {
            Id = "formatter.verifier.flood",
            Arguments = ["--fake-formatter", "flood"]
        }
    };
    var rejectedOutput = await new ExternalDocumentFormatterService(
            maximumOutputCharacters: 256)
        .FormatAsync(
            formattingRequest,
            new LanguageToolCatalogSnapshot { Tools = [floodTool] });
    Require(
        rejectedOutput.Outcome == DocumentFormattingOutcome.Rejected
        && rejectedOutput.ShouldUseEditorFallback,
        "Oversized formatter output was not rejected.");

    var oversizedInput = await new BoundedProcessRunner().RunAsync(
        new BoundedProcessRequest
        {
            ExecutablePath = helperPath,
            Arguments = ["--fake-formatter", "format"],
            StandardInput = "12345",
            MaximumStandardInputCharacters = 4
        });
    Require(
        !oversizedInput.Started
        && oversizedInput.FailureMessage.Contains("input", StringComparison.OrdinalIgnoreCase),
        "Oversized formatter input was not rejected before process launch.");

    var serverTool = new DiscoveredLanguageTool
    {
        Definition = new LanguageToolDefinition
        {
            Id = "lsp.verifier",
            DisplayName = "Verifier language server",
            Kind = LanguageToolKind.LanguageServer,
            LanguageIds = ["plaintext"],
            ExecutableCandidates = [helperCandidate],
            Arguments = ["--fake-language-server"]
        },
        Availability = LanguageToolAvailability.Available,
        ExecutablePath = helperPath,
        StatusMessage = "Verifier helper detected."
    };
    await using (var languageServer = new LanguageServerProcessHost())
    {
        Require(
            languageServer.State == LanguageServerProcessState.Stopped
            && languageServer.InputStream is null
            && languageServer.OutputStream is null,
            "The language-server host did not begin stopped.");
        await languageServer.StartAsync(new LanguageServerLaunchRequest
        {
            Tool = serverTool,
            WorkingDirectory = toolingRoot
        });
        Require(
            languageServer.State == LanguageServerProcessState.Running
            && languageServer.InputStream is not null
            && languageServer.OutputStream is not null,
            "The language-server host did not expose its bounded lifecycle streams.");
        await languageServer.StopAsync();
        Require(
            languageServer.State == LanguageServerProcessState.Stopped
            && languageServer.InputStream is null
            && languageServer.OutputStream is null,
            "The language-server host did not return to the stopped state.");
    }

    Console.WriteLine(
        $"LANGUAGE_TOOLING_OK|{snapshot.Tools.Count}|{LanguageToolRegistry.Definitions.Count}|stdin-stdout|bounded|lsp-stopped");
}

static async Task VerifyPanelMessagingAsync(string verifierRoot)
{
    var workspaceRoot = FindWorkspaceRoot();
    var sampleRoot = Path.Combine(
        workspaceRoot,
        "samples",
        "extensions",
        "goal-tracker");
    var appDataRoot = Path.Combine(verifierRoot, "panel-message-app-data");
    var packageRoot = Path.Combine(
        appDataRoot,
        "extensions",
        "goal-tracker");
    CopyDirectory(sampleRoot, packageRoot);

    var settingsService = new SettingsService(appDataRoot);
    var snapshot = new ExtensionDiscoveryService(
        settingsService.ExtensionDirectory).Discover();
    Require(
        snapshot.Packages.Count == 1
        && snapshot.Packages[0].Status == ExtensionPackageStatus.Validated
        && snapshot.PanelCandidates.Count == 1,
        "The API-v1 goal-tracker package did not pass extension discovery.");
    var candidate = snapshot.PanelCandidates[0];
    Require(
        candidate.Registration.Contribution.MessageApiVersion
            == ExtensionPanelMessagingContract.CurrentVersion
        && snapshot.Packages[0].Diagnostics.Any(diagnostic =>
            diagnostic.Code == "PANEL_MESSAGE_API_DECLARED"),
        "Discovery did not preserve or disclose the declared panel message API.");

    var approvalService = new ExtensionApprovalService(
        settingsService.ExtensionStatePath);
    approvalService.ApproveWebPanels(candidate.Package);
    var approvedCapabilities = approvalService.GetApprovedCapabilities(
        candidate.Package);
    Require(
        approvedCapabilities.SetEquals(
        [
            ExtensionCapability.WorkspacePanel,
            ExtensionCapability.WorkspaceMetadata
        ]),
        "The panel approval did not bind its exact declared message capabilities.");

    var context = CreateContext("Editor.cs", openTabCount: 2);
    var virtualHost = ExtensionPanelMessagingContract.CreateVirtualHostName(
        candidate.Package.PackageSha256);
    var source = $"https://{virtualHost}/panel/index.html";
    var host = new ExtensionPanelMessageHost(
        candidate,
        approvedCapabilities,
        virtualHost,
        settingsService.ExtensionPanelStateDirectory,
        () => context);

    Require(
        await host.HandleAsync(
            "https://untrusted.example/panel.html",
            CreateRequest("foreign-1", "host.ready")) is null,
        "A foreign origin reached the panel message host.");

    var handshakeRequired = await RequireResponseAsync(
        host,
        source,
        CreateRequest("preflight-1", "state.get"));
    Require(
        !handshakeRequired.GetProperty("ok").GetBoolean()
        && handshakeRequired.GetProperty("error").GetProperty("code").GetString()
            == "HANDSHAKE_REQUIRED",
        "A panel method ran before the mandatory host.ready handshake.");

    var handshake = await RequireResponseAsync(
        host,
        source,
        CreateRequest("ready-1", "host.ready"));
    Require(
        handshake.GetProperty("ok").GetBoolean()
        && handshake.GetProperty("result").GetProperty("methods")
            .EnumerateArray()
            .Any(method => method.GetString() == "host.getContext")
        && host.IsReady,
        "The API-v1 handshake did not disclose its approved methods.");

    var unknownMethod = await RequireResponseAsync(
        host,
        source,
        CreateRequest("unknown-1", "host.unavailable"));
    var reusedRequestId = await RequireResponseAsync(
        host,
        source,
        CreateRequest("unknown-1", "state.get"));
    Require(
        unknownMethod.GetProperty("error").GetProperty("code").GetString()
            == "METHOD_NOT_FOUND"
        && reusedRequestId.GetProperty("error").GetProperty("code").GetString()
            == "REQUEST_ID_REUSED",
        "Unknown methods or replayed request IDs were not rejected.");

    var contextResponse = await RequireResponseAsync(
        host,
        source,
        CreateRequest("context-1", "host.getContext"));
    var contextJson = contextResponse.GetRawText();
    Require(
        contextResponse.GetProperty("ok").GetBoolean()
        && contextResponse.GetProperty("result")
            .GetProperty("workspace")
            .GetProperty("activeTab")
            .GetProperty("title")
            .GetString() == "Editor.cs"
        && !contextJson.Contains(verifierRoot, StringComparison.OrdinalIgnoreCase)
        && !contextJson.Contains("filePath", StringComparison.OrdinalIgnoreCase)
        && !contextJson.Contains("commandText", StringComparison.OrdinalIgnoreCase),
        "Read-only host context leaked a path, command, or unexpected value.");

    var subscribe = await RequireResponseAsync(
        host,
        source,
        CreateRequest(
            "subscribe-1",
            "events.subscribe",
            new { events = new[] { "host.contextChanged" } }));
    Require(
        subscribe.GetProperty("ok").GetBoolean(),
        "The approved context event subscription was rejected.");
    context = CreateContext("Program.py", openTabCount: 3);
    var contextEvent = host.TryCreateContextChangedEvent();
    Require(
        contextEvent is not null
        && JsonDocument.Parse(contextEvent).RootElement
            .GetProperty("event").GetString() == "host.contextChanged"
        && host.TryCreateContextChangedEvent() is null,
        "Context events were missing or repeated without a state change.");

    var setState = await RequireResponseAsync(
        host,
        source,
        CreateRequest(
            "state-set-1",
            "state.set",
            new
            {
                value = new
                {
                    schemaVersion = 1,
                    selectedStageIndex = 3
                }
            }));
    Require(
        setState.GetProperty("ok").GetBoolean()
        && File.Exists(Path.Combine(
            settingsService.ExtensionPanelStateDirectory,
            "community.goal-tracker",
            $"{candidate.PanelId}.json")),
        "Device-local panel state was not written inside its extension scope.");

    var restoredHost = new ExtensionPanelMessageHost(
        candidate,
        approvedCapabilities,
        virtualHost,
        settingsService.ExtensionPanelStateDirectory,
        () => context);
    await RequireResponseAsync(
        restoredHost,
        source,
        CreateRequest("ready-2", "host.ready"));
    var restored = await RequireResponseAsync(
        restoredHost,
        source,
        CreateRequest("state-get-1", "state.get"));
    Require(
        restored.GetProperty("result").GetProperty("value")
            .GetProperty("selectedStageIndex").GetInt32() == 3,
        "A new panel session did not restore its device-local state.");

    var oversizedState = await RequireResponseAsync(
        restoredHost,
        source,
        CreateRequest(
            "state-large-1",
            "state.set",
            new { value = new { payload = new string('x', 50 * 1024) } }));
    Require(
        !oversizedState.GetProperty("ok").GetBoolean()
        && oversizedState.GetProperty("error").GetProperty("code").GetString()
            == "STATE_TOO_LARGE",
        "The host accepted panel state beyond its documented quota.");

    var metadataDeniedHost = new ExtensionPanelMessageHost(
        candidate,
        new HashSet<ExtensionCapability>
        {
            ExtensionCapability.WorkspacePanel
        },
        virtualHost,
        Path.Combine(appDataRoot, "no-metadata-state"),
        () => context);
    await RequireResponseAsync(
        metadataDeniedHost,
        source,
        CreateRequest("ready-3", "host.ready"));
    var metadataDenied = await RequireResponseAsync(
        metadataDeniedHost,
        source,
        CreateRequest("context-denied-1", "host.getContext"));
    Require(
        !metadataDenied.GetProperty("ok").GetBoolean()
        && metadataDenied.GetProperty("error").GetProperty("code").GetString()
            == "CAPABILITY_DENIED",
        "Workspace context was exposed without an approved WorkspaceMetadata capability.");

    var rateHost = new ExtensionPanelMessageHost(
        candidate,
        approvedCapabilities,
        virtualHost,
        Path.Combine(appDataRoot, "rate-state"),
        () => context);
    await RequireResponseAsync(
        rateHost,
        source,
        CreateRequest("rate-ready", "host.ready"));
    for (var index = 0;
         index < ExtensionPanelMessagingContract.MaximumRequestsPerMinute - 1;
         index++)
    {
        var accepted = await RequireResponseAsync(
            rateHost,
            source,
            CreateRequest($"rate-{index}", "host.getContext"));
        Require(
            accepted.GetProperty("ok").GetBoolean(),
            "The panel message rate limiter rejected a request below its quota.");
    }

    var rateLimited = await RequireResponseAsync(
        rateHost,
        source,
        CreateRequest("rate-over", "host.getContext"));
    Require(
        !rateLimited.GetProperty("ok").GetBoolean()
        && rateLimited.GetProperty("error").GetProperty("code").GetString()
            == "RATE_LIMITED",
        "The panel message rate limiter did not enforce its per-minute quota.");

    var messageTooLarge = await restoredHost.HandleAsync(
        source,
        new string('x',
            ExtensionPanelMessagingContract.MaximumInboundMessageBytes + 1));
    Require(
        messageTooLarge is { IsSuccess: false }
        && JsonDocument.Parse(messageTooLarge.ResponseJson).RootElement
            .GetProperty("error").GetProperty("code").GetString()
            == "MESSAGE_TOO_LARGE",
        "The panel host accepted an oversized inbound message.");

    var unsupportedApiRoot = Path.Combine(
        appDataRoot,
        "unsupported-api-extensions",
        "goal-tracker");
    CopyDirectory(sampleRoot, unsupportedApiRoot);
    var unsupportedManifestPath = Path.Combine(
        unsupportedApiRoot,
        ExtensionDiscoveryService.ManifestFileName);
    await File.WriteAllTextAsync(
        unsupportedManifestPath,
        (await File.ReadAllTextAsync(unsupportedManifestPath)).Replace(
            "\"messageApiVersion\": 1",
            "\"messageApiVersion\": 2",
            StringComparison.Ordinal));
    var unsupportedSnapshot = new ExtensionDiscoveryService(
        Path.GetDirectoryName(unsupportedApiRoot)!).Discover();
    Require(
        unsupportedSnapshot.Packages.Single().Status
            == ExtensionPackageStatus.Invalid
        && unsupportedSnapshot.PanelCandidates.Count == 0
        && unsupportedSnapshot.Packages.Single().Diagnostics.Any(diagnostic =>
            diagnostic.Code == "PANEL_MESSAGE_API_UNSUPPORTED"),
        "A package declaring an unsupported panel message API was not quarantined.");

    var noApiRoot = Path.Combine(
        appDataRoot,
        "no-api-extensions",
        "goal-tracker");
    CopyDirectory(sampleRoot, noApiRoot);
    var noApiManifestPath = Path.Combine(
        noApiRoot,
        ExtensionDiscoveryService.ManifestFileName);
    var noApiManifest = await File.ReadAllTextAsync(noApiManifestPath);
    noApiManifest = noApiManifest
        .Replace(
            "        \"messageApiVersion\": 1,\r\n",
            string.Empty,
            StringComparison.Ordinal)
        .Replace(
            "        \"messageApiVersion\": 1,\n",
            string.Empty,
            StringComparison.Ordinal);
    await File.WriteAllTextAsync(
        noApiManifestPath,
        noApiManifest);
    var noApiSnapshot = new ExtensionDiscoveryService(
        Path.GetDirectoryName(noApiRoot)!).Discover();
    var noApiPackage = noApiSnapshot.Packages.Single();
    var noApiApproval = new ExtensionApprovalService(
        Path.Combine(appDataRoot, "no-api-approval.json"));
    noApiApproval.ApproveWebPanels(noApiPackage);
    Require(
        noApiPackage.Status == ExtensionPackageStatus.Validated
        && noApiPackage.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "WORKSPACE_METADATA_UNUSED")
        && noApiSnapshot.PanelCandidates.Single()
            .Registration.Contribution.MessageApiVersion is null
        && noApiApproval.GetApprovedCapabilities(noApiPackage).SetEquals(
        [
            ExtensionCapability.WorkspacePanel
        ]),
        "A panel without an API declaration received messaging or metadata approval.");

    await File.AppendAllTextAsync(
        Path.Combine(packageRoot, "panel", "panel.css"),
        Environment.NewLine + "/* verifier package-hash mutation */");
    var changedSnapshot = new ExtensionDiscoveryService(
        settingsService.ExtensionDirectory).Discover();
    Require(
        changedSnapshot.Packages.Single().PackageSha256
            != candidate.Package.PackageSha256
        && !approvalService.IsWebPanelApproved(
            changedSnapshot.Packages.Single()),
        "Changed package content inherited an approval bound to the previous hash.");

    Console.WriteLine(
        "PANEL_MESSAGING_OK|origin-bound|capability-gated|state-restored|events-deduplicated|limits-enforced");

    static ExtensionPanelHostContext CreateContext(
        string activeTabTitle,
        int openTabCount) =>
        new(
            1,
            new ExtensionPanelWorkspaceContext(
                new ExtensionPanelActiveProjectContext(
                    "verifier-project",
                    "Verifier project"),
                openTabCount,
                new ExtensionPanelActiveTabContext(
                    "document",
                    activeTabTitle,
                    true,
                    "csharp",
                    string.Empty)),
            new ExtensionPanelThemeContext(
                "dark",
                "#0D1117",
                "#161B22",
                "#E6EDF3",
                "#8B949E",
                "#58A6FF"));
}

static string CreateRequest(
    string id,
    string method,
    object? parameters = null) =>
    JsonSerializer.Serialize(new
    {
        protocol = ExtensionPanelMessagingContract.Protocol,
        version = ExtensionPanelMessagingContract.CurrentVersion,
        type = "request",
        id,
        method,
        @params = parameters ?? new { }
    });

static async Task<JsonElement> RequireResponseAsync(
    ExtensionPanelMessageHost host,
    string source,
    string request)
{
    var outcome = await host.HandleAsync(source, request);
    Require(outcome is not null, "The host did not return a protocol response.");
    using var response = JsonDocument.Parse(outcome!.ResponseJson);
    return response.RootElement.Clone();
}

static string FindWorkspaceRoot()
{
    foreach (var startingPath in new[]
             {
                 Environment.CurrentDirectory,
                 AppContext.BaseDirectory
             })
    {
        var candidate = new DirectoryInfo(startingPath);
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(
                    candidate.FullName,
                    "samples",
                    "extensions",
                    "goal-tracker",
                    "hypershell.extension.json")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }
    }

    throw new DirectoryNotFoundException(
        "The Hypershell workspace root could not be located for extension verification.");
}

static void CopyDirectory(string sourceRoot, string destinationRoot)
{
    Directory.CreateDirectory(destinationRoot);
    foreach (var sourcePath in Directory.EnumerateFiles(
                 sourceRoot,
                 "*",
                 SearchOption.AllDirectories))
    {
        var destinationPath = Path.Combine(
            destinationRoot,
            Path.GetRelativePath(sourceRoot, sourcePath));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }
}

static async Task VerifyTerminalTabsAsync(string verifierRoot)
{
    var settingsService = new SettingsService(
        Path.Combine(verifierRoot, "terminal-tab-app-data"));
    var settings = settingsService.Load();
    settings.Application.CreateTerminalOnStartup = false;
    settings.Application.NewTabPlacement = NewTabPlacement.AfterLast;
    settings.ActiveTerminalProfileId = "powershell";
    settingsService.Save(settings);

    using var viewModel = new MainWindowViewModel(settingsService);
    var profile = viewModel.SelectedTerminalProfile;
    TerminalTabViewModel CreateTab(string title) =>
        viewModel.CreateTerminal(new TerminalLaunchRequest
        {
            Profile = profile,
            TitleTemplate = title
        }) ?? throw new InvalidOperationException(
            $"Unable to create verifier terminal tab {title}.");

    var first = CreateTab("First");
    var second = CreateTab("Second");
    var third = CreateTab("Third");
    Require(
        ReferenceEquals(viewModel.ActiveTerminal, third)
        && viewModel.Terminals.SequenceEqual([first, second, third]),
        "Terminal creation did not produce the expected tab order.");

    Require(
        viewModel.CloseTerminal(second)
        && viewModel.Terminals.SequenceEqual([first, third])
        && ReferenceEquals(viewModel.ActiveTerminal, third),
        "Closing a background terminal changed the active tab or tab order.");
    Require(
        viewModel.CloseTerminal(third)
        && viewModel.Terminals.SequenceEqual([first])
        && ReferenceEquals(viewModel.ActiveTerminal, first),
        "Closing the active last tab did not select its left neighbor.");
    Require(
        viewModel.CloseTerminal(first)
        && viewModel.Terminals.Count() == 0
        && viewModel.ActiveTerminal is null,
        "Closing the final terminal did not leave a valid empty tab surface.");
    Require(
        !viewModel.CloseTerminal(first),
        "A terminal could be closed more than once.");

    var running = CreateTab("Running");
    var execution = running.ExecuteTextAsync("Start-Sleep -Seconds 30");
    var runningDeadline = DateTime.UtcNow.AddSeconds(5);
    while (!running.IsRunning && DateTime.UtcNow < runningDeadline)
    {
        await Task.Delay(10);
    }

    Require(
        running.IsRunning,
        "The running-tab cancellation check did not start its command.");
    Require(
        viewModel.CloseTerminal(running)
        && viewModel.Terminals.Count() == 0
        && viewModel.ActiveTerminal is null
        && viewModel.StatusMessage.Contains(
            "running command was stopped",
            StringComparison.OrdinalIgnoreCase),
        "Closing a running terminal did not remove the tab and request cancellation.");
    await execution.WaitAsync(TimeSpan.FromSeconds(10));
}

static async Task VerifyLibraryBoundaryAsync(string verifierRoot)
{
    var settingsService = new SettingsService(
        Path.Combine(verifierRoot, "app-data"));
    Directory.CreateDirectory(settingsService.CommandLibraryDirectory);
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    var collidingLibrary = new CommandLibrary
    {
        Id = "csharp-basics",
        DisplayName = "Outdated local collision",
        Language = "C#",
        Category = "Test",
        ExecutionTarget = ExecutionTarget.CSharp,
        Commands =
        [
            new CommandDefinition
            {
                Name = "Collision.ShouldNotLoad",
                DisplayName = "Collision.ShouldNotLoad",
                Description = "This entry must never mask bundled content.",
                GuideDefinition = "Verification-only collision.",
                ModuleName = "Verifier",
                Action = "Test",
                ObjectName = "Collision",
                Template = "Console.WriteLine(\"collision\");"
            }
        ]
    };
    await File.WriteAllTextAsync(
        Path.Combine(
            settingsService.CommandLibraryDirectory,
            "outdated-csharp.json"),
        JsonSerializer.Serialize(collidingLibrary, jsonOptions));

    var externalLibrary = new CommandLibrary
    {
        Id = "verifier-external",
        DisplayName = "Verifier external library",
        Description = "Confirms that unique external catalogs remain loadable.",
        Language = "C#",
        Category = "Verification",
        ExecutionTarget = ExecutionTarget.CSharp,
        Commands =
        [
            new CommandDefinition
            {
                Name = "Verifier.External",
                DisplayName = "Verifier.External",
                Description = "A unique external verification command.",
                GuideDefinition = "A unique external verification command.",
                ModuleName = "Verifier",
                Action = "Test",
                ObjectName = "External library",
                Template = "Console.WriteLine(\"external\");"
            }
        ]
    };
    await File.WriteAllTextAsync(
        Path.Combine(
            settingsService.CommandLibraryDirectory,
            "verifier-external.json"),
        JsonSerializer.Serialize(externalLibrary, jsonOptions));

    var catalogService = new CommandCatalogService(settingsService);
    var libraries = await catalogService.LoadAsync();
    var bundledCSharp = libraries
        .Where(library =>
            library.ExecutionTarget == ExecutionTarget.CSharp
            && library.Origin == CommandLibraryOrigin.Bundled)
        .ToList();
    var bundledCSharpCore = bundledCSharp
        .Where(library => library.Kind == CommandLibraryKind.Core)
        .ToList();
    var bundledCSharpPackages = bundledCSharp
        .Where(library => library.Kind == CommandLibraryKind.Package)
        .ToList();
    Require(
        bundledCSharpCore.Count == 1
        && bundledCSharpCore[0].Id == "csharp-core"
        && bundledCSharpCore[0].EntryFiles.Count > 0
        && bundledCSharp.All(library => library.Commands.Count == 0),
        "The published C# boundary did not contain exactly one lazy C# Core catalog.");
    Require(
        bundledCSharpPackages.Select(library => library.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(
            [
                "newtonsoft-json-popular",
                "serilog-popular",
                "dapper-popular"
            ])
        && bundledCSharpPackages.All(library =>
            library.EntryFiles.Count > 0
            && library.IsStarterLibrary
            && !library.IsExecutionSupported),
        "The published C# package boundary did not contain the three lazy, project-only starter catalogs.");
    Require(
        libraries.All(library =>
            !CommandCatalogService.UnpublishedCSharpCatalogIds.Contains(
                library.Id)),
        "An unpublished C# catalog appeared in the Command Lexicon.");
    Require(
        libraries.Single(library => library.Id == "verifier-external").Origin
            == CommandLibraryOrigin.External,
        "The unique external catalog did not load with External origin.");
    Require(
        libraries.All(library =>
            library.Commands.All(command =>
                command.Name != "Collision.ShouldNotLoad")),
        "A colliding external catalog replaced bundled content.");

    var dotNet8 = libraries.Single(library =>
        library.Id == CSharpFrameworkCatalogService.DotNet8LibraryId);
    var dotNet10 = libraries.Single(library =>
        library.Id == CSharpFrameworkCatalogService.DotNet10LibraryId);
    Require(
        dotNet8.Kind == CommandLibraryKind.Framework
        && dotNet8.FrameworkName == ".NET"
        && dotNet8.TargetFramework == "net8.0"
        && dotNet8.ReferenceAssemblyCount > 0
        && dotNet8.IsExecutionSupported,
        "The installed .NET 8 framework surface is incomplete.");
    Require(
        dotNet10.Kind == CommandLibraryKind.Framework
        && dotNet10.FrameworkName == ".NET"
        && dotNet10.TargetFramework == "net10.0"
        && dotNet10.FrameworkVersion.StartsWith(
            "10.",
            StringComparison.Ordinal)
        && dotNet10.ReferenceAssemblyCount > 0
        && dotNet10.IsExecutionSupported
        && dotNet10.IsStarterLibrary
        && !string.Equals(
            dotNet10.SourcePath,
            dotNet8.SourcePath,
            StringComparison.OrdinalIgnoreCase),
        "The installed .NET 10 framework surface is incomplete or not isolated from .NET 8.");

    var dotNetSdk = libraries.Single(library =>
        library.Id == DotNetToolchainCatalogService.DotNetSdkLibraryId);
    var dotNetCli = libraries.Single(library =>
        library.Id == DotNetToolchainCatalogService.DotNetCliLibraryId);
    Require(
        dotNetSdk.Kind == CommandLibraryKind.Sdk
        && dotNetSdk.Origin == CommandLibraryOrigin.Runtime
        && dotNetSdk.FrameworkName == ".NET SDK"
        && dotNetSdk.FrameworkVersion.StartsWith(
            "10.",
            StringComparison.Ordinal)
        && Directory.Exists(dotNetSdk.SourcePath)
        && dotNetSdk.Commands.Count > 25
        && dotNetSdk.Commands.Any(command =>
            command.Name == "dotnet sdk check")
        && dotNetSdk.Commands.Any(command =>
            command.Name == "dotnet package add")
        && dotNetSdk.Commands.All(command =>
            command.CompositionMode == CommandCompositionMode.Statement),
        "The installed .NET SDK command hierarchy was not discovered.");
    Require(
        dotNetCli.Kind == CommandLibraryKind.CommandLineTool
        && dotNetCli.Origin == CommandLibraryOrigin.Runtime
        && dotNetCli.FrameworkName == ".NET CLI"
        && File.Exists(dotNetCli.SourcePath)
        && dotNetCli.Commands.Any(command =>
            command.Name == "dotnet --info")
        && dotNetCli.Commands.Any(command =>
            command.Name == "dotnet --list-sdks")
        && dotNetCli.Commands.Any(command =>
            command.Name == "dotnet <application.dll>"),
        "The installed .NET CLI driver surface was not discovered.");
    Console.WriteLine(
        $"TOOLCHAIN_OK|sdk:{dotNetSdk.FrameworkVersion}:" +
        $"{dotNetSdk.Commands.Count}|cli:{dotNetCli.Commands.Count}");

    var frameworkReferenceRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Reference Assemblies",
        "Microsoft",
        "Framework",
        ".NETFramework");
    if (Directory.Exists(frameworkReferenceRoot))
    {
        var dotNetFramework = libraries.Single(library =>
            library.Id
            == CSharpFrameworkCatalogService.DotNetFrameworkLibraryId);
        Require(
            dotNetFramework.Kind == CommandLibraryKind.Framework
            && dotNetFramework.FrameworkName == ".NET Framework"
            && dotNetFramework.TargetFramework.StartsWith(
                "net4",
                StringComparison.Ordinal)
            && dotNetFramework.ReferenceAssemblyCount > 0
            && !dotNetFramework.IsExecutionSupported
            && dotNetFramework.Origin == CommandLibraryOrigin.Runtime,
            "The installed .NET Framework reference surface is incomplete or incorrectly marked executable.");
    }
}

#pragma warning disable CS8321
static Task VerifyCatalogSearchAsync(
    string verifierRoot,
    bool languageOnly = false)
{
    var completion = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(dispatcher));
        _ = VerifyCatalogSearchCoreAsync(
                verifierRoot,
                languageOnly).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    if (task.Exception is { } exception)
                    {
                        completion.SetException(exception.InnerExceptions);
                    }
                    else
                    {
                        completion.SetException(
                            new InvalidOperationException(
                                "Search verification failed."));
                    }
                }
                else if (task.IsCanceled)
                {
                    completion.SetCanceled();
                }
                else
                {
                    completion.SetResult();
                }

                dispatcher.BeginInvokeShutdown(
                    DispatcherPriority.Background);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.Run();
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return completion.Task;
}
#pragma warning restore CS8321

static Task VerifyCSharpComposerAsync(string verifierRoot)
{
    var completion = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(dispatcher));
        _ = VerifyCSharpComposerCoreAsync(verifierRoot).ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    completion.SetException(
                        task.Exception?.InnerExceptions
                        ?? [new InvalidOperationException(
                            "C# composer verification failed.")]);
                }
                else if (task.IsCanceled)
                {
                    completion.SetCanceled();
                }
                else
                {
                    completion.SetResult();
                }

                dispatcher.BeginInvokeShutdown(
                    DispatcherPriority.Background);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
        Dispatcher.Run();
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    return completion.Task;
}

static async Task VerifyCSharpComposerCoreAsync(string verifierRoot)
{
    var settingsService = new SettingsService(
        Path.Combine(verifierRoot, "composer-app-data"));
    var settings = settingsService.Load();
    settings.ActiveEnvironmentId = "csharp";
    settings.CommandBuilder.ActiveLanguage = "C#";
    var builder = new CommandBuilderViewModel(settings, settingsService);
    await builder.LoadLibrariesAsync(
        new CommandCatalogService(settingsService));

    Require(
        CSharpKeywordCatalogService.Load().Count
            == CSharpKeywordCatalogService.ExpectedKeywordCount,
        "The PDF-derived C# keyword catalog did not expose all 130 terms.");
    Require(
        builder.IsCSharpComposerWorkspace
        && !builder.IsProfileDrivenBuilderWorkspace,
        "The C# workspace still exposed the profile-driven builder.");
    Require(
        builder.SelectedBuilderSurface is
        {
            LibraryId.Length: 0,
            DisplayName: "Select C# Surface"
        }
        && !builder.HasCSharpComposerFramework
        && builder.CSharpComposerTokenKinds.Count == 0,
        "The C# composer exposed statement choices before the user selected a surface.");
    Require(
        builder.BuilderSurfaces.Select(surface => surface.LibraryId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .IsSupersetOf(
            [
                CSharpFrameworkCatalogService.DotNet10LibraryId,
                CSharpFrameworkCatalogService.DotNet8LibraryId,
                DotNetToolchainCatalogService.DotNetSdkLibraryId,
                DotNetToolchainCatalogService.DotNetCliLibraryId
            ]),
        "The framework selector did not expose .NET 10, .NET 8, SDK, and CLI.");

    var emptyProfile = new EnvironmentProfile
    {
        Id = "empty-csharp-composer-verifier",
        Name = "Empty C# composer verifier",
        Language = "C#",
        LibraryIds = []
    };
    builder.Environments.Add(emptyProfile);
    builder.SelectedEnvironment = emptyProfile;
    Require(
        builder.BuilderSurfaces.Any(surface =>
            surface.LibraryId
            == CSharpFrameworkCatalogService.DotNet10LibraryId)
        && builder.BuilderSurfaces.Any(surface =>
            surface.LibraryId
            == DotNetToolchainCatalogService.DotNetSdkLibraryId),
        "An empty C# profile still filtered the framework-first workspace.");

    var frameworkCombo = new ComboBox
    {
        DataContext = builder,
        IsTextSearchEnabled = true
    };
    TextSearch.SetTextPath(
        frameworkCombo,
        nameof(BuilderSurfaceOption.DisplayName));
    BindingOperations.SetBinding(
        frameworkCombo,
        ItemsControl.ItemsSourceProperty,
        new Binding(nameof(builder.BuilderSurfaces)));
    BindingOperations.SetBinding(
        frameworkCombo,
        ComboBox.SelectedItemProperty,
        new Binding(nameof(builder.SelectedBuilderSurface))
        {
            Mode = BindingMode.TwoWay
        });
    frameworkCombo.SelectedItem = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId == CSharpFrameworkCatalogService.WpfLibraryId);
    var wpfDeadline = DateTime.UtcNow.AddSeconds(15);
    while (!builder.IsCSharpComposerApiFramework
           && DateTime.UtcNow < wpfDeadline)
    {
        await Task.Delay(10);
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    Require(
        builder.SelectedBuilderSurface?.LibraryId
            == CSharpFrameworkCatalogService.WpfLibraryId
        && frameworkCombo.SelectionBoxItem is BuilderSurfaceOption
        {
            DisplayName: "WPF 10"
        }
        && builder.IsCSharpComposerApiFramework
        && builder.CSharpNamespaceLevels.Count > 0,
        "A lazily loaded installed framework did not expose its classes in the composer.");

    frameworkCombo.SelectedItem = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId
        == CSharpFrameworkCatalogService.DotNet10LibraryId);
    await Dispatcher.Yield(DispatcherPriority.DataBind);
    Require(
        builder.SelectedBuilderSurface?.LibraryId
        == CSharpFrameworkCatalogService.DotNet10LibraryId
        && frameworkCombo.SelectionBoxItem is BuilderSurfaceOption
        {
            DisplayName: ".NET 10 Base Class Libraries"
        },
        "The selected framework was not retained in the visible selector text: " +
        $"selected={builder.SelectedBuilderSurface?.DisplayName ?? "<null>"}; " +
        $"control={frameworkCombo.SelectionBoxItem ?? "<null>"}.");
    builder.SelectedDotNetInvocationSyntax =
        builder.DotNetInvocationSyntaxOptions.Single(option =>
            option.ExecutionTarget == ExecutionTarget.PowerShell);
    var composerCombo = new ComboBox
    {
        DataContext = builder,
        IsEditable = true,
        IsTextSearchEnabled = true,
        StaysOpenOnEdit = true
    };
    BindingOperations.SetBinding(
        composerCombo,
        ItemsControl.ItemsSourceProperty,
        new Binding(nameof(builder.CSharpComposerMenuItems)));
    BindingOperations.SetBinding(
        composerCombo,
        ComboBox.SelectedItemProperty,
        new Binding(nameof(builder.SelectedCSharpComposerMenuItem))
        {
            Mode = BindingMode.TwoWay
        });
    BindingOperations.SetBinding(
        composerCombo,
        ComboBox.TextProperty,
        new Binding(nameof(builder.CSharpComposerMenuText))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

    Require(
        builder.CSharpComposerMenuHeading == "ADD ITEM"
        && builder.CSharpComposerMenuItems.Any(item =>
            item.TokenKind?.Kind == CSharpComposerTokenKind.TypePath)
        && builder.CSharpComposerMenuItems.Any(item =>
            item.TokenKind?.Kind == CSharpComposerTokenKind.Layout),
        "The reusable add-item selector did not expose its clean category state.");

    ChooseMenu("Class");
    builder.IsCSharpComposerMenuOpen = false;
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CSharpComposerMenuHeading == "SELECT CLASS"
        && builder.IsCSharpComposerMenuOpen
        && builder.CSharpComposerMenuItems.Any(item =>
            item.DisplayName == "System"
            && item.ShowsAddGlyph),
        "The selector did not remain open on the expandable framework choices.");
    ChooseMenu("System");
    builder.IsCSharpComposerMenuOpen = false;
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CSharpComposerMenuHeading == "SELECT CLASS"
        && builder.IsCSharpComposerMenuOpen
        && builder.CSharpComposerMenuItems.Any(item =>
            item.DisplayName == "IO"
            && item.ShowsAddGlyph)
        && builder.Pipeline.Count == 1
        && builder.CommandText == "System"
        && string.IsNullOrEmpty(composerCombo.Text),
        "Selecting System did not advance in place or update the live script.");
    ChooseMenu("Back");
    builder.IsCSharpComposerMenuOpen = false;
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CSharpComposerMenuHeading == "SELECT CLASS"
        && builder.IsCSharpComposerMenuOpen
        && builder.CSharpComposerMenuItems.Any(item =>
            item.DisplayName == "System")
        && builder.Pipeline.Count == 0
        && string.IsNullOrEmpty(builder.CommandText),
        "Class Back did not remove one namespace segment and reopen its parent list.");
    ChooseMenu("System");
    ChooseMenu("IO");
    builder.IsCSharpComposerMenuOpen = false;
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CSharpComposerMenuHeading == "SELECT CLASS"
        && builder.IsCSharpComposerMenuOpen
        && builder.CSharpComposerMenuItems.Any(item =>
            item.DisplayName == "Path")
        && builder.Pipeline.Count == 1
        && builder.CommandText == "System.IO",
        "The continuous class drill-down did not expose System.IO types.");
    ChooseMenu("Path");
    builder.IsCSharpComposerMenuOpen = false;
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CSharpComposerMenuHeading == "ADD ITEM"
        && builder.IsCSharpComposerMenuOpen
        && builder.CSharpComposerMenuItems.Any(item =>
            item.TokenKind?.Kind == CSharpComposerTokenKind.Method)
        && builder.CSharpComposerMenuItems.Any(item =>
            item is { IsBack: true, UndoesTypePath: true })
        && builder.CommandText == "[System.IO.Path]",
        "Selecting a final type did not expose its members or preserve path Back.");
    ChooseMenu("Back");
    builder.IsCSharpComposerMenuOpen = false;
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CSharpComposerMenuHeading == "SELECT CLASS"
        && builder.IsCSharpComposerMenuOpen
        && builder.CSharpComposerMenuItems.Any(item =>
            item.DisplayName == "Path")
        && builder.CommandText == "System.IO",
        "Back from a selected type did not return to its declaring namespace.");
    ChooseMenu("Path");
    ChooseMenu("Method");
    ChooseMenu("Combine");
    ChooseMenu("Overload");
    ChooseMenuWhere(signature => signature.Contains(
        "Combine(string path1, string path2)",
        StringComparison.Ordinal), "Overload");
    SetArgument("path1", "first");
    SetArgument("path2", "second");

    Require(
        builder.CSharpComposerPathLabel == "System.IO.Path"
        && builder.SelectedCSharpType?.IsStaticClass == true
        && builder.SelectedCSharpMember?.KindLabel.Contains(
            "static",
            StringComparison.OrdinalIgnoreCase) == true,
        "The progressive Class / type path lost static type metadata.");
    Require(
        builder.CSharpComposerStatementText
            == "[System.IO.Path]::Combine('first', 'second')",
        $"The PowerShell .NET invocation was malformed: " +
        $"{builder.CSharpComposerStatementText}");

    Require(
        builder.Pipeline.Count == 1
        && builder.ParameterEditors.Count > 0,
        "The live class statement was not projected into Pipeline with parameters.");
    builder.RemoveStatementCommand.Execute(builder.Pipeline.Single());
    Require(
        builder.Pipeline.Count == 0
        && string.IsNullOrWhiteSpace(builder.CommandText)
        && builder.CSharpComposerTokens.Count == 0
        && builder.SelectedCSharpType is null
        && builder.SelectedCSharpMember is null
        && builder.SelectedCSharpOverload is null
        && builder.ParameterEditors.Count == 0
        && builder.CSharpComposerMenuHeading == "ADD ITEM"
        && builder.CSharpComposerMenuItems.Any(item =>
            item.TokenKind?.Kind == CSharpComposerTokenKind.TypePath)
        && builder.CSharpComposerMenuItems.All(item =>
            item.TokenKind?.Kind != CSharpComposerTokenKind.Method),
        "Deleting the live class statement did not clear its cached type, member, and parameter state.");

    builder.SelectedDotNetInvocationSyntax =
        builder.DotNetInvocationSyntaxOptions.Single(option =>
            option.ExecutionTarget == ExecutionTarget.CSharp);
    ChooseMenu("Keyword");
    ChooseMenu("var");
    ChooseMenu("Value");
    composerCombo.Text = "combined";
    builder.CommitCSharpComposerMenuTextCommand.Execute(null);
    ChooseMenu("Operator");
    ChooseMenu("=");
    ChooseMenu("Class");
    ChooseMenu("System");
    ChooseMenu("IO");
    ChooseMenu("Path");
    ChooseMenu("Method");
    ChooseMenu("Combine");
    ChooseMenu("Overload");
    ChooseMenuWhere(signature => signature.Contains(
        "Combine(string path1, string path2)",
        StringComparison.Ordinal), "Overload");
    SetArgument("path1", "first");
    SetArgument("path2", "second");
    ChooseMenu("Punctuation");
    ChooseMenu(";");
    Require(
        builder.CSharpComposerStatementText
            == "var combined = System.IO.Path.Combine(\"first\", \"second\");",
        $"The C# all-in-one statement was malformed: " +
        $"{builder.CSharpComposerStatementText}");

    ChooseMenu("Layout");
    Require(
        builder.CSharpComposerMenuItems.Select(item => item.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .IsSupersetOf(
                ["Back", "Space", "Double Space", "New Line", "Blank Line", "Tab"]),
        "The layout category did not expose the requested spacing controls.");
    var statementBeforeBack = builder.CSharpComposerStatementText;
    ChooseMenu("Back");
    builder.IsCSharpComposerMenuOpen = false;
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CSharpComposerMenuHeading == "ADD ITEM"
        && builder.IsCSharpComposerMenuOpen
        && builder.CSharpComposerMenuItems.Any(item =>
            item.TokenKind?.Kind == CSharpComposerTokenKind.Layout)
        && builder.CSharpComposerStatementText == statementBeforeBack
        && builder.CommandText == statementBeforeBack,
        "Back did not return to the category list without changing the script.");
    ChooseMenu("Layout");
    ChooseMenu("New Line");
    ChooseMenu("Layout");
    ChooseMenu("Tab");
    ChooseMenu("Keyword");
    ChooseMenu("return");
    ChooseMenu("Value");
    composerCombo.Text = "combined";
    builder.CommitCSharpComposerMenuTextCommand.Execute(null);
    ChooseMenu("Punctuation");
    ChooseMenu(";");
    var expectedMultilineStatement =
        "var combined = System.IO.Path.Combine(\"first\", \"second\");"
        + Environment.NewLine
        + "\treturn combined;";
    Require(
        builder.CSharpComposerStatementText == expectedMultilineStatement
        && builder.CommandText == expectedMultilineStatement
        && builder.Pipeline.Count == 1,
        "New-line and tab selections did not update the live multiline script.");

    CommandExecutionRequest? dispatch = null;
    builder.CommandDispatchRequested += (_, request) => dispatch = request;
    builder.RunBuilderCommand.Execute(null);
    Require(
        builder.Pipeline.Count == 1
        && builder.CommandText
            == expectedMultilineStatement
        && dispatch is
        {
            ExecutionTarget: ExecutionTarget.CSharp,
            ExecuteImmediately: true
        },
        "The composed statement did not stage and dispatch to the C# runner.");

    builder.ClearBuilderCommand.Execute(null);
    builder.SelectedBuilderSurface = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId == DotNetToolchainCatalogService.DotNetCliLibraryId);
    ChooseMenu("Command");
    ChooseMenuWhere(
        display => string.Equals(
            display,
            "dotnet --version",
            StringComparison.Ordinal),
        "Command");
    Require(
        builder.CSharpComposerStatementText == "dotnet --version",
        "The framework-first composer did not expose the .NET CLI command.");

    builder.SearchQuery = "System IO Path Combine";
    Require(
        builder.SearchResults.All(result => result.ApiEntry is null),
        "The selected .NET CLI surface leaked .NET framework API search results.");
    builder.SelectedBuilderSurface = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId
        == CSharpFrameworkCatalogService.DotNet10LibraryId);
    builder.SearchQuery = "System IO Path Combine";
    Require(
        builder.SearchResults.Any(result =>
            result.ApiEntry?.Type.FullDisplayName == "System.IO.Path"
            && result.ApiEntry.Member.Name == "Combine"),
        "The selected .NET 10 surface did not expose Path.Combine to search.");

    var exposedFrameworkCount = builder.BuilderSurfaces.Count(surface =>
        !string.IsNullOrWhiteSpace(surface.LibraryId));
    Console.WriteLine(
        $"CSHARP_COMPOSER_OK|keywords:{CSharpKeywordCatalogService.ExpectedKeywordCount}|" +
        $"frameworks:{exposedFrameworkCount}|" +
        "statement-and-dispatch");

    void ChooseMenu(string displayName) =>
        ChooseMenuWhere(
            display => string.Equals(
                display,
                displayName,
                StringComparison.Ordinal),
            builder.CSharpComposerMenuHeading);

    void ChooseMenuWhere(
        Func<string, bool> predicate,
        string context)
    {
        var item = builder.CSharpComposerMenuItems.Single(candidate =>
            predicate(candidate.DisplayName));
        composerCombo.SelectedItem = item;
        Require(
            builder.SelectedCSharpComposerMenuItem is null,
            $"{context} did not reset after selecting {item.DisplayName}.");
    }

    void SetArgument(string name, string value)
    {
        builder.ParameterEditors.Single(editor =>
                editor.Definition.Name == name)
            .Value = value;
    }
}

static async Task VerifyCatalogSearchCoreAsync(
    string verifierRoot,
    bool languageOnly = false)
{
    var settingsService = new SettingsService(
        Path.Combine(verifierRoot, "search-app-data"));
    var settings = settingsService.Load();
    settings.Environments.Add(new EnvironmentProfile
    {
        Id = "mixed-language-verifier",
        Name = "Mixed language verifier",
        Description = "Exercises legacy mixed-profile migration.",
        Language = "C#",
        LibraryIds =
        [
            "csharp-language",
            CommandCatalogService.PowerShellCatalogId
        ]
    });
    settings.ActiveEnvironmentId = "csharp";
    settings.CommandBuilder.ActiveLanguage = "C#";
    var catalogService = new CommandCatalogService(settingsService);
    var builder = new CommandBuilderViewModel(settings, settingsService);
    await builder.LoadLibrariesAsync(catalogService);

    var expectedInputSyntaxes = new[]
    {
        "PowerShell",
        "C#",
        "C++",
        "Python",
        "JavaScript",
        "TypeScript",
        "Java",
        "HTML",
        "CSS",
        "Web APIs"
    };
    Require(
        builder.InputSyntaxOptions.SequenceEqual(expectedInputSyntaxes)
        && builder.SelectedInputSyntax == "C#",
        "Input Syntax did not expose every registered command language in order.");
    var activeEnvironmentId = settings.ActiveEnvironmentId;
    var expectedSurfaceHeadings = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["PowerShell"] = "SELECT MODULE",
        ["C#"] = "SELECT LIBRARY, FRAMEWORK, OR TOOL",
        ["C++"] = "SELECT LIBRARY OR PACKAGE",
        ["Python"] = "SELECT LIBRARY OR PACKAGE",
        ["JavaScript"] = "SELECT LIBRARY OR FRAMEWORK",
        ["TypeScript"] = "SELECT LIBRARY OR FRAMEWORK",
        ["Java"] = "SELECT LIBRARY OR FRAMEWORK",
        ["HTML"] = "SELECT LIBRARY OR FRAMEWORK",
        ["CSS"] = "SELECT LIBRARY OR FRAMEWORK",
        ["Web APIs"] = "SELECT API LIBRARY OR FRAMEWORK"
    };
    var expectedEditorActionLabels = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["PowerShell"] = "To editor",
        ["C#"] = "To editor",
        ["C++"] = "To editor",
        ["Python"] = "To editor",
        ["JavaScript"] = "To editor",
        ["TypeScript"] = "To editor",
        ["Java"] = "To editor",
        ["HTML"] = "Insert markup",
        ["CSS"] = "Insert styles",
        ["Web APIs"] = "Insert snippet"
    };
    foreach (var inputSyntax in expectedInputSyntaxes)
    {
        builder.SelectedInputSyntax = inputSyntax;
        Require(
            CommandLanguageRegistry.TryGet(inputSyntax, out var language),
            $"{inputSyntax} did not resolve to a language presentation contract.");
        var populatedSurfaces = builder.BuilderSurfaces
            .Where(surface => !string.IsNullOrWhiteSpace(surface.LibraryId))
            .ToList();
        Require(
            populatedSurfaces.Count > 0
            && populatedSurfaces.All(surface => builder.Libraries.Any(library =>
                library.Id == surface.LibraryId
                && string.Equals(
                    library.Language,
                    inputSyntax,
                    StringComparison.OrdinalIgnoreCase)))
            && builder.BuilderSurfaceHeading
                == expectedSurfaceHeadings[inputSyntax]
            && builder.LibrarySelections
                .Where(selection => selection.IsSelected)
                .All(selection => string.Equals(
                    selection.Library.Language,
                    inputSyntax,
                    StringComparison.OrdinalIgnoreCase))
            && builder.InputSyntaxDescription == language.WorkflowDescription
            && builder.AddStatementActionLabel
                == $"Add {language.CompositionUnitSingular}"
            && builder.EditorActionLabel
                == expectedEditorActionLabels[inputSyntax]
            && builder.TerminalActionLabel == "Stage"
            && builder.HasTerminalActions
                == language.SupportsTerminalExecution
            && builder.IsEditorPrimaryAction
                == (language.PrimaryDestination
                    == CommandBuilderPrimaryDestination.Editor)
            && builder.IsPipelineEmpty
            && builder.PipelineSummaryLabel
                == $"0 {language.CompositionUnitPlural}"
            && builder.PipelineEmptyHint.Contains(
                language.CompositionUnitPlural,
                StringComparison.OrdinalIgnoreCase)
            && builder.EditorActionToolTip.Contains(
                EditorLanguageCatalog.GetLanguage(
                    language.PreferredEditorLanguageId).DisplayName,
                StringComparison.OrdinalIgnoreCase)
            && (!language.SupportsTerminalExecution
                || builder.RunActionToolTip.Contains(
                    language.RunActionDescription,
                    StringComparison.Ordinal))
            && settings.ActiveEnvironmentId == activeEnvironmentId
            && settings.CommandBuilder.ActiveLanguage == inputSyntax,
            $"{inputSyntax} did not produce an independent, language-filtered builder stage with coherent actions.");
    }

    var newtonsoftLibrary = builder.Libraries.Single(library =>
        library.Id == "newtonsoft-json-popular");
    await catalogService.LoadLibraryEntriesAsync(newtonsoftLibrary);
    await Task.WhenAll(newtonsoftLibrary.Commands.Select(
        catalogService.LoadCommandGuideAsync));
    builder.SelectedInputSyntax = "PowerShell";
    builder.SelectedInputSyntax = "C#";
    builder.SelectedBuilderSurface = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId == newtonsoftLibrary.Id);
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.IsCSharpRecipeSurface
        && !builder.IsCSharpComposerWorkspace
        && builder.IsHierarchySurface
        && builder.HierarchyLevels.Count > 0,
        "A C# package recipe did not switch from the .NET API composer to the shared library hierarchy.");
    builder.SelectedCommand = newtonsoftLibrary.Commands[0];
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    Require(
        builder.CanAddCurrentStatement
        && builder.ParameterEditors.Count > 0
        && !builder.SelectedCommand.IsExecutionSupported,
        "A hydrated C# package recipe did not expose its configurable inputs and project-only policy.");
    builder.AddStatementCommand.Execute(null);
    var popularDispatches = new List<CommandExecutionRequest>();
    EventHandler<CommandExecutionRequest> popularDispatchHandler =
        (_, request) => popularDispatches.Add(request);
    builder.CommandDispatchRequested += popularDispatchHandler;
    builder.RunBuilderCommand.Execute(null);
    Require(
        builder.Pipeline.Count == 1
        && popularDispatches.Count == 0
        && builder.StatusMessage
            == newtonsoftLibrary.ExecutionSupportMessage,
        "The isolated C# runner accepted a project-only package recipe instead of explaining its dependency boundary.");
    builder.ClearBuilderCommand.Execute(null);

    var requestsLibrary = builder.Libraries.Single(library =>
        library.Id == "requests-popular");
    await catalogService.LoadLibraryEntriesAsync(requestsLibrary);
    await Task.WhenAll(requestsLibrary.Commands.Select(
        catalogService.LoadCommandGuideAsync));
    builder.SelectedInputSyntax = "Python";
    builder.SelectedBuilderSurface = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId == requestsLibrary.Id);
    builder.SelectedCommand = requestsLibrary.Commands[0];
    await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    builder.AddStatementCommand.Execute(null);
    builder.RunBuilderCommand.Execute(null);
    Require(
        popularDispatches.Count == 1
        && popularDispatches[0].ExecutionTarget
            == ExecutionTarget.External
        && popularDispatches[0].PreferredShellKind
            == TerminalShellKind.Python
        && popularDispatches[0].ExecuteImmediately,
        "A runnable Python package recipe did not retain the approved Python terminal route.");
    builder.CommandDispatchRequested -= popularDispatchHandler;
    builder.ClearBuilderCommand.Execute(null);

    builder.SelectedInputSyntax = "Python";
    var pythonProbe = new CommandDefinition
    {
        Id = "verifier.python.optional-parameter",
        CatalogId = "verifier-python",
        Name = "print optional value",
        DisplayName = "Print optional value",
        Template = "print({value})",
        ExecutionTarget = ExecutionTarget.External,
        PreferredShellKind = TerminalShellKind.Python,
        CompositionMode = CommandCompositionMode.Statement,
        Parameters =
        [
            new CommandParameterDefinition
            {
                Name = "value",
                DisplayName = "value",
                Placeholder = "value",
                TypeName = "expression"
            }
        ]
    };
    builder.SelectedCommand = pythonProbe;
    Require(
        builder.ParameterEditors.Single() is
        {
            IsIncluded: false,
            CanToggleInclusion: true
        }
        && builder.StagedStatementText == "print()",
        "An unchecked external parameter still leaked a placeholder into composed source.");
    builder.AddStatementCommand.Execute(null);
    Require(
        builder.Pipeline.Count == 1
        && builder.CommandText == "print()"
        && !builder.IsPipelineEmpty,
        "The Python Add action did not project the preview into the script.");

    var dispatches = new List<CommandExecutionRequest>();
    builder.CommandDispatchRequested += (_, request) => dispatches.Add(request);
    builder.SendToTerminalCommand.Execute(null);
    builder.RunBuilderCommand.Execute(null);
    Require(
        dispatches.Count == 2
        && dispatches.All(request =>
            request.ExecutionTarget == ExecutionTarget.External
            && request.PreferredShellKind == TerminalShellKind.Python)
        && !dispatches[0].ExecuteImmediately
        && dispatches[1].ExecuteImmediately,
        "Stage and Run did not preserve the Python runtime or execution intent.");

    EditorInsertionRequest? insertion = null;
    builder.EditorInsertionRequested += (_, request) =>
    {
        insertion = request;
        request.Succeeded = true;
    };
    builder.InsertIntoEditorCommand.Execute(null);
    Require(
        insertion is
        {
            Text: "print()",
            Language: "Python",
            Succeeded: true
        },
        "To editor did not preserve the composed Python source and language.");

    builder.RemoveStatementCommand.Execute(builder.Pipeline.Single());
    var dispatchCountBeforeEmptyActions = dispatches.Count;
    builder.SendToTerminalCommand.Execute(null);
    Require(
        builder.IsPipelineEmpty
        && string.IsNullOrEmpty(builder.CommandText)
        && dispatches.Count == dispatchCountBeforeEmptyActions
        && builder.IsStatusMessageError
        && builder.StatusMessage.Contains("staging", StringComparison.OrdinalIgnoreCase),
        "Removing the final item did not leave Stage as a safe, explanatory no-op.");
    builder.RunBuilderCommand.Execute(null);
    Require(
        dispatches.Count == dispatchCountBeforeEmptyActions
        && builder.IsStatusMessageError
        && builder.StatusMessage.Contains("running", StringComparison.OrdinalIgnoreCase),
        "Run was not a safe, explanatory no-op for an empty composition.");

    var requiredExternalParameter = new ParameterEditorViewModel(
        new CommandParameterDefinition
        {
            Name = "required",
            DisplayName = "required",
            IsRequired = true
        },
        ExecutionTarget.External);
    Require(
        requiredExternalParameter.IsIncluded
        && !requiredExternalParameter.CanToggleInclusion,
        "A required non-PowerShell parameter did not start included and locked.");

    builder.SelectedInputSyntax = "HTML";
    var catalogPreviewNotificationRaised = false;
    builder.PropertyChanged += (_, eventArgs) =>
    {
        if (eventArgs.PropertyName == nameof(builder.HasCatalogStatementPreview))
        {
            catalogPreviewNotificationRaised = true;
        }
    };
    builder.SelectedCommand = new CommandDefinition
    {
        Id = "verifier.html.main",
        CatalogId = "verifier-html",
        Name = "main element",
        DisplayName = "Main element",
        Template = "<main></main>",
        ExecutionTarget = ExecutionTarget.External,
        PreferredShellKind = TerminalShellKind.External,
        CompositionMode = CommandCompositionMode.Statement
    };
    Require(
        builder.HasCatalogStatementPreview && catalogPreviewNotificationRaised,
        "Selecting an HTML hierarchy entry did not reveal the Ready to add card.");
    builder.AddStatementCommand.Execute(null);
    builder.RunBuilderCommand.Execute(null);
    Require(
        builder.CommandText == "<main></main>"
        && dispatches.Count == dispatchCountBeforeEmptyActions
        && builder.IsStatusMessageError
        && builder.StatusMessage.Contains("editor-only", StringComparison.OrdinalIgnoreCase),
        "An editor-only language was allowed onto the terminal dispatch path.");
    builder.ClearBuilderCommand.Execute(null);
    Require(
        builder.IsPipelineEmpty && string.IsNullOrEmpty(builder.CommandText),
        "Clear did not reset an editor-only composition.");

    foreach (var language in CommandLanguageRegistry.Definitions.Where(
                 definition => definition.Id != "csharp"))
    {
        builder.SelectedInputSyntax = language.DisplayName;
        var probeText = language.Id switch
        {
            "powershell" => "Write-Output 'hypershell-probe'",
            "cpp" => "std::cout << \"hypershell-probe\\n\";",
            "python" => "print('hypershell-probe')",
            "javascript" => "console.log('hypershell-probe');",
            "typescript" =>
                "const hypershellProbe: string = 'hypershell-probe';",
            "java" => "System.out.println(\"hypershell-probe\");",
            "html" => "<main>hypershell-probe</main>",
            "css" => ".hypershell-probe { display: block; }",
            "web" => "document.title = 'hypershell-probe';",
            _ => throw new InvalidOperationException(
                $"No action probe is defined for {language.DisplayName}.")
        };
        builder.SelectedCommand = new CommandDefinition
        {
            Id = $"verifier.{language.Id}.actions",
            CatalogId = $"verifier-{language.Id}",
            Name = $"{language.DisplayName} action probe",
            DisplayName = $"{language.DisplayName} action probe",
            Template = probeText,
            ExecutionTarget = language.ExecutionTarget,
            PreferredShellKind = language.PreferredShellKind,
            CompositionMode = CommandCompositionMode.Statement
        };
        builder.AddStatementCommand.Execute(null);
        Require(
            builder.Pipeline.Count == 1
            && builder.CommandText == probeText,
            $"{language.DisplayName} Add did not produce the exact composed source.");

        var dispatchCountBeforePrimary = dispatches.Count;
        insertion = null;
        builder.PrimaryActionCommand.Execute(null);
        if (language.PrimaryDestination
            == CommandBuilderPrimaryDestination.Editor)
        {
            Require(
                insertion is not null
                && insertion.Language == language.DisplayName
                && insertion.Text == probeText
                && dispatches.Count == dispatchCountBeforePrimary,
                $"{language.DisplayName} did not route its primary action to the editor.");
        }
        else
        {
            Require(
                insertion is null
                && dispatches.Count == dispatchCountBeforePrimary + 1
                && dispatches[^1].ExecuteImmediately,
                $"{language.DisplayName} did not route its primary action to Run.");
        }

        insertion = null;
        builder.InsertIntoEditorCommand.Execute(null);
        Require(
            insertion is not null
            && insertion.Language == language.DisplayName
            && insertion.Text == probeText,
            $"{language.DisplayName} To editor did not preserve source or language.");

        var dispatchCountBeforeTerminalActions = dispatches.Count;
        builder.SendToTerminalCommand.Execute(null);
        builder.RunBuilderCommand.Execute(null);
        if (language.SupportsTerminalExecution)
        {
            Require(
                dispatches.Count == dispatchCountBeforeTerminalActions + 2
                && dispatches[^2] is
                {
                    ExecuteImmediately: false
                }
                && dispatches[^1] is
                {
                    ExecuteImmediately: true
                }
                && dispatches[^2].ExecutionTarget == language.ExecutionTarget
                && dispatches[^2].PreferredShellKind
                    == language.PreferredShellKind
                && dispatches[^1].ExecutionTarget == language.ExecutionTarget
                && dispatches[^1].PreferredShellKind
                    == language.PreferredShellKind,
                $"{language.DisplayName} Stage and Run did not preserve their runtime contract.");
        }
        else
        {
            Require(
                dispatches.Count == dispatchCountBeforeTerminalActions
                && builder.IsStatusMessageError
                && builder.StatusMessage.Contains(
                    "editor-only",
                    StringComparison.OrdinalIgnoreCase),
                $"{language.DisplayName} escaped its editor-only terminal guard.");
        }

        builder.ClearBuilderCommand.Execute(null);
        Require(
            builder.IsPipelineEmpty && string.IsNullOrEmpty(builder.CommandText),
            $"{language.DisplayName} Clear did not reset the composition.");
    }

    builder.SelectedInputSyntax = "C#";
    var legacyEnvironment = builder.SelectedEnvironment;
    builder.SelectedEnvironment = builder.Environments.First(environment =>
        string.Equals(
            environment.Language,
            "PowerShell",
            StringComparison.OrdinalIgnoreCase));
    Require(
        builder.SelectedInputSyntax == "C#"
        && builder.BuilderSurfaces
            .Where(surface => !string.IsNullOrWhiteSpace(surface.LibraryId))
            .All(surface => builder.Libraries.Any(library =>
                library.Id == surface.LibraryId
                && string.Equals(
                    library.Language,
                    "C#",
                    StringComparison.OrdinalIgnoreCase))),
        "Changing legacy profile data still changed the active builder language.");
    if (legacyEnvironment is not null)
    {
        builder.SelectedEnvironment = legacyEnvironment;
    }

    Require(
        WorkspaceSideToolRegistry.Definitions.All(definition =>
            !string.Equals(
                definition.Id,
                "hypershell.tool.command-lexicon",
                StringComparison.OrdinalIgnoreCase)),
        "The retired Command Profiles and Lexicon side-tool tab is still registered.");
    settingsService.Save(settings);
    Require(
        settingsService.Load().CommandBuilder.ActiveLanguage == "C#",
        "The active Input Syntax did not persist independently of legacy profiles.");
    if (languageOnly)
    {
        Console.WriteLine(
            $"COMMAND_BUILDER_LANGUAGE_OK|languages:{builder.InputSyntaxOptions.Count}|" +
            "profile-independent|surface-filtered|adaptive-labels|" +
            "preview-reactive|add-editor-stage-run-clear|parameter-safe|" +
            "editor-only-guard|popular-library-routing|persisted");
        return;
    }

    var libraryLanguages = builder.Libraries.ToDictionary(
        library => library.Id,
        library => library.Language,
        StringComparer.OrdinalIgnoreCase);
    Require(
        settings.Environments.All(profile =>
            profile.LibraryIds
                .Where(libraryLanguages.ContainsKey)
                .All(libraryId => string.Equals(
                    profile.Language,
                    libraryLanguages[libraryId],
                    StringComparison.OrdinalIgnoreCase))),
        "A command profile retained a library from another language.");
    Require(
        settings.Environments.Single(profile =>
                profile.Id == "mixed-language-verifier")
            .LibraryIds.Count == 0,
        "Legacy profile migration retained a retired C# catalog.");
    Require(
        builder.LibraryLanguageTabs.Select(tab => tab.Language)
            .SequenceEqual(
                [
                    "PowerShell",
                    "C#",
                    "C++",
                    "Python",
                    "JavaScript",
                    "TypeScript",
                    "Java",
                    "HTML",
                    "CSS",
                    "Web APIs"
                ])
        && builder.SelectedLibraryLanguageTab?.Language == "C#",
        "The Command Lexicon did not create its registered language containers.");
    Require(
        builder.LibraryLanguageTabs
            .Single(tab => tab.Language == "PowerShell")
            .Libraries.All(selection =>
                !selection.IsCompatibleWithProfile),
        "PowerShell selections were not disabled for a C# profile.");
    var csharpTab = builder.LibraryLanguageTabs.Single(tab =>
        tab.Language == "C#");
    var dotNet10Group = csharpTab.Groups.Single(group =>
        group.Id == CSharpFrameworkCatalogService.DotNet10LibraryId);
    var dotNet8Group = csharpTab.Groups.Single(group =>
        group.Id == CSharpFrameworkCatalogService.DotNet8LibraryId);
    var dotNetSdkGroup = csharpTab.Groups.Single(group =>
        group.Id == DotNetToolchainCatalogService.DotNetSdkLibraryId);
    var dotNetCliGroup = csharpTab.Groups.Single(group =>
        group.Id == DotNetToolchainCatalogService.DotNetCliLibraryId);
    Require(
        dotNet10Group.Libraries.Select(selection => selection.Library.Id)
            .SequenceEqual(
            [
                CSharpFrameworkCatalogService.DotNet10LibraryId
            ])
        && dotNet8Group.Libraries.Select(selection =>
                selection.Library.Id)
            .SequenceEqual(
            [
                CSharpFrameworkCatalogService.DotNet8LibraryId
            ])
        && dotNetSdkGroup.Libraries.Select(selection =>
                selection.Library.Id)
            .SequenceEqual(
            [
                DotNetToolchainCatalogService.DotNetSdkLibraryId
            ])
        && dotNetCliGroup.Libraries.Select(selection =>
                selection.Library.Id)
            .SequenceEqual(
            [
                DotNetToolchainCatalogService.DotNetCliLibraryId
            ])
        && csharpTab.Groups.All(group =>
            !string.Equals(
                group.Id,
                "C#-language",
                StringComparison.OrdinalIgnoreCase))
        && csharpTab.Libraries.All(selection =>
            !CommandCatalogService.UnpublishedCSharpCatalogIds.Contains(
                selection.Library.Id)),
        "The C# Lexicon did not isolate framework, SDK, and CLI sections from unpublished catalogs.");

    var dotNetFrameworkSelection = builder.LibrarySelections
        .SingleOrDefault(selection =>
            selection.Library.Id
            == CSharpFrameworkCatalogService.DotNetFrameworkLibraryId);
    if (dotNetFrameworkSelection is not null)
    {
        Require(
            builder.LibraryLanguageTabs
                .Single(tab => tab.Language == "C#")
                .Libraries.Contains(dotNetFrameworkSelection)
            && dotNetFrameworkSelection.Library.Kind
                == CommandLibraryKind.Framework
            && dotNetFrameworkSelection.HasExecutionSupportNotice,
            ".NET Framework was not organized as a separate C# framework entry.");

        dotNetFrameworkSelection.IsSelected = true;
        var indexedDotNetSelections = builder.LibrarySelections
            .Where(selection => selection.Library.Id is
                CSharpFrameworkCatalogService.DotNet8LibraryId
                or CSharpFrameworkCatalogService.DotNet9LibraryId
                or CSharpFrameworkCatalogService.DotNet10LibraryId)
            .ToList();
        foreach (var selection in indexedDotNetSelections)
        {
            selection.IsSelected = false;
        }

        builder.SelectedBuilderSurface = builder.BuilderSurfaces.Single(
            surface => surface.LibraryId
                == CSharpFrameworkCatalogService.DotNetFrameworkLibraryId);
        var frameworkIndexDeadline = DateTime.UtcNow.AddSeconds(15);
        while (builder.CSharpNamespaceLevels.Count == 0
               && DateTime.UtcNow < frameworkIndexDeadline)
        {
            await Task.Delay(10);
        }

        builder.SearchQuery = "System Collections Generic List Add";
        Require(
            builder.CSharpNamespaceLevels.Count > 0
            && builder.SearchResults.Any(result =>
                result.ApiEntry?.LibraryId
                    == CSharpFrameworkCatalogService.DotNetFrameworkLibraryId)
            && !builder.DotNetApiSurfaceLabel.Contains(
                "adapter pending",
                StringComparison.OrdinalIgnoreCase),
            ".NET Framework did not expose its installed class index.");
        foreach (var selection in indexedDotNetSelections)
        {
            selection.IsSelected = true;
        }

        dotNetFrameworkSelection.IsSelected = false;
    }

    var defaultCSharpProfile = builder.SelectedEnvironment
        ?? throw new InvalidOperationException(
            "The default C# profile was not selected.");
    Require(
        defaultCSharpProfile.LibraryIds.Contains(
            CSharpFrameworkCatalogService.DotNet10LibraryId,
            StringComparer.OrdinalIgnoreCase)
        && defaultCSharpProfile.LibraryIds.Contains(
            CSharpFrameworkCatalogService.DotNet8LibraryId,
            StringComparer.OrdinalIgnoreCase)
        && defaultCSharpProfile.LibraryIds.Contains(
            DotNetToolchainCatalogService.DotNetSdkLibraryId,
            StringComparer.OrdinalIgnoreCase)
        && defaultCSharpProfile.LibraryIds.Contains(
            DotNetToolchainCatalogService.DotNetCliLibraryId,
            StringComparer.OrdinalIgnoreCase),
        "The C# starter profile did not include its framework, SDK, and CLI surfaces.");

    var originalProfileName = defaultCSharpProfile.Name;
    var originalProfileDescription = defaultCSharpProfile.Description;
    var changedProfileProperties = new List<string>();
    defaultCSharpProfile.PropertyChanged += (_, eventArgs) =>
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.PropertyName))
        {
            changedProfileProperties.Add(eventArgs.PropertyName);
        }
    };
    builder.SelectedEnvironmentName = ".NET 10 verifier profile";
    builder.SelectedEnvironmentDescription =
        "Version-specific .NET framework profile.";
    Require(
        defaultCSharpProfile.Name == originalProfileName
        && defaultCSharpProfile.Description == originalProfileDescription
        && !builder.ActiveBuilderProfileSummary.Contains(
            ".NET 10 verifier profile",
            StringComparison.Ordinal),
        "Profile name or description edits were committed before Save.");
    builder.SaveProfileCommand.Execute(null);
    var reloadedProfile = settingsService.Load().Environments.Single(profile =>
        profile.Id == defaultCSharpProfile.Id);
    Require(
        defaultCSharpProfile.Name == ".NET 10 verifier profile"
        && defaultCSharpProfile.Description
            == "Version-specific .NET framework profile."
        && reloadedProfile.Name == defaultCSharpProfile.Name
        && reloadedProfile.Description == defaultCSharpProfile.Description
        && changedProfileProperties.Contains(
            nameof(EnvironmentProfile.Name))
        && changedProfileProperties.Contains(
            nameof(EnvironmentProfile.Description))
        && builder.ActiveBuilderLanguageSummary.StartsWith(
            "C# ·",
            StringComparison.Ordinal),
        "Save did not commit the profile draft and notify selector bindings.");

    var environmentCount = builder.Environments.Count;
    Require(
        builder.DeleteProfileCommand.CanExecute(null),
        "The built-in profile Delete action became unreadable or unavailable.");
    builder.DeleteProfileCommand.Execute(null);
    Require(
        builder.Environments.Count == environmentCount
        && builder.StatusMessage.Contains(
            "built-in",
            StringComparison.OrdinalIgnoreCase),
        "Deleting a protected profile did not produce a clear explanation.");

    builder.ToggleProfileCreatorCommand.Execute(null);
    builder.NewProfileLanguage = "C#";
    builder.NewProfileName = defaultCSharpProfile.Name;
    Require(
        builder.CreateProfileCommand.CanExecute(null)
        && builder.NewProfileValidationMessage.Contains(
            "already exists",
            StringComparison.OrdinalIgnoreCase),
        "A duplicate profile name silently disabled Create.");
    builder.CreateProfileCommand.Execute(null);
    Require(
        builder.Environments.Count == environmentCount
        && builder.StatusMessage.Contains(
            "already exists",
            StringComparison.OrdinalIgnoreCase),
        "Clicking Create with a duplicate name did not explain the conflict.");

    builder.NewProfileName = "Created workflow verifier";
    builder.CreateProfileCommand.Execute(null);
    var createdProfile = builder.SelectedEnvironment
        ?? throw new InvalidOperationException(
            "The profile workflow did not select the new profile.");
    Require(
        createdProfile.LibraryIds.Count == 0
        && builder.BuilderSurfaces.Any(surface => surface.LibraryId
            == CSharpFrameworkCatalogService.DotNet10LibraryId),
        "A legacy profile selection incorrectly filtered the language-first builder.");
    builder.LibrarySelections.Single(selection =>
            selection.Library.Id
            == CSharpFrameworkCatalogService.DotNet10LibraryId)
        .IsSelected = true;
    builder.SaveProfileCommand.Execute(null);
    Require(
        builder.BuilderSurfaces.Any(surface => surface.LibraryId
            == CSharpFrameworkCatalogService.DotNet10LibraryId)
        && builder.SelectedInputSyntax == "C#",
        "Saving legacy profile data changed the language-first builder surface.");

    Require(
        builder.DeleteProfileCommand.CanExecute(null),
        "The custom-profile Delete action became unavailable.");
    builder.DeleteProfileCommand.Execute(null);
    Require(
        !builder.Environments.Contains(createdProfile)
        && builder.StatusMessage.Contains(
            "Deleted Created workflow verifier",
            StringComparison.Ordinal),
        "A custom profile could not be deleted.");

    builder.SelectedEnvironment = defaultCSharpProfile;
    Require(
        builder.SelectedInputSyntax == "C#"
        && builder.BuilderSurfaces.Any(surface => surface.LibraryId
            == DotNetToolchainCatalogService.DotNetSdkLibraryId),
        "Selecting legacy profile data changed the active language or its surfaces.");

    var sdkLibrary = builder.Libraries.Single(library =>
        library.Id == DotNetToolchainCatalogService.DotNetSdkLibraryId);
    builder.SelectedBuilderSurface = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId
        == DotNetToolchainCatalogService.DotNetSdkLibraryId);
    Require(
        builder.IsDotNetToolchainSurface
        && builder.PrimaryFacetHeading == "1 · COMMAND GROUP"
        && builder.SecondaryFacetHeading == "2 · COMMAND"
        && builder.Actions.Contains("SDK commands")
        && builder.Actions.Contains("package"),
        "The .NET SDK did not activate its dedicated command hierarchy.");
    builder.SelectedAction = "SDK commands";
    builder.SelectedObject = "build";
    Require(
        builder.SelectedCommand?.Name == "dotnet build"
        && builder.ParameterSectionHeading == "3 · ARGUMENTS",
        "The SDK command hierarchy did not resolve dotnet build.");
    builder.ParameterEditors.Single(editor =>
            editor.Definition.Name == "arguments")
        .Value = "--help";
    builder.AddStatementCommand.Execute(null);
    Require(
        builder.CommandText == "dotnet build --help",
        $"The SDK command preview was malformed: {builder.CommandText}");
    using (var cancellation =
           new CancellationTokenSource(TimeSpan.FromSeconds(15)))
    using (var powerShellSession = new PowerShellTerminalSession())
    {
        var sdkHelpResult = await powerShellSession.ExecuteAsync(
            builder.CommandText,
            verifierRoot,
            cancellation.Token);
        Require(
            sdkHelpResult.Errors.Count == 0
            && sdkHelpResult.Output.Any(line =>
                line.Contains(
                    "dotnet build",
                    StringComparison.OrdinalIgnoreCase)
                || line.Contains(
                    "Usage:",
                    StringComparison.OrdinalIgnoreCase)),
            "The generated dotnet build --help command failed.");
    }

    builder.ClearBuilderCommand.Execute(null);
    builder.SelectedBuilderSurface = builder.BuilderSurfaces.Single(surface =>
        surface.LibraryId
        == DotNetToolchainCatalogService.DotNetCliLibraryId);
    builder.SelectedAction = "Environment information";
    builder.SelectedObject = "--version";
    Require(
        builder.SelectedCommand?.Name == "dotnet --version"
        && builder.StagedStatementText == "dotnet --version",
        "The .NET CLI surface did not compose dotnet --version.");
    builder.AddStatementCommand.Execute(null);
    using (var powerShellSession = new PowerShellTerminalSession())
    {
        var versionResult = await powerShellSession.ExecuteAsync(
            builder.CommandText,
            verifierRoot);
        Require(
            versionResult.Errors.Count == 0
            && versionResult.Output.Any(line => string.Equals(
                line.Trim(),
                sdkLibrary.FrameworkVersion,
                StringComparison.OrdinalIgnoreCase)),
            "The generated dotnet --version command did not use the discovered CLI.");
    }

    builder.ClearBuilderCommand.Execute(null);
    builder.SearchQuery = "dotnet package add";
    var packageAdd = builder.SearchResults.SingleOrDefault(result =>
        result.Command?.Name == "dotnet package add");
    Require(
        packageAdd is not null,
        "Search did not find the installed dotnet package add command.");
    builder.SelectedSearchResult = packageAdd;
    Require(
        builder.SelectedBuilderSurface?.LibraryId
            == DotNetToolchainCatalogService.DotNetSdkLibraryId
        && builder.SelectedAction == "package"
        && builder.SelectedObject == "add"
        && builder.SelectedCommand?.Name == "dotnet package add",
        "Selecting an SDK search result did not reconstruct its command hierarchy.");

    builder.SearchQuery = "System Collections Generic List Add";
    var runtimeMatches = builder.SearchResults.Where(result =>
            result.ApiEntry?.Type.FullDisplayName
                == "System.Collections.Generic.List<T>"
            && result.ApiEntry.Member.Name == "Add")
        .ToList();
    Require(
        runtimeMatches.Select(result => result.ApiEntry!.LibraryId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            .IsSupersetOf(
            [
                CSharpFrameworkCatalogService.DotNet8LibraryId,
                CSharpFrameworkCatalogService.DotNet10LibraryId
            ]),
        "Search did not keep the .NET 8 and .NET 10 List<T>.Add entries separate.");
    var runtimeMatch = runtimeMatches.Single(result =>
        result.ApiEntry?.LibraryId
        == CSharpFrameworkCatalogService.DotNet10LibraryId);

    builder.SelectedSearchResult = runtimeMatch;
    Require(
        builder.CSharpNamespaceLevels.Count >= 3
        && builder.CSharpNamespaceLevels[0].SelectedOption?.Segment
            == "System"
        && builder.CSharpNamespaceLevels[1].SelectedOption?.Segment
            == "Collections"
        && builder.CSharpNamespaceLevels[2].SelectedOption?.Segment
            == "Generic"
        && builder.SelectedCSharpType?.DisplayName == "List<T>"
        && builder.SelectedCSharpMember?.DisplayName == "Add"
        && builder.SelectedCSharpOverload is not null
        && builder.SelectedBuilderSurface?.LibraryId
            == CSharpFrameworkCatalogService.DotNet10LibraryId
        && builder.SelectedCommand?.ExecutionTarget
            == ExecutionTarget.CSharp,
        "Selecting a runtime API result did not reconstruct the cascading namespace path.");

    builder.SelectedCSharpOverload = builder.CSharpOverloads.First(option =>
        option.RuntimeOverload is not null);
    foreach (var editor in builder.ParameterEditors)
    {
        editor.Value = editor.Definition.Name switch
        {
            "T" => "int",
            "instance" => "new List<int>()",
            "item" => "42",
            _ => editor.Value
        };
    }

    builder.AddStatementCommand.Execute(null);
    Require(
        builder.CommandText.Contains(
            "new List<int>()",
            StringComparison.Ordinal)
        && builder.CommandText.Contains(".Add(42)", StringComparison.Ordinal)
        && !builder.CommandText.Contains('{'),
        $"The runtime overload did not produce a completed C# statement: {builder.CommandText}");

    var dotnetExecutable = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "dotnet",
        "dotnet.exe");
    using var session = new CSharpTerminalSession(new TerminalProfile
    {
        Id = "verifier-generated-api",
        Name = "Verifier generated API",
        ShellKind = TerminalShellKind.CSharp,
        ExecutablePath = dotnetExecutable
    });
    var generatedResult = await session.ExecuteAsync(
        builder.CommandText,
        verifierRoot);
    Require(
        generatedResult.Errors.Count == 0,
        $"Generated API statement failed: {string.Join(Environment.NewLine, generatedResult.Errors)}");

    var namespaceSegments = new[]
    {
        "System",
        "Net",
        "Http",
        "Metrics"
    };
    for (var depth = 0; depth < namespaceSegments.Length; depth++)
    {
        Require(
            builder.CSharpNamespaceLevels.Count > depth,
            $"The namespace cascade ended before {namespaceSegments[depth]}.");
        var option = builder.CSharpNamespaceLevels[depth].Options
            .SingleOrDefault(candidate => string.Equals(
                candidate.Segment,
                namespaceSegments[depth],
                StringComparison.Ordinal));
        Require(
            option is not null,
            $"The namespace cascade did not expose {namespaceSegments[depth]}.");
        builder.CSharpNamespaceLevels[depth].SelectedOption = option;
    }

    Require(
        builder.SelectedNamespacePath == "System.Net.Http.Metrics",
        "The namespace cascade did not reach System.Net.Http.Metrics.");
    var metricsLevel = builder.CSharpNamespaceLevels[3];
    var useHttpSelection = metricsLevel.Options.SingleOrDefault(option =>
        option.IsParentNamespaceSelection);
    Require(
        useHttpSelection is not null
        && useHttpSelection.Segment.Length == 0
        && useHttpSelection.CountLabel.Length == 0
        && useHttpSelection.FullName == "System.Net.Http",
        "Level 4 did not provide a blank System.Net.Http selection.");

    metricsLevel.SelectedOption = useHttpSelection;
    Require(
        builder.SelectedNamespacePath == "System.Net.Http"
        && builder.CSharpNamespaceLevels.Count == 4
        && ReferenceEquals(
            builder.CSharpNamespaceLevels[3].SelectedOption,
            useHttpSelection)
        && builder.CSharpTypes.Count > 0,
        "Selecting the blank Level 4 entry did not return to System.Net.Http.");

    builder.ClearBuilderCommand.Execute(null);
    builder.SearchQuery = "System Text StringBuilder new";
    var stringBuilderConstructor = builder.SearchResults.FirstOrDefault(
        result =>
            result.ApiEntry?.Type.FullDisplayName
                == "System.Text.StringBuilder"
            && result.ApiEntry.Member.Kind
                == DotNetApiMemberKind.Constructor);
    Require(
        stringBuilderConstructor is not null,
        "Search did not find the StringBuilder constructors.");
    builder.SelectedSearchResult = stringBuilderConstructor;
    builder.SelectedDotNetInvocationSyntax =
        builder.DotNetInvocationSyntaxOptions.Single(option =>
            option.ExecutionTarget == ExecutionTarget.PowerShell);
    builder.SelectedCSharpOverload = builder.CSharpOverloads.Single(option =>
        string.Equals(
            option.Signature,
            "StringBuilder()",
            StringComparison.Ordinal));
    Require(
        builder.SelectedCSharpType?.TypeKindLabel == "CLASS"
        && builder.SelectedCSharpMember?.KindLabel.Contains(
            "creates instance",
            StringComparison.OrdinalIgnoreCase) == true
        && builder.SelectedCommand?.ExecutionTarget
            == ExecutionTarget.PowerShell
        && builder.SelectedCommand.CompositionMode
            == CommandCompositionMode.Statement
        && builder.ParameterEditors.Single(editor =>
                editor.Definition.Name == "objectName")
            .Value == "stringBuilder",
        "The PowerShell constructor workflow did not expose object creation metadata.");
    builder.AddStatementCommand.Execute(null);

    builder.SelectedCSharpMember = builder.CSharpMembers.Single(option =>
        option.RuntimeMember?.Kind == DotNetApiMemberKind.Method
        && option.DisplayName == "Append");
    builder.SelectedCSharpOverload = builder.CSharpOverloads.First(option =>
        option.RuntimeOverload is not null
        && option.Signature.Contains(
            "Append(string value)",
            StringComparison.Ordinal));
    builder.ParameterEditors.Single(editor =>
            editor.Definition.Name == "value")
        .Value = "hello";
    Require(
        builder.ParameterEditors.Single(editor =>
                editor.Definition.Name == "instance")
            .Value == "$stringBuilder"
        && builder.SelectedCSharpMember.KindLabel.Contains(
            "instance",
            StringComparison.OrdinalIgnoreCase),
        "The instance method workflow did not target the constructed object.");
    builder.AddStatementCommand.Execute(null);

    builder.SelectedCSharpMember = builder.CSharpMembers.Single(option =>
        option.RuntimeMember?.Kind == DotNetApiMemberKind.Property
        && option.DisplayName == "Length");
    builder.SelectedCSharpOverload = builder.CSharpOverloads.Single(option =>
        option.RuntimeOverload?.Operation == DotNetApiOperation.Read);
    builder.AddStatementCommand.Execute(null);
    Require(
        builder.Pipeline.Count == 3
        && builder.Pipeline.All(segment =>
            segment.ExecutionTarget == ExecutionTarget.PowerShell
            && segment.CompositionMode
                == CommandCompositionMode.Statement)
        && builder.CommandText.Contains(
            "$stringBuilder = [System.Text.StringBuilder]::new()",
            StringComparison.Ordinal)
        && builder.CommandText.Contains(
            "$result = $stringBuilder.Append('hello')",
            StringComparison.Ordinal)
        && builder.CommandText.Contains(
            "$value = $stringBuilder.Length",
            StringComparison.Ordinal)
        && !builder.CommandText.Contains(" | ", StringComparison.Ordinal),
        $"The PowerShell object workflow was not composed as sequential statements: {builder.CommandText}");

    using (var powerShellSession = new PowerShellTerminalSession())
    {
        var powerShellResult = await powerShellSession.ExecuteAsync(
            $"{builder.CommandText}{Environment.NewLine}$value",
            verifierRoot);
        Require(
            powerShellResult.Errors.Count == 0
            && powerShellResult.Output.Any(line =>
                string.Equals(
                    line.Trim(),
                    "5",
                    StringComparison.Ordinal)),
            "The generated PowerShell constructor, method, and property workflow failed.");
    }

    builder.ClearBuilderCommand.Execute(null);
    builder.SelectedDotNetInvocationSyntax =
        builder.DotNetInvocationSyntaxOptions.Single(option =>
            option.ExecutionTarget == ExecutionTarget.CSharp);
    builder.SelectedCSharpMember = builder.CSharpMembers.Single(option =>
        option.RuntimeMember?.Kind == DotNetApiMemberKind.Constructor);
    builder.SelectedCSharpOverload = builder.CSharpOverloads.Single(option =>
        string.Equals(
            option.Signature,
            "StringBuilder()",
            StringComparison.Ordinal));
    builder.AddStatementCommand.Execute(null);
    builder.SelectedCSharpMember = builder.CSharpMembers.Single(option =>
        option.RuntimeMember?.Kind == DotNetApiMemberKind.Method
        && option.DisplayName == "Append");
    builder.SelectedCSharpOverload = builder.CSharpOverloads.First(option =>
        option.RuntimeOverload is not null
        && option.Signature.Contains(
            "Append(string value)",
            StringComparison.Ordinal));
    builder.ParameterEditors.Single(editor =>
            editor.Definition.Name == "value")
        .Value = "hello";
    builder.AddStatementCommand.Execute(null);
    builder.SelectedCSharpMember = builder.CSharpMembers.Single(option =>
        option.RuntimeMember?.Kind == DotNetApiMemberKind.Property
        && option.DisplayName == "Length");
    builder.SelectedCSharpOverload = builder.CSharpOverloads.Single(option =>
        option.RuntimeOverload?.Operation == DotNetApiOperation.Read);
    builder.AddStatementCommand.Execute(null);
    Require(
        builder.CommandText.Contains(
            "var stringBuilder = new System.Text.StringBuilder();",
            StringComparison.Ordinal)
        && builder.CommandText.Contains(
            "var result = stringBuilder.Append(\"hello\");",
            StringComparison.Ordinal)
        && builder.CommandText.Contains(
            "var value = stringBuilder.Length;",
            StringComparison.Ordinal),
        $"The C# object workflow was not generated correctly: {builder.CommandText}");
    var cSharpObjectResult = await session.ExecuteAsync(
        $"{builder.CommandText}{Environment.NewLine}Console.WriteLine(value);",
        verifierRoot);
    Require(
        cSharpObjectResult.Errors.Count == 0
        && cSharpObjectResult.Output.Any(line =>
            string.Equals(
                line.Trim(),
                "5",
                StringComparison.Ordinal)),
        "The generated C# constructor, method, and property workflow failed.");

    builder.ClearBuilderCommand.Execute(null);
    builder.SelectedDotNetInvocationSyntax =
        builder.DotNetInvocationSyntaxOptions.Single(option =>
            option.ExecutionTarget == ExecutionTarget.PowerShell);
    builder.SearchQuery = "System IO File Exists";
    var fileExists = builder.SearchResults.FirstOrDefault(result =>
        result.ApiEntry?.Type.FullDisplayName == "System.IO.File"
        && result.ApiEntry.Member.Name == "Exists");
    Require(fileExists is not null, "Search did not find File.Exists.");
    builder.SelectedSearchResult = fileExists;
    builder.SelectedCSharpOverload = builder.CSharpOverloads.First(option =>
        option.Signature.Contains(
            "Exists(string path)",
            StringComparison.Ordinal));
    builder.ParameterEditors.Single(editor =>
            editor.Definition.Name == "path")
        .Value = @"Z:\hypershell-verifier-missing-file";
    Require(
        builder.SelectedCSharpType?.IsStaticClass == true
        && builder.SelectedCSharpType.UsageHint.Contains(
            "no object is required",
            StringComparison.OrdinalIgnoreCase)
        && builder.SelectedCSharpMember?.KindLabel.Contains(
            "static",
            StringComparison.OrdinalIgnoreCase) == true
        && builder.ParameterEditors.All(editor =>
            editor.Definition.Name != "instance")
        && builder.StagedStatementText.Contains(
            "[System.IO.File]::Exists(",
            StringComparison.Ordinal),
        "The .NET browser did not identify and generate a static PowerShell call.");
    using (var powerShellSession = new PowerShellTerminalSession())
    {
        var staticPowerShellResult = await powerShellSession.ExecuteAsync(
            $"{builder.StagedStatementText}{Environment.NewLine}$result",
            verifierRoot);
        Require(
            staticPowerShellResult.Errors.Count == 0
            && staticPowerShellResult.Output.Any(line =>
                string.Equals(
                    line.Trim(),
                    "False",
                    StringComparison.OrdinalIgnoreCase)),
            "The generated static PowerShell call failed.");
    }

    builder.SelectedDotNetInvocationSyntax =
        builder.DotNetInvocationSyntaxOptions.Single(option =>
            option.ExecutionTarget == ExecutionTarget.CSharp);
    builder.ParameterEditors.Single(editor =>
            editor.Definition.Name == "path")
        .Value = @"Z:\hypershell-verifier-missing-file";
    Require(
        builder.SelectedCommand?.ExecutionTarget == ExecutionTarget.CSharp
        && builder.StagedStatementText.Contains(
            "System.IO.File.Exists(",
            StringComparison.Ordinal)
        && !builder.StagedStatementText.Contains(
            "[System.IO.File]::",
            StringComparison.Ordinal),
        "Switching invocation syntax did not regenerate File.Exists as C#.");
    var staticCSharpResult = await session.ExecuteAsync(
        $"{builder.StagedStatementText}{Environment.NewLine}" +
        "Console.WriteLine(result);",
        verifierRoot);
    Require(
        staticCSharpResult.Errors.Count == 0
        && staticCSharpResult.Output.Any(line =>
            string.Equals(
                line.Trim(),
                "False",
                StringComparison.OrdinalIgnoreCase)),
        "The generated static C# call failed.");
}

static async Task VerifyCSharpRunnerAsync(string verifierRoot)
{
    var dotnetExecutable = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "dotnet",
        "dotnet.exe");
    Require(File.Exists(dotnetExecutable), ".NET executable was not found.");

    var profile = new TerminalProfile
    {
        Id = "verifier-csharp",
        Name = "Verifier C#",
        ShellKind = TerminalShellKind.CSharp,
        ExecutablePath = dotnetExecutable
    };
    using var session = new CSharpTerminalSession(profile);
    var result = await session.ExecuteAsync(
        """
        string[] stages = ["catalog", "compile", "run"];
        var stageLabel = string.Join(" -> ", stages);

        await using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(
            compressed,
            CompressionLevel.Fastest,
            leaveOpen: true))
        {
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(stageLabel));
        }

        var digest = Convert.ToHexString(
            SHA256.HashData(compressed.ToArray()));
        var reflectedTypes = Assembly.GetExecutingAssembly().GetTypes().Length;

        var channel = Channel.CreateBounded<int>(2);
        await channel.Writer.WriteAsync(21);
        channel.Writer.Complete();
        var doubled = (await channel.Reader.ReadAsync()) * 2;

        var uri = new UriBuilder("https://example.com")
        {
            Path = "api/catalog",
            Query = $"hash={WebUtility.UrlEncode(digest[..8])}"
        }.Uri;
        var addressParsed = IPAddress.TryParse("127.0.0.1", out _);

        Console.WriteLine(
            $"VERIFY_OK|{doubled}|{reflectedTypes > 0}|{addressParsed}|" +
            $"{uri.Scheme}|net{Environment.Version.Major}");
        """,
        verifierRoot);

    Require(
        result.Errors.Count == 0,
        $"C# runner errors: {string.Join(Environment.NewLine, result.Errors)}");
    Require(
        result.Output.Any(line =>
            line.StartsWith(
                "VERIFY_OK|42|True|True|https|net10",
                StringComparison.Ordinal)),
        $"Unexpected C# runner output: {string.Join(Environment.NewLine, result.Output)}");
}

static async Task VerifyDotNetApiIndexAsync()
{
    var started = DateTime.UtcNow;
    var frameworks = new CSharpFrameworkCatalogService().Discover();
    var dotNet8Library = frameworks.Single(library =>
        library.Id == CSharpFrameworkCatalogService.DotNet8LibraryId);
    var dotNet10Library = frameworks.Single(library =>
        library.Id == CSharpFrameworkCatalogService.DotNet10LibraryId);
    var indexService = new DotNetApiIndexService();
    var indexes = await Task.WhenAll(
        indexService.LoadAsync(dotNet8Library),
        indexService.LoadAsync(dotNet10Library));
    var dotNet8Index = indexes.Single(index =>
        index.LibraryId == CSharpFrameworkCatalogService.DotNet8LibraryId);
    var dotNet10Index = indexes.Single(index =>
        index.LibraryId == CSharpFrameworkCatalogService.DotNet10LibraryId);

    var system = dotNet10Index.GetNamespaceChildren(null)
        .SingleOrDefault(option => option.Segment == "System");
    var collections = dotNet10Index.GetNamespaceChildren("System")
        .SingleOrDefault(option => option.Segment == "Collections");
    var generic = dotNet10Index.GetNamespaceChildren("System.Collections")
        .SingleOrDefault(option => option.Segment == "Generic");
    var list = dotNet10Index.FindType(
        "System.Collections.Generic",
        "List<T>");
    var add = list?.MemberGroups.SingleOrDefault(member =>
        member.Kind == DotNetApiMemberKind.Method
        && member.Name == "Add");

    Require(system is not null, "The System root namespace was not indexed.");
    Require(
        dotNet10Index.TargetFramework == "net10.0"
        && dotNet10Index.ReferenceDirectory.Contains(
            "Microsoft.NETCore.App.Ref",
            StringComparison.OrdinalIgnoreCase),
        "The .NET 10 API index did not use its matching reference pack.");
    Require(
        dotNet8Index.TargetFramework == "net8.0"
        && dotNet8Index.ReferenceDirectory.Contains(
            Path.Combine("ref", "net8.0"),
            StringComparison.OrdinalIgnoreCase)
        && dotNet10Index.ReferenceDirectory.Contains(
            Path.Combine("ref", "net10.0"),
            StringComparison.OrdinalIgnoreCase)
        && !string.Equals(
            dotNet8Index.RuntimeCatalogId,
            dotNet10Index.RuntimeCatalogId,
            StringComparison.OrdinalIgnoreCase)
        && !string.Equals(
            dotNet8Index.ReferenceDirectory,
            dotNet10Index.ReferenceDirectory,
            StringComparison.OrdinalIgnoreCase),
        "The .NET 8 and .NET 10 indexes were not isolated.");
    Require(collections is not null, "System.Collections was not indexed.");
    Require(generic is not null, "System.Collections.Generic was not indexed.");
    Require(list is not null, "System.Collections.Generic.List<T> was not indexed.");
    Require(add is not null, "List<T>.Add was not indexed.");

    var command = dotNet10Index.CreateCommand(
        list!,
        add!,
        add!.Overloads[0]);
    Require(
        command.NamespaceName == "System.Collections.Generic"
        && command.TypeName == "List<T>"
        && command.MemberName == "Add"
        && command.CatalogId == dotNet10Index.RuntimeCatalogId
        && command.Keywords.Contains("net10"),
        "The generated command lost its C# API coordinates.");
    Require(
        command.Template.Contains(".Add(", StringComparison.Ordinal)
        && command.Parameters.Count >= 3,
        "List<T>.Add did not produce a usable generic instance template.");

    var elapsed = DateTime.UtcNow - started;
    Console.WriteLine(
        $"API_INDEX_OK|net8:{dotNet8Index.NamespaceCount}:" +
        $"{dotNet8Index.Types.Count}:{dotNet8Index.OverloadCount}|" +
        $"net10:{dotNet10Index.NamespaceCount}:{dotNet10Index.Types.Count}:" +
        $"{dotNet10Index.OverloadCount}|" +
        $"{elapsed.TotalSeconds:F2}s");
}

static async Task VerifyOfficialFrameworkCatalogAsync()
{
    var started = DateTime.UtcNow;
    var libraries = new CSharpFrameworkCatalogService().Discover();
    Require(
        libraries.Select(library => library.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == libraries.Count,
        "The official C# framework catalog contains duplicate IDs.");

    var requiredInstalledSurfaces = new[]
    {
        CSharpFrameworkCatalogService.DotNet10LibraryId,
        CSharpFrameworkCatalogService.DotNet9LibraryId,
        CSharpFrameworkCatalogService.DotNet8LibraryId,
        CSharpFrameworkCatalogService.DotNetFrameworkLibraryId,
        CSharpFrameworkCatalogService.WinRtLibraryId,
        CSharpFrameworkCatalogService.PowerShellSdkLibraryId,
        CSharpFrameworkCatalogService.WindowsAppSdkLibraryId,
        CSharpFrameworkCatalogService.WinUi3LibraryId,
        CSharpFrameworkCatalogService.WpfLibraryId,
        CSharpFrameworkCatalogService.WindowsFormsLibraryId,
        CSharpFrameworkCatalogService.WmiCimLibraryId,
        CSharpFrameworkCatalogService.MicrosoftExtensionsLibraryId,
        CSharpFrameworkCatalogService.RoslynLibraryId,
        CSharpFrameworkCatalogService.MsBuildLibraryId,
        CSharpFrameworkCatalogService.NuGetClientLibraryId,
        CSharpFrameworkCatalogService.AspNetCoreLibraryId,
        CSharpFrameworkCatalogService.WebView2LibraryId,
        CSharpFrameworkCatalogService.SystemCommandLineLibraryId,
        CSharpFrameworkCatalogService.WcfLibraryId,
        CSharpFrameworkCatalogService.WorkflowFoundationLibraryId,
        CSharpFrameworkCatalogService.MefLibraryId,
        CSharpFrameworkCatalogService.TplDataflowLibraryId,
        CSharpFrameworkCatalogService.CommunityToolkitMvvmLibraryId
    };
    foreach (var libraryId in requiredInstalledSurfaces)
    {
        Require(
            libraries.Any(library => string.Equals(
                library.Id,
                libraryId,
                StringComparison.OrdinalIgnoreCase)),
            $"The installed official surface {libraryId} was not discovered.");
    }

    var indexedCount = 0;
    long publicTypeCount = 0;
    foreach (var library in libraries.Where(
                 CSharpFrameworkCatalogService.IsIndexedManagedApiLibrary))
    {
        Require(
            library.ReferenceAssemblyCount > 0
            && library.ApiAssemblyPaths.Count > 0
            && library.ApiAssemblyPaths.All(File.Exists),
            $"{library.DisplayName} does not point to real managed assemblies.");
        var index = await new DotNetApiIndexService().LoadAsync(library);
        Require(
            index.Types.Count > 0,
            $"{library.DisplayName} produced an empty class index.");
        indexedCount++;
        publicTypeCount += index.Types.Count;
        Console.WriteLine(
            $"FRAMEWORK_INDEX_OK|{library.DisplayName}|" +
            $"{index.AssemblyCount}|{index.NamespaceCount}|{index.Types.Count}");
    }

    Require(
        indexedCount == libraries.Count,
        "An official managed surface was discovered without an active class index.");
    Console.WriteLine(
        $"OFFICIAL_FRAMEWORKS_OK|{indexedCount}|{publicTypeCount}|" +
        $"{(DateTime.UtcNow - started).TotalSeconds:F2}s");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
