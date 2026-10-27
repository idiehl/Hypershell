using System.Diagnostics;
using System.Text;
using Hypershell.Services;

internal static class AtlasArchivePublicationVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var atlasRoot = Path.Combine(verifierRoot, "atlas-checkout");
        var originRoot = Path.Combine(verifierRoot, "atlas-origin.git");
        await CreateAtlasFixtureAsync(atlasRoot, originRoot);

        var catalog = new VisualPageComponentCatalog();
        var validator = new VisualPageSchemaValidator(catalog);
        var generator = new VisualPageGenerator(catalog, validator);
        var projectService = new VisualPageProjectService(validator, generator, catalog);
        var visualRoot = Path.Combine(verifierRoot, "visual-project");
        var session = await projectService.CreateAsync(
            visualRoot,
            "Publication Verifier",
            "Publication.Verifier");
        var service = new AtlasArchivePublicationService(
            new BoundedProcessRunner(),
            projectService,
            generator,
            validator,
            Path.Combine(verifierRoot, "publication-worktrees"));

        var fastLane = await service.PrepareAsync(
            session,
            atlasRoot,
            "fast-lane-page",
            "main");
        Require(!fastLane.RequiresReviewedCode
                && fastLane.Manifest.Route == "/Archive/fast-lane-page"
                && fastLane.Manifest.AssemblyName == "Publication.Verifier"
                && fastLane.Manifest.Artifacts.Count >= 7
                && fastLane.ChangedPaths.All(path =>
                    path.StartsWith("src/Atlas.Archive/Projects/vp-", StringComparison.Ordinal)
                    || path == "src/Atlas.Web/Archive/manifests/fast-lane-page.json"),
            "Fast-lane preparation did not remain within the Archive source boundary.");
        var fastReceipt = await service.PublishExactCommitAsync(
            session,
            fastLane,
            approvedReviewedCode: false);
        Require(await GitAsync(atlasRoot, "ls-remote", "origin", "refs/heads/main")
                    is var remoteAfterFast
                && remoteAfterFast.StartsWith(fastReceipt.SourceCommit, StringComparison.Ordinal),
            "The fast-lane push did not publish the exact reviewed commit.");

        await GitAsync(atlasRoot, "pull", "--ff-only", "origin", "main");
        var userSources = new VisualPageUserSourceService();
        var customCssPath = $"src/Pages/wwwroot/{session.Page.ComponentName}.custom.css";
        var css = await userSources.LoadAsync(visualRoot, customCssPath);
        await userSources.SaveAsync(
            visualRoot,
            css,
            css.Content + Environment.NewLine + ".reviewed-code { color: rebeccapurple; }");

        var reviewed = await service.PrepareAsync(
            session,
            atlasRoot,
            "reviewed-page",
            "main");
        Require(reviewed.RequiresReviewedCode
                && reviewed.Manifest.Classifications.Contains(
                    Hypershell.Models.VisualPageCodeClassification.CustomCss),
            "Custom CSS was not routed to the reviewed-code lane.");
        await RequireThrowsAsync<AtlasArchivePublicationException>(
            () => service.PublishExactCommitAsync(
                session,
                reviewed,
                approvedReviewedCode: false),
            "Reviewed code was publishable without explicit approval.");
        var reviewedReceipt = await service.PublishExactCommitAsync(
            session,
            reviewed,
            approvedReviewedCode: true);
        var remoteAfterReview = await GitAsync(
            atlasRoot,
            "ls-remote",
            "origin",
            "refs/heads/main");
        Require(remoteAfterReview.StartsWith(reviewedReceipt.SourceCommit, StringComparison.Ordinal)
                && Directory.EnumerateFiles(
                        Path.Combine(visualRoot, ".visual-page", "publish-receipts"),
                        "*.json")
                    .Count() == 2,
            "Reviewed publication did not preserve its exact commit and local receipt.");

        Console.WriteLine(
            "ATLAS_ARCHIVE_PUBLICATION_OK|clean-worktree|portable-rcl|assets-hashes|a11y-gate|format|build|tests|fast-lane|reviewed-code-approval|exact-commit-push|receipt|no-activation");
    }

    private static async Task CreateAtlasFixtureAsync(
        string atlasRoot,
        string originRoot)
    {
        Directory.CreateDirectory(atlasRoot);
        await WriteAsync(Path.Combine(atlasRoot, ".gitignore"), """
            **/bin/
            **/obj/
            **/.runtime/
            """);
        await WriteAsync(
            Path.Combine(atlasRoot, "src", "Atlas.Archive", "Atlas.Archive.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <DefaultItemExcludes>$(DefaultItemExcludes);Projects\**\*</DefaultItemExcludes>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <ProjectReference Include="Projects\*\*.csproj" AdditionalProperties="Configuration=$(Configuration)" />
              </ItemGroup>
            </Project>
            """);
        await WriteAsync(
            Path.Combine(atlasRoot, "src", "Atlas.Archive", "ArchiveMarker.cs"),
            "namespace Atlas.Archive; public sealed class ArchiveMarker;\n");
        await WriteAsync(
            Path.Combine(atlasRoot, "src", "Atlas.Web", "Atlas.Web.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Atlas.Archive\Atlas.Archive.csproj" />
                <Content Update="Archive\manifests\**\*.json">
                  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
                </Content>
              </ItemGroup>
            </Project>
            """);
        await WriteAsync(
            Path.Combine(atlasRoot, "src", "Atlas.Web", "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));
            app.Run();

            public partial class Program;
            """);
        await WriteAsync(
            Path.Combine(atlasRoot, "tests", "Atlas.Web.Tests", "Atlas.Web.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
                <ProjectReference Include="..\..\src\Atlas.Web\Atlas.Web.csproj" />
              </ItemGroup>
            </Project>
            """);
        await WriteAsync(
            Path.Combine(atlasRoot, "tests", "Atlas.Web.Tests", "SmokeTests.cs"),
            """
            namespace Atlas.Web.Tests;

            public sealed class SmokeTests
            {
                [Xunit.Fact]
                public void SourceBuilds() => Xunit.Assert.True(true);
            }
            """);

        await GitAsync(atlasRoot, "init", "-b", "main");
        await GitAsync(atlasRoot, "config", "user.name", "Hypershell Verifier");
        await GitAsync(atlasRoot, "config", "user.email", "verifier@hypershell.local");
        await GitAsync(atlasRoot, "add", "-A");
        await GitAsync(atlasRoot, "commit", "-m", "Atlas fixture");
        await GitAsync(Path.GetDirectoryName(originRoot)!, "init", "--bare", originRoot);
        await GitAsync(atlasRoot, "remote", "add", "origin", originRoot);
        await GitAsync(atlasRoot, "push", "-u", "origin", "main");
    }

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            content.Replace("\r\n", "\n", StringComparison.Ordinal),
            new UTF8Encoding(false));
    }

    private static async Task<string> GitAsync(
        string verifierRoot,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git.exe",
            WorkingDirectory = verifierRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git did not start.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git fixture command failed: {error}{Environment.NewLine}{output}");
        }
        return output;
    }

    private static async Task RequireThrowsAsync<TException>(
        Func<Task> action,
        string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
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
