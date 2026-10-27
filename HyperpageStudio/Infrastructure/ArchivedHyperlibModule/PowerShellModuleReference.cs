// Archived from the active Hypershell application. This prototype is retained
// for the future standalone Hyperlib command-library application.
namespace Hypershell.Models;

public sealed record PowerShellModuleReference(
    string Name,
    string Version,
    string Path);
