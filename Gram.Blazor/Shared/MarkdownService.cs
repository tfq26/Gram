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

    public MarkdownService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseEmojiAndSmiley()
            .UseAutoLinks()
            .Build();
    }

    public string RenderToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        try
        {
            var rawHtml = Markdown.ToHtml(markdown, _pipeline);

            // Enhance code blocks with custom header and copy button
            var processedHtml = CodeBlockRegex.Replace(rawHtml, match =>
            {
                var lang = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(lang)) lang = "code";
                var codeContent = match.Groups[2].Value;

                return $@"<div class=""code-block-wrapper"">
    <div class=""code-block-header"">
        <span class=""code-lang"">{lang}</span>
        <button class=""btn-copy-code"" onclick=""gramAudio.copyCode(this)"" title=""Copy code"">
            <svg width=""12"" height=""12"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                <rect x=""9"" y=""9"" width=""13"" height=""13"" rx=""2"" ry=""2""></rect>
                <path d=""M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1""></path>
            </svg>
            <span>Copy</span>
        </button>
    </div>
    <pre><code class=""language-{lang}"">{codeContent}</code></pre>
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
}
