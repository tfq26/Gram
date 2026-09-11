using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public sealed record WorkspaceVerificationResult(
    bool Success,
    string ToolchainName,
    int ExitCode,
    IReadOnlyList<CompilerDiagnostic> Diagnostics,
    string RawOutput,
    long DurationMs);

public interface IWorkspaceDiagnosticService
{
    string? DetectToolchain(string workspacePath);
    Task<WorkspaceVerificationResult> VerifyAsync(string workspacePath, CancellationToken ct = default);
    Task<WorkspaceVerificationResult> VerifyTargetedAsync(string workspacePath, IEnumerable<string> modifiedFiles, CancellationToken ct = default);
}

public sealed class WorkspaceDiagnosticService : IWorkspaceDiagnosticService
{
    private readonly INativeProcessRunner _processRunner;

    public WorkspaceDiagnosticService(INativeProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new NativeProcessRunner();
    }

    public string? DetectToolchain(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            return null;

        try
        {
            // 1. .NET (sln or csproj in root or direct subdirectories)
            if (Directory.EnumerateFiles(workspacePath, "*.sln").Any() ||
                Directory.EnumerateFiles(workspacePath, "*.csproj", SearchOption.AllDirectories)
                    .Take(5)
                    .Any(f => !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/node_modules/")))
            {
                return "dotnet";
            }

            // 2. Rust
            if (File.Exists(Path.Combine(workspacePath, "Cargo.toml")))
            {
                return "cargo";
            }

            // 3. Go
            if (File.Exists(Path.Combine(workspacePath, "go.mod")))
            {
                return "go";
            }

            // 4. TypeScript / Node
            if (File.Exists(Path.Combine(workspacePath, "tsconfig.json")))
            {
                return "typescript";
            }

            if (File.Exists(Path.Combine(workspacePath, "package.json")))
            {
                return "npm";
            }

            // 5. Python
            if (File.Exists(Path.Combine(workspacePath, "pyproject.toml")) ||
                File.Exists(Path.Combine(workspacePath, "setup.py")) ||
                File.Exists(Path.Combine(workspacePath, "requirements.txt")))
            {
                return "python";
            }
        }
        catch {}

        return null;
    }

    public async Task<WorkspaceVerificationResult> VerifyAsync(string workspacePath, CancellationToken ct = default)
    {
        var toolchain = DetectToolchain(workspacePath);
        if (string.IsNullOrEmpty(toolchain))
        {
            return new WorkspaceVerificationResult(
                Success: true,
                ToolchainName: "None",
                ExitCode: 0,
                Diagnostics: [],
                RawOutput: "No automated toolchain detected for workspace.",
                DurationMs: 0);
        }

        var command = ResolveVerificationCommand(toolchain, workspacePath);
        if (string.IsNullOrEmpty(command))
        {
            return new WorkspaceVerificationResult(
                Success: true,
                ToolchainName: toolchain,
                ExitCode: 0,
                Diagnostics: [],
                RawOutput: "No verification command configured.",
                DurationMs: 0);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var procResult = await _processRunner.ExecuteAsync(new ProcessExecutionOptions
            {
                CommandLine = command,
                WorkingDirectory = workspacePath,
                WorkspaceRoot = workspacePath,
                TimeoutSeconds = 20,
                ScrubEnvironment = false,
                RunId = "verify-" + Guid.NewGuid().ToString("N")[..8]
            }, ct).ConfigureAwait(false);

            sw.Stop();

            var combined = procResult.CombinedOutput;
            var diagnostics = CompilerDiagnosticParser.Parse(combined);
            var errorCount = diagnostics.Count(d => d.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase));

            // If exit code is 0 and no error diagnostics, verification succeeded
            var success = procResult.ExitCode == 0 && errorCount == 0;

            return new WorkspaceVerificationResult(
                Success: success,
                ToolchainName: toolchain,
                ExitCode: procResult.ExitCode,
                Diagnostics: diagnostics,
                RawOutput: combined,
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new WorkspaceVerificationResult(
                Success: false,
                ToolchainName: toolchain,
                ExitCode: -1,
                Diagnostics: [new CompilerDiagnostic(null, null, null, null, "ERROR", ex.Message, ex.Message)],
                RawOutput: ex.ToString(),
                DurationMs: sw.ElapsedMilliseconds);
        }
    }

    public async Task<WorkspaceVerificationResult> VerifyTargetedAsync(
        string workspacePath,
        IEnumerable<string> modifiedFiles,
        CancellationToken ct = default)
    {
        var toolchain = DetectToolchain(workspacePath);
        if (string.IsNullOrEmpty(toolchain))
        {
            return new WorkspaceVerificationResult(
                Success: true,
                ToolchainName: "None",
                ExitCode: 0,
                Diagnostics: [],
                RawOutput: "No automated toolchain detected for workspace.",
                DurationMs: 0);
        }

        var filesList = modifiedFiles.ToList();
        var testCommand = ResolveTargetedTestCommand(toolchain, workspacePath, filesList);
        if (string.IsNullOrEmpty(testCommand))
        {
            // Fallback to general compile check
            return await VerifyAsync(workspacePath, ct).ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var procResult = await _processRunner.ExecuteAsync(new ProcessExecutionOptions
            {
                CommandLine = testCommand,
                WorkingDirectory = workspacePath,
                WorkspaceRoot = workspacePath,
                TimeoutSeconds = 30,
                ScrubEnvironment = false,
                RunId = "verify-test-" + Guid.NewGuid().ToString("N")[..8]
            }, ct).ConfigureAwait(false);

            sw.Stop();

            var combined = procResult.CombinedOutput;
            var diagnostics = CompilerDiagnosticParser.Parse(combined);

            if (procResult.ExitCode != 0 && !diagnostics.Any(d => d.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics = diagnostics.Concat([
                    new CompilerDiagnostic(null, null, null, "TEST_RUNNER_ERROR", "ERROR", $"Targeted test command '{testCommand}' failed with exit code {procResult.ExitCode}.", combined)
                ]).ToList();
            }

            var errorCount = diagnostics.Count(d => d.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase));
            var success = procResult.ExitCode == 0 && errorCount == 0;

            return new WorkspaceVerificationResult(
                Success: success,
                ToolchainName: $"{toolchain}:test",
                ExitCode: procResult.ExitCode,
                Diagnostics: diagnostics,
                RawOutput: combined,
                DurationMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new WorkspaceVerificationResult(
                Success: false,
                ToolchainName: $"{toolchain}:test",
                ExitCode: -1,
                Diagnostics: [new CompilerDiagnostic(null, null, null, null, "ERROR", ex.Message, ex.Message)],
                RawOutput: ex.ToString(),
                DurationMs: sw.ElapsedMilliseconds);
        }
    }

    public static string? ResolveTargetedTestCommand(string toolchain, string workspacePath, IReadOnlyList<string> modifiedFiles)
    {
        if (modifiedFiles.Count == 0) return null;

        var targetNames = modifiedFiles
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetNames.Count == 0) return null;

        return toolchain switch
        {
            "dotnet" => ResolveDotNetTargetedTestCommand(targetNames),
            "typescript" or "npm" => ResolveNodeTargetedTestCommand(targetNames),
            "cargo" => $"cargo test {targetNames[0]}",
            "go" => $"go test -run {targetNames[0]} ./...",
            "python" => $"python3 -m unittest discover -p \"*{targetNames[0]}*\" -q",
            _ => null
        };
    }

    private static string? ResolveDotNetTargetedTestCommand(IReadOnlyList<string> targetNames)
    {
        var testPattern = targetNames.FirstOrDefault(t => t.EndsWith("Test", StringComparison.OrdinalIgnoreCase) || t.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
            ?? targetNames[0];

        return $"dotnet test --filter \"FullyQualifiedName~{testPattern}\" --no-restore -clp:NoSummary -v:q";
    }

    private static string? ResolveNodeTargetedTestCommand(IReadOnlyList<string> targetNames)
    {
        var target = targetNames[0];
        return $"npm test --if-present -- -t \"{target}\"";
    }

    private static string ResolveVerificationCommand(string toolchain, string workspacePath)
    {
        return toolchain switch
        {
            "dotnet" => "dotnet build --no-restore -clp:NoSummary -v:q /p:GenerateFullPaths=true",
            "typescript" => "npx --no-install tsc --noEmit",
            "npm" => ResolveNpmScript(workspacePath),
            "cargo" => "cargo check --message-format=short",
            "go" => "go build ./...",
            "python" => "python3 -m compileall -q .",
            _ => ""
        };
    }

    private static string ResolveNpmScript(string workspacePath)
    {
        try
        {
            var pkgJson = Path.Combine(workspacePath, "package.json");
            if (File.Exists(pkgJson))
            {
                var text = File.ReadAllText(pkgJson);
                var doc = JsonNode.Parse(text);
                var scripts = doc?["scripts"]?.AsObject();
                if (scripts != null)
                {
                    if (scripts.ContainsKey("typecheck")) return "npm run typecheck";
                    if (scripts.ContainsKey("check")) return "npm run check";
                    if (scripts.ContainsKey("build")) return "npm run build";
                }
            }
        }
        catch {}

        return "npm test --if-present";
    }
}
