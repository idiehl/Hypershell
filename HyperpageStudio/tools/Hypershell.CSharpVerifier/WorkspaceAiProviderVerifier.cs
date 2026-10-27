using Hypershell.Models;
using Hypershell.Profiles;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class WorkspaceAiProviderVerifier
{
    private const string WorkspaceId = "project.verifier:provider";
    private const string RetiredOperatorModelId =
        "granite-4.1-3b-operator";

    public static async Task VerifyAsync(string verifierRoot)
    {
        VerifyCrashSafeProcessBoundary();

        var root = Path.Combine(verifierRoot, "workspace-ai-provider");
        var workspaceRoot = Path.Combine(root, "workspace");
        var appDataRoot = Path.Combine(root, "app-data");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(appDataRoot);

        var configurationPath = Path.Combine(
            appDataRoot,
            "ai-provider.device.json");
        var credentialPath = Path.Combine(
            appDataRoot,
            "ai-provider-credential.device.bin");
        var configurationService =
            new WorkspaceAiProviderConfigurationService(configurationPath);
        var defaultConfiguration = configurationService.Load();
        Require(
            !defaultConfiguration.AllowCloudRequests
            && defaultConfiguration.Model
                == WorkspaceAiProviderConfiguration.DefaultModel
            && defaultConfiguration.Endpoint
                == WorkspaceAiProviderConfiguration.OpenAiResponsesEndpoint
            && defaultConfiguration.SafetyIdentifier.Length == 35,
            "The AI provider did not default to a disabled, fixed-endpoint configuration.");

        var legacyConfigurationPath = Path.Combine(
            appDataRoot,
            "ai-provider-schema-2.device.json");
        await File.WriteAllTextAsync(
            legacyConfigurationPath,
            """
            {
              "schemaVersion": 2,
              "providerId": "llama.cpp.responses",
              "endpoint": "http://127.0.0.1:11435/v1/responses",
              "model": "qwen3.5-9b-engineer",
              "reasoningEffort": "medium",
              "textVerbosity": "medium",
              "allowCloudRequests": false,
              "safetyIdentifier": "hs_0123456789abcdef0123456789abcdef"
            }
            """,
            new UTF8Encoding(false));
        var legacyService =
            new WorkspaceAiProviderConfigurationService(
                legacyConfigurationPath);
        var migratedLegacy = legacyService.Load();
        Require(
            migratedLegacy.SchemaVersion
                == WorkspaceAiProviderConfiguration.CurrentSchemaVersion
            && migratedLegacy.IsLocal
            && migratedLegacy.LocalModel
                == WorkspaceAiModelCatalog.EngineerModelId
            && migratedLegacy.OpenAiModel
                == WorkspaceAiProviderConfiguration.DefaultModel
            && !migratedLegacy.OpenAiCloudRequestsEnabled,
            "The schema-2 local provider configuration did not migrate safely to independent provider preferences.");
        var upgradedLegacy = legacyService.Save(migratedLegacy);
        using (var upgradedDocument = JsonDocument.Parse(
                   await File.ReadAllTextAsync(legacyConfigurationPath)))
        {
            Require(
                upgradedLegacy.SchemaVersion == 3
                && upgradedDocument.RootElement
                    .GetProperty("schemaVersion").GetInt32() == 3
                && upgradedDocument.RootElement
                    .GetProperty("openAiModel").GetString()
                    == WorkspaceAiProviderConfiguration.DefaultModel
                && upgradedDocument.RootElement
                    .GetProperty("localModel").GetString()
                    == WorkspaceAiModelCatalog.EngineerModelId,
                "Saving a migrated provider configuration did not persist the lossless toggle schema.");
        }

        var configuration = configurationService.Save(
            defaultConfiguration with
            {
                AllowCloudRequests = true,
                ReasoningEffort = "medium",
                TextVerbosity = "low"
            });
        var reloadedConfiguration = configurationService.Load();
        Require(
            reloadedConfiguration == configuration,
            "The device-local AI provider configuration did not round-trip.");

        var credentialStore =
            new DpapiWorkspaceAiCredentialStore(credentialPath);
        credentialStore.Save("sk-verifier-device-key");
        Require(
            credentialStore.Exists
            && credentialStore.Load()?.ApiKey
                == "sk-verifier-device-key"
            && !Encoding.UTF8.GetString(
                    await File.ReadAllBytesAsync(credentialPath))
                .Contains("sk-verifier-device-key", StringComparison.Ordinal),
            "The OpenAI API key was not protected with the current Windows user scope.");

        var handler = new CapturingHandler(
            """
            {
              "id": "resp_hypershell_verifier",
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "provider ready",
                      "annotations": []
                    }
                  ]
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        using var provider = new ResponsesWorkspaceAiProvider(
            httpClient);
        var providerResult = await provider.CreateResponseAsync(
            configuration,
            "sk-verifier-device-key",
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content = "Verify request shape"
            })]);
        Require(
            providerResult.AssistantText == "provider ready"
            && providerResult.FunctionCalls.Count == 0
            && handler.Authorization == "Bearer sk-verifier-device-key"
            && handler.RequestUri
                == WorkspaceAiProviderConfiguration.OpenAiResponsesEndpoint,
            "The OpenAI Responses adapter did not parse or authorize the bounded request.");
        using (var captured = JsonDocument.Parse(handler.RequestBody))
        {
            var rootElement = captured.RootElement;
            var tools = rootElement.GetProperty("tools");
            Require(
                rootElement.GetProperty("store").ValueKind
                    == JsonValueKind.False
                && rootElement.GetProperty("parallel_tool_calls").ValueKind
                    == JsonValueKind.False
                && rootElement.GetProperty("model").GetString()
                    == WorkspaceAiProviderConfiguration.DefaultModel
                && rootElement.GetProperty("reasoning")
                    .GetProperty("effort").GetString() == "medium"
                && rootElement.GetProperty("text")
                    .GetProperty("verbosity").GetString() == "low"
                && rootElement.GetProperty("safety_identifier")
                    .GetString() == configuration.SafetyIdentifier
                && tools.GetArrayLength() == 23
                && tools.EnumerateArray().All(tool =>
                    tool.GetProperty("strict").GetBoolean()
                    && tool.GetProperty("parameters")
                        .GetProperty("additionalProperties").ValueKind
                        == JsonValueKind.False)
                && tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == "inspect_git_repository")
                && tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == "propose_git_operation")
                && tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == "search_hyperlib")
                && tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == "prepare_workspace_write")
                && tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == "prepare_workspace_edit")
                && tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == "search_workspace_text")
                && tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == "run_project_check")
                && new[]
                {
                    "read_active_visual_page",
                    "validate_visual_page",
                    "propose_visual_page_changes",
                    "prepare_visual_page_markup",
                    "capture_web_template",
                    "build_visual_page_preview"
                }.All(name => tools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString() == name)),
                "The provider request lost store:false, bounded reasoning/text settings, safety identity, or strict host tools.");
            var semanticMutationTool = tools.EnumerateArray().Single(tool =>
                tool.GetProperty("name").GetString()
                == "propose_visual_page_changes");
            Require(
                semanticMutationTool.GetProperty("parameters")
                    .GetProperty("properties")
                    .GetProperty("operations")
                    .GetProperty("maxItems")
                    .GetInt32() == 4,
                "The focused semantic Hyperpage tool can still invite an oversized full-page JSON call.");
        }

        _ = await provider.CreateResponseAsync(
            configuration with
            {
                TaskRouteName = "coding",
                EnabledToolNames =
                [
                    "list_workspace",
                    "prepare_workspace_write"
                ]
            },
            "sk-verifier-device-key",
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content = "Verify task-routed tools"
            })]);
        using (var routedPayload = JsonDocument.Parse(handler.RequestBody))
        {
            var routedTools = routedPayload.RootElement
                .GetProperty("tools");
            Require(
                routedTools.GetArrayLength() == 2
                && routedTools.EnumerateArray().Select(tool =>
                       tool.GetProperty("name").GetString())
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(
                    [
                        "list_workspace",
                        "prepare_workspace_write"
                    ]),
                "The monolithic assistant did not receive the task-routed tool subset.");
        }

        var openAiDesktopResult = await provider.CreateResponseAsync(
            configuration with
            {
                ComputerControl = WorkspaceAiComputerControlMode.Desktop
            },
            "sk-verifier-device-key",
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content = "Verify OpenAI desktop tool shape"
            })]);
        Require(
            openAiDesktopResult.AssistantText == "provider ready",
            "The OpenAI desktop-capable request did not complete.");
        using (var openAiDesktopPayload = JsonDocument.Parse(
                   handler.RequestBody))
        {
            var openAiDesktopTools = openAiDesktopPayload.RootElement
                .GetProperty("tools");
            Require(
                openAiDesktopTools.GetArrayLength() == 25
                && openAiDesktopTools.EnumerateArray().Count(tool =>
                    tool.GetProperty("type").GetString() == "computer") == 1
                && openAiDesktopTools.EnumerateArray().Any(tool =>
                    tool.GetProperty("type").GetString() == "function"
                    && tool.GetProperty("name").GetString()
                    == WorkspaceAiWindowsUiFunctionTool.FunctionName),
                "The OpenAI provider lost its native computer tool or semantic Windows UI tool.");
        }

        var localConfiguration =
            WorkspaceAiProviderConfigurationService.Normalize(
                defaultConfiguration with
                {
                    ProviderId =
                        WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                    Endpoint = WorkspaceAiProviderConfiguration
                        .LlamaCppResponsesEndpoint,
                    Model = WorkspaceAiProviderConfiguration.DefaultLocalModel,
                    AllowCloudRequests = true
                });
        Require(
            localConfiguration.IsLocal
            && !localConfiguration.AllowCloudRequests
            && !localConfiguration.RequiresCredential
            && WorkspaceAiModelCatalog.Generative.Count == 1
            && WorkspaceAiModelCatalog.All.Count == 2
            && WorkspaceAiModelCatalog.Find(
                WorkspaceAiModelCatalog.EngineerModelId)
                is
            {
                SupportsVision: true,
                VisionProjector.FileName: "mmproj-F16.gguf"
            }
            && WorkspaceAiModelCatalog.Find(
                WorkspaceAiModelCatalog.LibrarianModelId)
                is { SupportsEmbeddings: true, SupportsConversation: false },
            "The trusted local model catalog or local provider normalization is invalid.");
        Require(
            WorkspaceAiModelCatalog.ResolveContextModel(
                localConfiguration with
                {
                    Model = RetiredOperatorModelId
                },
                RetiredOperatorModelId,
                WorkspaceAiComputerControlMode.Desktop)
            == WorkspaceAiModelCatalog.EngineerModelId
            && WorkspaceAiModelCatalog.ResolveContextModel(
                localConfiguration with
                {
                    Model = RetiredOperatorModelId
                },
                RetiredOperatorModelId)
            == WorkspaceAiModelCatalog.EngineerModelId
            && WorkspaceAiModelCatalog.Find(RetiredOperatorModelId) is null,
            "The retired Operator identifier was not migrated to Engineer or remained in the active catalog.");
        var localHandler = new CapturingHandler(
            """
            {
              "id": "resp_hypershell_local_verifier",
              "output": [
                {
                  "type": "message",
                  "role": "assistant",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "local provider ready"
                    }
                  ]
                }
              ]
            }
            """);
        using (var localHttpClient = new HttpClient(localHandler))
        using (var localProvider = new ResponsesWorkspaceAiProvider(
                   localHttpClient))
        {
            var localResult = await localProvider.CreateResponseAsync(
                localConfiguration,
                string.Empty,
                [JsonSerializer.SerializeToElement(new
                {
                    role = "user",
                    content = "Verify local request shape"
                })]);
            Require(
                localResult.AssistantText == "local provider ready"
                && string.IsNullOrEmpty(localHandler.Authorization)
                && localHandler.RequestUri
                    == WorkspaceAiProviderConfiguration
                        .LlamaCppResponsesEndpoint,
                "The local Responses adapter required a cloud credential or used a non-loopback endpoint.");
            using var localPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            var localRoot = localPayload.RootElement;
            Require(
                localRoot.GetProperty("model").GetString()
                    == WorkspaceAiProviderConfiguration.DefaultLocalModel
                && localRoot.GetProperty("max_output_tokens").GetInt32()
                    == 4096
                && localRoot.GetProperty("chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean()
                && localRoot.GetProperty("input").EnumerateArray().All(item =>
                    item.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "message")
                && !localRoot.TryGetProperty("reasoning", out _)
                && !localRoot.TryGetProperty("text", out _)
                && !localRoot.TryGetProperty("safety_identifier", out _)
                && localRoot.GetProperty("tools").GetArrayLength() == 23
                && localRoot.GetProperty("tools").EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                    == "execute_terminal_command")
                && localRoot.GetProperty("tools").EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                    == "stage_terminal_command")
                && localRoot.GetProperty("tools").EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                    == "prepare_workspace_write")
                && localRoot.GetProperty("tools").EnumerateArray().All(tool =>
                    tool.GetProperty("name").GetString()
                    != "propose_terminal_command")
                && localRoot.GetProperty("instructions").GetString()!
                    .Contains(
                        "Never ask for permission that the selected Context already grants",
                        StringComparison.Ordinal),
                "The local request was not reduced to the typed, bounded llama.cpp-compatible Responses contract.");

            var localDesktopResult = await localProvider.CreateResponseAsync(
                localConfiguration with
                {
                    ComputerControl = WorkspaceAiComputerControlMode.Desktop
                },
                string.Empty,
                [JsonSerializer.SerializeToElement(new
                {
                    role = "user",
                    content = "Verify local desktop tool shape"
                })]);
            Require(
                localDesktopResult.AssistantText == "local provider ready",
                "The local desktop-capable request did not complete.");
            using var localDesktopPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            var localDesktopTools = localDesktopPayload.RootElement
                .GetProperty("tools");
            Require(
                localDesktopTools.GetArrayLength() == 25
                && localDesktopTools.EnumerateArray().All(tool =>
                    tool.GetProperty("type").GetString() == "function")
                && localDesktopTools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                        == WorkspaceAiComputerFunctionTool.FunctionName
                    && tool.GetProperty("strict").GetBoolean()
                    && tool.GetProperty("parameters")
                        .GetProperty("properties")
                        .GetProperty("actions")
                        .GetProperty("maxItems").GetInt32()
                        == WorkspaceAiComputerFunctionTool.MaximumActions),
                "The vision-capable local provider did not receive the bounded desktop function tools.");
            Require(
                localDesktopTools.EnumerateArray().Any(tool =>
                    tool.GetProperty("name").GetString()
                    == WorkspaceAiWindowsUiFunctionTool.FunctionName
                    && tool.GetProperty("strict").GetBoolean()
                    && tool.GetProperty("parameters")
                        .GetProperty("required")
                        .GetArrayLength() == 3),
                "The local provider did not receive the strict semantic Windows UI tool.");

            _ = await localProvider.CreateResponseAsync(
                localConfiguration with
                {
                    ComputerControl = WorkspaceAiComputerControlMode.Desktop
                },
                string.Empty,
                [JsonSerializer.SerializeToElement(new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text =
                                $"{WorkspaceAiComputerFunctionTool.ScreenshotMarker} Continue the current desktop action loop."
                        }
                    }
                })]);
            using var localActionLoopPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            Require(
                localActionLoopPayload.RootElement
                    .GetProperty("max_output_tokens").GetInt32() == 1536
                && !localActionLoopPayload.RootElement
                    .GetProperty("chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean(),
                "The local Engineer desktop continuation did not switch to the low-latency action-loop budget.");

            _ = await localProvider.CreateResponseAsync(
                localConfiguration with
                {
                    ComputerControl = WorkspaceAiComputerControlMode.Desktop
                },
                string.Empty,
                [JsonSerializer.SerializeToElement(new
                {
                    role = "user",
                    content =
                        $"{WorkspaceAiWindowsUiFunctionTool.SnapshotMarker} Continue the semantic Windows action loop."
                })]);
            using var localSemanticLoopPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            Require(
                localSemanticLoopPayload.RootElement
                    .GetProperty("max_output_tokens").GetInt32() == 1536
                && !localSemanticLoopPayload.RootElement
                    .GetProperty("chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean(),
                "The local Engineer semantic continuation did not switch to the low-latency action-loop budget.");

            _ = await localProvider.CreateResponseAsync(
                localConfiguration,
                string.Empty,
                [
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call",
                        call_id = "source-transfer-provider",
                        name = "prepare_workspace_write",
                        arguments =
                            "{\"relative_path\":\"index.html\",\"expected_sha256\":null,\"allow_create\":true}"
                    }),
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call_output",
                        call_id = "source-transfer-provider",
                        output = new
                        {
                            ok = true,
                            source_transfer = WorkspaceAiToolHost
                                .SourceTransferRequestMarker
                        }
                    })
                ]);
            using var sourceTransferPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            var sourceTransferRoot = sourceTransferPayload.RootElement;
            Require(
                sourceTransferRoot.GetProperty("tool_choice").GetString()
                    == "none"
                && sourceTransferRoot.GetProperty("tools").GetArrayLength()
                    == 0
                && sourceTransferRoot.GetProperty("max_output_tokens")
                    .GetInt32() == 8192
                && !sourceTransferRoot.GetProperty("chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean()
                && sourceTransferRoot.GetProperty("instructions").GetString()!
                    .Contains(
                        "ordinary response text",
                        StringComparison.Ordinal)
                && sourceTransferRoot.GetProperty("instructions").GetString()!
                    .Contains(
                        "Preserve quotes",
                    StringComparison.Ordinal),
                "The prepared source-file continuation still exposed JSON tools, thinking, or an underspecified raw-content transfer.");

            _ = await localProvider.CreateResponseAsync(
                localConfiguration,
                string.Empty,
                [
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call",
                        call_id = "visual-markup-transfer-provider",
                        name = "prepare_visual_page_markup",
                        arguments =
                            "{\"expected_revision\":\"revision-1\",\"parent_element_id\":\"page-root\",\"markup_language\":\"html\",\"reason\":\"Build a portfolio landing page\"}"
                    }),
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call_output",
                        call_id = "visual-markup-transfer-provider",
                        output = new
                        {
                            ok = true,
                            visual_page_markup_transfer = WorkspaceAiToolHost
                                .VisualPageMarkupTransferRequestMarker
                        }
                    })
                ]);
            using var visualMarkupTransferPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            var visualMarkupTransferRoot =
                visualMarkupTransferPayload.RootElement;
            Require(
                visualMarkupTransferRoot.GetProperty("tool_choice")
                    .GetString() == "none"
                && visualMarkupTransferRoot.GetProperty("tools")
                    .GetArrayLength() == 0
                && visualMarkupTransferRoot.GetProperty("max_output_tokens")
                    .GetInt32() == 8192
                && !visualMarkupTransferRoot.GetProperty(
                        "chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean()
                && visualMarkupTransferRoot.GetProperty("instructions")
                    .GetString()!.Contains(
                        "HYPERPAGE MARKUP TRANSFER MODE",
                        StringComparison.Ordinal)
                && visualMarkupTransferRoot.GetProperty("instructions")
                    .GetString()!.Contains(
                        "scoped style element",
                        StringComparison.Ordinal),
                "The staged Hyperpage markup continuation still exposed tools, thinking, or JSON-bound page content.");

            _ = await localProvider.CreateResponseAsync(
                localConfiguration,
                string.Empty,
                [JsonSerializer.SerializeToElement(new
                {
                    type = "function_call_output",
                    call_id = "truncated-visual-page-call",
                    output = new
                    {
                        ok = false,
                        message = WorkspaceAiToolHost
                                      .VisualPageArgumentRecoveryMarker
                                  + " The prior page operation was truncated."
                    }
                })]);
            using var visualRecoveryPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            var visualRecoveryRoot = visualRecoveryPayload.RootElement;
            var visualRecoveryTools = visualRecoveryRoot.GetProperty("tools");
            Require(
                visualRecoveryRoot.GetProperty("max_output_tokens")
                    .GetInt32() == 1024
                && !visualRecoveryRoot.GetProperty("chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean()
                && visualRecoveryTools.GetArrayLength() == 1
                && visualRecoveryTools[0].GetProperty("name").GetString()
                    == "prepare_visual_page_markup"
                && visualRecoveryRoot.GetProperty("instructions")
                    .GetString()!.Contains(
                        "HYPERPAGE RECOVERY",
                        StringComparison.Ordinal),
                "A truncated semantic page call did not narrow the retry to the small prepared-markup preflight.");

            _ = await localProvider.CreateResponseAsync(
                localConfiguration,
                string.Empty,
                [
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call",
                        call_id = "exact-edit-transfer-provider",
                        name = "prepare_workspace_edit",
                        arguments =
                            $"{{\"relative_path\":\"index.html\",\"expected_sha256\":\"{new string('a', 64)}\"}}"
                    }),
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call_output",
                        call_id = "exact-edit-transfer-provider",
                        output = new
                        {
                            ok = true,
                            source_transfer = WorkspaceAiToolHost
                                .ExactEditTransferRequestMarker
                        }
                    })
                ]);
            using var exactEditTransferPayload = JsonDocument.Parse(
                localHandler.RequestBody);
            var exactEditTransferRoot = exactEditTransferPayload.RootElement;
            Require(
                exactEditTransferRoot.GetProperty("tool_choice").GetString()
                    == "none"
                && exactEditTransferRoot.GetProperty("tools")
                    .GetArrayLength() == 0
                && exactEditTransferRoot.GetProperty("max_output_tokens")
                    .GetInt32() == 8192
                && !exactEditTransferRoot.GetProperty("chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean()
                && exactEditTransferRoot.GetProperty("instructions")
                    .GetString()!.Contains(
                        "HYPERSHELL EXACT EDIT TRANSFER MODE",
                        StringComparison.Ordinal)
                && exactEditTransferRoot.GetProperty("instructions")
                    .GetString()!.Contains(
                        ">>>>>>> REPLACE",
                        StringComparison.Ordinal),
                "The exact-edit continuation did not disable tools and provide the bounded SEARCH/REPLACE contract.");
        }

        var malformedToolHandler = new SequencedHandler(
            (
                HttpStatusCode.InternalServerError,
                """
                {
                  "error": {
                    "message": "Failed to parse tool call arguments as JSON: invalid string: missing closing quote; last read: <!DOCTYPE html>"
                  }
                }
                """),
            (
                HttpStatusCode.OK,
                """
                {
                  "id": "resp_hypershell_recovered_tool",
                  "output": [
                    {
                      "type": "function_call",
                      "call_id": "call_recovered_terminal",
                      "name": "execute_terminal_command",
                      "arguments": "{\"command\":\"dotnet build\"}"
                    }
                  ]
                }
                """));
        using (var malformedToolClient = new HttpClient(malformedToolHandler))
        using (var malformedToolProvider = new ResponsesWorkspaceAiProvider(
                   malformedToolClient))
        {
            var recovered = await malformedToolProvider.CreateResponseAsync(
                localConfiguration,
                string.Empty,
                [JsonSerializer.SerializeToElement(new
                {
                    role = "user",
                    content = "Build the current project."
                })]);
            using var retryPayload = JsonDocument.Parse(
                malformedToolHandler.RequestBodies[1]);
            Require(
                malformedToolHandler.RequestBodies.Count == 2
                && recovered.FunctionCalls.Count == 1
                && recovered.FunctionCalls[0].Name
                    == "execute_terminal_command"
                && retryPayload.RootElement.GetProperty("input")
                    .EnumerateArray().Last().GetProperty("content")
                     .GetString()!.Contains(
                         "[HYPERSHELL_TOOL_CALL_RECOVERY]",
                         StringComparison.Ordinal)
                && retryPayload.RootElement.GetProperty("input")
                    .EnumerateArray().Last().GetProperty("content")
                    .GetString()!.Contains(
                        "prepare_workspace_write",
                        StringComparison.Ordinal)
                && retryPayload.RootElement
                    .GetProperty("max_output_tokens").GetInt32() == 2048
                && !retryPayload.RootElement
                    .GetProperty("chat_template_kwargs")
                    .GetProperty("enable_thinking").GetBoolean(),
                "A malformed llama.cpp tool call did not receive one bounded, low-latency recovery turn.");
        }

        var repeatedMalformedHandler = new SequencedHandler(
            (
                HttpStatusCode.InternalServerError,
                "{\"error\":{\"message\":\"Failed to parse tool call arguments as JSON: <!DOCTYPE html> sensitive generated body\"}}"),
            (
                HttpStatusCode.InternalServerError,
                "{\"error\":{\"message\":\"Failed to parse tool call arguments as JSON: <!DOCTYPE html> sensitive generated body\"}}"));
        using (var repeatedMalformedClient = new HttpClient(
                   repeatedMalformedHandler))
        using (var repeatedMalformedProvider =
               new ResponsesWorkspaceAiProvider(repeatedMalformedClient))
        {
            try
            {
                _ = await repeatedMalformedProvider.CreateResponseAsync(
                    localConfiguration,
                    string.Empty,
                    [JsonSerializer.SerializeToElement(new
                    {
                        role = "user",
                        content = "Create an HTML file."
                    })]);
                throw new InvalidOperationException(
                    "Repeated malformed local tool calls were accepted unexpectedly.");
            }
            catch (WorkspaceAiProviderException exception)
            {
                Require(
                    repeatedMalformedHandler.RequestBodies.Count == 2
                    && exception.Message.Contains(
                        "retried automatically",
                        StringComparison.Ordinal)
                    && !exception.Message.Contains(
                        "DOCTYPE",
                        StringComparison.OrdinalIgnoreCase)
                    && !exception.Message.Contains(
                        "sensitive generated body",
                        StringComparison.Ordinal),
                    "A repeated malformed local tool call leaked its generated file body or skipped the single-retry limit.");
            }
        }

        var openAiPreferences = configurationService.Save(
            configuration with
            {
                Model = "gpt-5.6",
                ReasoningEffort = "high",
                TextVerbosity = "low",
                AllowCloudRequests = true,
                LocalModel = RetiredOperatorModelId,
                LocalReasoningEffort = "none",
                LocalTextVerbosity = "medium"
            });
        var switchedToLocal = configurationService.Save(
            openAiPreferences with
            {
                ProviderId =
                    WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                Endpoint = WorkspaceAiProviderConfiguration
                    .LlamaCppResponsesEndpoint,
                Model = openAiPreferences.LocalModel,
                ReasoningEffort =
                    openAiPreferences.LocalReasoningEffort,
                TextVerbosity = openAiPreferences.LocalTextVerbosity,
                AllowCloudRequests = false
            });
        Require(
            switchedToLocal.IsLocal
            && switchedToLocal.Model
                == WorkspaceAiModelCatalog.EngineerModelId
            && switchedToLocal.OpenAiModel == "gpt-5.6"
            && switchedToLocal.OpenAiReasoningEffort == "high"
            && switchedToLocal.OpenAiTextVerbosity == "low"
            && switchedToLocal.OpenAiCloudRequestsEnabled
            && !switchedToLocal.AllowCloudRequests,
            "Switching to local AI did not preserve the independent OpenAI provider preferences.");
        var switchedBackToOpenAi = configurationService.Save(
            switchedToLocal with
            {
                ProviderId =
                    WorkspaceAiProviderConfiguration.OpenAiProviderId,
                Endpoint = WorkspaceAiProviderConfiguration
                    .OpenAiResponsesEndpoint,
                Model = switchedToLocal.OpenAiModel,
                ReasoningEffort =
                    switchedToLocal.OpenAiReasoningEffort,
                TextVerbosity = switchedToLocal.OpenAiTextVerbosity,
                AllowCloudRequests =
                    switchedToLocal.OpenAiCloudRequestsEnabled
            });
        Require(
            !switchedBackToOpenAi.IsLocal
            && switchedBackToOpenAi.Model == "gpt-5.6"
            && switchedBackToOpenAi.ReasoningEffort == "high"
            && switchedBackToOpenAi.TextVerbosity == "low"
            && switchedBackToOpenAi.AllowCloudRequests
            && switchedBackToOpenAi.LocalModel
                == WorkspaceAiModelCatalog.EngineerModelId
            && switchedBackToOpenAi.LocalReasoningEffort == "none"
            && switchedBackToOpenAi.LocalTextVerbosity == "medium",
            "Switching back to OpenAI did not restore its complete provider preferences.");

        var appSettingsService = new SettingsService(appDataRoot);
        var appSettings = appSettingsService.Load();
        appSettings.Explorer.StartupDirectory = workspaceRoot;
        appSettingsService.Save(appSettings);
        using (var mainWindow = new MainWindowViewModel(appSettingsService))
        using (var settings = new SettingsWindowViewModel(mainWindow))
        {
            var deviceSelectionBeforeAccessSave =
                configurationService.Load();
            Require(
                settings.AllowWorkspaceAiCloudRequests,
                "The Settings AI access page did not load the remembered OpenAI access choice.");
            settings.AllowWorkspaceAiCloudRequests = false;
            settings.SaveWorkspaceAiAccess(apiKey: null);
            var disabledAccess = configurationService.Load();
            Require(
                disabledAccess.ProviderId
                    == deviceSelectionBeforeAccessSave.ProviderId
                && disabledAccess.Model
                    == deviceSelectionBeforeAccessSave.Model
                && disabledAccess.ReasoningEffort
                    == deviceSelectionBeforeAccessSave.ReasoningEffort
                && disabledAccess.TextVerbosity
                    == deviceSelectionBeforeAccessSave.TextVerbosity
                && !disabledAccess.OpenAiCloudRequestsEnabled,
                "Saving AI access from Settings changed the device provider/model defaults.");
            settings.AllowWorkspaceAiCloudRequests = true;
            settings.SaveWorkspaceAiAccess(apiKey: null);

            Require(
                settings.WorkspaceAiAutonomyContextName == "Default"
                && !settings.AllowWorkspaceAiAutonomousChanges,
                "Settings did not expose the selected Context's terminal-autonomy state.");
            settings.AllowWorkspaceAiAutonomousChanges = true;
            settings.SaveWorkspaceAiAutonomy(riskConfirmed: true);
            Require(
                mainWindow.WorkspaceAiContextService.GetContext(
                    WorkspaceAiContextDefinition.DefaultId)
                    is { AskBeforeChanges: false }
                && mainWindow.WorkspaceAiPermissionBroker
                    .GetTerminalExecutionPolicy(
                        WorkspaceAiContextDefinition.DefaultId)
                    == WorkspaceAiTerminalExecutionPolicy.AutoExecute
                && settings.WorkspaceAiAutonomyStatus.Contains(
                    "without asking",
                    StringComparison.Ordinal),
                "The visible Settings autonomy toggle did not enable automatic terminal execution for the selected Context.");

            var assistant = mainWindow.WorkspaceAiAssistant;
            assistant.RefreshState();
            Require(
                assistant.ProviderModelOptions.Count == 2
                && assistant.SelectedProviderModel is
                {
                    IsLocal: true,
                    ModelId: WorkspaceAiModelCatalog.EngineerModelId
                }
                && assistant.ProviderModelOptions.All(option =>
                    string.Equals(
                        option.ToString(),
                        option.DisplayName,
                        StringComparison.Ordinal)),
                "The AI conversation pane did not expose friendly OpenAI and local model choices.");

            Require(
                assistant is
                {
                    SessionProviderId: WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                    SessionModelId: WorkspaceAiModelCatalog.EngineerModelId
                }
                && configurationService.Load()
                    == deviceSelectionBeforeAccessSave,
                "Choosing Engineer in one conversation rewrote the device provider/model configuration.");

            var secondConversation =
                mainWindow.CreateWorkspaceAiConversation();
            var secondAssistant = secondConversation.Assistant;
            Require(
                assistant.SessionModelId
                    == WorkspaceAiModelCatalog.EngineerModelId
                && secondAssistant.SessionModelId
                    == WorkspaceAiModelCatalog.EngineerModelId
                && configurationService.Load()
                    == deviceSelectionBeforeAccessSave,
                "New AI conversations did not independently default to Engineer.");
            var persistedConversations = new LocalAiConversationStore(
                    appSettingsService.ConversationDirectory,
                    appSettingsService.ProfileConflictDirectory)
                .Load(appSettings.PortableProfileId);
            Require(
                persistedConversations.Any(document =>
                    document.Id == secondConversation.ConversationId
                    && document.ProviderId
                    == WorkspaceAiProviderConfiguration.LlamaCppProviderId
                    && document.ModelId
                    == WorkspaceAiModelCatalog.EngineerModelId),
                "The conversation-specific model choice did not round-trip through local history storage.");

            var openAiOption = assistant.ProviderModelOptions.Single(option =>
                !option.IsLocal);
            assistant.SelectedProviderModel = openAiOption;
            Require(
                assistant.SessionProviderId
                    == WorkspaceAiProviderConfiguration.OpenAiProviderId
                && secondAssistant.SessionModelId
                    == WorkspaceAiModelCatalog.EngineerModelId,
                "Changing one conversation to OpenAI changed another conversation's model selection.");

            Require(
                new WorkspaceAiChatMessageViewModel("You", "Prompt").IsUser
                && !new WorkspaceAiChatMessageViewModel(
                    "Hypershell AI",
                    "Answer").IsUser,
                "AI transcript messages did not expose stable user/assistant presentation roles.");
        }

        var savedDeviceConfiguration = configurationService.Load();
        Require(
            !savedDeviceConfiguration.IsLocal
            && savedDeviceConfiguration.Model == "gpt-5.6"
            && savedDeviceConfiguration.LocalModel
                == WorkspaceAiModelCatalog.EngineerModelId
            && savedDeviceConfiguration.OpenAiCloudRequestsEnabled
            && credentialStore.Exists,
            "Conversation-level model choices changed the device access configuration or protected key.");

        var documentPath = Path.Combine(workspaceRoot, "notes.txt");
        await File.WriteAllTextAsync(
            documentPath,
            "original\r\n",
            new UTF8Encoding(false));
        var audit = new WorkspaceAiAuditService(
            Path.Combine(appDataRoot, "ai-operations.audit.jsonl"));
        var broker = new WorkspaceAiPermissionBroker(
            Path.Combine(appDataRoot, "ai-permissions.device.json"),
            audit);
        broker.Approve(
            WorkspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Provider verifier root",
            allowTerminalRead: true);
        var operations = new WorkspaceAiOperationService(broker, audit);

        var liveContent = "live editor content\r\n";
        var textProvider = new TextDocumentProvider();
        WorkspaceAiActiveDocumentHandle ActiveDocument() => new(
            "session-provider-verifier",
            "notes.txt",
            "notes.txt",
            "plaintext",
            TextDocumentProvider.ProviderId,
            HasUnsavedChanges: true,
            () => liveContent,
            () => textProvider.Read(new DocumentReadRequest(
                liveContent,
                "plaintext",
                documentPath)),
            replacement =>
            {
                var mutation = textProvider.Replace(
                    new DocumentReplaceRequest(
                        liveContent,
                        0,
                        liveContent.Length,
                        replacement,
                        "plaintext",
                        documentPath));
                if (mutation.Succeeded)
                {
                    liveContent = mutation.Content;
                }

                return mutation;
            });
        var terminalDispatchCount = 0;
        var terminalExecutionCount = 0;
        var terminalConfirmedExecutionCount = 0;
        var terminalDraft = "Get-ChildItem";
        var terminalTranscript = new List<WorkspaceAiTerminalLineSnapshot>
        {
            new("System", "PowerShell terminal ready."),
            new("Input", "PS> Get-Location"),
            new("Output", workspaceRoot)
        };
        WorkspaceAiActiveTerminalHandle ActiveTerminal() => new(
            "terminal-provider-verifier",
            "Verifier shell",
            "PowerShell",
            workspaceRoot,
            WorkspaceLocation.Local(workspaceRoot).Identity,
            string.Empty,
            $"PS {workspaceRoot}>",
            IsRunning: false,
            () => terminalDraft,
            () => terminalTranscript);
        var hyperlibSearch = new StubHyperlibSearch();
        var toolHost = new WorkspaceAiToolHost(
            broker,
            operations,
            (_, _) => ActiveDocument(),
            (_, _, sessionId) => sessionId == "session-provider-verifier"
                ? ActiveDocument()
                : null,
            (_, _) => ActiveTerminal(),
            (_, _, _, command, execute, userConfirmed, _) =>
            {
                terminalDispatchCount++;
                if (execute)
                {
                    terminalExecutionCount++;
                    terminalTranscript.Add(new WorkspaceAiTerminalLineSnapshot(
                        "Input",
                        command));
                    terminalTranscript.Add(new WorkspaceAiTerminalLineSnapshot(
                        "Output",
                        "Synthetic terminal command completed."));
                    if (userConfirmed)
                    {
                        terminalConfirmedExecutionCount++;
                    }
                }

                return Task.FromResult(
                    new WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>
                    {
                        Status = WorkspaceAiOperationStatus.Succeeded,
                        Message = "Synthetic terminal dispatch completed.",
                        Value = new WorkspaceAiTerminalDispatchReceipt(
                            execute,
                            new string('a', 64))
                    });
            },
            hyperlibSearch: hyperlibSearch);
        Task<WorkspaceAiToolOutcome> ExecuteToolAsync(
            WorkspaceAiFunctionCall call) => toolHost.ExecuteAsync(
            WorkspaceId,
            call,
            string.Empty,
            workspaceRoot);

        var hyperlibOutcome = await ExecuteToolAsync(
            Call(
                "hyperlib-search",
                "search_hyperlib",
                new
                {
                    query = "copy a file",
                    limit = 3
                }));
        using (var hyperlibOutput = JsonDocument.Parse(
                   hyperlibOutcome.FunctionOutput))
        {
            Require(
                hyperlibOutput.RootElement.GetProperty("ok").GetBoolean()
                && hyperlibOutput.RootElement.GetProperty("count").GetInt32()
                    == 1
                && hyperlibOutput.RootElement.GetProperty("results")[0]
                    .GetProperty("name").GetString() == "Copy-Item"
                && hyperlibSearch.LastQuery == "copy a file"
                && hyperlibSearch.LastLimit == 3,
                "The host-owned Hyperlib semantic search tool lost its bounded request or provenance-bearing result.");
        }

        var diskRead = await operations.ReadTextAsync(
            WorkspaceId,
            "notes.txt");
        Require(
            diskRead.Succeeded && diskRead.Value is not null,
            "The provider verifier could not read its workspace fixture.");
        var diskProposal = await ExecuteToolAsync(
            Call(
                "disk-write",
                "propose_workspace_write",
                new
                {
                    relative_path = "notes.txt",
                    content = "reviewed replacement\r\n",
                    expected_sha256 = diskRead.Value!.Sha256,
                    allow_create = false
                }));
        Require(
            diskProposal.PendingReview is not null
            && await File.ReadAllTextAsync(documentPath) == "original\r\n",
            "A proposed workspace write mutated the file before review.");
        await toolHost.ApproveAsync(diskProposal.PendingReview!);
        Require(
            await File.ReadAllTextAsync(documentPath)
                == "reviewed replacement\r\n",
            "The explicitly approved workspace replacement was not applied.");

        var generatedDirectory = Path.Combine(workspaceRoot, "generated");
        Directory.CreateDirectory(generatedDirectory);
        var generatedHtmlPath = Path.Combine(
            generatedDirectory,
            "index.html");
        var stringNullPrepare = await ExecuteToolAsync(
            Call(
                "string-null-source-transfer",
                "prepare_workspace_write",
                new
                {
                    relative_path = "generated/string-null.html",
                    expected_sha256 = "null",
                    allow_create = true
                }));
        Require(
            stringNullPrepare.PreparedWorkspaceWrite is
            {
                ExpectedSha256: null
            },
            "The prepared source path did not normalize the local model's quoted null SHA without weakening target validation.");
        var generatedHtml = string.Join(
            '\n',
            "<!doctype html>",
            "<html lang=\"en\">",
            "<head><meta charset=\"utf-8\"><title>AI Learning Journey</title></head>",
            "<body>",
            "  <button id=\"next\">Next lesson</button>",
            "  <script>",
            "    const lesson = { title: \"What JSON does\", complete: false };",
            "    document.querySelector(\"#next\").addEventListener(\"click\", () => { lesson.complete = true; });",
            "  </script>",
            "</body>",
            "</html>") + "\n";
        var sourceTransferProvider = new ScriptedProvider(
            FunctionResponse(
                "coordinator-source-transfer",
                "prepare_workspace_write",
                new
                {
                    relative_path = "generated/index.html",
                    expected_sha256 = (string?)null,
                    allow_create = true
                }),
            TextResponse(generatedHtml),
            TextResponse(
                "Created the HTML file through the reviewed source-transfer path."));
        var sourceTransferCoordinator =
            new WorkspaceAiConversationCoordinator(
                sourceTransferProvider,
                configurationService,
                credentialStore,
                toolHost);
        sourceTransferCoordinator.SetWorkingDirectory(workspaceRoot);
        var sourceTransferPending = await sourceTransferCoordinator.SendAsync(
            WorkspaceId,
            "Create a browser app in generated/index.html.");
        Require(
            sourceTransferPending.PendingReview is
            {
                Kind: WorkspaceAiPendingActionKind.WorkspaceTextWrite
            }
            && !File.Exists(generatedHtmlPath)
            && sourceTransferProvider.Inputs.Count == 2
            && sourceTransferProvider.Inputs[1].Last()
                .GetProperty("output").GetString()!.Contains(
                    WorkspaceAiToolHost.SourceTransferRequestMarker,
                    StringComparison.Ordinal)
            && sourceTransferProvider.Inputs[1]
                .Where(item => item.TryGetProperty("arguments", out _))
                .All(item => !item.GetProperty("arguments").GetString()!
                    .Contains("DOCTYPE", StringComparison.OrdinalIgnoreCase)),
            "The coordinator did not isolate multiline HTML from JSON tool arguments or preserve review-before-write.");
        var sourceTransferCompleted = await sourceTransferCoordinator
            .ApproveAsync();
        Require(
            await File.ReadAllTextAsync(generatedHtmlPath) == generatedHtml
            && sourceTransferCompleted.AssistantText.Contains(
                "source-transfer path",
                StringComparison.Ordinal)
            && sourceTransferProvider.Inputs.Count == 3
            && sourceTransferProvider.Inputs[2].Last()
                .GetProperty("content").GetString()!.Contains(
                    "[HYPERSHELL_SOURCE_TRANSFER_RESULT_V1]",
                    StringComparison.Ordinal),
            "The approved multiline HTML transfer was not written exactly or resumed through a host result.");

        var hyperpageDirectPath = Path.Combine(
            workspaceRoot,
            "generated",
            "hyperpage-direct.html");
        var hyperpageDirectProvider = new ScriptedProvider(
            TextResponse(
                "Copy and paste this into the project manually:\n```html\n<h1>Apples</h1>\n```"),
            FunctionResponse(
                "hyperpage-direct-write",
                "prepare_workspace_write",
                new
                {
                    relative_path = "generated/hyperpage-direct.html",
                    expected_sha256 = (string?)null,
                    allow_create = true
                }),
            TextResponse(
                $"{WorkspaceAiToolHost.SourceTransferBeginMarker}\n<h1>Apples</h1>\n{WorkspaceAiToolHost.SourceTransferEndMarker}"));
        var hyperpageDirectCoordinator =
            new WorkspaceAiConversationCoordinator(
                hyperpageDirectProvider,
                configurationService,
                credentialStore,
                toolHost);
        hyperpageDirectCoordinator.SetWorkingDirectory(workspaceRoot);
        hyperpageDirectCoordinator.SetHyperpageProjectWorkflow(enabled: true);
        var hyperpageDirectPending = await hyperpageDirectCoordinator.SendAsync(
            WorkspaceId,
            "1. Families\n2. Minimal\n3. Three pages");
        Require(
            hyperpageDirectPending.PendingReview is
            {
                Kind: WorkspaceAiPendingActionKind.WorkspaceTextWrite
            }
            && !File.Exists(hyperpageDirectPath)
            && hyperpageDirectProvider.Inputs.Count == 3
            && hyperpageDirectProvider.Inputs[1].Last()
                .GetProperty("content").GetString()!.Contains(
                    "HYPERPAGE_HOST_CORRECTION",
                    StringComparison.Ordinal),
            "A Hyperpage project answer was shown as manual-install code instead of being corrected into a reviewed project write.");

        var generatedRead = await operations.ReadTextAsync(
            WorkspaceId,
            Path.Combine("generated", "index.html"));
        Require(
            generatedRead.Succeeded && generatedRead.Value is not null,
            "The exact-edit verifier could not read the generated HTML revision.");
        var preparedEdit = await ExecuteToolAsync(
            Call(
                "prepare-exact-edit",
                "prepare_workspace_edit",
                new
                {
                    relative_path = "generated/index.html",
                    expected_sha256 = generatedRead.Value!.Sha256
                }));
        Require(
            preparedEdit.PreparedWorkspaceWrite is
            {
                TransferKind: WorkspaceAiSourceTransferKind.ExactEdit
            }
            && preparedEdit.FunctionOutput.Contains(
                WorkspaceAiToolHost.ExactEditTransferRequestMarker,
                StringComparison.Ordinal),
            "The host did not prepare an existing source file for exact-edit transfer.");
        var exactEditOutcome = await toolHost
            .CompletePreparedWorkspaceWriteAsync(
                preparedEdit.PreparedWorkspaceWrite!,
                """
                <<<<<<< SEARCH
                  <button id="next">Next lesson</button>
                =======
                  <button id="next">Continue lesson</button>
                >>>>>>> REPLACE
                """,
                conversationId: "exact-edit-verifier");
        Require(
            exactEditOutcome.PendingReview is not null
            && (await File.ReadAllTextAsync(generatedHtmlPath)).Contains(
                "Next lesson",
                StringComparison.Ordinal),
            "Exact-edit transfer bypassed the configured review boundary.");
        await toolHost.ApproveAsync(exactEditOutcome.PendingReview!);
        Require(
            (await File.ReadAllTextAsync(generatedHtmlPath)).Contains(
                "Continue lesson",
                StringComparison.Ordinal),
            "The reviewed exact-edit transfer was not applied atomically.");

        var workspaceSearch = await ExecuteToolAsync(
            Call(
                "repository-search",
                "search_workspace_text",
                new
                {
                    relative_path = ".",
                    query = "Continue lesson",
                    file_pattern = "*.html",
                    use_regex = false,
                    case_sensitive = true
                }));
        using (var searchOutput = JsonDocument.Parse(
                   workspaceSearch.FunctionOutput))
        {
            Require(
                searchOutput.RootElement.GetProperty("ok").GetBoolean()
                && searchOutput.RootElement.GetProperty("match_count")
                    .GetInt32() == 1
                && searchOutput.RootElement.GetProperty("matches")[0]
                    .GetProperty("display_path").GetString()!
                    .Replace('\\', '/') == "generated/index.html",
                "The host-owned repository search did not return its bounded source match.");
        }

        var activeRead = await ExecuteToolAsync(
            Call("active-read", "read_active_document", new { }));
        string activeSha;
        using (var activeOutput = JsonDocument.Parse(
                   activeRead.FunctionOutput))
        {
            activeSha = activeOutput.RootElement
                .GetProperty("sha256")
                .GetString()!;
        }

        var activeProposal = await ExecuteToolAsync(
            Call(
                "active-replace",
                "propose_active_document_replace",
                new
                {
                    content = "reviewed live editor replacement\r\n",
                    expected_sha256 = activeSha
                }));
        Require(
            activeProposal.PendingReview is not null
            && liveContent == "live editor content\r\n",
            "A proposed active-document replacement changed editor content before review.");
        await toolHost.ApproveAsync(activeProposal.PendingReview!);
        Require(
            liveContent == "reviewed live editor replacement\r\n",
            "The approved typed-provider editor replacement was not applied.");

        var terminalRead = await ExecuteToolAsync(
            Call("terminal-read", "read_active_terminal", new { }));
        using (var terminalOutput = JsonDocument.Parse(
                   terminalRead.FunctionOutput))
        {
            Require(
                terminalOutput.RootElement
                    .GetProperty("draft_command").GetString()
                    == terminalDraft
                && terminalOutput.RootElement
                    .GetProperty("transcript").GetArrayLength() == 3,
                "The approved live terminal draft and transcript were not exposed as a bounded snapshot.");
        }

        var terminalProposal = await ExecuteToolAsync(
            Call(
                "terminal-stage",
                "stage_terminal_command",
                new
                {
                    command = "Write-Output verifier"
                }));
        Require(
            terminalProposal.PendingReview is not null
            && terminalDispatchCount == 0,
            "A proposed terminal command dispatched before review.");
        await toolHost.ApproveAsync(terminalProposal.PendingReview!);
        Require(
            terminalDispatchCount == 1,
            "The reviewed terminal command was not dispatched.");

        var scriptedProvider = new ScriptedProvider(
            FunctionResponse(
                "coordinator-terminal",
                "execute_terminal_command",
                new
                {
                    command = "Write-Output should-not-run"
                }),
            TextResponse("The rejection was acknowledged."));
        var coordinator = new WorkspaceAiConversationCoordinator(
            scriptedProvider,
            configurationService,
            credentialStore,
            toolHost);
        var pendingTurn = await coordinator.SendAsync(
            WorkspaceId,
            "Propose a command but do not run it.");
        Require(
            pendingTurn.PendingReview is
            {
                ExecutesCommand: true,
                RequiresNativeConfirmation: true
            }
            && terminalDispatchCount == 1,
            "The conversation coordinator did not pause on a terminal execution proposal.");
        var rejectedTurn = await coordinator.RejectAsync();
        Require(
            rejectedTurn.AssistantText.Contains(
                "No mutation was performed",
                StringComparison.Ordinal)
            && scriptedProvider.SawFunctionOutput
            && terminalDispatchCount == 1,
            "Rejecting a coordinator review did not return a tool result or improperly dispatched the command.");

        var contextService = new WorkspaceAiContextService(
            Path.Combine(appDataRoot, "ai-contexts.device.json"));
        var fullAccess = new WorkspaceAiContextPathPermission
        {
            Mode = WorkspaceAiContextAccessMode.FullAccess
        };
        var desktopContext = contextService.SaveContext(
            new WorkspaceAiContextDefinition
            {
                Id = WorkspaceId,
                Name = "Provider desktop verifier",
                Read = fullAccess,
                Create = fullAccess,
                Edit = fullAccess,
                Delete = fullAccess,
                Terminal = fullAccess,
                Model = RetiredOperatorModelId,
                ReasoningEffort = "none",
                TextVerbosity = "low",
                DefaultSaveDirectory = workspaceRoot,
                AskBeforeChanges = false,
                ComputerControl = WorkspaceAiComputerControlMode.Desktop,
                RiskWarningAcknowledged = true
            });
        using (var computerControl =
               new WorkspaceAiComputerControlService(contextService))
        {
            var computerTarget = new StubComputerControlTarget();
            computerControl.Attach(computerTarget);
            var desktopProvider = new ScriptedProvider(
                FunctionResponse(
                    "local-desktop-screenshot",
                    WorkspaceAiComputerFunctionTool.FunctionName,
                    new
                    {
                        actions = new[]
                        {
                            new
                            {
                                type = "screenshot",
                                x = (double?)null,
                                y = (double?)null,
                                button = (string?)null,
                                scroll_x = (double?)null,
                                scroll_y = (double?)null,
                                text = (string?)null,
                                keys = Array.Empty<string>(),
                                path = Array.Empty<object>()
                            }
                        }
                    }),
                TextResponse("The local desktop screenshot was inspected."));
            var desktopCoordinator =
                new WorkspaceAiConversationCoordinator(
                    desktopProvider,
                    configurationService,
                    contextService,
                    credentialStore,
                    toolHost,
                    computerControl);
            desktopCoordinator.SetSessionModelSelection(
                new WorkspaceAiSessionModelSelection(
                    WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                    RetiredOperatorModelId));
            var desktopTurn = await desktopCoordinator.SendAsync(
                desktopContext.Id,
                "Inspect the full desktop.");
            Require(
                desktopTurn.AssistantText
                    == "The local desktop screenshot was inspected."
                && desktopProvider.Configurations[0].Model
                    == WorkspaceAiModelCatalog.EngineerModelId
                && desktopProvider.Configurations[0]
                    .RuntimeEnvironmentContext.Contains(
                        "[HYPERSHELL_RUNTIME_ENVIRONMENT]",
                        StringComparison.Ordinal)
                && desktopProvider.Configurations[0]
                    .RuntimeEnvironmentContext.Contains(
                        "windows_ui_automation=available",
                        StringComparison.Ordinal)
                && computerTarget.ActionTypes.SequenceEqual(["screenshot"])
                && desktopProvider.Inputs.Count == 2
                && desktopProvider.Inputs[1].Any(item =>
                    item.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "function_call_output")
                && desktopProvider.Inputs[1].Any(item =>
                    item.TryGetProperty("role", out var role)
                    && role.ValueKind == JsonValueKind.String
                    && role.GetString() == "user"
                    && item.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.Array
                    && content.EnumerateArray().Any(part =>
                        part.TryGetProperty("type", out var partType)
                        && partType.ValueKind == JsonValueKind.String
                        && partType.GetString() == "input_image"
                        && part.TryGetProperty(
                            "image_url",
                            out var imageUrl)
                        && imageUrl.ValueKind == JsonValueKind.String
                        && (imageUrl.GetString() ?? string.Empty).StartsWith(
                            "data:image/png;base64,",
                            StringComparison.Ordinal))),
                "The local desktop function did not execute through the Context-gated host and return a multimodal screenshot.");

            var windowsUiProvider = new ScriptedProvider(
                FunctionResponse(
                    "local-windows-ui-inspect",
                    WorkspaceAiWindowsUiFunctionTool.FunctionName,
                    new
                    {
                        operation = "inspect",
                        element_id = "None",
                        value = "None"
                    }),
                TextResponse("The semantic Windows controls were inspected."));
            var windowsUiCoordinator =
                new WorkspaceAiConversationCoordinator(
                    windowsUiProvider,
                    configurationService,
                    contextService,
                    credentialStore,
                    toolHost,
                    computerControl);
            windowsUiCoordinator.SetSessionModelSelection(
                new WorkspaceAiSessionModelSelection(
                    WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                    RetiredOperatorModelId));
            var windowsUiTurn = await windowsUiCoordinator.SendAsync(
                desktopContext.Id,
                "Inspect the named Windows controls.");
            Require(
                windowsUiTurn.AssistantText
                    == "The semantic Windows controls were inspected."
                && computerTarget.WindowsUiOperations
                    .SequenceEqual(["inspect"])
                && windowsUiProvider.Inputs.Count == 2
                && windowsUiProvider.Inputs[1].Any(item =>
                    item.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "function_call_output"
                    && item.TryGetProperty("output", out var output)
                    && output.ValueKind == JsonValueKind.String
                    && (output.GetString() ?? string.Empty).Contains(
                        "uia-verifier-1",
                        StringComparison.Ordinal)),
                "The semantic Windows UI call did not execute through the Context-gated target and return its bounded snapshot.");
        }

        var providerSwitch = new ScriptedProvider(
            TextResponse("Local provider answer."),
            TextResponse("OpenAI provider answer."));
        var switchingCoordinator = new WorkspaceAiConversationCoordinator(
            providerSwitch,
            configurationService,
            credentialStore,
            toolHost);
        switchingCoordinator.SetSessionModelSelection(
            new WorkspaceAiSessionModelSelection(
                WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                WorkspaceAiModelCatalog.EngineerModelId));
        var localProviderTurn = await switchingCoordinator.SendAsync(
            WorkspaceId,
            "Start this conversation locally.");
        Require(
            localProviderTurn.AssistantText == "Local provider answer.",
            "The provider-switch conversation did not start on local AI.");
        var deviceBeforeCloudSwitch = configurationService.Load();
        switchingCoordinator.SetSessionModelSelection(
            new WorkspaceAiSessionModelSelection(
                WorkspaceAiProviderConfiguration.OpenAiProviderId,
                deviceBeforeCloudSwitch.OpenAiModel));
        var cloudAfterSwitch = switchingCoordinator
            .SessionModelSelection
            .ApplyTo(deviceBeforeCloudSwitch);
        Require(
            WorkspaceAiModelCatalog.ResolveContextModel(
                cloudAfterSwitch,
                WorkspaceAiModelCatalog.EngineerModelId)
                == cloudAfterSwitch.Model,
            "An existing local Context model was not remapped to the active OpenAI model.");
        var cloudProviderTurn = await switchingCoordinator.SendAsync(
            WorkspaceId,
            "Continue this conversation with OpenAI.");
        Require(
            cloudProviderTurn.AssistantText == "OpenAI provider answer."
            && configurationService.Load() == deviceBeforeCloudSwitch
            && providerSwitch.Configurations.Count == 2
            && providerSwitch.Configurations[0].IsLocal
            && !providerSwitch.Configurations[1].IsLocal
            && providerSwitch.Inputs[1].Count == 3
            && providerSwitch.Inputs[1].All(item =>
                item.TryGetProperty("role", out var role)
                && role.ValueKind == JsonValueKind.String
                && role.GetString() is "user" or "assistant"
                && item.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String
                && item.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "message")
            && providerSwitch.Inputs[1][1]
                .GetProperty("content").GetString()
                == "Local provider answer.",
            "Switching an existing conversation replayed provider-specific items or lost its neutral transcript.");

        var legacyRestoreProvider = new ScriptedProvider(
            TextResponse("Legacy continuation restored."));
        var legacyRestoreCoordinator =
            new WorkspaceAiConversationCoordinator(
                legacyRestoreProvider,
                configurationService,
                credentialStore,
                toolHost);
        legacyRestoreCoordinator.RestoreContinuationContext(
            """
            [
              {
                "Role": "user",
                "Content": "Legacy saved prompt."
              },
              {
                "Role": "assistant",
                "Content": "Legacy saved answer."
              }
            ]
            """);
        var legacyRestoreTurn = await legacyRestoreCoordinator.SendAsync(
            WorkspaceId,
            "Continue the restored conversation.");
        Require(
            legacyRestoreTurn.AssistantText
                == "Legacy continuation restored."
            && legacyRestoreProvider.Inputs.Count == 1
            && legacyRestoreProvider.Inputs[0].Count == 3
            && legacyRestoreProvider.Inputs[0].All(item =>
                item.TryGetProperty("role", out var role)
                && role.ValueKind == JsonValueKind.String
                && item.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String
                && item.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "message"
                && !item.TryGetProperty("Role", out _)
                && !item.TryGetProperty("Content", out _)),
            "A legacy PascalCase AI continuation was not restored and normalized to the provider contract.");

        using (var restoreComputerControl =
               new WorkspaceAiComputerControlService(contextService))
        {
            var fallbackCoordinator =
                new WorkspaceAiConversationCoordinator(
                    new ScriptedProvider(),
                    configurationService,
                    contextService,
                    credentialStore,
                    toolHost,
                    restoreComputerControl);
            using var fallbackAssistant =
                new WorkspaceAiAssistantViewModel(
                    contextService,
                    configurationService,
                    credentialStore,
                    fallbackCoordinator,
                    restoreComputerControl,
                    new StubLocalRuntime());
            var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
            fallbackAssistant.RestoreConversation(
                new LocalAiConversationDocument
                {
                    Id = "conversation:legacy-fallback",
                    ProfileId = "profile:verifier",
                    Title = "Legacy fallback",
                    IsOpenOnThisDevice = true,
                    ProviderId = WorkspaceAiProviderConfiguration
                        .LlamaCppProviderId,
                    ModelId = RetiredOperatorModelId,
                    CompactedContext = "[{\"unexpected\":true}]",
                    Items =
                    [
                        new AiConversationItem
                        {
                            Sequence = 1,
                            Role = AiConversationRole.User,
                            Kind = AiConversationItemKind.Message,
                            Text = "Recover this visible message.",
                            CreatedAtUtc = timestamp
                        },
                        new AiConversationItem
                        {
                            Sequence = 2,
                            Role = AiConversationRole.Assistant,
                            Kind = AiConversationItemKind.Message,
                            Text = "Recovered answer.",
                            CreatedAtUtc = timestamp.AddSeconds(1)
                        }
                    ],
                    CreatedAtUtc = timestamp,
                    UpdatedAtUtc = timestamp.AddSeconds(1)
                });
            using var exportedContinuation = JsonDocument.Parse(
                fallbackAssistant.ExportContinuationContext());
            Require(
                fallbackAssistant.Messages.Count == 2
                && string.IsNullOrEmpty(fallbackAssistant.StatusMessage)
                && fallbackAssistant.SessionModelId
                    == WorkspaceAiModelCatalog.EngineerModelId
                && exportedContinuation.RootElement.GetArrayLength() == 2
                && exportedContinuation.RootElement.EnumerateArray().All(
                    item => item.TryGetProperty("role", out _)
                            && item.TryGetProperty("content", out _)
                            && !item.TryGetProperty("Role", out _)
                            && !item.TryGetProperty("Content", out _)),
                "A saved conversation did not restore its model choice, recover malformed continuation state, or export compatible casing.");
        }

        var composerConfigurationPath = Path.Combine(
            appDataRoot,
            "ai-provider-composer-recovery.device.json");
        var composerCredentialPath = Path.Combine(
            appDataRoot,
            "ai-provider-composer-recovery-credential.device.bin");
        var composerConfigurationService =
            new WorkspaceAiProviderConfigurationService(
                composerConfigurationPath);
        _ = composerConfigurationService.Save(
            new WorkspaceAiProviderConfiguration
            {
                ProviderId =
                    WorkspaceAiProviderConfiguration.OpenAiProviderId,
                Endpoint = WorkspaceAiProviderConfiguration
                    .OpenAiResponsesEndpoint,
                Model = "gpt-5.6",
                OpenAiModel = "gpt-5.6",
                LocalModel = WorkspaceAiModelCatalog.EngineerModelId,
                AllowCloudRequests = false,
                OpenAiCloudRequestsEnabled = false
            });
        var composerCredentialStore =
            new DpapiWorkspaceAiCredentialStore(composerCredentialPath);
        composerCredentialStore.Save("sk-composer-recovery-verifier");
        using (var composerComputerControl =
               new WorkspaceAiComputerControlService(contextService))
        {
            var composerCoordinator =
                new WorkspaceAiConversationCoordinator(
                    new ScriptedProvider(),
                    composerConfigurationService,
                    contextService,
                    composerCredentialStore,
                    toolHost,
                    composerComputerControl);
            composerCoordinator.SetSessionModelSelection(
                new WorkspaceAiSessionModelSelection(
                    WorkspaceAiProviderConfiguration.OpenAiProviderId,
                    "gpt-5.6"));
            using var composerAssistant =
                new WorkspaceAiAssistantViewModel(
                    contextService,
                    composerConfigurationService,
                    composerCredentialStore,
                    composerCoordinator,
                    composerComputerControl,
                    new StubLocalRuntime());
            composerAssistant.Prompt =
                "Explain why this send button is disabled.";
            Require(
                !composerAssistant.CanSend
                && composerAssistant.HasComposerBlocker
                && composerAssistant.ComposerBlockerMessage.Contains(
                    "cloud requests are off",
                    StringComparison.OrdinalIgnoreCase)
                && composerAssistant.CanEnableSelectedOpenAi
                && composerAssistant.EnableSelectedOpenAiCommand
                    .CanExecute(null),
                "A cloud-disabled OpenAI selection still looked like a dead composer without an actionable explanation.");

            composerAssistant.EnableSelectedOpenAiCommand.Execute(null);
            Require(
                composerAssistant.CanSend
                && !composerAssistant.HasComposerBlocker
                && composerConfigurationService.Load()
                    .AllowCloudRequests,
                "The AI pane could not recover a configured OpenAI provider in place.");

            var localOption = composerAssistant.ProviderModelOptions
                .First(option => option.IsLocal);
            composerAssistant.SelectedProviderModel = localOption;
            var localWithDisabledOpenAi =
                composerConfigurationService.Save(
                    composerConfigurationService.Load() with
                    {
                        AllowCloudRequests = false,
                        OpenAiCloudRequestsEnabled = false
                    });
            composerAssistant.RefreshState();
            var openAiOption = composerAssistant.ProviderModelOptions
                .Single(option => !option.IsLocal);
            composerAssistant.SelectedProviderModel = openAiOption;
            var unchangedByProviderChoice =
                composerConfigurationService.Load();
            Require(
                !localWithDisabledOpenAi.IsLocal
                && !localWithDisabledOpenAi
                    .OpenAiCloudRequestsEnabled
                && !unchangedByProviderChoice.AllowCloudRequests
                && !unchangedByProviderChoice
                    .OpenAiCloudRequestsEnabled
                && composerAssistant.CanEnableSelectedOpenAi
                && !composerAssistant.CanSend,
                "Choosing OpenAI in a conversation silently enabled cloud access or left no recovery action.");

            composerAssistant.EnableSelectedOpenAiCommand.Execute(null);
            Require(
                composerConfigurationService.Load()
                    .OpenAiCloudRequestsEnabled
                && composerAssistant.CanSend,
                "Explicitly enabling OpenAI access did not recover the selected conversation.");
        }

        using (var cancellationComputerControl =
               new WorkspaceAiComputerControlService(contextService))
        {
            var cancellationProvider = new CancellationAwareProvider();
            var cancellationCoordinator =
                new WorkspaceAiConversationCoordinator(
                    cancellationProvider,
                    configurationService,
                    contextService,
                    credentialStore,
                    toolHost,
                    cancellationComputerControl);
            using var cancellationAssistant =
                new WorkspaceAiAssistantViewModel(
                    contextService,
                    configurationService,
                    credentialStore,
                    cancellationCoordinator,
                    cancellationComputerControl,
                    new StubLocalRuntime());
            cancellationAssistant.Prompt =
                "Wait for the verifier to stop this work cycle.";
            Require(
                cancellationAssistant.SendCommand.CanExecute(null),
                "The AI assistant could not start the cancellation verifier request.");

            cancellationAssistant.SendCommand.Execute(null);
            await cancellationProvider.Started.WaitAsync(
                TimeSpan.FromSeconds(5));
            Require(
                cancellationAssistant.IsBusy
                && cancellationAssistant.CanStopWorkCycle
                && cancellationAssistant.StopWorkCycleCommand
                    .CanExecute(null),
                "An active AI work cycle did not expose an enabled Stop command.");

            cancellationAssistant.StopWorkCycleCommand.Execute(null);
            await cancellationProvider.CancellationObserved.WaitAsync(
                TimeSpan.FromSeconds(5));
            var cancellationWait = Stopwatch.StartNew();
            while (cancellationAssistant.IsBusy
                   && cancellationWait.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(10);
            }

            Require(
                !cancellationAssistant.IsBusy
                && !cancellationAssistant.CanStopWorkCycle
                && !cancellationAssistant.StopWorkCycleCommand
                    .CanExecute(null)
                && cancellationAssistant.StatusMessage
                    == "Stop requested for the current AI work cycle."
                && cancellationAssistant.Messages.All(message =>
                    !message.IsError),
                "Stopping an AI work cycle did not cancel cleanly and return the assistant to its idle state.");
        }

        broker.Approve(
            WorkspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Provider verifier root",
            allowTerminalRead: true,
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.ReviewOnly);
        var reviewOnlyProposal = await ExecuteToolAsync(
            Call(
                "terminal-review-only",
                "execute_terminal_command",
                new
                {
                    command = "Write-Output reviewed-once"
                }));
        Require(
            reviewOnlyProposal.PendingReview is
            {
                ExecutesCommand: true,
                RequiresNativeConfirmation: false
            }
            && terminalDispatchCount == 1,
            "The review-only terminal policy did not pause exactly once.");
        await toolHost.ApproveAsync(
            reviewOnlyProposal.PendingReview!,
            userConfirmedExecution: false);
        Require(
            terminalDispatchCount == 2
            && terminalExecutionCount == 1
            && terminalConfirmedExecutionCount == 0,
            "The reviewed-once terminal command did not dispatch without a second confirmation.");

        broker.Approve(
            WorkspaceId,
            workspaceRoot,
            allowRead: true,
            allowWrite: true,
            allowTerminal: true,
            displayName: "Provider verifier root",
            allowTerminalRead: true,
            terminalExecutionPolicy:
                WorkspaceAiTerminalExecutionPolicy.AutoExecute,
            allowAutomaticFileAndEditorChanges: true);
        var autonomousOutcome = await ExecuteToolAsync(
            Call(
                "terminal-auto-execute",
                "execute_terminal_command",
                new
                {
                    command = "Write-Output autonomous"
                }));
        using var autonomousOutput = JsonDocument.Parse(
            autonomousOutcome.FunctionOutput);
        Require(
            autonomousOutcome.PendingReview is null
            && terminalDispatchCount == 3
            && terminalExecutionCount == 2
            && terminalConfirmedExecutionCount == 0
            && autonomousOutput.RootElement
                .GetProperty("terminal_output")
                .EnumerateArray()
                .Any(line => line.GetProperty("text").GetString()
                    == "Synthetic terminal command completed."),
            "Standing terminal permission did not bypass per-command review or return completed terminal evidence.");

        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "package.json"),
            """
            { "scripts": { "build": "node --check app.js" } }
            """,
            new UTF8Encoding(false));
        var projectCheckOutcome = await ExecuteToolAsync(
            Call(
                "semantic-project-check",
                "run_project_check",
                new
                {
                    check = "build"
                }));
        using (var projectCheckOutput = JsonDocument.Parse(
                   projectCheckOutcome.FunctionOutput))
        {
            Require(
                projectCheckOutcome.PendingReview is null
                && terminalDispatchCount == 4
                && terminalExecutionCount == 3
                && projectCheckOutput.RootElement
                    .GetProperty("project_check")
                    .GetProperty("ProjectKind").GetString() == "node"
                && projectCheckOutput.RootElement
                    .GetProperty("project_check")
                    .GetProperty("Command").GetString()
                    == "npm run 'build'"
                && projectCheckOutput.RootElement
                    .GetProperty("terminal_output")
                    .EnumerateArray()
                    .Any(line => line.GetProperty("text").GetString()
                        == "Synthetic terminal command completed."),
                "The semantic project-check tool did not detect package.json, obey auto-execution policy, or return completed output.");
        }

        var automaticDiskRead = await operations.ReadTextAsync(
            WorkspaceId,
            "notes.txt");
        Require(
            automaticDiskRead.Succeeded
            && automaticDiskRead.Value is not null,
            "The automatic-change verifier could not read its disk revision.");
        var automaticDiskChange = await ExecuteToolAsync(
            Call(
                "disk-auto-apply",
                "propose_workspace_write",
                new
                {
                    relative_path = "notes.txt",
                    content = "automatically applied replacement\r\n",
                    expected_sha256 = automaticDiskRead.Value!.Sha256,
                    allow_create = false
                }));
        Require(
            automaticDiskChange.PendingReview is null
            && await File.ReadAllTextAsync(documentPath)
                == "automatically applied replacement\r\n",
            "Standing file-change permission did not bypass review or preserve the atomic write path.");

        var generatedScriptPath = Path.Combine(
            generatedDirectory,
            "app.js");
        var generatedScript = string.Join(
            '\n',
            "const state = { lesson: \"JSON is a transport here\", complete: false };",
            "document.querySelector(\"#next\")?.addEventListener(\"click\", () => {",
            "  state.complete = true;",
            "});") + "\n";
        var automaticSourceProvider = new ScriptedProvider(
            FunctionResponse(
                "coordinator-auto-source-transfer",
                "prepare_workspace_write",
                new
                {
                    relative_path = "generated/app.js",
                    expected_sha256 = (string?)null,
                    allow_create = true
                }),
            TextResponse(
                "Here is the JavaScript source you requested:"),
            TextResponse(
                $"{WorkspaceAiToolHost.SourceTransferBeginMarker}\n{generatedScript}{WorkspaceAiToolHost.SourceTransferEndMarker}"),
            TextResponse(
                "Created and verified the JavaScript source file."));
        var automaticSourceCoordinator =
            new WorkspaceAiConversationCoordinator(
                automaticSourceProvider,
                configurationService,
                credentialStore,
                toolHost);
        automaticSourceCoordinator.SetWorkingDirectory(workspaceRoot);
        var automaticSourceTurn = await automaticSourceCoordinator.SendAsync(
            WorkspaceId,
            "Create generated/app.js without asking again.");
        Require(
            automaticSourceTurn.PendingReview is null
            && automaticSourceTurn.AssistantText.Contains(
                "Created and verified",
                StringComparison.Ordinal)
            && await File.ReadAllTextAsync(generatedScriptPath)
                == generatedScript
            && automaticSourceProvider.Inputs.Count == 4
            && automaticSourceProvider.Inputs[2].Last()
                .GetProperty("content").GetString()!.Contains(
                    "previous transfer response",
                    StringComparison.Ordinal),
            "The prepared source-write path did not correct one malformed envelope and auto-apply the exact source under standing permission.");

        var automaticActiveRead = await ExecuteToolAsync(
            Call("active-auto-read", "read_active_document", new { }));
        string automaticActiveSha;
        using (var activeOutput = JsonDocument.Parse(
                   automaticActiveRead.FunctionOutput))
        {
            automaticActiveSha = activeOutput.RootElement
                .GetProperty("sha256")
                .GetString()!;
        }

        var automaticActiveChange = await ExecuteToolAsync(
            Call(
                "active-auto-apply",
                "propose_active_document_replace",
                new
                {
                    content = "automatically applied editor replacement\r\n",
                    expected_sha256 = automaticActiveSha
                }));
        Require(
            automaticActiveChange.PendingReview is null
            && liveContent
                == "automatically applied editor replacement\r\n",
            "Standing editor-change permission did not bypass review or preserve the typed-provider path.");

        credentialStore.Delete();
        Require(
            !credentialStore.Exists,
            "The device-protected AI credential could not be removed.");
        Console.WriteLine(
            "WORKSPACE_AI_PROVIDER_OK|openai+llama.cpp|settings-access-only|engineer-only-conversation-model|conversation-model-isolation|conversation-model-restore|friendly-pane-models|composer-recovery|explicit-openai-enable|crash-safe-local-process|pane-model-switch|cancelable-work-cycle|chat-presentation-roles|neutral-provider-handoff|legacy-continuation-restore|catalog:2|store:false|host-tools:23|visual-page-tools:6|terminal-tools:execute+stage+project-check|prepared-source-transfer|prepared-hyperpage-markup|truncated-hyperpage-recovery|hyperpage-direct-tool-enforcement|exact-edit-transfer|repository-search|source-envelope-retry|engineer-desktop-routing|runtime-environment-card|low-latency-action-loop|semantic-windows-ui|local-desktop-function|multimodal-screenshot|context-gated-computer-control|semantic-hyperlib|semantic-git-tools|live-terminal-snapshot|auto-file-editor|terminal-policies:3|dpapi:ok");
    }

    private static void VerifyCrashSafeProcessBoundary()
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException(
                             "The verifier executable path is unavailable.");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--fake-language-server");
        Require(
            process.Start(),
            "The verifier could not start its synthetic child process.");

        try
        {
            using (var job = WindowsProcessLifetimeJob.Create())
            {
                job.Assign(process);
            }

            Require(
                process.WaitForExit(5000),
                "Closing the crash-safe process boundary did not stop its child process.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }

    public static async Task VerifyLocalRuntimeAsync()
    {
        using var runtime = new WorkspaceAiLocalRuntimeService();
        Require(
            runtime.IsInstalled,
            $"llama.cpp {WorkspaceAiLocalRuntimeService.ExpectedLlamaCppVersion} is not installed.");
        using var provider = new ResponsesWorkspaceAiProvider(
            localRuntime: runtime);
        var configuration =
            WorkspaceAiProviderConfigurationService.Normalize(new()
            {
                ProviderId =
                    WorkspaceAiProviderConfiguration.LlamaCppProviderId,
                Endpoint = WorkspaceAiProviderConfiguration
                    .LlamaCppResponsesEndpoint,
                Model = WorkspaceAiProviderConfiguration.DefaultLocalModel,
                ReasoningEffort = "none",
                TextVerbosity = "low",
                ComputerControl = WorkspaceAiComputerControlMode.Desktop
            });
        var result = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content =
                    "Reply with exactly HYPERSHELL_LOCAL_READY and do not call a tool."
            })]);
        Require(
            result.FunctionCalls.Count == 0
            && result.ComputerCalls.Count == 0
            && string.Equals(
                result.AssistantText.Trim(),
                "HYPERSHELL_LOCAL_READY",
            StringComparison.Ordinal),
            $"The managed local model returned an unexpected result: {result.AssistantText}");
        var toolResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content =
                    "Call search_hyperlib exactly once with query \"copy a file\" and limit 3. Do not answer with text."
            })]);
        Require(
            toolResult.FunctionCalls.Count == 1
            && toolResult.FunctionCalls[0].Name == "search_hyperlib",
            "Qwen Engineer did not emit the requested strict Hyperlib tool call.");
        using (var arguments = JsonDocument.Parse(
                   toolResult.FunctionCalls[0].Arguments))
        {
            Require(
                arguments.RootElement.GetProperty("query").GetString()
                    == "copy a file"
                && arguments.RootElement.GetProperty("limit").GetInt32() == 3,
                "Qwen Engineer emitted malformed bounded Hyperlib tool arguments.");
        }

        var sourcePrepareResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content =
                    "Call prepare_workspace_write exactly once for a new file named index.html. Set expected_sha256 to null and allow_create to true. Do not include any HTML yet and do not answer with text."
            })]);
        Require(
            sourcePrepareResult.FunctionCalls.Count == 1
            && sourcePrepareResult.FunctionCalls[0].Name
                == "prepare_workspace_write",
            "Qwen Engineer did not select the prepared source-write tool for a new HTML file.");
        using (var arguments = JsonDocument.Parse(
                   sourcePrepareResult.FunctionCalls[0].Arguments))
        {
            var expectedSha = arguments.RootElement.GetProperty(
                "expected_sha256");
            Require(
                arguments.RootElement.GetProperty("relative_path")
                    .GetString() == "index.html"
                && (expectedSha.ValueKind == JsonValueKind.Null
                    || expectedSha.ValueKind == JsonValueKind.String
                    && string.Equals(
                        expectedSha.GetString(),
                        "null",
                        StringComparison.OrdinalIgnoreCase))
                && arguments.RootElement.GetProperty("allow_create")
                    .GetBoolean()
                && !sourcePrepareResult.FunctionCalls[0].Arguments.Contains(
                    "DOCTYPE",
                    StringComparison.OrdinalIgnoreCase),
                $"Qwen Engineer emitted unsafe prepared-write arguments: {sourcePrepareResult.FunctionCalls[0].Arguments}");
        }

        var visualMarkupPrepareResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content =
                    "Call prepare_visual_page_markup exactly once. Set expected_revision to revision-1, parent_element_id to page-root, markup_language to html, and reason to Build a static portfolio landing page. Do not include markup yet and do not answer with text."
            })]);
        Require(
            visualMarkupPrepareResult.FunctionCalls.Count == 1
            && visualMarkupPrepareResult.FunctionCalls[0].Name
                == "prepare_visual_page_markup",
            "Qwen Engineer did not select the prepared Hyperpage markup tool for a full landing page.");
        using (var arguments = JsonDocument.Parse(
                   visualMarkupPrepareResult.FunctionCalls[0].Arguments))
        {
            Require(
                arguments.RootElement.GetProperty("expected_revision")
                    .GetString() == "revision-1"
                && arguments.RootElement.GetProperty("parent_element_id")
                    .GetString() == "page-root"
                && arguments.RootElement.GetProperty("markup_language")
                    .GetString() == "html"
                && arguments.RootElement.GetProperty("reason").GetString()!
                    .Contains("portfolio", StringComparison.OrdinalIgnoreCase)
                && !visualMarkupPrepareResult.FunctionCalls[0].Arguments
                    .Contains("<section", StringComparison.OrdinalIgnoreCase),
                "Qwen Engineer mixed landing-page markup into the small Hyperpage preflight arguments.");
        }

        var sourceEnvelopeResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [
                JsonSerializer.SerializeToElement(new
                {
                    role = "user",
                    content =
                        "Create a tiny index.html containing an h1 whose text is Engineer ready."
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "live-source-transfer",
                    name = "prepare_workspace_write",
                    arguments =
                        "{\"relative_path\":\"index.html\",\"expected_sha256\":null,\"allow_create\":true}"
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call_output",
                    call_id = "live-source-transfer",
                    output = JsonSerializer.Serialize(new
                    {
                        ok = true,
                        source_transfer = WorkspaceAiToolHost
                            .SourceTransferRequestMarker,
                        message =
                            $"Return the exact HTML between {WorkspaceAiToolHost.SourceTransferBeginMarker} and {WorkspaceAiToolHost.SourceTransferEndMarker}."
                    })
                })
            ]);
        Require(
            sourceEnvelopeResult.FunctionCalls.Count == 0
            && sourceEnvelopeResult.ComputerCalls.Count == 0
            && sourceEnvelopeResult.AssistantText.Contains(
                WorkspaceAiToolHost.SourceTransferBeginMarker,
                StringComparison.Ordinal)
            && sourceEnvelopeResult.AssistantText.Contains(
                WorkspaceAiToolHost.SourceTransferEndMarker,
                StringComparison.Ordinal)
            && sourceEnvelopeResult.AssistantText.Contains(
                "<h1",
                StringComparison.OrdinalIgnoreCase)
            && sourceEnvelopeResult.AssistantText.Contains(
                "Engineer ready",
                StringComparison.Ordinal),
            "Qwen Engineer did not return raw HTML through the no-tools source envelope.");

        var visualMarkupResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [
                JsonSerializer.SerializeToElement(new
                {
                    role = "user",
                    content =
                        "Create a compact dark portfolio fragment with a scoped style element, hero section, projects section, and contact section."
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call",
                    call_id = "live-visual-markup-transfer",
                    name = "prepare_visual_page_markup",
                    arguments =
                        "{\"expected_revision\":\"revision-1\",\"parent_element_id\":\"page-root\",\"markup_language\":\"html\",\"reason\":\"Build a static portfolio landing page\"}"
                }),
                JsonSerializer.SerializeToElement(new
                {
                    type = "function_call_output",
                    call_id = "live-visual-markup-transfer",
                    output = JsonSerializer.Serialize(new
                    {
                        ok = true,
                        visual_page_markup_transfer = WorkspaceAiToolHost
                            .VisualPageMarkupTransferRequestMarker,
                        message =
                            "Return only one complete HTML body fragment with a scoped style element."
                    })
                })
            ]);
        Require(
            visualMarkupResult.FunctionCalls.Count == 0
            && visualMarkupResult.ComputerCalls.Count == 0
            && visualMarkupResult.AssistantText.Contains(
                "<style",
                StringComparison.OrdinalIgnoreCase)
            && visualMarkupResult.AssistantText.Contains(
                "<section",
                StringComparison.OrdinalIgnoreCase)
            && visualMarkupResult.AssistantText.Contains(
                "portfolio",
                StringComparison.OrdinalIgnoreCase),
            "Qwen Engineer did not return a styled static landing-page fragment through the no-tools Hyperpage transfer.");

        var windowsUiToolResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [JsonSerializer.SerializeToElement(new
            {
                role = "user",
                content =
                    "Call control_windows_ui exactly once with operation inspect. Set element_id and value to null. Do not answer with text."
            })]);
        Require(
            windowsUiToolResult.FunctionCalls.Count == 1
            && windowsUiToolResult.FunctionCalls[0].Name
                == WorkspaceAiWindowsUiFunctionTool.FunctionName,
            "Qwen Engineer did not emit the requested semantic Windows UI inspection call.");
        var parsedWindowsUiCall = WorkspaceAiWindowsUiFunctionTool.Parse(
            windowsUiToolResult.FunctionCalls[0]);
        Require(
            parsedWindowsUiCall.Operation == "inspect"
            && parsedWindowsUiCall.ElementId is null
            && parsedWindowsUiCall.Value is null,
            "Qwen Engineer emitted malformed semantic Windows UI arguments.");

        var desktopPrompt = JsonSerializer.SerializeToElement(new
        {
            role = "user",
            content =
                "Call control_computer exactly once with one screenshot action. Set x, y, button, scroll_x, scroll_y, and text to null. Set keys and path to empty arrays. Do not answer with text."
        });
        var desktopToolResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            [desktopPrompt]);
        Require(
            desktopToolResult.FunctionCalls.Count == 1
            && desktopToolResult.FunctionCalls[0].Name
                == WorkspaceAiComputerFunctionTool.FunctionName,
            "Qwen Engineer did not emit the requested local desktop screenshot call.");
        var parsedDesktopCall = WorkspaceAiComputerFunctionTool.Parse(
            desktopToolResult.FunctionCalls[0]);
        Require(
            parsedDesktopCall.Actions.Count == 1
            && parsedDesktopCall.Actions[0].Type == "screenshot",
            "Qwen Engineer emitted malformed local desktop screenshot arguments.");

        var continuationInput = new List<JsonElement> { desktopPrompt };
        continuationInput.AddRange(
            desktopToolResult.OutputItems.Select(item => item.Clone()));
        continuationInput.Add(JsonSerializer.SerializeToElement(new
        {
            type = "function_call_output",
            call_id = desktopToolResult.FunctionCalls[0].CallId,
            output =
                "{\"ok\":true,\"screenshot_width\":64,\"screenshot_height\":64}"
        }));
        const string syntheticScreenshot =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAACMSURBVHhe5cgxAQAwDMCgqq3aetoERAIHDzN7j5bQJDQJTUKT0CQ0CU1Ck9AkNAlNQpPQJDQJTUKT0CQ0CU1Ck9AkNAlNQpPQJDQJTUKT0CQ0CU1Ck9AkNAlNQpPQJDQJTUKT0CQ0CU1Ck9AkNAlNQpPQJDQJTUKT0CQ0CU1Ck9AkNAlNQpPQJDQJzAcqRxIO1bJyKwAAAABJRU5ErkJggg==";
        continuationInput.Add(JsonSerializer.SerializeToElement(new
        {
            role = "user",
            content = new object[]
            {
                new
                {
                    type = "input_text",
                    text =
                        $"{WorkspaceAiComputerFunctionTool.ScreenshotMarker} This synthetic verifier screenshot is filled with one color. Reply with exactly that color's common name in uppercase and do not call a tool."
                },
                new
                {
                    type = "input_image",
                    image_url =
                        $"data:image/png;base64,{syntheticScreenshot}",
                    detail = "original"
                }
            }
        }));
        var visionResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            continuationInput);
        Require(
            visionResult.FunctionCalls.Count == 0
            && string.Equals(
                visionResult.AssistantText.Trim(),
                "BLUE",
                StringComparison.OrdinalIgnoreCase),
            $"Qwen Engineer did not inspect the post-tool screenshot correctly: {visionResult.AssistantText}");

        var cursorContinuation = continuationInput
            .Take(continuationInput.Count - 1)
            .Select(item => item.Clone())
            .ToList();
        cursorContinuation.Add(JsonSerializer.SerializeToElement(new
        {
            role = "user",
            content = new object[]
            {
                new
                {
                    type = "input_text",
                    text =
                        $"{WorkspaceAiComputerFunctionTool.ScreenshotMarker} This screenshot is 64x64 pixels. Call control_computer exactly once with one move action that places the cursor at the center of the screenshot. Set button, scroll_x, scroll_y, and text to null. Set keys and path to empty arrays. Do not answer with text."
                },
                new
                {
                    type = "input_image",
                    image_url =
                        $"data:image/png;base64,{syntheticScreenshot}",
                    detail = "original"
                }
            }
        }));
        var cursorResult = await provider.CreateResponseAsync(
            configuration,
            string.Empty,
            cursorContinuation);
        Require(
            cursorResult.FunctionCalls.Count == 1
            && cursorResult.FunctionCalls[0].Name
                == WorkspaceAiComputerFunctionTool.FunctionName,
            "Qwen Engineer did not emit a cursor action after inspecting the host screenshot.");
        var parsedCursorCall = WorkspaceAiComputerFunctionTool.Parse(
            cursorResult.FunctionCalls[0]);
        var cursorPayload = parsedCursorCall.Actions.Single().Payload;
        Require(
            parsedCursorCall.Actions[0].Type == "move"
            && Math.Abs(cursorPayload.GetProperty("x").GetDouble() - 32) <= 4
            && Math.Abs(cursorPayload.GetProperty("y").GetDouble() - 32) <= 4,
            "Qwen Engineer did not ground the cursor action in the screenshot coordinate space.");

        Console.WriteLine(
            "WORKSPACE_AI_LOCAL_RUNTIME_OK|llama.cpp:b10448|qwen3.5-9b:q4_k_m|vision:mmproj-f16|responses|auth|host-tools:23|visual-page-tools:6|terminal-tools:execute+stage+project-check|prepared-source-transfer|prepared-hyperpage-markup|exact-edit-transfer|repository-search|desktop-tools:control_windows_ui+control_computer|post-tool-image:blue|visual-coordinate:move|search-hyperlib");
    }

    public static async Task VerifyEmbeddingRuntimeAsync()
    {
        using var embeddings = new WorkspaceAiEmbeddingService();
        Require(
            embeddings.IsInstalled,
            $"llama.cpp {WorkspaceAiLocalRuntimeService.ExpectedLlamaCppVersion} is not installed.");
        var vectors = await embeddings.EmbedAsync(
            [
                "Instruct: Given a user question, retrieve relevant Hypershell Hyperlib commands and programming references\nQuery: copy a file",
                "Copy-Item copies files and directories from one location to another.",
                "Get-Process lists the processes running on a computer."
            ]);
        var copySimilarity = Cosine(vectors[0], vectors[1]);
        var processSimilarity = Cosine(vectors[0], vectors[2]);
        Require(
            vectors.Count == 3
            && vectors.All(vector => vector.Length
                == WorkspaceAiEmbeddingService.EmbeddingDimension)
            && copySimilarity > processSimilarity + 0.15,
            "The managed Librarian did not return normalized, relevance-sensitive embeddings.");
        Console.WriteLine(
            $"WORKSPACE_AI_EMBEDDING_OK|llama.cpp:b10448|qwen3-embedding-0.6b:q8_0|dimensions:1024|copy:{copySimilarity:F6}|process:{processSimilarity:F6}|auth");
    }

    public static async Task VerifyHyperlibSemanticAsync(string verifierRoot)
    {
        var settings = new SettingsService(Path.Combine(
            verifierRoot,
            "app-data"));
        var catalog = new CommandCatalogService(settings);
        using var embeddings = new WorkspaceAiEmbeddingService();
        var search = new HyperlibSemanticSearchService(catalog, embeddings);
        var results = await search.SearchAsync(
            "Which PowerShell command copies a file to another folder?",
            10);
        var copyItem = results.FirstOrDefault(result => string.Equals(
            result.Name,
            "Copy-Item",
            StringComparison.OrdinalIgnoreCase));
        if (copyItem is null)
        {
            throw new InvalidOperationException(
                "The real Librarian pipeline did not retrieve Copy-Item. Returned: "
                + string.Join(
                    " | ",
                    results.Select(result =>
                        $"{result.Name} [{result.LibraryName}] {result.Score:F4}")));
        }

        Require(
            !string.IsNullOrWhiteSpace(copyItem.LibraryName)
            && !string.IsNullOrWhiteSpace(copyItem.ReviewState),
            "The real Librarian pipeline did not retrieve Copy-Item with catalog provenance.");
        Console.WriteLine(
            $"HYPERLIB_SEMANTIC_SEARCH_OK|query:copy-file|result:{copyItem.Name}|library:{copyItem.LibraryId}|rank:{Array.IndexOf(results.ToArray(), copyItem) + 1}|provenance|review-state");
    }

    private static WorkspaceAiFunctionCall Call(
        string callId,
        string name,
        object arguments) =>
        new(callId, name, JsonSerializer.Serialize(arguments));

    private static WorkspaceAiProviderResponse FunctionResponse(
        string callId,
        string name,
        object arguments)
    {
        var argumentsJson = JsonSerializer.Serialize(arguments);
        return new WorkspaceAiProviderResponse(
            [JsonSerializer.SerializeToElement(new
            {
                type = "function_call",
                call_id = callId,
                name,
                arguments = argumentsJson
            })],
            string.Empty,
             [new WorkspaceAiFunctionCall(
                 callId,
                 name,
                 argumentsJson)],
             []);
    }

    private static WorkspaceAiProviderResponse TextResponse(string text) =>
        new([], text, [], []);

    private static double Cosine(float[] left, float[] right)
    {
        var dot = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
        }

        return dot;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public string RequestBody { get; private set; } = string.Empty;
        public string Authorization { get; private set; } = string.Empty;
        public string RequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            Authorization = request.Headers.Authorization?.ToString()
                            ?? string.Empty;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responseJson,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)>
            _responses;

        public SequencedHandler(
            params (HttpStatusCode Status, string Body)[] responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "The sequenced HTTP verifier ran out of responses.");
            }

            var next = _responses.Dequeue();
            return new HttpResponseMessage(next.Status)
            {
                Content = new StringContent(
                    next.Body,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class ScriptedProvider : IWorkspaceAiProvider
    {
        private readonly Queue<WorkspaceAiProviderResponse> _responses;

        public ScriptedProvider(
            params WorkspaceAiProviderResponse[] responses)
        {
            _responses = new Queue<WorkspaceAiProviderResponse>(responses);
        }

        public bool SawFunctionOutput { get; private set; }

        public List<WorkspaceAiProviderConfiguration> Configurations
        { get; } = [];

        public List<IReadOnlyList<JsonElement>> Inputs { get; } = [];

        public Task<WorkspaceAiProviderResponse> CreateResponseAsync(
            WorkspaceAiProviderConfiguration configuration,
            string apiKey,
            IReadOnlyList<JsonElement> input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Configurations.Add(configuration);
            Inputs.Add(input.Select(item => item.Clone()).ToArray());
            SawFunctionOutput |= input.Any(item =>
                item.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "function_call_output");
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "The scripted provider ran out of responses.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class CancellationAwareProvider : IWorkspaceAiProvider
    {
        private readonly TaskCompletionSource<bool> _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public Task CancellationObserved => _cancellationObserved.Task;

        public async Task<WorkspaceAiProviderResponse> CreateResponseAsync(
            WorkspaceAiProviderConfiguration configuration,
            string apiKey,
            IReadOnlyList<JsonElement> input,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _cancellationObserved.TrySetResult(true);
                throw;
            }

            throw new InvalidOperationException(
                "The cancellation-aware provider completed unexpectedly.");
        }
    }

    private sealed class StubComputerControlTarget
        : IWorkspaceAiComputerControlTarget,
          IWorkspaceAiWindowsUiControlTarget
    {
        private static readonly byte[] Screenshot = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII=");

        public WorkspaceAiComputerControlMode Mode =>
            WorkspaceAiComputerControlMode.Desktop;

        public List<string> ActionTypes { get; } = [];

        public List<string> WindowsUiOperations { get; } = [];

        public void ResetWindowsUiSnapshot()
        {
        }

        public Task ExecuteActionsAsync(
            IReadOnlyList<WorkspaceAiComputerAction> actions,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActionTypes.AddRange(actions.Select(action => action.Type));
            return Task.CompletedTask;
        }

        public Task<byte[]> CaptureScreenshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Screenshot.ToArray());
        }

        public Task<JsonElement> ExecuteWindowsUiAsync(
            WorkspaceAiWindowsUiCall call,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WindowsUiOperations.Add(call.Operation);
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                available = true,
                host_marker = WorkspaceAiWindowsUiFunctionTool.SnapshotMarker,
                scope = "Desktop",
                snapshot_id = "uia-verifier-1",
                ids_valid_until_next_result = true,
                element_count = 0,
                truncated = false,
                elements = Array.Empty<object>()
            }));
        }
    }

    private sealed class StubLocalRuntime : IWorkspaceAiLocalRuntime
    {
        public bool IsInstalled => true;

        public string AuthorizationToken => "verifier-local-runtime-token";

        public Task EnsureReadyAsync(
            string modelId,
            bool requireVision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class StubHyperlibSearch
        : IHyperlibSemanticSearchService
    {
        public string LastQuery { get; private set; } = string.Empty;

        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<HyperlibSemanticSearchResult>> SearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastQuery = query;
            LastLimit = limit;
            IReadOnlyList<HyperlibSemanticSearchResult> result =
            [
                new(
                    "powershell:copy-item",
                    "powershell",
                    "PowerShell",
                    "Copy-Item",
                    "Copies files and directories.",
                    "Copy-Item -Path <source> -Destination <target>",
                    "PowerShell",
                    "PowerShell",
                    "Verified",
                    "Bundled",
                    "Hypershell bundled catalog",
                    "HumanAuthored",
                    "Reviewed",
                    0.93)
            ];
            return Task.FromResult(result);
        }
    }
}
