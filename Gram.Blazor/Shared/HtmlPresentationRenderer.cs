using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed class HtmlPresentationRenderer
{
    private readonly StorageService _storage;
    private readonly ILogger<HtmlPresentationRenderer> _logger;

    public HtmlPresentationRenderer(StorageService storage, ILogger<HtmlPresentationRenderer> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<string> RenderAsync(PresentationAssemblyInput input, CancellationToken cancellationToken = default)
    {
        var directory = _storage.GetFilePath(Path.Combine("presentations", input.ProjectId));
        var assetsDirectory = Path.Combine(directory, "assets");
        Directory.CreateDirectory(assetsDirectory);
        var assetUrls = new List<string>();
        foreach (var asset in input.Assets.Where(asset => File.Exists(asset.Path)))
        {
            var safeName = SanitizeFileName(Path.GetFileName(asset.Path));
            var target = Path.Combine(assetsDirectory, safeName);
            await using var source = File.OpenRead(asset.Path);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination, cancellationToken);
            assetUrls.Add($"assets/{Uri.EscapeDataString(safeName)}");
        }

        var slides = new StringBuilder();
        for (var index = 0; index < input.Sections.Count; index++)
        {
            var section = input.Sections[index];
            var content = ParseContent(section.ContentJson);
            slides.AppendLine($"<section class=\"slide {(index == 0 ? "active" : "hidden")}\" data-slide=\"{index}\">");
            slides.AppendLine($"  <div class=\"slide-container\">");
            slides.AppendLine($"    <div class=\"slide-kicker\">{Html(section.Title)}</div>");
            slides.AppendLine($"    <h2 class=\"slide-title\">{Html(content.TakeawayTitle ?? section.Title)}</h2>");
            if (!string.IsNullOrWhiteSpace(content.Subtitle))
            {
                slides.AppendLine($"    <p class=\"slide-subtitle\">{Html(content.Subtitle)}</p>");
            }
            if (content.BodyPoints.Count > 0)
            {
                slides.AppendLine("    <div class=\"slide-points\">");
                foreach (var point in content.BodyPoints.Take(6))
                {
                    slides.AppendLine($"      <div class=\"slide-point-item\"><span class=\"point-bullet\"></span><span>{Html(point)}</span></div>");
                }
                slides.AppendLine("    </div>");
            }
            if (!string.IsNullOrWhiteSpace(content.VisualBrief))
            {
                slides.AppendLine($"    <div class=\"visual-brief-box\"><span class=\"brief-label\">Visual Concept</span><p class=\"brief-text\">{Html(content.VisualBrief)}</p></div>");
            }
            slides.AppendLine($"  </div>");
            slides.AppendLine($"</section>");
        }

        var sourceList = string.Join("", input.Sections.SelectMany(section => ParseContent(section.ContentJson).SourceIds).Distinct().Select(source => $"<span class=\"source-tag\">{Html(source)}</span>"));
        var htmlTemplate = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>__TITLE__ · Gram Presentation</title>
          <link rel="preconnect" href="https://fonts.googleapis.com">
          <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
          <link href="https://fonts.googleapis.com/css2?family=Geist+Mono:wght@100..900&family=Geist:wght@100..900&display=swap" rel="stylesheet">
          <style>
            :root {
              --bg-page: #09090b;
              --bg-card: rgba(24, 24, 27, 0.65);
              --border: rgba(255, 255, 255, 0.08);
              --text-main: #f4f4f5;
              --text-muted: #a1a1aa;
              --accent: #c084fc;
              --accent-dim: rgba(192, 132, 252, 0.15);
              --font-sans: 'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
              --font-mono: 'Geist Mono', ui-monospace, SFMono-Regular, monospace;
            }
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
              background: var(--bg-page);
              color: var(--text-main);
              font-family: var(--font-sans);
              min-height: 100vh;
              display: flex;
              flex-direction: column;
              overflow-x: hidden;
              background-image: 
                radial-gradient(ellipse 80% 50% at 50% -20%, rgba(120, 119, 198, 0.15), transparent),
                radial-gradient(circle at 100% 100%, rgba(147, 51, 234, 0.08), transparent);
            }
            header.deck-topbar {
              padding: 20px 32px;
              display: flex;
              align-items: center;
              justify-content: space-between;
              border-bottom: 1px solid var(--border);
              backdrop-filter: blur(12px);
              position: sticky;
              top: 0;
              z-index: 100;
            }
            .deck-brand {
              display: flex;
              align-items: center;
              gap: 10px;
            }
            .deck-brand-icon {
              width: 24px;
              height: 24px;
              border-radius: 6px;
              background: var(--accent-dim);
              color: var(--accent);
              display: flex;
              align-items: center;
              justify-content: center;
              font-size: 13px;
            }
            .deck-title-group {
              display: flex;
              flex-direction: column;
              gap: 2px;
            }
            .deck-top-title {
              font-size: 14px;
              font-weight: 600;
              color: var(--text-main);
            }
            .deck-top-meta {
              font-size: 11px;
              color: var(--text-muted);
              font-family: var(--font-mono);
            }
            main.deck-viewport {
              flex: 1;
              display: flex;
              align-items: center;
              justify-content: center;
              padding: 40px 24px 100px;
              max-width: 1080px;
              margin: 0 auto;
              width: 100%;
            }
            .slide {
              width: 100%;
              opacity: 0;
              transform: translateY(12px);
              transition: opacity 0.3s cubic-bezier(0.16, 1, 0.3, 1), transform 0.3s cubic-bezier(0.16, 1, 0.3, 1);
              display: none;
            }
            .slide.active {
              display: block;
              opacity: 1;
              transform: translateY(0);
            }
            .slide-container {
              background: var(--bg-card);
              border: 1px solid var(--border);
              border-radius: 16px;
              padding: 48px 56px;
              box-shadow: 0 20px 50px rgba(0, 0, 0, 0.4);
              backdrop-filter: blur(20px);
              position: relative;
            }
            .slide-kicker {
              font-family: var(--font-mono);
              font-size: 11.5px;
              font-weight: 600;
              text-transform: uppercase;
              letter-spacing: 0.08em;
              color: var(--accent);
              margin-bottom: 12px;
            }
            .slide-title {
              font-size: clamp(26px, 3.8vw, 42px);
              font-weight: 700;
              line-height: 1.18;
              color: var(--text-main);
              margin-bottom: 14px;
              letter-spacing: -0.02em;
            }
            .slide-subtitle {
              font-size: clamp(15px, 1.8vw, 18px);
              color: var(--text-muted);
              line-height: 1.5;
              margin-bottom: 28px;
            }
            .slide-points {
              display: flex;
              flex-direction: column;
              gap: 14px;
              margin-bottom: 28px;
            }
            .slide-point-item {
              display: flex;
              align-items: flex-start;
              gap: 14px;
              font-size: clamp(15px, 1.8vw, 18px);
              line-height: 1.55;
              color: #e4e4e7;
            }
            .point-bullet {
              width: 7px;
              height: 7px;
              border-radius: 50%;
              background: var(--accent);
              margin-top: 10px;
              flex-shrink: 0;
            }
            .visual-brief-box {
              background: rgba(0, 0, 0, 0.35);
              border: 1px solid var(--border);
              border-left: 3px solid var(--accent);
              border-radius: 8px;
              padding: 14px 18px;
              margin-top: 24px;
            }
            .brief-label {
              font-size: 10.5px;
              font-weight: 600;
              text-transform: uppercase;
              letter-spacing: 0.06em;
              color: var(--text-muted);
              display: block;
              margin-bottom: 4px;
              font-family: var(--font-mono);
            }
            .brief-text {
              font-size: 13px;
              color: var(--text-muted);
              line-height: 1.5;
            }
            /* Minimal Floating Bottom Toolbar */
            .deck-controls {
              position: fixed;
              bottom: 24px;
              left: 50%;
              transform: translateX(-50%);
              background: rgba(18, 18, 20, 0.85);
              border: 1px solid var(--border);
              backdrop-filter: blur(16px);
              padding: 6px 14px;
              border-radius: 9999px;
              display: flex;
              align-items: center;
              gap: 14px;
              box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5);
              z-index: 1000;
            }
            .deck-ctrl-btn {
              background: transparent;
              border: none;
              color: var(--text-main);
              width: 30px;
              height: 30px;
              border-radius: 50%;
              display: flex;
              align-items: center;
              justify-content: center;
              cursor: pointer;
              transition: background 0.15s ease;
            }
            .deck-ctrl-btn:hover {
              background: rgba(255, 255, 255, 0.1);
            }
            .deck-ctrl-btn:disabled {
              opacity: 0.3;
              cursor: not-allowed;
            }
            .deck-slide-counter {
              font-family: var(--font-mono);
              font-size: 12px;
              font-weight: 500;
              color: var(--text-muted);
              min-width: 50px;
              text-align: center;
            }
            .deck-shortcut-hint {
              font-family: var(--font-mono);
              font-size: 10.5px;
              color: var(--text-muted);
              padding: 2px 6px;
              background: rgba(255, 255, 255, 0.06);
              border-radius: 4px;
            }
            @media (max-width: 640px) {
              .slide-container { padding: 28px 20px; }
              header.deck-topbar { padding: 14px 18px; }
            }
          </style>
        </head>
        <body>
          <header class="deck-topbar">
            <div class="deck-brand">
              <div class="deck-brand-icon">📽️</div>
              <div class="deck-title-group">
                <span class="deck-top-title">__TITLE__</span>
                <span class="deck-top-meta">__AUDIENCE__ · __JOB__</span>
              </div>
            </div>
            <button type="button" class="deck-ctrl-btn" onclick="toggleFullscreen()" title="Fullscreen (F)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"></path>
              </svg>
            </button>
          </header>

          <main class="deck-viewport">
            __SLIDES__
          </main>

          <footer class="deck-controls" aria-label="Slide navigation">
            <button type="button" class="deck-ctrl-btn" id="prevBtn" onclick="changeSlide(-1)" title="Previous Slide (Left Arrow)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg>
            </button>
            <span class="deck-slide-counter" id="slideCounter">1 / 1</span>
            <button type="button" class="deck-ctrl-btn" id="nextBtn" onclick="changeSlide(1)" title="Next Slide (Right Arrow)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg>
            </button>
            <span class="deck-shortcut-hint">← / →</span>
          </footer>

          <script>
            const slides = [...document.querySelectorAll('.slide')];
            let current = 0;
            const counter = document.getElementById('slideCounter');
            const prevBtn = document.getElementById('prevBtn');
            const nextBtn = document.getElementById('nextBtn');

            function renderSlide(index) {
              current = Math.max(0, Math.min(slides.length - 1, index));
              slides.forEach((s, i) => s.classList.toggle('active', i === current));
              if (counter) counter.textContent = (current + 1) + ' / ' + slides.length;
              if (prevBtn) prevBtn.disabled = (current === 0);
              if (nextBtn) nextBtn.disabled = (current === slides.length - 1);
            }

            function changeSlide(delta) {
              renderSlide(current + delta);
            }

            function toggleFullscreen() {
              if (!document.fullscreenElement) {
                document.documentElement.requestFullscreen().catch(() => {});
              } else {
                document.exitFullscreen().catch(() => {});
              }
            }

            document.addEventListener('keydown', (e) => {
              if (e.key === 'ArrowRight' || e.key === ' ' || e.key === 'PageDown') {
                e.preventDefault();
                changeSlide(1);
              } else if (e.key === 'ArrowLeft' || e.key === 'PageUp') {
                e.preventDefault();
                changeSlide(-1);
              } else if (e.key === 'f' || e.key === 'F') {
                toggleFullscreen();
              }
            });

            renderSlide(0);
          </script>
        </body>
        </html>
        """;

        var html = htmlTemplate
            .Replace("__TITLE__", Html(input.Title), StringComparison.Ordinal)
            .Replace("__AUDIENCE__", Html(input.Audience), StringComparison.Ordinal)
            .Replace("__JOB__", Html(input.CommunicationJob), StringComparison.Ordinal)
            .Replace("__SLIDES__", slides.ToString(), StringComparison.Ordinal);
        var path = Path.Combine(directory, "index.html");
        await File.WriteAllTextAsync(path, html, Encoding.UTF8, cancellationToken);
        _logger.LogInformation("Rendered HTML presentation {ProjectId} at {Path} with {SlideCount} slides and {AssetCount} assets.", input.ProjectId, path, input.Sections.Count, assetUrls.Count);
        return path;
    }

    private static PresentationSlideContent ParseContent(string json)
    {
        try { return JsonSerializer.Deserialize<PresentationSlideContent>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
        catch { return new(); }
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string SanitizeFileName(string value) => Regex.Replace(string.IsNullOrWhiteSpace(value) ? "asset" : value, @"[^A-Za-z0-9._-]", "_");
}
