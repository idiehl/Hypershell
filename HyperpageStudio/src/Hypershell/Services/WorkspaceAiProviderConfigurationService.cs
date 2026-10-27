using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public sealed record WorkspaceAiProviderConfiguration
{
    public const int CurrentSchemaVersion = 3;
    public const string OpenAiProviderId = "openai.responses";
    public const string LlamaCppProviderId = "llama.cpp.responses";
    public const string OpenAiResponsesEndpoint =
        "https://api.openai.com/v1/responses";
    public const string LlamaCppResponsesEndpoint =
        "http://127.0.0.1:11435/v1/responses";
    public const string DefaultModel = "gpt-5.6";
    public const string DefaultLocalModel =
        WorkspaceAiModelCatalog.EngineerModelId;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string ProviderId { get; init; } = OpenAiProviderId;
    public string Endpoint { get; init; } = OpenAiResponsesEndpoint;
    public string Model { get; init; } = DefaultModel;
    public string ReasoningEffort { get; init; } = "medium";
    public string TextVerbosity { get; init; } = "medium";
    public bool AllowCloudRequests { get; init; }
    public string OpenAiModel { get; init; } = DefaultModel;
    public string LocalModel { get; init; } = DefaultLocalModel;
    public string OpenAiReasoningEffort { get; init; } = "medium";
    public string LocalReasoningEffort { get; init; } = "medium";
    public string OpenAiTextVerbosity { get; init; } = "medium";
    public string LocalTextVerbosity { get; init; } = "medium";
    public bool OpenAiCloudRequestsEnabled { get; init; }
    public string SafetyIdentifier { get; init; } = CreateSafetyIdentifier();

    [JsonIgnore]
    public bool IsLocal => string.Equals(
        ProviderId,
        LlamaCppProviderId,
        StringComparison.Ordinal);

    [JsonIgnore]
    public bool RequiresCredential => !IsLocal;

    [JsonIgnore]
    public WorkspaceAiComputerControlMode ComputerControl { get; init; } =
        WorkspaceAiComputerControlMode.Off;

    [JsonIgnore]
    public string RuntimeEnvironmentContext { get; init; } = string.Empty;

    [JsonIgnore]
    public string TaskRouteName { get; init; } = string.Empty;

    /// <summary>
    /// Null preserves the complete host tool catalog for compatibility. A
    /// non-null list, including an empty list, is the task-time tool surface
    /// selected for the current prompt.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string>? EnabledToolNames { get; init; }

    // Native-owned profile; never persisted or accepted from browser messages.
    [JsonIgnore]
    public WorkspaceAiRequestProfile? RequestProfile { get; init; }

    public static string CreateSafetyIdentifier() =>
        "hs_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
}

/// <summary>
/// Chooses the provider and model for one AI conversation without rewriting
/// the device's credential and runtime-access configuration.
/// </summary>
public sealed record WorkspaceAiSessionModelSelection(
    string ProviderId,
    string ModelId)
{
    public static WorkspaceAiSessionModelSelection Default { get; } = new(
        WorkspaceAiProviderConfiguration.LlamaCppProviderId,
        WorkspaceAiModelCatalog.EngineerModelId);

    public bool IsLocal => string.Equals(
        ProviderId,
        WorkspaceAiProviderConfiguration.LlamaCppProviderId,
        StringComparison.Ordinal);

    public static WorkspaceAiSessionModelSelection FromConfiguration(
        WorkspaceAiProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new WorkspaceAiSessionModelSelection(
            configuration.ProviderId,
            configuration.Model);
    }

    public WorkspaceAiProviderConfiguration ApplyTo(
        WorkspaceAiProviderConfiguration deviceConfiguration)
    {
        ArgumentNullException.ThrowIfNull(deviceConfiguration);
        var providerId = (ProviderId ?? string.Empty).Trim();
        var modelId = (ModelId ?? string.Empty).Trim();
        var isLocal = string.Equals(
            providerId,
            WorkspaceAiProviderConfiguration.LlamaCppProviderId,
            StringComparison.Ordinal);
        if (!isLocal
            && !string.Equals(
                providerId,
                WorkspaceAiProviderConfiguration.OpenAiProviderId,
                StringComparison.Ordinal))
        {
            throw new WorkspaceAiProviderConfigurationException(
                "The conversation's AI provider is not supported by this Hypershell build.");
        }

        var reasoningEffort = isLocal
            ? deviceConfiguration.IsLocal
                ? deviceConfiguration.ReasoningEffort
                : deviceConfiguration.LocalReasoningEffort
            : deviceConfiguration.IsLocal
                ? deviceConfiguration.OpenAiReasoningEffort
                : deviceConfiguration.ReasoningEffort;
        var textVerbosity = isLocal
            ? deviceConfiguration.IsLocal
                ? deviceConfiguration.TextVerbosity
                : deviceConfiguration.LocalTextVerbosity
            : deviceConfiguration.IsLocal
                ? deviceConfiguration.OpenAiTextVerbosity
                : deviceConfiguration.TextVerbosity;

        return WorkspaceAiProviderConfigurationService.Normalize(
            deviceConfiguration with
            {
                ProviderId = providerId,
                Endpoint = isLocal
                    ? WorkspaceAiProviderConfiguration
                        .LlamaCppResponsesEndpoint
                    : WorkspaceAiProviderConfiguration
                        .OpenAiResponsesEndpoint,
                Model = modelId,
                ReasoningEffort = reasoningEffort,
                TextVerbosity = textVerbosity,
                AllowCloudRequests = !isLocal
                                     && deviceConfiguration
                                         .OpenAiCloudRequestsEnabled,
                OpenAiModel = isLocal
                    ? deviceConfiguration.OpenAiModel
                    : modelId,
                LocalModel = isLocal
                    ? modelId
                    : deviceConfiguration.LocalModel,
                OpenAiCloudRequestsEnabled = deviceConfiguration
                    .OpenAiCloudRequestsEnabled
            });
    }
}

