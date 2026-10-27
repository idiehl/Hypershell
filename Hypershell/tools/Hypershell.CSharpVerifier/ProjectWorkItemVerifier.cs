using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Collections.ObjectModel;

public static class ProjectWorkItemVerifier
{
    public static void Verify(string verifierRoot)
    {
        var projectRoot = Path.Combine(verifierRoot, "project");
        var incomingRoot = Path.Combine(verifierRoot, "incoming");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(incomingRoot);
        var attachmentSource = Path.Combine(incomingRoot, "evidence.txt");
        File.WriteAllText(attachmentSource, "attachment evidence");

        var scope = new ProjectWorkItemScope(
            "hypershell-verifier",
            "Hypershell verifier",
            projectRoot,
            IsSavedProject: true);
        var service = new ProjectWorkItemService();
        var document = new ProjectWorkItemDocument
        {
            ProjectId = scope.Id,
            ProjectName = scope.Name,
            Items =
            [
                new ProjectWorkItem
                {
                    Id = "later-item",
                    Title = "Later item",
                    Description = "This item should sort second.",
                    Order = 2
                },
                new ProjectWorkItem
                {
                    Id = "starred-item",
                    Title = "Aligned fix",
                    Description = "Use this item when it fits the current cycle.",
                    IsStarred = true,
                    Order = 0,
                    Attachments =
                    [
                        new ProjectWorkItemAttachment
                        {
                            Id = "evidence",
                            FileName = "evidence.txt",
                            SourcePath = attachmentSource
                        }
                    ]
                }
            ]
        };

        service.Save(scope, document);
        Require(File.Exists(service.GetStorePath(scope)),
            "The work-item JSON file was not created.");
        Require(string.IsNullOrWhiteSpace(
                    document.Items[0].Attachments[0].SourcePath),
            "The persisted attachment retained a device-local source path.");
        var importedAttachment = service.ResolveAttachmentPath(
            scope,
            document.Items[0].Attachments[0]);
        Require(File.Exists(importedAttachment),
            "The attachment was not imported beneath project metadata.");
        Require(File.ReadAllText(importedAttachment) == "attachment evidence",
            "The imported attachment content changed.");

        var loaded = service.Load(scope);
        Require(loaded.Items.Count == 2,
            "The saved work-item count changed during reload.");
        Require(loaded.Items[0].Id == "starred-item"
                && loaded.Items[0].Order == 0
                && loaded.Items[1].Order == 1,
            "Work-item ordering was not normalized deterministically.");

        var viewModel = new ProjectWorkItemsViewModel(service);
        viewModel.UpdateProjectScopes(
            [
                new ProjectDefinition
                {
                    Id = scope.Id,
                    Name = scope.Name,
                    Directories = new ObservableCollection<string>([projectRoot])
                }
            ],
            scope.Id,
            projectRoot);
        Require(viewModel.Items.Count == 2,
            "The project tracker did not load its persisted items.");
        var aiContext = viewModel.CreateAiWorkCycleContext();
        Require(aiContext is not null
                && aiContext.Contains("[STARRED] Aligned fix", StringComparison.Ordinal)
                && aiContext.Contains(importedAttachment, StringComparison.OrdinalIgnoreCase),
            "The AI work-cycle context omitted the starred item or attachment.");

        viewModel.MoveItem(viewModel.Items[1], viewModel.Items[0]);
        var reordered = service.Load(scope);
        Require(reordered.Items[0].Id == "later-item",
            "Drag-style priority changes were not persisted.");

        Console.WriteLine(
            $"PROJECT_WORK_ITEMS_OK|{reordered.Items.Count}|{Path.GetFileName(importedAttachment)}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
