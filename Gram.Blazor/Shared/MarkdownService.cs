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

    // Matches <table>...</table> to wrap in responsive scroll container with copy action
    private static readonly Regex TableRegex = new(
        @"<table>[\s\S]*?</table>",
        RegexOptions.Compiled);

    // Matches file:// links to convert them into interactive workspace file deep links
    private static readonly Regex FileLinkRegex = new(
        @"<a\s+href=""(file://[^""]+)""(.*?)>(.*?)</a>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public MarkdownService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAlertBlocks()
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

            // 2. Enhance standard code blocks with custom header (supporting filenames) and copy button
            processedHtml = CodeBlockRegex.Replace(processedHtml, match =>
            {
                var rawLang = match.Groups[1].Value?.Trim() ?? string.Empty;
                string langPart = rawLang;
                string? fileName = null;

                if (rawLang.Contains(':'))
                {
                    var split = rawLang.Split(':', 2);
                    langPart = split[0].Trim();
                    fileName = split[1].Trim();
                }
                else if (rawLang.Contains('.') && !rawLang.Contains(' '))
                {
                    fileName = rawLang;
                    var ext = System.IO.Path.GetExtension(rawLang).TrimStart('.');
                    if (!string.IsNullOrEmpty(ext))
                    {
                        langPart = ext;
                    }
                }

                var cleanLang = langPart.ToLowerInvariant();
                var langDisplayName = FormatLanguageName(langPart);
                var codeContent = match.Groups[2].Value;

                string headerLeft;
                if (!string.IsNullOrEmpty(fileName))
                {
                    headerLeft = $@"<div class=""code-block-lang-label has-file"">
            <svg width=""13"" height=""13"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"" class=""code-header-icon file-icon"">
                <path d=""M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z""></path>
                <polyline points=""14 2 14 8 20 8""></polyline>
                <line x1=""16"" y1=""13"" x2=""8"" y2=""13""></line>
                <line x1=""16"" y1=""17"" x2=""8"" y2=""17""></line>
            </svg>
            <span class=""code-filename"" title=""{System.Net.WebUtility.HtmlEncode(fileName)}"">{System.Net.WebUtility.HtmlEncode(fileName)}</span>
            <span class=""code-lang-tag"">{langDisplayName}</span>
        </div>";
                }
                else
                {
                    headerLeft = $@"<div class=""code-block-lang-label"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.2"" stroke-linecap=""round"" stroke-linejoin=""round"" class=""code-header-icon"">
                <polyline points=""16 18 22 12 16 6""></polyline>
                <polyline points=""8 6 2 12 8 18""></polyline>
            </svg>
            <span class=""code-lang"">{langDisplayName}</span>
        </div>";
                }

                return $@"<div class=""code-block-wrapper"">
    <div class=""code-block-header"">
        {headerLeft}
        <button type=""button"" class=""btn-copy-code"" onclick=""gramAudio.copyCode(this)"" title=""Copy code"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <rect x=""9"" y=""9"" width=""13"" height=""13"" rx=""2"" ry=""2""></rect>
                <path d=""M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1""></path>
            </svg>
            <span>Copy</span>
        </button>
    </div>
    <pre><code class=""language-{cleanLang}"">{codeContent}</code></pre>
</div>";
            });

            // 3. Wrap tables in responsive container with header toolbar and copy button
            processedHtml = TableRegex.Replace(processedHtml, match =>
            {
                return $@"<div class=""dt-table-wrapper"">
    <div class=""dt-table-toolbar"">
        <span class=""dt-table-label"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <rect x=""3"" y=""3"" width=""18"" height=""18"" rx=""2"" ry=""2""></rect>
                <line x1=""3"" y1=""9"" x2=""21"" y2=""9""></line>
                <line x1=""3"" y1=""15"" x2=""21"" y2=""15""></line>
                <line x1=""9"" y1=""3"" x2=""9"" y2=""21""></line>
            </svg>
            <span>Table</span>
        </span>
        <button type=""button"" class=""btn-copy-table"" onclick=""gramAudio.copyTable(this)"" title=""Copy table as Markdown"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <rect x=""9"" y=""9"" width=""13"" height=""13"" rx=""2"" ry=""2""></rect>
                <path d=""M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1""></path>
            </svg>
            <span>Copy Table</span>
        </button>
    </div>
    <div class=""dt-table-scroll"">
        {match.Value}
    </div>
</div>";
            });

            // 4. Enhance file:// links into clickable workspace deep links
            processedHtml = FileLinkRegex.Replace(processedHtml, match =>
            {
                var fileUri = match.Groups[1].Value;
                var remainingAttrs = match.Groups[2].Value;
                var label = match.Groups[3].Value;

                return $@"<a href=""{fileUri}"" class=""dt-file-link"" title=""Open {System.Net.WebUtility.HtmlEncode(fileUri)}""{remainingAttrs}><svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"" class=""dt-file-icon""><path d=""M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z""></path><polyline points=""14 2 14 8 20 8""></polyline></svg><span>{label}</span></a>";
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
            "jsx" => "JSX",
            "tsx" => "TSX",
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
            "diff" or "patch" => "Diff",
            "md" or "markdown" => "Markdown",
            _ => string.IsNullOrWhiteSpace(raw) ? "Code" : raw
        };
    }

    private static string StabilizeStreamingMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return markdown;

        // Auto-close unclosed fenced code blocks (```)
        var backtickMatches = Regex.Matches(markdown, @"(?m)^[ \t]*```");
        if (backtickMatches.Count % 2 != 0)
        {
            markdown += "\n```";
        }

        // Auto-close unclosed tilde code blocks (~~~)
        var tildeMatches = Regex.Matches(markdown, @"(?m)^[ \t]*~~~");
        if (tildeMatches.Count % 2 != 0)
        {
            markdown += "\n~~~";
        }

        // Auto-close unclosed display math blocks ($$)
        var mathMatches = Regex.Matches(markdown, @"\$\$");
        if (mathMatches.Count % 2 != 0)
        {
            markdown += "\n$$";
        }

        return markdown;
    }

    // Protects fenced code blocks (``` or ~~~) and inline code spans (`...`) from prose normalization.
    private static readonly Regex CodeSpanOrBlockRegex = new(
        @"(`{3,}[\s\S]*?`{3,}|~{3,}[\s\S]*?~{3,}|`[^`\r\n]+`)",
        RegexOptions.Compiled);

    private static string NormalizeModelMarkdown(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

        // Stabilize streaming markdown: virtually auto-close unclosed blocks
        normalized = StabilizeStreamingMarkdown(normalized);

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

        // Normalize over-escaped characters in prose (e.g. Gram\'s, it\'s, user\\'s) outside of code blocks
        normalized = NormalizeEscapedCharacters(normalized);

        return NormalizeMathDelimiters(normalized);
    }

    private static string NormalizeEscapedCharacters(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return markdown;

        // Split markdown so that code blocks (fenced ``` and inline `) are protected from alteration.
        // Captured code blocks appear at odd indices (1, 3, 5, ...); prose at even indices (0, 2, 4, ...).
        var parts = CodeSpanOrBlockRegex.Split(markdown);
        for (int i = 0; i < parts.Length; i += 2)
        {
            var prose = parts[i];
            if (string.IsNullOrEmpty(prose)) continue;

            // 1. Normalize escaped contractions and possessives: Gram\'s, Gram\\'s, it\'s, user\'s, don\'t
            prose = Regex.Replace(prose, @"(?<=\w)\\{1,3}'(?=\w)", "'");

            // 2. Normalize over-escaped single quotes in prose (e.g. \'word\')
            prose = Regex.Replace(prose, @"\\+'", "'");

            // 3. Normalize over-escaped double quotes in prose outside code
            prose = Regex.Replace(prose, @"(?<=^|\s)\\{1,3}""(?=\w)", "\"");
            prose = Regex.Replace(prose, @"(?<=\w)\\{1,3}""(?=[\s.,;:!?]|$)", "\"");

            // 4. Normalize concatenated sentence boundaries outside code blocks (e.g. "codebase.Mapped", "layout.Explored", "implementation.I've")
            prose = Regex.Replace(prose, @"(?<=[a-z]{2,}[.!?])(?=[A-Z][a-z]{2,}|I've|I'll|I\s|\[Agent)", "\n\n");

            parts[i] = prose;
        }

        return string.Concat(parts);
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

        // Convert short mathematical parentheticals such as (a = 2) and (\Delta < 0).
        // Require preceding whitespace, line-start, or math operator so attached plurals like file(s) are never touched.
        markdown = Regex.Replace(markdown, @"(?<=^|[\s=+\-*/<>~,(])\(([^()\r\n]{1,240})\)", match =>
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
        if (candidate.Length == 0) return false;

        // Never consider bare letters (e.g. (s), (a), (x)), numbers ((1), (2)),
        // or Roman numerals ((i), (iv)) as math.
        if (Regex.IsMatch(candidate, @"^(?:[A-Za-z]{1,3}|\d+|[ivxIVX]+)$")) return false;

        // If it contains quotes, colons, or code structure, it's code/prose, not math
        if (candidate.Contains('"') || candidate.Contains('\'') || candidate.Contains(": ") || candidate.Contains(';') || candidate.Contains('{') || candidate.Contains('}'))
            return false;

        // LaTeX math commands (e.g. \Delta, \alpha, \frac, \sqrt, \sum, \int, \le, \ge, \neq, \approx)
        if (Regex.IsMatch(candidate, @"\\(?:[A-Za-z]+|pm|mp|times|cdot|leq|geq|neq|approx|alpha|beta|gamma|delta|theta|pi|sigma|omega|Delta|Sigma|Omega|sqrt|frac|sum|int|partial|infty)\b"))
            return true;

        // Math relations / operators between variables/numbers (e.g. a = 2, x + y = z, \Delta < 0)
        if (Regex.IsMatch(candidate, @"^[A-Za-z0-9\s.+\-*/^=<>~]+$") &&
            (candidate.Contains('^') || Regex.IsMatch(candidate, @"\b[A-Za-z0-9]+\s*[=<>≤≥≠≈~]\s*[A-Za-z0-9\-+]")))
            return true;

        return false;
    }
}
