using System.Text.Json.Nodes;

namespace Gram.Blazor.Shared;

public enum ProjectEcosystem
{
    DotNet,
    NodeJs,
    Rust,
    Python,
    Go,
    Unknown
}

public sealed record DetectedProjectValidation(
    ProjectEcosystem Ecosystem,
    string BuildCommand,
    string TestCommand,
    string ManifestPath,
    string ManifestFileName);

public static class ProjectValidationDetector
{
    public static DetectedProjectValidation? Detect(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return null;
        }

        // 1. .NET Solution or Project
        var slnFiles = Directory.GetFiles(workspacePath, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length > 0)
        {
            return new DetectedProjectValidation(
                Ecosystem: ProjectEcosystem.DotNet,
                BuildCommand: "dotnet build",
                TestCommand: "dotnet test",
                ManifestPath: slnFiles[0],
                ManifestFileName: Path.GetFileName(slnFiles[0]));
        }

        var csprojFiles = Directory.GetFiles(workspacePath, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .ToArray();

        if (csprojFiles.Length > 0)
        {
            return new DetectedProjectValidation(
                Ecosystem: ProjectEcosystem.DotNet,
                BuildCommand: "dotnet build",
                TestCommand: "dotnet test",
                ManifestPath: csprojFiles[0],
                ManifestFileName: Path.GetFileName(csprojFiles[0]));
        }

        // 2. Node.js (package.json)
        var pkgJsonPath = Path.Combine(workspacePath, "package.json");
        if (File.Exists(pkgJsonPath))
        {
            var hasTestScript = false;
            var hasBuildScript = false;
            try
            {
                var content = File.ReadAllText(pkgJsonPath);
                var node = JsonNode.Parse(content);
                var scripts = node?["scripts"]?.AsObject();
                if (scripts != null)
                {
                    hasTestScript = scripts.ContainsKey("test");
                    hasBuildScript = scripts.ContainsKey("build");
                }
            }
            catch { }

            var pkgManager = File.Exists(Path.Combine(workspacePath, "pnpm-lock.yaml")) ? "pnpm"
                : (File.Exists(Path.Combine(workspacePath, "yarn.lock")) ? "yarn" : "npm");

            var buildCmd = hasBuildScript ? $"{pkgManager} run build" : $"{pkgManager} test";
            var testCmd = hasTestScript ? $"{pkgManager} test" : $"{pkgManager} test";

            return new DetectedProjectValidation(
                Ecosystem: ProjectEcosystem.NodeJs,
                BuildCommand: buildCmd,
                TestCommand: testCmd,
                ManifestPath: pkgJsonPath,
                ManifestFileName: "package.json");
        }

        // 3. Rust (Cargo.toml)
        var cargoPath = Path.Combine(workspacePath, "Cargo.toml");
        if (File.Exists(cargoPath))
        {
            return new DetectedProjectValidation(
                Ecosystem: ProjectEcosystem.Rust,
                BuildCommand: "cargo check",
                TestCommand: "cargo test",
                ManifestPath: cargoPath,
                ManifestFileName: "Cargo.toml");
        }

        // 4. Python (pytest.ini, pyproject.toml, requirements.txt, setup.py)
        string[] pythonIndicators = ["pytest.ini", "pyproject.toml", "requirements.txt", "setup.py"];
        foreach (var ind in pythonIndicators)
        {
            var p = Path.Combine(workspacePath, ind);
            if (File.Exists(p))
            {
                return new DetectedProjectValidation(
                    Ecosystem: ProjectEcosystem.Python,
                    BuildCommand: "python -m py_compile",
                    TestCommand: "pytest",
                    ManifestPath: p,
                    ManifestFileName: ind);
            }
        }

        // 5. Go (go.mod)
        var goModPath = Path.Combine(workspacePath, "go.mod");
        if (File.Exists(goModPath))
        {
            return new DetectedProjectValidation(
                Ecosystem: ProjectEcosystem.Go,
                BuildCommand: "go build ./...",
                TestCommand: "go test ./...",
                ManifestPath: goModPath,
                ManifestFileName: "go.mod");
        }

        return null;
    }
}
