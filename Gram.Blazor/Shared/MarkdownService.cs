using System.Text.RegularExpressions;
using Markdig;

namespace Gram.Blazor.Shared;

public sealed class MarkdownService
{
    private readonly MarkdownPipeline _pipeline;

    // Matches <pre><code class="language-(.*?)">(.*?)</code></pre> or <pre><code>(.*?)</code></pre>
    private static readonly Regex CodeBlockRegex = new(
        @"<pre><code(?:\s+class=""language-(.*?)"")?>(.*?)</code></pre>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches <pre class="mermaid">(.*?)</pre> from Markdig's UseDiagrams
    private static readonly Regex MermaidBlockRegex = new(
        @"<pre class=""mermaid"">(.*?)</pre>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public MarkdownService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseSoftlineBreakAsHardlineBreak()
            .UseDiagrams()
            .UseMathematics()
            .UseEmphasisExtras()
            .UseEmojiAndSmiley()
            .UseAutoLinks()
            .Build();
    }

    public string RenderToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        try
        {
            var normalizedMarkdown = NormalizeModelMarkdown(markdown);
            var rawHtml = Markdown.ToHtml(normalizedMarkdown, _pipeline);

            // 1. Enhance Mermaid diagrams from UseDiagrams()
            var processedHtml = MermaidBlockRegex.Replace(rawHtml, match =>
            {
                var mermaidContent = match.Groups[1].Value;
                var rawText = System.Net.WebUtility.HtmlDecode(mermaidContent);

                return $@"<div class=""mermaid-wrapper"">
    <div class=""code-block-header"">
        <div class=""code-block-lang-label"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.2"" stroke-linecap=""round"" stroke-linejoin=""round"" class=""code-header-icon"">
                <rect x=""3"" y=""3"" width=""18"" height=""18"" rx=""2"" ry=""2""></rect>
                <line x1=""3"" y1=""9"" x2=""21"" y2=""9""></line>
                <line x1=""9"" y1=""21"" x2=""9"" y2=""9""></line>
            </svg>
            <span class=""code-lang"">Diagram (Mermaid)</span>
        </div>
        <div class=""mermaid-actions"">
            <div class=""diagram-zoom-controls"">
                <button type=""button"" class=""btn-diagram-zoom"" onclick=""gramAudio.zoomDiagram(this, -0.2)"" title=""Zoom Out (-)"">
                    <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                        <circle cx=""11"" cy=""11"" r=""8""></circle>
                        <line x1=""21"" y1=""21"" x2=""16.65"" y2=""16.65""></line>
                        <line x1=""8"" y1=""11"" x2=""14"" y2=""11""></line>
                    </svg>
                </button>
                <button type=""button"" class=""btn-diagram-zoom reset"" onclick=""gramAudio.resetDiagramZoom(this)"" title=""Reset Zoom (100%)"">
                    <span class=""diagram-zoom-level"">100%</span>
                </button>
                <button type=""button"" class=""btn-diagram-zoom"" onclick=""gramAudio.zoomDiagram(this, 0.2)"" title=""Zoom In (+)"">
                    <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                        <circle cx=""11"" cy=""11"" r=""8""></circle>
                        <line x1=""21"" y1=""21"" x2=""16.65"" y2=""16.65""></line>
                        <line x1=""11"" y1=""8"" x2=""11"" y2=""14""></line>
                        <line x1=""8"" y1=""11"" x2=""14"" y2=""11""></line>
                    </svg>
                </button>
            </div>
            <button type=""button"" class=""btn-copy-code"" onclick=""gramAudio.copyCode(this)"" title=""Copy source"">
                <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                    <rect x=""9"" y=""9"" width=""13"" height=""13"" rx=""2"" ry=""2""></rect>
                    <path d=""M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1""></path>
                </svg>
                <span>Copy</span>
            </button>
        </div>
    </div>
    <div class=""mermaid-viewport"" data-scale=""1"" data-x=""0"" data-y=""0"">
        <div class=""mermaid"">{rawText}</div>
    </div>
</div>";
            });

            // 2. Enhance standard code blocks with custom header and copy button
            processedHtml = CodeBlockRegex.Replace(processedHtml, match =>
            {
                var rawLang = match.Groups[1].Value;
                var lang = FormatLanguageName(rawLang);
                var codeContent = match.Groups[2].Value;

                return $@"<div class=""code-block-wrapper"">
    <div class=""code-block-header"">
        <div class=""code-block-lang-label"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.2"" stroke-linecap=""round"" stroke-linejoin=""round"" class=""code-header-icon"">
                <polyline points=""16 18 22 12 16 6""></polyline>
                <polyline points=""8 6 2 12 8 18""></polyline>
            </svg>
            <span class=""code-lang"">{lang}</span>
        </div>
        <button class=""btn-copy-code"" onclick=""gramAudio.copyCode(this)"" title=""Copy code"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <rect x=""9"" y=""9"" width=""13"" height=""13"" rx=""2"" ry=""2""></rect>
                <path d=""M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1""></path>
            </svg>
            <span>Copy</span>
        </button>
    </div>
    <pre><code class=""language-{rawLang}"">{codeContent}</code></pre>
</div>";
            });

            return processedHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MarkdownService] Error rendering markdown: {ex.Message}");
            return System.Net.WebUtility.HtmlEncode(markdown);
        }
    }

    private static string FormatLanguageName(string? raw)
    {
        return (raw?.ToLowerInvariant()) switch
        {
            "cs" or "csharp" => "C#",
            "cpp" or "c++" => "C++",
            "c" => "C",
            "js" or "javascript" => "JavaScript",
            "ts" or "typescript" => "TypeScript",
            "py" or "python" => "Python",
            "html" => "HTML",
            "css" => "CSS",
            "scss" => "SCSS",
            "json" => "JSON",
            "xml" => "XML",
            "yaml" or "yml" => "YAML",
            "sql" => "SQL",
            "sh" or "bash" or "shell" or "zsh" => "Bash",
            "powershell" or "ps1" => "PowerShell",
            "dockerfile" or "docker" => "Dockerfile",
            "rust" or "rs" => "Rust",
            "go" or "golang" => "Go",
            "razor" => "Razor",
            "md" or "markdown" => "Markdown",
            _ => string.IsNullOrWhiteSpace(raw) ? "Code" : raw
        };
    }

    private static string NormalizeModelMarkdown(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

        // Some provider responses contain escaped newlines instead of actual line breaks.
        // Decode those only when the entire response has no real newlines, so literal
        // backslash sequences inside code blocks are preserved.
        if (!normalized.Contains('\n') && normalized.Contains("\\n", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\t", "\t", StringComparison.Ordinal);
        }

        // A few model/font combinations emit this private-use glyph where a
        // not-equal sign belongs. Convert it to a normal Unicode operator.
        normalized = normalized.Replace("\uE020", "≠", StringComparison.Ordinal);
        // KaTeX accepts the LaTeX ASCII minus reliably; normalize typographic
        // Unicode minus signs that models frequently insert into formulas.
        normalized = normalized.Replace("−", "-", StringComparison.Ordinal);

        return NormalizeMathDelimiters(normalized);
    }

    private static string NormalizeMathDelimiters(string markdown)
    {
        // KaTeX supports these delimiters, but Markdig's math extension expects
        // dollar delimiters when converting the Markdown to HTML.
        markdown = Regex.Replace(markdown, @"\\\[(.*?)\\\]", match =>
            $"$$\n{match.Groups[1].Value.Trim()}\n$$", RegexOptions.Singleline);
        markdown = Regex.Replace(markdown, @"\\\((.*?)\\\)", match =>
            $"${match.Groups[1].Value.Trim()}$", RegexOptions.Singleline);

        // Recover from providers that escape the closing brace of a boxed
        // expression as \}. KaTeX treats that as a literal token and reports
        // the entire expression in its error color.
        markdown = Regex.Replace(markdown, @"\\boxed\{(.*?)\\\}", match =>
            "\\boxed{" + match.Groups[1].Value + "}", RegexOptions.Singleline);

        // Some model providers emit display math in ordinary square brackets.
        // Convert only mathematical-looking brackets so Markdown links remain links.
        markdown = Regex.Replace(markdown, @"(?<!\\)\[\s*([^\[\]\r\n]{2,1200})\s*\]", match =>
            IsLikelyMath(match.Groups[1].Value) ? $"$$\n{match.Groups[1].Value.Trim()}\n$$" : match.Value,
            RegexOptions.Singleline);

        // Convert short mathematical parentheticals such as (x), (a = 2), and
        // (\Delta < 0), without changing ordinary prose parentheses.
        markdown = Regex.Replace(markdown, @"(?<!\\)\(([^()\r\n]{1,240})\)", match =>
            IsLikelyMath(match.Groups[1].Value) ? $"${match.Groups[1].Value.Trim()}$" : match.Value,
            RegexOptions.Singleline);

        // Clean punctuation occasionally inserted around LaTeX commands by models.
        markdown = Regex.Replace(markdown, @"\\[,;]+\s*(?=\})", "");
        markdown = Regex.Replace(markdown, @"\s*\\[,;]+\s*(?=\\(?:pm|mp|times|cdot|leq|geq|neq|approx))", " ");
        markdown = Regex.Replace(markdown, @"(?<=\{)\s*[,;]+\s*", "");
        markdown = Regex.Replace(markdown, @"\s*[,;]+\s*(?=\})", "");
        markdown = Regex.Replace(markdown, @"\s*;\s*(\\(?:pm|mp|times|cdot|leq|geq|neq|approx))\s*;\s*", " $1 ");

        return markdown;
    }

    private static bool IsLikelyMath(string value)
    {
        var candidate = value.Trim();
        if (Regex.IsMatch(candidate, @"^[A-Za-z](?:[_^][A-Za-z0-9]+)?$")) return true;
        if (Regex.IsMatch(candidate, @"^[+-]?\d+(?:\.\d+)?$")) return true;
        return candidate.Contains('=')
            || candidate.Contains('^')
            || candidate.Contains('_')
            || candidate.Contains('\\')
            || candidate.Contains('Δ')
            || Regex.IsMatch(candidate, @"\s[+\-*/<>]\s");
    }
}