public sealed class WorkspaceAiProviderConfigurationException
    : Exception
{
    public WorkspaceAiProviderConfigurationException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class WorkspaceAiProviderConfigurationService
{
    private const long MaximumConfigurationBytes = 64 * 1024;

    private static readonly HashSet<string> ReasoningEfforts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "none",
            "low",
            "medium",
            "high",
            "xhigh",
            "max"
        };

    private static readonly HashSet<string> TextVerbosities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "low",
            "medium",
            "high"
        };

    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public WorkspaceAiProviderConfigurationService(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ConfigurationPath = Path.GetFullPath(configurationPath);
    }

    public string ConfigurationPath { get; }

    public string LastLoadWarning { get; private set; } = string.Empty;

    public WorkspaceAiProviderConfiguration Load()
    {
        lock (_gate)
        {
            LastLoadWarning = string.Empty;
            try
            {
                if (!File.Exists(ConfigurationPath))
                {
                    return new WorkspaceAiProviderConfiguration();
                }

                var info = new FileInfo(ConfigurationPath);
                if (info.Length is <= 0 or > MaximumConfigurationBytes)
                {
                    throw new InvalidDataException(
                        "The AI provider configuration has an invalid size.");
                }

                var configuration =
                    JsonSerializer.Deserialize<WorkspaceAiProviderConfiguration>(
                        File.ReadAllText(ConfigurationPath),
                        _jsonOptions)
                    ?? throw new JsonException(
                        "The AI provider configuration was empty.");
                return Normalize(configuration);
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or InvalidDataException
                                             or JsonException
                                             or ArgumentException
                                             or NotSupportedException
                                             or WorkspaceAiProviderConfigurationException)
            {
                LastLoadWarning =
                    $"The AI provider configuration was disabled: {exception.Message}";
                return new WorkspaceAiProviderConfiguration();
            }
        }
    }

    public WorkspaceAiProviderConfiguration Save(
        WorkspaceAiProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var normalized = Normalize(configuration);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            normalized,
            _jsonOptions);
        if (payload.LongLength > MaximumConfigurationBytes)
        {
            throw Failure(
                "The AI provider configuration exceeds its storage limit.");
        }

        try
        {
            lock (_gate)
            {
                WriteAtomic(payload);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
        return normalized;
    }

    public static WorkspaceAiProviderConfiguration Normalize(
        WorkspaceAiProviderConfiguration configuration)
    {
        if (configuration.SchemaVersion is < 1
            or > WorkspaceAiProviderConfiguration.CurrentSchemaVersion)
        {
            throw Failure(
                $"AI provider schema {configuration.SchemaVersion} is unsupported.");
        }

        var providerId = (configuration.ProviderId ?? string.Empty).Trim();
        if (providerId is not WorkspaceAiProviderConfiguration.OpenAiProviderId
            and not WorkspaceAiProviderConfiguration.LlamaCppProviderId)
        {
            throw Failure(
                "The AI provider is not supported by this Hypershell build.");
        }

        var isLocal = providerId
                      == WorkspaceAiProviderConfiguration.LlamaCppProviderId;
        var endpoint = isLocal
            ? NormalizeLlamaCppEndpoint(configuration.Endpoint)
            : NormalizeOpenAiEndpoint(configuration.Endpoint);
        var model = NormalizeModelIdentifier(configuration.Model, isLocal);
        var openAiModel = isLocal
            ? NormalizeModelIdentifier(
                configuration.OpenAiModel,
                isLocal: false)
            : model;
        var localModel = isLocal
            ? model
            : NormalizeModelIdentifier(
                configuration.LocalModel,
                isLocal: true);

        var reasoningEffort = NormalizeReasoningEffort(
            configuration.ReasoningEffort);
        var openAiReasoningEffort = isLocal
            ? NormalizeReasoningEffort(
                configuration.OpenAiReasoningEffort)
            : reasoningEffort;
        var localReasoningEffort = isLocal
            ? reasoningEffort
            : NormalizeReasoningEffort(
                configuration.LocalReasoningEffort);

        var textVerbosity = NormalizeTextVerbosity(
            configuration.TextVerbosity);
        var openAiTextVerbosity = isLocal
            ? NormalizeTextVerbosity(
                configuration.OpenAiTextVerbosity)
            : textVerbosity;
        var localTextVerbosity = isLocal
            ? textVerbosity
            : NormalizeTextVerbosity(
                configuration.LocalTextVerbosity);
        var openAiCloudRequestsEnabled = isLocal
            ? configuration.OpenAiCloudRequestsEnabled
            : configuration.AllowCloudRequests;

        if (!Enum.IsDefined(configuration.ComputerControl))
        {
            throw Failure(
                "The AI provider tool policy is not supported.");
        }

        var safetyIdentifier =
            (configuration.SafetyIdentifier ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (safetyIdentifier.Length != 35
            || !safetyIdentifier.StartsWith("hs_", StringComparison.Ordinal)
            || safetyIdentifier[3..].Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw Failure(
                "The privacy-preserving safety identifier is malformed.");
        }

        return configuration with
        {
            SchemaVersion = WorkspaceAiProviderConfiguration.CurrentSchemaVersion,
            ProviderId = providerId,
            Endpoint = endpoint,
            Model = model,
            ReasoningEffort = reasoningEffort,
            TextVerbosity = textVerbosity,
            AllowCloudRequests = !isLocal
                                 && openAiCloudRequestsEnabled,
            OpenAiModel = openAiModel,
            LocalModel = localModel,
            OpenAiReasoningEffort = openAiReasoningEffort,
            LocalReasoningEffort = localReasoningEffort,
            OpenAiTextVerbosity = openAiTextVerbosity,
            LocalTextVerbosity = localTextVerbosity,
            OpenAiCloudRequestsEnabled = openAiCloudRequestsEnabled,
            SafetyIdentifier = safetyIdentifier
        };
    }

    private static string NormalizeModelIdentifier(
        string? value,
        bool isLocal)
    {
        var model = (value ?? string.Empty).Trim();
        if (model.Length is < 1 or > 100
            || model.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '.' and not '_' and not ':' and not '-'))
        {
            throw Failure(
                "The AI model identifier is malformed.");
        }

        if (isLocal)
        {
            return WorkspaceAiModelCatalog.RequireGenerative(model).Id;
        }

        return model;
    }

    private static string NormalizeReasoningEffort(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (!ReasoningEfforts.Contains(normalized))
        {
            throw Failure(
                "The AI reasoning effort is not supported.");
        }

        return normalized;
    }

    private static string NormalizeTextVerbosity(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (!TextVerbosities.Contains(normalized))
        {
            throw Failure(
                "The AI response verbosity is not supported.");
        }

        return normalized;
    }

    public static string NormalizeOpenAiEndpoint(string? value)
    {
        if (!Uri.TryCreate(
                (value ?? string.Empty).Trim(),
                UriKind.Absolute,
                out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                endpoint.Host,
                "api.openai.com",
                StringComparison.OrdinalIgnoreCase)
            || !endpoint.IsDefaultPort
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !string.Equals(
                endpoint.AbsolutePath.TrimEnd('/'),
                "/v1/responses",
                StringComparison.Ordinal))
        {
            throw Failure(
                "The OpenAI provider endpoint must be exactly https://api.openai.com/v1/responses.");
        }

        return WorkspaceAiProviderConfiguration.OpenAiResponsesEndpoint;
    }

    public static string NormalizeLlamaCppEndpoint(string? value)
    {
        if (!Uri.TryCreate(
                (value ?? string.Empty).Trim(),
                UriKind.Absolute,
                out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttp
            || !string.Equals(
                endpoint.Host,
                "127.0.0.1",
                StringComparison.Ordinal)
            || endpoint.Port != 11435
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || !string.Equals(
                endpoint.AbsolutePath.TrimEnd('/'),
                "/v1/responses",
                StringComparison.Ordinal))
        {
            throw Failure(
                "The managed llama.cpp endpoint must be exactly http://127.0.0.1:11435/v1/responses.");
        }

        return WorkspaceAiProviderConfiguration.LlamaCppResponsesEndpoint;
    }

    private void WriteAtomic(ReadOnlySpan<byte> payload)
    {
        var directory = Path.GetDirectoryName(ConfigurationPath)
                        ?? throw Failure(
                            "The AI provider configuration path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath =
            $"{ConfigurationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                ConfigurationPath,
                overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException)
        {
            throw Failure(
                "The AI provider configuration could not be saved.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static WorkspaceAiProviderConfigurationException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}

public sealed record WorkspaceAiStoredCredential(string ApiKey);

public interface IWorkspaceAiCredentialStore
{
    bool Exists { get; }

    string CredentialPath { get; }

    WorkspaceAiStoredCredential? Load();

    void Save(string apiKey);

    void Delete();
}

public sealed class DpapiWorkspaceAiCredentialStore
    : IWorkspaceAiCredentialStore
{
    private const int CurrentSchemaVersion = 1;
    private const long MaximumProtectedBytes = 64 * 1024;

    private static readonly byte[] AdditionalEntropy =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "Hypershell.WorkspaceAi.OpenAiCredential.v1"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly object _gate = new();

    public DpapiWorkspaceAiCredentialStore(string credentialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialPath);
        CredentialPath = Path.GetFullPath(credentialPath);
    }

    public string CredentialPath { get; }

    public bool Exists
    {
        get
        {
            lock (_gate)
            {
                return File.Exists(CredentialPath);
            }
        }
    }

    public WorkspaceAiStoredCredential? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(CredentialPath))
            {
                return null;
            }

            byte[] protectedPayload;
            try
            {
                var info = new FileInfo(CredentialPath);
                if (info.Length is <= 0 or > MaximumProtectedBytes)
                {
                    throw Failure(
                        "The saved AI provider credential has an invalid size.");
                }

                protectedPayload = File.ReadAllBytes(CredentialPath);
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
                throw Failure(
                    "The saved AI provider credential could not be read.",
                    exception);
            }

            byte[]? plaintext = null;
            try
            {
                plaintext = ProtectedData.Unprotect(
                    protectedPayload,
                    AdditionalEntropy,
                    DataProtectionScope.CurrentUser);
                var envelope =
                    JsonSerializer.Deserialize<CredentialEnvelope>(
                        plaintext,
                        JsonOptions)
                    ?? throw new JsonException(
                        "The saved AI provider credential was empty.");
                ValidateEnvelope(envelope);
                return new WorkspaceAiStoredCredential(envelope.ApiKey);
            }
            catch (Exception exception) when (exception is CryptographicException
                                             or JsonException
                                             or ArgumentException)
            {
                throw Failure(
                    "The saved AI provider credential could not be decrypted or validated for this Windows user.",
                    exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedPayload);
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }
    }

    public void Save(string apiKey)
    {
        ValidateApiKey(apiKey);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            new CredentialEnvelope { ApiKey = apiKey },
            JsonOptions);
        byte[]? protectedPayload = null;
        try
        {
            protectedPayload = ProtectedData.Protect(
                plaintext,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);
            if (protectedPayload.LongLength > MaximumProtectedBytes)
            {
                throw Failure(
                    "The protected AI provider credential exceeds its storage limit.");
            }

            lock (_gate)
            {
                WriteAtomic(protectedPayload);
            }
        }
        catch (Exception exception) when (exception is CryptographicException
                                         or IOException
                                         or UnauthorizedAccessException)
        {
            throw Failure(
                "Windows could not protect or save the OpenAI API key.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (protectedPayload is not null)
            {
                CryptographicOperations.ZeroMemory(protectedPayload);
            }
        }
    }

    public void Delete()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(CredentialPath))
                {
                    File.Delete(CredentialPath);
                }
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
                throw Failure(
                    "The saved AI provider credential could not be removed.",
                    exception);
            }
        }
    }

    private void WriteAtomic(ReadOnlySpan<byte> payload)
    {
        var directory = Path.GetDirectoryName(CredentialPath)
                        ?? throw Failure(
                            "The AI provider credential path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath =
            $"{CredentialPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporaryPath,
                CredentialPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateEnvelope(CredentialEnvelope envelope)
    {
        if (envelope.SchemaVersion != CurrentSchemaVersion
            || !string.Equals(
                envelope.ProviderId,
                WorkspaceAiProviderConfiguration.OpenAiProviderId,
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "The saved AI provider credential identity is malformed.");
        }

        ValidateApiKey(envelope.ApiKey);
    }

    private static void ValidateApiKey(string? apiKey)
    {
        if (apiKey is null
            || apiKey.Length is < 8 or > 512
            || apiKey.Any(char.IsWhiteSpace)
            || apiKey.Any(char.IsControl))
        {
            throw Failure(
                "The OpenAI API key is malformed.");
        }
    }

    private static WorkspaceAiProviderConfigurationException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);

    private sealed class CredentialEnvelope
    {
        [JsonRequired]
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        [JsonRequired]
        public string ProviderId { get; init; } =
            WorkspaceAiProviderConfiguration.OpenAiProviderId;

        [JsonRequired]
        public string ApiKey { get; init; } = string.Empty;
    }
}
