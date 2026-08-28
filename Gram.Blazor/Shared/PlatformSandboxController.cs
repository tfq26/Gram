using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Gram.Blazor.Shared;

public sealed record PlatformSandboxCapabilities(string Platform, bool HardResourceLimitsAvailable, string EnforcementMode, string Explanation);

public interface IPlatformSandboxController
{
    PlatformSandboxCapabilities Capabilities { get; }
    Task<IDisposable?> AttachAsync(Process process, SandboxResourceBudget budget, string sandboxId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies host-native limits when the host exposes a safe controller. Unsupported
/// platforms deliberately return a monitored-only attachment rather than claiming
/// hard isolation.
/// </summary>
public sealed class PlatformSandboxController : IPlatformSandboxController
{
    private readonly ILogger<PlatformSandboxController> _logger;
    private readonly string _cgroupRoot = "/sys/fs/cgroup";

    public PlatformSandboxController(ILogger<PlatformSandboxController> logger) => _logger = logger;

    public PlatformSandboxCapabilities Capabilities
    {
        get
        {
            if (OperatingSystem.IsLinux() && Directory.Exists(_cgroupRoot) && File.Exists(Path.Combine(_cgroupRoot, "cgroup.controllers")))
                return new("Linux", true, "cgroup-v2", "CPU, memory, and process limits can be delegated to cgroup v2 when permissions allow.");
            if (OperatingSystem.IsWindows())
                return new("Windows", false, "monitored", "Native Job Object enforcement is not enabled in this build; Gram monitoring remains active.");
            if (OperatingSystem.IsMacOS())
                return new("macOS", false, "monitored", "Native per-process hard limits are not available through the current .NET runner; Gram uses monitoring and inherited shell limits.");
            return new(RuntimeInformation.OSDescription, false, "monitored", "No native resource controller is configured for this platform.");
        }
    }

    public Task<IDisposable?> AttachAsync(Process process, SandboxResourceBudget budget, string sandboxId, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() || !Capabilities.HardResourceLimitsAvailable) return Task.FromResult<IDisposable?>(null);
        try
        {
            var safeId = string.Concat(sandboxId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
            var path = Path.Combine(_cgroupRoot, "gram", safeId);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "memory.max"), budget.MemoryBytes.ToString());
            File.WriteAllText(Path.Combine(path, "pids.max"), "128");
            File.WriteAllText(Path.Combine(path, "cpu.max"), $"{Math.Max(1000, budget.CpuCores * 100000)} 100000");
            File.WriteAllText(Path.Combine(path, "cgroup.procs"), process.Id.ToString());
            return Task.FromResult<IDisposable?>(new CgroupAttachment(path, _logger));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not attach process {ProcessId} to cgroup; monitored limits remain active.", process.Id);
            return Task.FromResult<IDisposable?>(null);
        }
    }

    private sealed class CgroupAttachment(string path, ILogger logger) : IDisposable
    {
        public void Dispose()
        {
            try { if (Directory.Exists(path)) Directory.Delete(path); }
            catch (Exception ex) { logger.LogDebug(ex, "Could not remove cgroup {Path}.", path); }
        }
    }
}
