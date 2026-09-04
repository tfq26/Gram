using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Gram.Blazor.Shared;

public sealed class SessionStorageService
{
    private readonly StorageService _storageService;
    private readonly string _conversationsBaseDir;
    private readonly string _projectsBaseDir;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SessionStorageService(StorageService storageService)
    {
        _storageService = storageService;
        _conversationsBaseDir = Path.Combine(_storageService.DataDirectory, "conversations");
        _projectsBaseDir = Path.Combine(_storageService.DataDirectory, "projects");
        Directory.CreateDirectory(_conversationsBaseDir);
        Directory.CreateDirectory(_projectsBaseDir);
    }

    private SemaphoreSlim GetLock(string conversationId) =>
        _sessionLocks.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1));

    public string GetConversationDirectory(string conversationId) =>
        Path.Combine(_conversationsBaseDir, SanitizeFileName(conversationId));

    public string GetAttachmentsDirectory(string conversationId) =>
        Path.Combine(GetConversationDirectory(conversationId), "attachments");

    public string GetAttachmentPath(string conversationId, string fileName) =>
        Path.Combine(GetAttachmentsDirectory(conversationId), SanitizeFileName(fileName));

    public string GetArtifactsDirectory(string conversationId) =>
        Path.Combine(GetConversationDirectory(conversationId), "artifacts");

    public string GetArtifactPath(string conversationId, string fileName) =>
        Path.Combine(GetArtifactsDirectory(conversationId), SanitizeFileName(fileName));

    public void EnsureConversationDirectories(string conversationId)
    {
        var convDir = GetConversationDirectory(conversationId);
        Directory.CreateDirectory(convDir);
        Directory.CreateDirectory(GetAttachmentsDirectory(conversationId));
        Directory.CreateDirectory(GetArtifactsDirectory(conversationId));
    }

    public async Task SaveConversationMetaAsync(Conversation conv)
    {
        EnsureConversationDirectories(conv.Id);
        var meta = new ConversationMeta
        {
            Id = conv.Id,
            Title = conv.Title,
            Created = conv.Created,
            Updated = conv.Updated,
            ModelId = conv.ModelId,
            ProjectId = conv.ProjectId,
            IsArchived = conv.IsArchived,
            SystemPromptOverride = conv.SystemPromptOverride,
            Rules = conv.Rules,
            MessageCount = conv.Messages.Count,
            AttachmentCount = conv.Attachments.Count,
            ArtifactCount = conv.Artifacts.Count,
            Version = conv.Version
        };

        var metaPath = Path.Combine(GetConversationDirectory(conv.Id), "meta.json");
        var dataPath = Path.Combine(GetConversationDirectory(conv.Id), "conversation.json");
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        var fullJson = JsonSerializer.Serialize(conv, JsonOptions);
        var sem = GetLock(conv.Id);
        await sem.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(metaPath, json);
            await File.WriteAllTextAsync(dataPath, fullJson);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<ConversationMeta?> LoadConversationMetaAsync(string conversationId)
    {
        var metaPath = Path.Combine(GetConversationDirectory(conversationId), "meta.json");
        if (!File.Exists(metaPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(metaPath);
            return JsonSerializer.Deserialize<ConversationMeta>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error reading meta for {conversationId}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<ConversationMeta>> ListAllConversationMetasAsync()
    {
        var result = new List<ConversationMeta>();
        if (!Directory.Exists(_conversationsBaseDir)) return result;

        var dirs = Directory.GetDirectories(_conversationsBaseDir);
        foreach (var dir in dirs)
        {
            var id = Path.GetFileName(dir);
            var meta = await LoadConversationMetaAsync(id);
            if (meta != null)
            {
                result.Add(meta);
            }
        }

        return result.OrderByDescending(m => m.Updated).ToList();
    }

    public async Task AppendTranscriptEntryAsync(string conversationId, TranscriptEntry entry)
    {
        EnsureConversationDirectories(conversationId);
        var transcriptPath = Path.Combine(GetConversationDirectory(conversationId), "transcript.jsonl");
        var jsonLine = JsonSerializer.Serialize(entry) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(jsonLine);

        var sem = GetLock(conversationId);
        await sem.WaitAsync();
        try
        {
            await using var fs = new FileStream(transcriptPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, true);
            await fs.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error appending transcript for {conversationId}: {ex.Message}");
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<List<TranscriptEntry>> LoadTranscriptAsync(string conversationId)
    {
        var transcriptPath = Path.Combine(GetConversationDirectory(conversationId), "transcript.jsonl");
        var entries = new List<TranscriptEntry>();
        if (!File.Exists(transcriptPath)) return entries;

        var sem = GetLock(conversationId);
        await sem.WaitAsync();
        try
        {
            using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<TranscriptEntry>(line, JsonOptions);
                    if (entry != null) entries.Add(entry);
                }
                catch {}
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error reading transcript for {conversationId}: {ex.Message}");
        }
        finally
        {
            sem.Release();
        }

        return entries;
    }

    public async Task<Conversation> LoadFullConversationAsync(string conversationId)
    {
        EnsureConversationDirectories(conversationId);
        var meta = await LoadConversationMetaAsync(conversationId);
        var conv = new Conversation
        {
            Id = conversationId,
            Title = meta?.Title ?? "New chat",
            Created = meta?.Created ?? DateTimeOffset.UtcNow,
            Updated = meta?.Updated ?? DateTimeOffset.UtcNow,
            ModelId = meta?.ModelId,
            ProjectId = meta?.ProjectId,
            IsArchived = meta?.IsArchived ?? false,
            SystemPromptOverride = meta?.SystemPromptOverride ?? "",
            Rules = meta?.Rules ?? "",
            Version = meta?.Version ?? 1
        };

        // 1. Try loading full conversation.json first
        var dataPath = Path.Combine(GetConversationDirectory(conversationId), "conversation.json");
        if (File.Exists(dataPath))
        {
            try
            {
                var fullJson = await File.ReadAllTextAsync(dataPath);
                var full = JsonSerializer.Deserialize<Conversation>(fullJson, JsonOptions);
                if (full != null && full.Messages is { Count: > 0 })
                {
                    conv.Messages = full.Messages;
                    conv.SystemPromptOverride = full.SystemPromptOverride ?? conv.SystemPromptOverride;
                    conv.Rules = full.Rules ?? conv.Rules;
                    conv.ModelId = full.ModelId ?? conv.ModelId;
                    conv.ProjectId = full.ProjectId ?? conv.ProjectId;
                    conv.IsArchived = full.IsArchived;
                    conv.Title = full.Title ?? conv.Title;
                    conv.Version = full.Version;
                    conv.Updated = full.Updated;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionStorage] Error reading conversation.json for {conversationId}: {ex.Message}");
            }
        }

        // 2. Fallback to Transcript if Messages are still empty
        if (conv.Messages.Count == 0)
        {
            var entries = await LoadTranscriptAsync(conversationId);
            if (entries.Count > 0)
            {
                foreach (var entry in entries)
                {
                    if (entry.Type == TranscriptEntryType.USER_INPUT)
                    {
                        conv.Messages.Add(new Message("user", entry.Content ?? "")
                        {
                            Timestamp = entry.Timestamp,
                            Mentions = entry.Mentions ?? []
                        });
                    }
                    else if (entry.Type == TranscriptEntryType.MODEL_RESPONSE)
                    {
                        conv.Messages.Add(new Message("assistant", entry.Content ?? "")
                        {
                            Timestamp = entry.Timestamp,
                            Thinking = entry.Thinking
                        });
                    }
                }
            }
        }

        // 2. Load Attachments
        var attDir = GetAttachmentsDirectory(conversationId);
        if (Directory.Exists(attDir))
        {
            foreach (var file in Directory.GetFiles(attDir))
            {
                var fi = new FileInfo(file);
                conv.Attachments.Add(new AttachmentInfo
                {
                    FileName = fi.Name,
                    SizeBytes = fi.Length,
                    CreatedAt = fi.CreationTimeUtc,
                    RelativePath = Path.Combine("attachments", fi.Name),
                    IsText = IsTextFile(fi.Name)
                });
            }
        }

        // 3. Load Artifacts
        var artDir = GetArtifactsDirectory(conversationId);
        if (Directory.Exists(artDir))
        {
            foreach (var file in Directory.GetFiles(artDir))
            {
                var fi = new FileInfo(file);
                conv.Artifacts.Add(new ArtifactInfo
                {
                    FileName = fi.Name,
                    Title = Path.GetFileNameWithoutExtension(fi.Name),
                    CreatedAt = fi.CreationTimeUtc,
                    RelativePath = Path.Combine("artifacts", fi.Name),
                    Language = Path.GetExtension(fi.Name).TrimStart('.')
                });
            }
        }

        // 4. Load Context Summary
        var summaryPath = Path.Combine(GetConversationDirectory(conversationId), "context_summary.md");
        if (File.Exists(summaryPath))
        {
            conv.ContextSummary = await File.ReadAllTextAsync(summaryPath);
        }

        return conv;
    }

    public async Task<AttachmentInfo> SaveAttachmentAsync(string conversationId, string fileName, Stream contentStream, string contentType = "application/octet-stream")
    {
        EnsureConversationDirectories(conversationId);
        var safeName = SanitizeFileName(fileName);
        var targetPath = Path.Combine(GetAttachmentsDirectory(conversationId), safeName);

        await using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await contentStream.CopyToAsync(fs);

        var fi = new FileInfo(targetPath);
        return new AttachmentInfo
        {
            FileName = safeName,
            ContentType = contentType,
            SizeBytes = fi.Length,
            CreatedAt = DateTimeOffset.UtcNow,
            RelativePath = Path.Combine("attachments", safeName),
            IsText = IsTextFile(safeName)
        };
    }

    public async Task<string?> ReadAttachmentTextAsync(string conversationId, string fileName)
    {
        var targetPath = Path.Combine(GetAttachmentsDirectory(conversationId), SanitizeFileName(fileName));
        if (!File.Exists(targetPath) || !IsTextFile(fileName)) return null;

        return await File.ReadAllTextAsync(targetPath, Encoding.UTF8);
    }

    public async Task<ArtifactInfo> SaveArtifactAsync(string conversationId, string fileName, string content, string? title = null, string? description = null)
    {
        EnsureConversationDirectories(conversationId);
        var safeName = SanitizeFileName(fileName);
        var targetPath = Path.Combine(GetArtifactsDirectory(conversationId), safeName);

        await File.WriteAllTextAsync(targetPath, content, Encoding.UTF8);

        return new ArtifactInfo
        {
            FileName = safeName,
            Title = title ?? Path.GetFileNameWithoutExtension(safeName),
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
            RelativePath = Path.Combine("artifacts", safeName),
            Language = Path.GetExtension(safeName).TrimStart('.')
        };
    }

    public async Task<string?> ReadArtifactTextAsync(string conversationId, string fileName)
    {
        var targetPath = Path.Combine(GetArtifactsDirectory(conversationId), SanitizeFileName(fileName));
        if (!File.Exists(targetPath)) return null;

        return await File.ReadAllTextAsync(targetPath, Encoding.UTF8);
    }

    public async Task SaveContextSummaryAsync(string conversationId, string summaryContent)
    {
        EnsureConversationDirectories(conversationId);
        var targetPath = Path.Combine(GetConversationDirectory(conversationId), "context_summary.md");
        await File.WriteAllTextAsync(targetPath, summaryContent, Encoding.UTF8);
    }

    public async Task<string?> GetContextSummaryAsync(string conversationId)
    {
        var targetPath = Path.Combine(GetConversationDirectory(conversationId), "context_summary.md");
        if (!File.Exists(targetPath)) return null;

        return await File.ReadAllTextAsync(targetPath, Encoding.UTF8);
    }

    public async Task DeleteConversationAsync(string conversationId)
    {
        var convDir = GetConversationDirectory(conversationId);
        var sem = GetLock(conversationId);
        await sem.WaitAsync();
        try
        {
            if (Directory.Exists(convDir))
            {
                Directory.Delete(convDir, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error deleting conversation {conversationId}: {ex.Message}");
        }
        finally
        {
            sem.Release();
            _sessionLocks.TryRemove(conversationId, out _);
        }
    }

    public string GetProjectDirectory(string projectId) =>
        Path.Combine(_projectsBaseDir, SanitizeFileName(projectId));

    public string GetProjectFilesDirectory(string projectId) =>
        Path.Combine(GetProjectDirectory(projectId), "files");

    public void EnsureProjectDirectories(string projectId)
    {
        var projDir = GetProjectDirectory(projectId);
        Directory.CreateDirectory(projDir);
        Directory.CreateDirectory(GetProjectFilesDirectory(projectId));
    }

    public async Task SaveProjectAsync(Project project)
    {
        EnsureProjectDirectories(project.Id);
        var projectPath = Path.Combine(GetProjectDirectory(project.Id), "project.json");
        var json = JsonSerializer.Serialize(project, JsonOptions);
        await File.WriteAllTextAsync(projectPath, json, Encoding.UTF8);
    }

    public async Task<Project?> LoadProjectAsync(string projectId)
    {
        var projectPath = Path.Combine(GetProjectDirectory(projectId), "project.json");
        if (!File.Exists(projectPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(projectPath, Encoding.UTF8);
            var project = JsonSerializer.Deserialize<Project>(json, JsonOptions);
            if (project != null)
            {
                var filesDir = GetProjectFilesDirectory(projectId);
                if (Directory.Exists(filesDir))
                {
                    project.Files.Clear();
                    foreach (var file in Directory.GetFiles(filesDir))
                    {
                        var fi = new FileInfo(file);
                        project.Files.Add(new ProjectFileInfo
                        {
                            FileName = fi.Name,
                            SizeBytes = fi.Length,
                            CreatedAt = fi.CreationTimeUtc,
                            RelativePath = Path.Combine("files", fi.Name),
                            IsText = IsTextFile(fi.Name)
                        });
                    }
                }
            }
            return project;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error loading project {projectId}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<Project>> ListAllProjectsAsync()
    {
        var list = new List<Project>();
        if (!Directory.Exists(_projectsBaseDir)) return list;

        var dirs = Directory.GetDirectories(_projectsBaseDir);
        foreach (var dir in dirs)
        {
            var id = Path.GetFileName(dir);
            var project = await LoadProjectAsync(id);
            if (project != null) list.Add(project);
        }
        return list.OrderByDescending(p => p.Updated).ToList();
    }

    public async Task<ProjectFileInfo> SaveProjectFileAsync(string projectId, string fileName, Stream contentStream, string contentType = "text/plain")
    {
        EnsureProjectDirectories(projectId);
        var safeName = SanitizeFileName(fileName);
        var targetPath = Path.Combine(GetProjectFilesDirectory(projectId), safeName);

        await using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await contentStream.CopyToAsync(fs);

        var fi = new FileInfo(targetPath);
        return new ProjectFileInfo
        {
            FileName = safeName,
            ContentType = contentType,
            SizeBytes = fi.Length,
            CreatedAt = DateTimeOffset.UtcNow,
            RelativePath = Path.Combine("files", safeName),
            IsText = IsTextFile(safeName)
        };
    }

    public async Task<string?> ReadProjectFileTextAsync(string projectId, string fileName)
    {
        var targetPath = Path.Combine(GetProjectFilesDirectory(projectId), SanitizeFileName(fileName));
        if (!File.Exists(targetPath) || !IsTextFile(fileName)) return null;

        return await File.ReadAllTextAsync(targetPath, Encoding.UTF8);
    }

    public Task<bool> DeleteProjectFileAsync(string projectId, string fileName)
    {
        var targetPath = Path.Combine(GetProjectFilesDirectory(projectId), SanitizeFileName(fileName));
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task DeleteProjectAsync(string projectId)
    {
        var projDir = GetProjectDirectory(projectId);
        try
        {
            if (Directory.Exists(projDir))
            {
                Directory.Delete(projDir, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Error deleting project {projectId}: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public async Task MigrateLegacyConversationsAsync(string legacyJsonPath)
    {
        if (!File.Exists(legacyJsonPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(legacyJsonPath);
            var legacyList = JsonSerializer.Deserialize<List<Conversation>>(json, JsonOptions);
            if (legacyList == null || legacyList.Count == 0) return;

            foreach (var conv in legacyList)
            {
                var convDir = GetConversationDirectory(conv.Id);
                if (Directory.Exists(convDir) && File.Exists(Path.Combine(convDir, "meta.json")))
                {
                    continue; // Already migrated
                }

                EnsureConversationDirectories(conv.Id);
                await SaveConversationMetaAsync(conv);

                int step = 1;
                foreach (var msg in conv.Messages)
                {
                    var entryType = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                        ? TranscriptEntryType.USER_INPUT
                        : TranscriptEntryType.MODEL_RESPONSE;

                    await AppendTranscriptEntryAsync(conv.Id, new TranscriptEntry
                    {
                        Step = step++,
                        Type = entryType,
                        Timestamp = msg.Timestamp,
                        Role = msg.Role,
                        Content = msg.Text
                    });
                }
            }

            Console.WriteLine($"[SessionStorage] Successfully migrated {legacyList.Count} conversations to directory storage.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SessionStorage] Migration warning: {ex.Message}");
        }
    }

    private static bool IsTextFile(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".txt" or ".md" or ".json" or ".cs" or ".js" or ".ts" or ".html" or ".css" or ".razor"
            or ".py" or ".cpp" or ".c" or ".h" or ".sql" or ".xml" or ".yaml" or ".yml" or ".csv" or ".sh" or ".bat" or ".ps1";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }
}
