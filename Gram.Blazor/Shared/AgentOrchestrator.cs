using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public sealed class AgentStepUpdate
{
    public string? ContentDelta { get; set; }
    public string? ThinkingDelta { get; set; }
    public ToolCallInfo? ToolCall { get; set; }
    public ToolExecutionResult? ToolResult { get; set; }
    public bool IsComplete { get; set; }
    public string? ErrorMessage { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public long? LatencyMs { get; set; }
}

public sealed class AgentOrchestrator
{
    private readonly ModelService _modelService;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly SettingsService _settingsService;
    private readonly GitService _gitService;
    private readonly HttpClient _httpClient = new();

    public AgentOrchestrator(
        ModelService modelService,
        IAgentToolRegistry toolRegistry,
        SettingsService settingsService,
        GitService gitService)
    {
        _modelService = modelService;
        _toolRegistry = toolRegistry;
        _settingsService = settingsService;
        _gitService = gitService;
    }

    public async IAsyncEnumerable<AgentStepUpdate> RunAgentLoopAsync(
        string modelId,
        List<Message> history,
        string workspacePath,
        string? systemPrompt = null,
        int maxIterations = 8,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var isLocal = _settingsService.IsFeatureSupported("localFileStorage");
        var activeGit = _gitService.Accounts.FirstOrDefault(a => a.IsDefault) ?? _gitService.Accounts.FirstOrDefault();
        var tools = _toolRegistry.GetAvailableTools(isLocal, activeGit);

        // Build system prompt with workspace and tools guidance
        var fullSystemPrompt = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            fullSystemPrompt.AppendLine(systemPrompt);
        }
        else
        {
            fullSystemPrompt.AppendLine("You are Gram, an expert AI software engineer and autonomous agent.");
        }

        fullSystemPrompt.AppendLine("\n## Environment, Tools & Capabilities");
        fullSystemPrompt.AppendLine($"- Workspace Directory: {workspacePath}");
        fullSystemPrompt.AppendLine($"- Environment Type: {(isLocal ? "Desktop / Self-Hosted (Full Filesystem & Terminal Execution)" : "Web (Remote Git & Web API Tools)")}");
        fullSystemPrompt.AppendLine("- **Web Search (`search_web`) & Web Fetch (`fetch_web_content`)**: Use `search_web` to look up live documentation, latest library releases, API methods, and online solutions.");
        fullSystemPrompt.AppendLine("- **Gram Toolbox (`toolbox_list_tools`, `toolbox_execute_tool`)**: You have full access to modular sandbox engines, Coder CDE bridges, Tree-Sitter AST syntax parsers, and Language Server (LSP) diagnostics.");
        fullSystemPrompt.AppendLine("- Autonomously pick and use the right tools to thoroughly solve the user's task.");

        // Internal working messages for the multi-turn loop
        var loopMessages = new List<JsonObject>();

        // System message
        loopMessages.Add(new JsonObject
        {
            ["role"] = "system",
            ["content"] = fullSystemPrompt.ToString()
        });

        // Add chat history
        foreach (var msg in history)
        {
            loopMessages.Add(new JsonObject
            {
                ["role"] = msg.Role == "user" ? "user" : (msg.Role == "assistant" ? "assistant" : "system"),
                ["content"] = msg.Text
            });
        }

        var iteration = 0;
        while (iteration < maxIterations && !cancellationToken.IsCancellationRequested)
        {
            iteration++;

            var requestPayload = new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = new JsonArray(loopMessages.Select(m => m.DeepClone()).ToArray()),
                ["temperature"] = 0.2
            };

            if (tools.Count > 0)
            {
                var toolsArray = new JsonArray();
                foreach (var t in tools)
                {
                    toolsArray.Add(t.ToOpenAiFunctionSchema());
                }
                requestPayload["tools"] = toolsArray;
                requestPayload["tool_choice"] = "auto";
            }

            // Resolve provider endpoint and API key
            var model = _modelService.Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                        ?? new GramModel { Id = modelId, Provider = "OpenRouter" };
            var providerId = model.Provider.ToLowerInvariant();
            var provider = _modelService.Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(model.Provider, StringComparison.OrdinalIgnoreCase));
            var apiKey = _modelService.GetApiKey(provider?.Id ?? "openrouter");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = _modelService.GetApiKey("openrouter");
            }

            // Check for missing API key for cloud providers
            if (string.IsNullOrWhiteSpace(apiKey) && providerId != "ollama")
            {
                yield return new AgentStepUpdate
                {
                    IsComplete = true,
                    ErrorMessage = $"API key not configured for {(provider?.Name ?? model.Provider)}. Please open Settings > Intelligence > Providers to enter your API key."
                };
                yield break;
            }

            var endpoint = providerId switch
            {
                "openai" => $"{provider?.BaseUrl ?? "https://api.openai.com/v1"}/chat/completions",
                "xai" => $"{provider?.BaseUrl ?? "https://api.x.ai/v1"}/chat/completions",
                "deepseek" => $"{provider?.BaseUrl ?? "https://api.deepseek.com/v1"}/chat/completions",
                "ollama" => $"{provider?.BaseUrl ?? "http://localhost:11434"}/v1/chat/completions",
                _ => $"{provider?.BaseUrl ?? "https://openrouter.ai/api/v1"}/chat/completions"
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey) && providerId != "ollama")
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            if (providerId == "openrouter" || endpoint.Contains("openrouter.ai"))
            {
                req.Headers.Add("HTTP-Referer", "https://gram.local");
                req.Headers.Add("X-Title", "Gram Agent");
            }

            req.Content = new StringContent(requestPayload.ToJsonString(), Encoding.UTF8, "application/json");

            HttpResponseMessage? response = null;
            string? networkError = null;
            try
            {
                response = await _httpClient.SendAsync(req, cancellationToken);
            }
            catch (Exception ex)
            {
                networkError = ex.Message;
            }

            if (networkError != null || response == null)
            {
                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = $"Network request failed: {networkError}" };
                yield break;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = $"API Error {(int)response.StatusCode}: {responseJson}" };
                yield break;
            }

            JsonNode? rootNode = null;
            string? parseError = null;
            try
            {
                rootNode = JsonNode.Parse(responseJson);
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            if (parseError != null || rootNode == null)
            {
                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = $"Invalid JSON response: {parseError}" };
                yield break;
            }

            var choice = rootNode?["choices"]?[0];
            var choiceMsg = choice?["message"];
            var content = choiceMsg?["content"]?.ToString() ?? "";
            var toolCallsNode = choiceMsg?["tool_calls"]?.AsArray();

            // Extract usage telemetry
            var usageNode = rootNode?["usage"];
            int? pTokens = usageNode?["prompt_tokens"]?.GetValue<int>();
            int? cTokens = usageNode?["completion_tokens"]?.GetValue<int>();
            int? tTokens = usageNode?["total_tokens"]?.GetValue<int>();

            // Stream content if present
            if (!string.IsNullOrWhiteSpace(content))
            {
                yield return new AgentStepUpdate 
                { 
                    ContentDelta = content,
                    PromptTokens = pTokens,
                    CompletionTokens = cTokens,
                    TotalTokens = tTokens
                };
            }

            // If no tool calls, model finished its task
            if (toolCallsNode == null || toolCallsNode.Count == 0)
            {
                yield return new AgentStepUpdate { IsComplete = true };
                yield break;
            }

            // Append assistant tool request to conversation loop
            loopMessages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = content,
                ["tool_calls"] = toolCallsNode.DeepClone()
            });

            // Execute each tool call
            foreach (var tc in toolCallsNode)
            {
                if (tc == null) continue;
                var callId = tc["id"]?.ToString() ?? Guid.NewGuid().ToString("N")[..8];
                var func = tc["function"];
                var toolName = func?["name"]?.ToString() ?? "";
                var argsStr = func?["arguments"]?.ToString() ?? "{}";

                var toolCallInfo = new ToolCallInfo
                {
                    Id = callId,
                    ToolName = toolName,
                    Arguments = argsStr
                };

                // Emit tool start notification
                yield return new AgentStepUpdate { ToolCall = toolCallInfo };

                // Execute tool
                var toolResult = await _toolRegistry.ExecuteToolAsync(
                    toolName,
                    callId,
                    argsStr,
                    workspacePath,
                    activeGit,
                    cancellationToken);

                toolCallInfo.Result = toolResult.Output;
                toolCallInfo.IsError = toolResult.IsError;

                // Emit tool result
                yield return new AgentStepUpdate
                {
                    ToolCall = toolCallInfo,
                    ToolResult = toolResult
                };

                // Append tool response to loop
                loopMessages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = callId,
                    ["content"] = toolResult.Output
                });
            }
        }

        yield return new AgentStepUpdate { IsComplete = true };
    }
}
