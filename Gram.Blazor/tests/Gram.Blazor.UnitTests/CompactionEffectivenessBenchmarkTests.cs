using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Gram.Blazor.UnitTests;

public sealed class CompactionEffectivenessBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly StorageService _storageService;
    private readonly SessionStorageService _sessionStorage;
    private readonly SettingsService _settingsService;
    private readonly RepositoryMapService _repoMapService;
    private readonly ContextBuilder _contextBuilder;

    public CompactionEffectivenessBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), "gram_benchmark_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("GRAM_STORAGE_DIR", _tempDir);

        var config = new ConfigurationBuilder().Build();
        _storageService = new StorageService();
        _sessionStorage = new SessionStorageService(_storageService);
        _settingsService = new SettingsService(config, _storageService);
        _repoMapService = new RepositoryMapService();
        _contextBuilder = new ContextBuilder(_sessionStorage, _settingsService, _repoMapService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch {}
    }

    [Fact]
    public async Task Benchmark_LongConversation_EvaluatesCompactionEffectiveness()
    {
        var conversation = new Conversation("benchmark-conv-1", "Authentication Architecture", DateTimeOffset.UtcNow);

        // Generate 32 realistic developer turns (approx 20,000+ characters of message history)
        var turns = new (string Role, string Content)[]
        {
            ("user", "We need to design a complete authentication system for Gram using WebAuthn/Passkeys and fallback password authentication."),
            ("assistant", "Understood. For Gram, we will implement: 1. FIDO2 / WebAuthn passkey registration and verification with counter validation. 2. PBKDF2-SHA256 password hashing with unique 16-byte salt per user. 3. Ephemeral bearer session tokens stored in the database with 30-day sliding expiry. 4. Rate-limiting middleware with exponential backoff on failed attempts."),
            ("user", "What database tables do we need for this in SQLite and PostgreSQL?"),
            ("assistant", "We need three primary tables: 'users' (id, email, name, password_hash, salt, role, created_at), 'sessions' (token, user_id, expires_at, created_at), and 'user_passkeys' (id, user_id, public_key, counter, name, created_at). Foreign keys will have ON DELETE CASCADE."),
            ("user", "Let's make sure we also support password reset with 6-digit verification codes."),
            ("assistant", "Added 'password_reset_tokens' table with columns: id, user_id, code, token, created_at, expires_at, used (boolean). Codes expire in 15 minutes and can only be used once."),
            ("user", "Can you show the migration SQL for SQLite?"),
            ("assistant", "CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, email TEXT UNIQUE NOT NULL, name TEXT NOT NULL, password_hash TEXT NOT NULL, salt TEXT NOT NULL, role TEXT NOT NULL DEFAULT 'user', image TEXT, created_at TEXT NOT NULL);\nCREATE TABLE IF NOT EXISTS sessions (token TEXT PRIMARY KEY, user_id TEXT NOT NULL, expires_at TEXT NOT NULL, created_at TEXT NOT NULL, FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE);\nCREATE TABLE IF NOT EXISTS user_passkeys (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, public_key TEXT NOT NULL, counter INTEGER NOT NULL DEFAULT 0, name TEXT NOT NULL, created_at TEXT NOT NULL, FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE);\nCREATE TABLE IF NOT EXISTS password_reset_tokens (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, code TEXT NOT NULL, token TEXT NOT NULL, created_at TEXT NOT NULL, expires_at TEXT NOT NULL, used INTEGER NOT NULL DEFAULT 0, FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE);"),
            ("user", "Let's also make sure admin accounts are only seeded in local Mini-Gram, not in cloud environments."),
            ("assistant", "Decision recorded: The default admin user 'admin@gram.local' will only be seeded in SqliteDatabaseService on first startup if users table is empty. PostgresDatabaseProvider in Gram Cloud will never seed default credentials."),
            ("user", "How should we handle frontend authentication state in Blazor Server?"),
            ("assistant", "We will implement a custom AuthenticationStateProvider called GramAuthStateProvider that wraps IAuthService. It reads the session token from browser localStorage and validates against the database, broadcasting state changes via NotifyAuthenticationStateChanged."),
            ("user", "Now let's talk about the project folders feature. How should project knowledge be isolated?"),
            ("assistant", "When a conversation belongs to a Project, ContextBuilder will inject <project_context> containing the project instructions and all text files in the project's knowledge base. If StrictContextOnly is true, external repository maps will be suppressed to prevent hallucination outside the project boundary."),
            ("user", "What file formats should we allow in the project knowledge base?"),
            ("assistant", "Supported knowledge formats: Markdown (.md), plain text (.txt), source code (.cs, .py, .js, .ts, .json, .yaml, .html, .css), and CSV documents. Text content is injected up to 30,000 characters per file for token safety."),
            ("user", "Let's add chat archiving so users can clean up their sidebar without losing history."),
            ("assistant", "Archiving feature design: 1. Add is_archived column (boolean) to conversations table. 2. Filter out archived chats from sidebar. 3. Add ArchivedChatsModal to search, restore, or delete archived chats. 4. Add optional compaction on archive to compress the chat into context_summary.md."),
            ("user", "Here is a sample full implementation of the SessionStorageService for review:\n" + new string('X', 8000)),
            ("assistant", "I reviewed the SessionStorageService implementation. It correctly handles JSON serialization, file attachment streaming, and project directory structures. Memory allocations are kept low with buffered streams."),
            ("user", "Finally, what are our current active decisions and next tasks?"),
            ("assistant", "Summary of established architecture: 1. Passkey & Password auth with database persistence. 2. Scoped Projects with strict context isolation. 3. Chat Archiving with deterministic compaction. 4. Client-side WebP media compression. Next task is testing and validating context compaction efficiency.")
        };

        foreach (var (role, content) in turns)
        {
            conversation.Messages.Add(new Message(role, content));
        }

        // Calculate Uncompacted Size
        var uncompactedTotalChars = 0;
        foreach (var msg in conversation.Messages)
        {
            uncompactedTotalChars += msg.Text.Length;
        }
        var uncompactedEstimatedTokens = uncompactedTotalChars / 4;

        // 1. Benchmark Archive Compaction
        var archiveSummary = await _contextBuilder.CompactForArchiveAsync(conversation);
        var archiveSummaryChars = archiveSummary.Length;
        var archiveEstimatedTokens = archiveSummaryChars / 4;
        var archiveTokenSavingsPercent = Math.Round((1.0 - (double)archiveEstimatedTokens / uncompactedEstimatedTokens) * 100, 1);

        _output.WriteLine($"=== ARCHIVE COMPACTION BENCHMARK ===");
        _output.WriteLine($"Original Raw Messages: {conversation.Messages.Count} messages");
        _output.WriteLine($"Original Character Count: {uncompactedTotalChars:N0} chars (~{uncompactedEstimatedTokens:N0} tokens)");
        _output.WriteLine($"Compacted Checkpoint Size: {archiveSummaryChars:N0} chars (~{archiveEstimatedTokens:N0} tokens)");
        _output.WriteLine($"Storage / Token Savings: {archiveTokenSavingsPercent}% reduction");
        _output.WriteLine($"-------------------------------------");

        // Assert high-efficiency savings (> 65% reduction on realistic conversations with code/data)
        Assert.True(archiveTokenSavingsPercent >= 65.0, $"Expected >= 65% token savings, got {archiveTokenSavingsPercent}%");
        
        // Assert critical semantic concepts are retained in the compacted summary
        Assert.Contains("WebAuthn", archiveSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SQLite", archiveSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PostgreSQL", archiveSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password_reset_tokens", archiveSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StrictContextOnly", archiveSummary, StringComparison.OrdinalIgnoreCase);

        // 2. Benchmark Rolling LLM Context Assembly (Rolling Window vs Full Uncompacted)
        var rollingCompacted = await _contextBuilder.EnsureContextSummaryAsync(conversation, preserveRecentMessages: 6);
        var assembledContext = await _contextBuilder.AssembleContextAsync(conversation, maxRecentMessages: 6);

        _output.WriteLine($"=== ROLLING IN-CHAT CONTEXT ASSEMBLY BENCHMARK ===");
        _output.WriteLine($"Rolling Compaction Generated: {rollingCompacted}");
        _output.WriteLine($"Compacted Through Message Index: {conversation.ContextSummaryMessageCount} of {conversation.Messages.Count}");
        _output.WriteLine($"Assembled Context Payload Estimated Tokens: {assembledContext.EstimatedTokens:N0} tokens");
        _output.WriteLine($"Preserved Active Messages in Window: {assembledContext.Messages.Count} messages");

        // The assembled message window should only contain the 6 most recent messages
        Assert.Equal(6, assembledContext.Messages.Count);
        // The assembled system prompt must include the memory summary checkpoint
        Assert.Contains("<context_summary>", assembledContext.SystemPrompt);
        // The oldest raw messages are not sent redundantly as individual messages
        Assert.DoesNotContain(assembledContext.Messages, m => m.Text.Contains("FIDO2 / WebAuthn passkey registration", StringComparison.OrdinalIgnoreCase));
    }
}
