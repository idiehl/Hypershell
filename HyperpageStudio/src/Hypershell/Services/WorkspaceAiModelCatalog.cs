namespace Hypershell.Services;

public enum WorkspaceAiModelRole
{
    Engineer,
    Librarian
}

public sealed record WorkspaceAiModelArtifactManifest(
    string FileName,
    long FileSizeBytes,
    string Sha256);

public sealed record WorkspaceAiModelManifest(
    string Id,
    string DisplayName,
    WorkspaceAiModelRole Role,
    string ProviderId,
    string HuggingFaceRepository,
    string Revision,
    string FileName,
    string Quantization,
    long FileSizeBytes,
    string Sha256,
    string License,
    int ContextSize,
    bool SupportsConversation,
    bool SupportsTools,
    bool SupportsEmbeddings,
    WorkspaceAiModelArtifactManifest? VisionProjector = null)
{
    public string HuggingFaceReference =>
        $"{HuggingFaceRepository}:{Quantization}";

    public bool SupportsVision => VisionProjector is not null;
}

public static class WorkspaceAiModelCatalog
{
    private const string RetiredOperatorModelId =
        "granite-4.1-3b-operator";

    public const string EngineerModelId = "qwen3.5-9b-engineer";
    public const string LibrarianModelId = "qwen3-embedding-0.6b-librarian";

    private static readonly IReadOnlyList<WorkspaceAiModelManifest> Models =
    [
        new(
            EngineerModelId,
            "Qwen 3.5 9B · Engineer · Vision + Desktop",
            WorkspaceAiModelRole.Engineer,
            WorkspaceAiProviderConfiguration.LlamaCppProviderId,
            "unsloth/Qwen3.5-9B-GGUF",
            "3885219b6810b007914f3a7950a8d1b469d598a5",
            "Qwen3.5-9B-Q4_K_M.gguf",
            "Q4_K_M",
            5_680_522_464,
            "03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8",
            "Apache-2.0",
            16_384,
            SupportsConversation: true,
            SupportsTools: true,
            SupportsEmbeddings: false,
            VisionProjector: new WorkspaceAiModelArtifactManifest(
                "mmproj-F16.gguf",
                918_166_080,
                "f70dc3509053962b0d0d3ee8a7eacebf5d60aa560cad78254ae8698516ae029f")),
        new(
            LibrarianModelId,
            "Qwen 3 Embedding 0.6B · Librarian",
            WorkspaceAiModelRole.Librarian,
            WorkspaceAiProviderConfiguration.LlamaCppProviderId,
            "Qwen/Qwen3-Embedding-0.6B-GGUF",
            "370f27d7550e0def9b39c1f16d3fbaa13aa67728",
            "Qwen3-Embedding-0.6B-Q8_0.gguf",
            "Q8_0",
            639_150_592,
            "06507c7b42688469c4e7298b0a1e16deff06caf291cf0a5b278c308249c3e439",
            "Apache-2.0",
            32_768,
            SupportsConversation: false,
            SupportsTools: false,
            SupportsEmbeddings: true)
    ];

    public static IReadOnlyList<WorkspaceAiModelManifest> All => Models;

    public static IReadOnlyList<WorkspaceAiModelManifest> Generative =>
        Models.Where(model => model.SupportsConversation).ToArray();

    public static WorkspaceAiModelManifest? Find(string? id) =>
        Models.FirstOrDefault(model => string.Equals(
            model.Id,
            id,
            StringComparison.Ordinal));

    public static WorkspaceAiModelManifest RequireGenerative(string? id)
    {
        var model = Find(NormalizeGenerativeId(id));
        if (model is not { SupportsConversation: true })
        {
            throw new WorkspaceAiProviderConfigurationException(
                "Choose a trusted conversational model from Hypershell's local model catalog.");
        }

        return model;
    }

    public static string NormalizeGenerativeId(string? id)
    {
        var normalized = (id ?? string.Empty).Trim();
        return string.Equals(
            normalized,
            RetiredOperatorModelId,
            StringComparison.Ordinal)
            ? EngineerModelId
            : normalized;
    }

    public static WorkspaceAiModelManifest RequireEmbedding(string? id)
    {
        var model = Find(id);
        if (model is not { SupportsEmbeddings: true })
        {
            throw new WorkspaceAiProviderConfigurationException(
                "Choose a trusted embedding model from Hypershell's local model catalog.");
        }

        return model;
    }

    public static string ResolveContextModel(
        WorkspaceAiProviderConfiguration configuration,
        string? requestedModel,
        WorkspaceAiComputerControlMode computerControl =
            WorkspaceAiComputerControlMode.Off)
    {
        var normalizedRequestedModel = NormalizeGenerativeId(requestedModel);
        var requested = Find(normalizedRequestedModel);
        if (configuration.IsLocal)
        {
            var resolved = requested is { SupportsConversation: true }
                ? requested.Id
                : NormalizeGenerativeId(configuration.Model);
            if (computerControl != WorkspaceAiComputerControlMode.Off
                && Find(resolved) is not { SupportsVision: true })
            {
                return EngineerModelId;
            }

            return resolved;
        }

        var normalized = (requestedModel ?? string.Empty).Trim();
        return requested is not null || string.IsNullOrWhiteSpace(normalized)
            ? configuration.Model
            : normalized;
    }
}
