using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public sealed class ModelCapabilityProbeService
{
    private readonly HttpClient _httpClient;

    public ModelCapabilityProbeService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ModelQualificationResult> RunBatteryAsync(
        string modelId,
        string providerEndpoint,
        string apiKey,
        string providerType = "OpenRouter",
        CancellationToken cancellationToken = default)
    {
        var probes = new List<ProbeResult>();

        // 1. Probe 1: Chat & Reasoning Baseline
        var chatProbe = await RunChatReasoningProbeAsync(modelId, providerEndpoint, apiKey, cancellationToken);
        probes.Add(chatProbe);

        if (!chatProbe.Passed)
        {
            return EvaluateProbes(probes, "Model failed basic conversational and reasoning checks.");
        }

        // 2. Probe 2: JSON Tool-Calling Grammar
        var toolProbe = await RunToolCallingGrammarProbeAsync(modelId, providerEndpoint, apiKey, cancellationToken);
        probes.Add(toolProbe);

        // 3. Probe 3: Surgical Patch & Whitespace Accuracy
        var patchProbe = await RunSurgicalPatchAccuracyProbeAsync(modelId, providerEndpoint, apiKey, cancellationToken);
        probes.Add(patchProbe);

        // 4. Probe 4: Context & Negative Constraint Following
        var contextProbe = await RunContextCapacityProbeAsync(modelId, providerEndpoint, apiKey, cancellationToken);
        probes.Add(contextProbe);

        return EvaluateProbes(probes);
    }

    public static ModelQualificationResult EvaluateProbes(
        IReadOnlyList<ProbeResult> probes,
        string? failureReasonOverride = null)
    {
        var chatProbe = probes.FirstOrDefault(p => p.ProbeName == "ChatReasoning");
        var toolProbe = probes.FirstOrDefault(p => p.ProbeName == "ToolCallingGrammar");
        var patchProbe = probes.FirstOrDefault(p => p.ProbeName == "SurgicalPatchAccuracy");
        var contextProbe = probes.FirstOrDefault(p => p.ProbeName == "ContextCapacity");

        var isChatSupported = chatProbe != null && chatProbe.Passed;

        if (!isChatSupported)
        {
            return new ModelQualificationResult
            {
                OverallTier = ModelCodingSupportTier.Unsupported,
                IsChatSupported = false,
                CodingTier = ModelCodingSupportTier.Unsupported,
                ProbeResults = probes.ToList(),
                QualificationTimestamp = DateTimeOffset.UtcNow,
                UnsupportedReason = failureReasonOverride ?? "Model failed basic conversational and reasoning checks."
            };
        }

        // Chat is supported. Now evaluate coding tier:
        bool toolsValid = toolProbe != null && toolProbe.Passed;
        bool patchValid = patchProbe != null && patchProbe.Passed;
        bool contextValid = contextProbe == null || contextProbe.Passed;

        if (!toolsValid || !patchValid)
        {
            var reasons = new List<string>();
            if (!toolsValid) reasons.Add("Model failed native JSON tool-calling grammar probe.");
            if (!patchValid) reasons.Add("Model failed surgical whitespace/indentation code editing probe.");

            return new ModelQualificationResult
            {
                OverallTier = ModelCodingSupportTier.ChatOnly,
                IsChatSupported = true,
                CodingTier = ModelCodingSupportTier.ChatOnly,
                ProbeResults = probes.ToList(),
                QualificationTimestamp = DateTimeOffset.UtcNow,
                UnsupportedReason = string.Join(" ", reasons)
            };
        }

        // Both tool calling and patching passed
        if ((patchProbe?.Score ?? 0.0) >= 0.8 && contextValid)
        {
            return new ModelQualificationResult
            {
                OverallTier = ModelCodingSupportTier.VerifiedAutonomous,
                IsChatSupported = true,
                CodingTier = ModelCodingSupportTier.VerifiedAutonomous,
                ProbeResults = probes.ToList(),
                QualificationTimestamp = DateTimeOffset.UtcNow,
                UnsupportedReason = null
            };
        }

        return new ModelQualificationResult
        {
            OverallTier = ModelCodingSupportTier.VerifiedAssisted,
            IsChatSupported = true,
            CodingTier = ModelCodingSupportTier.VerifiedAssisted,
            ProbeResults = probes.ToList(),
            QualificationTimestamp = DateTimeOffset.UtcNow,
            UnsupportedReason = "Model qualified for assisted coding (manual review recommended for complex edits)."
        };
    }

    private async Task<ProbeResult> RunChatReasoningProbeAsync(
        string modelId,
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var payload = new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = "Sort the following 4 words alphabetically and output ONLY the sorted words separated by commas: zebra, apple, mango, banana"
                    }
                },
                ["temperature"] = 0.0
            };

            var responseText = await PostCompletionAsync(endpoint, apiKey, payload, cts.Token);
            sw.Stop();

            var normalized = responseText.ToLowerInvariant();
            var hasAllWords = normalized.Contains("apple") && normalized.Contains("banana") && normalized.Contains("mango") && normalized.Contains("zebra");
            var appleIdx = normalized.IndexOf("apple", StringComparison.Ordinal);
            var bananaIdx = normalized.IndexOf("banana", StringComparison.Ordinal);
            var mangoIdx = normalized.IndexOf("mango", StringComparison.Ordinal);
            var zebraIdx = normalized.IndexOf("zebra", StringComparison.Ordinal);

            var inOrder = appleIdx >= 0 && appleIdx < bananaIdx && bananaIdx < mangoIdx && mangoIdx < zebraIdx;

            return new ProbeResult
            {
                ProbeName = "ChatReasoning",
                Passed = inOrder,
                Score = inOrder ? 1.0 : (hasAllWords ? 0.5 : 0.0),
                DiagnosticMessage = inOrder ? "Passed alphabetical sorting logic." : "Failed alphabetical ordering or word extraction.",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                ProbeName = "ChatReasoning",
                Passed = false,
                Score = 0.0,
                DiagnosticMessage = $"Probe threw exception: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<ProbeResult> RunToolCallingGrammarProbeAsync(
        string modelId,
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var payload = new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = "Use the calculator_add tool to calculate the sum of 17 and 25."
                    }
                },
                ["tools"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = "calculator_add",
                            ["description"] = "Adds two numbers together.",
                            ["parameters"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["a"] = new JsonObject { ["type"] = "integer", ["description"] = "First number" },
                                    ["b"] = new JsonObject { ["type"] = "integer", ["description"] = "Second number" }
                                },
                                ["required"] = new JsonArray { "a", "b" }
                            }
                        }
                    }
                },
                ["tool_choice"] = "auto",
                ["temperature"] = 0.0
            };

            var rawJson = await PostRawAsync(endpoint, apiKey, payload, cts.Token);
            sw.Stop();

            var doc = JsonNode.Parse(rawJson);
            var choices = doc?["choices"]?.AsArray();
            var message = choices?.FirstOrDefault()?["message"];
            var toolCalls = message?["tool_calls"]?.AsArray();

            if (toolCalls != null && toolCalls.Count > 0)
            {
                var firstCall = toolCalls[0];
                var funcName = firstCall?["function"]?["name"]?.ToString();
                var argsStr = firstCall?["function"]?["arguments"]?.ToString() ?? "{}";

                if (funcName == "calculator_add")
                {
                    var argsNode = JsonNode.Parse(argsStr);
                    var a = argsNode?["a"]?.GetValue<int>() ?? 0;
                    var b = argsNode?["b"]?.GetValue<int>() ?? 0;

                    if ((a == 17 && b == 25) || (a == 25 && b == 17))
                    {
                        return new ProbeResult
                        {
                            ProbeName = "ToolCallingGrammar",
                            Passed = true,
                            Score = 1.0,
                            DiagnosticMessage = "Valid function call emitted with exact argument schema.",
                            LatencyMs = sw.ElapsedMilliseconds
                        };
                    }

                    return new ProbeResult
                    {
                        ProbeName = "ToolCallingGrammar",
                        Passed = true,
                        Score = 0.7,
                        DiagnosticMessage = $"Function called but unexpected arguments: {argsStr}",
                        LatencyMs = sw.ElapsedMilliseconds
                    };
                }
            }

            return new ProbeResult
            {
                ProbeName = "ToolCallingGrammar",
                Passed = false,
                Score = 0.0,
                DiagnosticMessage = "Model did not emit a valid native tool_calls JSON payload.",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                ProbeName = "ToolCallingGrammar",
                Passed = false,
                Score = 0.0,
                DiagnosticMessage = $"Tool calling probe error: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<ProbeResult> RunSurgicalPatchAccuracyProbeAsync(
        string modelId,
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var prompt = """
                You are a surgical code editor. Given this snippet:
                1: public class Config
                2: {
                3:     public int Port = 8080;
                4:     public bool Enabled = true;
                5: }
                Output ONLY the exact line 3 replacement to change Port to 9090. Do NOT include markdown code fences, backticks, line numbers, or explanations.
                """;

            var payload = new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "user", ["content"] = prompt }
                },
                ["temperature"] = 0.0
            };

            var responseText = await PostCompletionAsync(endpoint, apiKey, payload, cts.Token);
            sw.Stop();

            var cleaned = responseText.Trim().Trim('`').Trim();
            // Remove optional '3:' if model outputted line number
            if (cleaned.StartsWith("3:")) cleaned = cleaned[2..].TrimStart();

            var exactMatch = cleaned.Equals("public int Port = 9090;", StringComparison.Ordinal);
            var containsTarget = cleaned.Contains("Port = 9090");

            return new ProbeResult
            {
                ProbeName = "SurgicalPatchAccuracy",
                Passed = containsTarget,
                Score = exactMatch ? 1.0 : (containsTarget ? 0.6 : 0.0),
                DiagnosticMessage = exactMatch
                    ? "Exact surgical code line match."
                    : (containsTarget ? "Target line modified with minor whitespace/wrapper discrepancy." : "Failed to output target modification."),
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                ProbeName = "SurgicalPatchAccuracy",
                Passed = false,
                Score = 0.0,
                DiagnosticMessage = $"Surgical patch probe error: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<ProbeResult> RunContextCapacityProbeAsync(
        string modelId,
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var prompt = """
                [NEGATIVE CONSTRAINT INSTRUCTION]
                You must follow this constraint strictly: Do not output any vowels (A, E, I, O, U, a, e, i, o, u) in your final response.
                Respond ONLY with the three letters: B, C, D.
                """;

            var payload = new JsonObject
            {
                ["model"] = modelId,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "user", ["content"] = prompt }
                },
                ["temperature"] = 0.0
            };

            var responseText = await PostCompletionAsync(endpoint, apiKey, payload, cts.Token);
            sw.Stop();

            var text = responseText.Trim();
            var vowels = new[] { 'a', 'e', 'i', 'o', 'u', 'A', 'E', 'I', 'O', 'U' };
            var hasVowels = text.IndexOfAny(vowels) >= 0;
            var hasB = text.Contains('B') || text.Contains('b');
            var hasC = text.Contains('C') || text.Contains('c');
            var hasD = text.Contains('D') || text.Contains('d');

            var passed = !hasVowels && hasB && hasC && hasD;

            return new ProbeResult
            {
                ProbeName = "ContextCapacity",
                Passed = passed,
                Score = passed ? 1.0 : (!hasVowels ? 0.5 : 0.0),
                DiagnosticMessage = passed
                    ? "Strict negative constraint followed successfully without vowel leakage."
                    : (hasVowels ? "Failed negative constraint: leaked vowels." : "Failed to produce required output letters."),
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                ProbeName = "ContextCapacity",
                Passed = false,
                Score = 0.0,
                DiagnosticMessage = $"Context capacity probe error: {ex.Message}",
                LatencyMs = sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<string> PostCompletionAsync(string endpoint, string apiKey, JsonObject payload, CancellationToken ct)
    {
        var raw = await PostRawAsync(endpoint, apiKey, payload, ct);
        var doc = JsonNode.Parse(raw);
        var choices = doc?["choices"]?.AsArray();
        return choices?.FirstOrDefault()?["message"]?["content"]?.ToString() ?? "";
    }

    private async Task<string> PostRawAsync(string endpoint, string apiKey, JsonObject payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
