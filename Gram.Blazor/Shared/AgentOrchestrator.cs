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
    public int? CachedPromptTokens { get; set; }
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
        string? repositoryMap = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var isLocal = _settingsService.IsFeatureSupported("localFileStorage");
        var activeGit = _gitService.Accounts.FirstOrDefault(a => a.IsDefault) ?? _gitService.Accounts.FirstOrDefault();
        var intent = string.Join("\n", history.Where(x => x.Role.Equals("user", StringComparison.OrdinalIgnoreCase)).TakeLast(3).Select(x => x.Text));
        var tools = _toolRegistry.GetAvailableTools(isLocal, activeGit, intent);

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
        fullSystemPrompt.AppendLine($"- Environment Type: {(isLocal ? "Desktop / Self-Hosted (Full Filesystem & Terminal Execution)" : "Web (Remote Git & Web API Tools)")}");
        fullSystemPrompt.AppendLine("- Workspace files are accessed through the approved tools; do not assume or expose local filesystem paths.");
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

        // Keep volatile runtime data out of the stable system prefix while still
        // making the current date available to the agent when it is relevant.
        var runtimeNow = DateTimeOffset.UtcNow;
        loopMessages.Add(new JsonObject
        {
            ["role"] = "system",
            ["content"] = $"<runtime_context>Current UTC time: {runtimeNow:yyyy-MM-dd HH:mm:ss} UTC. Current date: {runtimeNow:dddd, MMMM d, yyyy}.</runtime_context>"
        });

        if (!string.IsNullOrWhiteSpace(repositoryMap))
        {
            loopMessages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = repositoryMap
            });
        }

        // Tool schemas are stable across the iterations of one agent run. Build
        // them once and deep-clone the cached prototype when attaching it to a
        // request so JSON nodes never acquire multiple parents.
        var toolSchemas = tools.Count > 0
            ? new JsonArray(tools.Select(t => t.ToOpenAiFunctionSchema()).ToArray())
            : null;

        var initialTarget = ResolveTargetModel(modelId);
        if (IsNativeOpenAiTarget(initialTarget.providerId, initialTarget.endpoint))
        {
            await foreach (var update in RunNativeOpenAiResponsesLoopAsync(
                initialTarget.actualModel,
                initialTarget.apiKey,
                fullSystemPrompt.ToString(),
                loopMessages.Skip(1).ToList(),
                tools,
                workspacePath,
                activeGit,
                maxIterations,
                cancellationToken))
            {
                yield return update;
            }

            yield break;
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
                requestPayload["tools"] = toolSchemas!.DeepClone();
                requestPayload["tool_choice"] = "auto";
            }

            // Resolve target model, provider endpoint, and API key
            var (actualModel, providerId, endpoint, apiKey) = ResolveTargetModel(modelId);
            requestPayload["model"] = actualModel;

            // Check for missing API key for cloud providers
            if (string.IsNullOrWhiteSpace(apiKey) && providerId != "ollama")
            {
                var friendlyName = modelId switch
                {
                    "gram-q" => "Gram Q (Groq API Key)",
                    "gram-q-plus" => "Gram Q+ (Google Gemini API Key)",
                    _ => providerId
                };
                yield return new AgentStepUpdate
                {
                    IsComplete = true,
                    ErrorMessage = $"API key not configured for {friendlyName}. Please set GROQ_API_KEY / GEMINI_API_KEY in environment or Settings > Intelligence > Providers."
                };
                yield break;
            }

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

            // Fallback attempt on Rate Limit (429) or Network Failure for Gram Q / Gram Q+
            if ((response == null || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) &&
                (modelId == "gram-q" || modelId == "gram-q-plus"))
            {
                var cfKey = _modelService.GetApiKey("cloudflare");
                if (!string.IsNullOrWhiteSpace(cfKey))
                {
                    try
                    {
                        var cfPayload = requestPayload.DeepClone() as JsonObject;
                        if (cfPayload != null)
                        {
                            cfPayload["model"] = "@cf/meta/llama-3.3-70b-instruct";
                            using var cfReq = new HttpRequestMessage(HttpMethod.Post, "https://api.cloudflare.com/client/v4/accounts/me/ai/v1/chat/completions");
                            cfReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfKey);
                            cfReq.Content = new StringContent(cfPayload.ToJsonString(), Encoding.UTF8, "application/json");
                            var cfRes = await _httpClient.SendAsync(cfReq, cancellationToken);
                            if (cfRes.IsSuccessStatusCode)
                            {
                                response = cfRes;
                                networkError = null;
                            }
                        }
                    }
                    catch {}
                }
            }

            if (networkError != null || response == null)
            {
                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = $"Network request failed: {networkError}" };
                yield break;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var friendlyError = status switch
                {
                    401 => $"[401 Unauthorized] API key invalid for {providerId}. Please verify your key in Settings > Intelligence > Providers.",
                    429 => $"[429 Quota / Rate Limit] Daily allowance or rate limit reached for {actualModel} ({providerId}). Please wait a moment or add your own key in Settings.",
                    500 or 502 or 503 => $"[{status} Provider Error] {providerId} upstream service is currently overloaded. Please try again in a moment.",
                    _ => $"[API Error {status}] {responseJson}"
                };

                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = friendlyError };
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
            int? cachedTokens = usageNode?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>()
                ?? usageNode?["cache_read_input_tokens"]?.GetValue<int>()
                ?? usageNode?["cache_read_tokens"]?.GetValue<int>();
            int? cTokens = usageNode?["completion_tokens"]?.GetValue<int>();
            int? tTokens = usageNode?["total_tokens"]?.GetValue<int>();

            // Stream content if present
            var usageEmitted = false;
            if (!string.IsNullOrWhiteSpace(content))
            {
                // Clean reasoning / thinking tokens for models like Qwen or DeepSeek
                var displayContent = System.Text.RegularExpressions.Regex.Replace(content, @"<think>[\s\S]*?</think>\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                if (!string.IsNullOrWhiteSpace(displayContent))
                {
                    yield return new AgentStepUpdate 
                    { 
                        ContentDelta = displayContent,
                        PromptTokens = pTokens,
                        CachedPromptTokens = cachedTokens,
                        CompletionTokens = cTokens,
                        TotalTokens = tTokens
                    };
                    usageEmitted = true;
                }
            }

            if (!usageEmitted && (pTokens.HasValue || cachedTokens.HasValue || cTokens.HasValue || tTokens.HasValue))
            {
                yield return new AgentStepUpdate
                {
                    PromptTokens = pTokens,
                    CachedPromptTokens = cachedTokens,
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
                    ["content"] = ToolResultReducer.ForModel(toolName, toolResult.Output)
                });
            }
        }

        yield return new AgentStepUpdate { IsComplete = true };
    }

    private async IAsyncEnumerable<AgentStepUpdate> RunNativeOpenAiResponsesLoopAsync(
        string actualModel,
        string? apiKey,
        string systemPrompt,
        List<JsonObject> initialMessages,
        List<AgentToolDefinition> tools,
        string workspacePath,
        GitAccount? activeGit,
        int maxIterations,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return new AgentStepUpdate
            {
                IsComplete = true,
                ErrorMessage = "API key not configured for OpenAI. Please set the OpenAI key in Settings > Intelligence > Providers."
            };
            yield break;
        }

        var inputItems = initialMessages.Select(x => x.DeepClone().AsObject()).ToList();
        var responseTools = ConvertToResponsesTools(tools);
        var cacheKey = PromptCacheKey.ForAgent(actualModel, workspacePath);

        for (var iteration = 0; iteration < maxIterations && !cancellationToken.IsCancellationRequested; iteration++)
        {
            var payload = new JsonObject
            {
                ["model"] = actualModel,
                ["instructions"] = systemPrompt,
                ["input"] = new JsonArray(inputItems.Select(x => x.DeepClone()).ToArray()),
                ["prompt_cache_key"] = cacheKey,
                ["store"] = false,
                ["stream"] = true,
                ["tool_choice"] = "auto",
                ["parallel_tool_calls"] = false
            };

            if (responseTools.Count > 0)
            {
                payload["tools"] = responseTools.DeepClone();
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage? response = null;
            string? requestError = null;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                requestError = ex.Message;
            }

            if (requestError != null)
            {
                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = $"OpenAI Responses request failed: {requestError}" };
                yield break;
            }

            if (response == null)
            {
                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = "OpenAI Responses request returned no response." };
                yield break;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return new AgentStepUpdate
                {
                    IsComplete = true,
                    ErrorMessage = $"[OpenAI Responses error {(int)response.StatusCode}] {body}"
                };
                yield break;
            }

            JsonObject? completedResponse = null;
            using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                    var data = line["data:".Length..].Trim();
                    if (data == "[DONE]") continue;

                    JsonNode? eventNode;
                    try { eventNode = JsonNode.Parse(data); }
                    catch { continue; }
                    if (eventNode is not JsonObject eventObject) continue;

                    var eventType = eventObject["type"]?.ToString() ?? "";
                    if (eventType == "response.output_text.delta")
                    {
                        var delta = eventObject["delta"]?.ToString();
                        if (!string.IsNullOrEmpty(delta))
                        {
                            yield return new AgentStepUpdate { ContentDelta = delta };
                        }
                    }
                    else if (eventType == "response.completed")
                    {
                        completedResponse = eventObject["response"]?.AsObject();
                    }
                    else if (eventType is "response.failed" or "error")
                    {
                        yield return new AgentStepUpdate
                        {
                            IsComplete = true,
                            ErrorMessage = eventObject["error"]?.ToString() ?? "OpenAI Responses request failed."
                        };
                        yield break;
                    }
                }
            }

            if (completedResponse == null)
            {
                yield return new AgentStepUpdate { IsComplete = true, ErrorMessage = "OpenAI Responses stream ended without a completed response." };
                yield break;
            }

            var usage = completedResponse["usage"]?.AsObject();
            var promptTokens = ReadInt(usage, "input_tokens");
            var completionTokens = ReadInt(usage, "output_tokens");
            var totalTokens = ReadInt(usage, "total_tokens");
            var cachedTokens = ReadInt(usage?["input_tokens_details"]?.AsObject(), "cached_tokens");
            if (promptTokens.HasValue || completionTokens.HasValue || totalTokens.HasValue || cachedTokens.HasValue)
            {
                yield return new AgentStepUpdate
                {
                    PromptTokens = promptTokens,
                    CachedPromptTokens = cachedTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = totalTokens
                };
            }

            var outputItems = completedResponse["output"]?.AsArray() ?? new JsonArray();
            foreach (var outputItem in outputItems)
            {
                if (outputItem is JsonObject item)
                {
                    inputItems.Add(item.DeepClone().AsObject());
                }
            }

            var functionCalls = outputItems
                .OfType<JsonObject>()
                .Where(item => item["type"]?.ToString() == "function_call")
                .ToList();

            if (functionCalls.Count == 0)
            {
                yield return new AgentStepUpdate { IsComplete = true };
                yield break;
            }

            foreach (var functionCall in functionCalls)
            {
                var callId = functionCall["call_id"]?.ToString() ?? Guid.NewGuid().ToString("N")[..8];
                var toolName = functionCall["name"]?.ToString() ?? "";
                var args = functionCall["arguments"]?.ToString() ?? "{}";
                var toolCall = new ToolCallInfo { Id = callId, ToolName = toolName, Arguments = args };

                yield return new AgentStepUpdate { ToolCall = toolCall };

                var toolResult = await _toolRegistry.ExecuteToolAsync(
                    toolName,
                    callId,
                    args,
                    workspacePath,
                    activeGit,
                    cancellationToken);

                toolCall.Result = toolResult.Output;
                toolCall.IsError = toolResult.IsError;
                yield return new AgentStepUpdate { ToolCall = toolCall, ToolResult = toolResult };

                inputItems.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = callId,
                    ["output"] = ToolResultReducer.ForModel(toolName, toolResult.Output)
                });
            }
        }

        yield return new AgentStepUpdate { IsComplete = true };
    }

    private static JsonArray ConvertToResponsesTools(IEnumerable<AgentToolDefinition> tools)
    {
        var result = new JsonArray();
        foreach (var tool in tools)
        {
            var schema = tool.ToOpenAiFunctionSchema();
            var function = schema["function"]?.AsObject();
            if (function == null) continue;

            result.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = function["name"]?.DeepClone(),
                ["description"] = function["description"]?.DeepClone(),
                ["parameters"] = function["parameters"]?.DeepClone()
            });
        }

        return result;
    }

    private static int? ReadInt(JsonObject? node, string propertyName)
    {
        return node?[propertyName]?.GetValue<int>();
    }

    private static bool IsNativeOpenAiTarget(string providerId, string endpoint)
    {
        return providerId.Equals("openai", StringComparison.OrdinalIgnoreCase)
            && endpoint.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    private (string actualModel, string providerId, string endpoint, string? apiKey) ResolveTargetModel(string modelId)
    {
        if (modelId.Equals("gram-q", StringComparison.OrdinalIgnoreCase))
        {
            var groqKey = _modelService.GetInternalApiKey("groq");
            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                return ("llama-3.3-70b-versatile", "groq", "https://api.groq.com/openai/v1/chat/completions", groqKey);
            }
            var cfKey = _modelService.GetInternalApiKey("cloudflare");
            if (!string.IsNullOrWhiteSpace(cfKey))
            {
                return ("@cf/meta/llama-3.3-70b-instruct", "cloudflare", "https://api.cloudflare.com/client/v4/accounts/me/ai/v1/chat/completions", cfKey);
            }
            var geminiKey = _modelService.GetInternalApiKey("google");
            if (!string.IsNullOrWhiteSpace(geminiKey))
            {
                return ("gemini-2.0-flash", "google", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", geminiKey);
            }
            return ("llama-3.3-70b-versatile", "groq", "https://api.groq.com/openai/v1/chat/completions", null);
        }

        if (modelId.Equals("gram-q-plus", StringComparison.OrdinalIgnoreCase))
        {
            var geminiKey = _modelService.GetInternalApiKey("google");
            if (!string.IsNullOrWhiteSpace(geminiKey))
            {
                return ("gemini-2.0-flash", "google", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", geminiKey);
            }
            var groqKey = _modelService.GetInternalApiKey("groq");
            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                return ("llama-3.3-70b-versatile", "groq", "https://api.groq.com/openai/v1/chat/completions", groqKey);
            }
            return ("gemini-2.0-flash", "google", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", null);
        }

        var model = _modelService.Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                    ?? new GramModel { Id = modelId, Provider = "OpenRouter" };
        var provId = model.Provider.ToLowerInvariant();
        var prov = _modelService.Providers.FirstOrDefault(p => p.Id.Equals(provId, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(model.Provider, StringComparison.OrdinalIgnoreCase));
        var key = _modelService.GetApiKey(prov?.Id ?? "openrouter") ?? _modelService.GetApiKey("openrouter");

        var ep = provId switch
        {
            "groq" => $"{prov?.BaseUrl ?? "https://api.groq.com/openai/v1"}/chat/completions",
            "google" => $"{prov?.BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta/openai"}/chat/completions",
            "openai" => $"{prov?.BaseUrl ?? "https://api.openai.com/v1"}/chat/completions",
            "anthropic" => $"{prov?.BaseUrl ?? "https://api.anthropic.com/v1"}/chat/completions",
            "xai" => $"{prov?.BaseUrl ?? "https://api.x.ai/v1"}/chat/completions",
            "deepseek" => $"{prov?.BaseUrl ?? "https://api.deepseek.com/v1"}/chat/completions",
            "ollama" => $"{prov?.BaseUrl ?? "http://localhost:11434"}/v1/chat/completions",
            "cloudflare" => $"{prov?.BaseUrl ?? "https://api.cloudflare.com/client/v4/accounts/me/ai/v1"}/chat/completions",
            _ => $"{prov?.BaseUrl ?? "https://openrouter.ai/api/v1"}/chat/completions"
        };

        return (modelId, provId, ep, key);
    }
}
