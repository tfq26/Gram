using System.Diagnostics;
using System.Security;
using System.Text;

namespace Gram.Blazor.Shared;

public sealed record MentionResolutionResult(string Context, string? ForcedWebSearchQuery)
{
    public static readonly MentionResolutionResult Empty = new("", null);
}

public sealed class MentionResolver
{
    private readonly ToolboxManagerService _toolbox;
    private readonly SessionStorageService _sessionStorage;

    public MentionResolver(ToolboxManagerService toolbox, SessionStorageService sessionStorage)
    {
        _toolbox = toolbox;
        _sessionStorage = sessionStorage;
    }

    public async Task<MentionResolutionResult> ResolveAsync(
        IReadOnlyList<MentionReference>? mentions,
        Conversation conversation,
        string workspacePath,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (mentions is null || mentions.Count == 0) return MentionResolutionResult.Empty;

        var root = Path.GetFullPath(workspacePath);
        var context = new StringBuilder();
        string? forcedWebSearchQuery = null;

        foreach (var mention in mentions.DistinctBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Take(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (mention.Kind)
            {
                case MentionKind.File:
                    await AppendAttachmentAsync(context, conversation, mention.Target, cancellationToken);
                    break;
                case MentionKind.Folder:
                    // Folder discovery is intentionally unsupported until Gram has an
                    // explicit user-authorized workspace selection flow.
                    break;
                case MentionKind.GitDiff:
                    await AppendGitDiffAsync(context, root, cancellationToken);
                    break;
                case MentionKind.GitBranch:
                    await AppendGitBranchAsync(context, root, cancellationToken);
                    break;
                case MentionKind.Web:
                    forcedWebSearchQuery = string.IsNullOrWhiteSpace(mention.Query) ? userPrompt : mention.Query.Trim();
                    break;
                case MentionKind.Toolbox:
                    AppendToolbox(context);
                    break;
                case MentionKind.Docs:
                    await AppendAttachedDocsAsync(context, conversation, cancellationToken);
                    break;
                case MentionKind.Artifact:
                    AppendArtifact(context, conversation, mention.Target);
                    break;
            }

            if (context.Length > 48000) break;
        }

        return new MentionResolutionResult(Trim(context.ToString(), 48000), forcedWebSearchQuery);
    }

    private async Task AppendAttachmentAsync(StringBuilder context, Conversation conversation, string attachmentTarget, CancellationToken cancellationToken)
    {
        var attachment = conversation.Attachments.FirstOrDefault(item =>
            item.Id.Equals(attachmentTarget, StringComparison.OrdinalIgnoreCase)
            || item.FileName.Equals(attachmentTarget, StringComparison.OrdinalIgnoreCase));
        if (attachment is null) return;

        context.AppendLine($"<mentioned_file name=\"{Escape(attachment.FileName)}\" source=\"user_attachment\">");
        if (attachment.IsText)
        {
            var content = await _sessionStorage.ReadAttachmentTextAsync(conversation.Id, attachment.FileName);
            if (!string.IsNullOrWhiteSpace(content)) context.AppendLine(Escape(Trim(content, 24000)));
        }
        else
        {
            context.AppendLine($"User-authorized attachment metadata: type={Escape(attachment.ContentType)}, size={attachment.SizeBytes} bytes.");
        }
        context.AppendLine("</mentioned_file>");
    }

    private static async Task AppendGitDiffAsync(StringBuilder context, string root, CancellationToken cancellationToken)
    {
        var status = await RunGitAsync(root, cancellationToken, "status", "--short");
        var diff = await RunGitAsync(root, cancellationToken, "diff", "--no-ext-diff", "--unified=3");
        context.AppendLine("<mentioned_git_diff>");
        if (!string.IsNullOrWhiteSpace(status)) context.AppendLine(Escape($"Status:\n{Trim(status, 5000)}"));
        if (!string.IsNullOrWhiteSpace(diff)) context.AppendLine(Escape($"Diff:\n{Trim(diff, 26000)}"));
        if (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(diff)) context.AppendLine("Working tree is clean or Git data is unavailable.");
        context.AppendLine("</mentioned_git_diff>");
    }

    private static async Task AppendGitBranchAsync(StringBuilder context, string root, CancellationToken cancellationToken)
    {
        var branch = await RunGitAsync(root, cancellationToken, "status", "--short", "--branch");
        var log = await RunGitAsync(root, cancellationToken, "log", "-6", "--date=short", "--pretty=format:%h %ad %s");
        context.AppendLine("<mentioned_git_branch>");
        context.AppendLine(Escape(Trim($"{branch}\n\nRecent commits:\n{log}", 12000)));
        context.AppendLine("</mentioned_git_branch>");
    }

    private void AppendToolbox(StringBuilder context)
    {
        context.AppendLine("<mentioned_toolbox>");
        foreach (var tool in _toolbox.InstalledTools.Where(tool => tool.IsEnabled).Take(30))
        {
            context.AppendLine($"- {Escape(tool.Name)} [{Escape(tool.Category)}]: {Escape(tool.Description)}");
        }
        foreach (var server in _toolbox.McpServers.Where(server => server.IsEnabled).Take(15))
        {
            context.AppendLine($"- MCP: {Escape(server.Name)} ({Escape(server.Transport)}, {server.ToolCount} tools)");
        }
        context.AppendLine("</mentioned_toolbox>");
    }

    private async Task AppendAttachedDocsAsync(StringBuilder context, Conversation conversation, CancellationToken cancellationToken)
    {
        var docs = conversation.Attachments
            .Where(attachment => attachment.IsText &&
                (Path.GetExtension(attachment.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
                 || Path.GetExtension(attachment.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                 || Path.GetFileName(attachment.FileName).StartsWith("README", StringComparison.OrdinalIgnoreCase)))
            .Take(10)
            .ToList();

        if (docs.Count == 0) return;
        context.AppendLine("<mentioned_attached_docs>");
        var remaining = 24000;
        foreach (var attachment in docs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await _sessionStorage.ReadAttachmentTextAsync(conversation.Id, attachment.FileName) ?? "";
            var excerpt = Trim(content, Math.Min(6000, remaining));
            context.AppendLine($"<doc name=\"{Escape(attachment.FileName)}\" source=\"user_attachment\">");
            context.AppendLine(Escape(excerpt));
            context.AppendLine("</doc>");
            remaining -= excerpt.Length;
            if (remaining <= 0) break;
        }
        context.AppendLine("</mentioned_attached_docs>");
    }

    private static void AppendArtifact(StringBuilder context, Conversation conversation, string artifactId)
    {
        var artifact = conversation.Artifacts.FirstOrDefault(item =>
            item.Id.Equals(artifactId, StringComparison.OrdinalIgnoreCase)
            || item.FileName.Equals(artifactId, StringComparison.OrdinalIgnoreCase));
        if (artifact is null) return;

        context.AppendLine($"<mentioned_artifact name=\"{Escape(artifact.FileName)}\" language=\"{Escape(artifact.Language ?? "text")}\">");
        context.AppendLine(Escape(Trim(artifact.Content, 24000)));
        context.AppendLine("</mentioned_artifact>");
    }

    private static async Task<string> RunGitAsync(string root, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? output.Trim() : "";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return "";
        }
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";

    private static string Trim(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var clean = value.Trim();
        return clean.Length <= maxCharacters ? clean : clean[..maxCharacters] + "\n...[mention context truncated]";
    }
}
