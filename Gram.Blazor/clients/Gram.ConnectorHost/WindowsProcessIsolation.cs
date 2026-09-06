using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gram.ConnectorHost;

internal sealed class WindowsProcessIsolation : IDisposable
{
    private readonly IntPtr _job;

    private WindowsProcessIsolation(IntPtr job) => _job = job;

    public static WindowsProcessIsolation? TryAssign(Process process, long memoryLimitBytes)
    {
        if (!OperatingSystem.IsWindows()) return new WindowsProcessIsolation(IntPtr.Zero);

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return null;

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitFlags.KillOnJobClose | JobObjectLimitFlags.ProcessMemory | JobObjectLimitFlags.ActiveProcess,
                ActiveProcessLimit = 1
            },
            ProcessMemoryLimit = (UIntPtr)Math.Max(64L * 1024 * 1024, memoryLimitBytes)
        };

        if (!SetInformationJobObject(job, JobObjectInfoClass.ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()) ||
            !AssignProcessToJobObject(job, process.Handle))
        {
            CloseHandle(job);
            return null;
        }

        return new WindowsProcessIsolation(job);
    }

    public void Dispose()
    {
        if (_job != IntPtr.Zero) CloseHandle(_job);
    }

    [Flags]
    private enum JobObjectLimitFlags : uint
    {
        ActiveProcess = 0x00000008,
        ProcessMemory = 0x00000100,
        KillOnJobClose = 0x00002000
    }

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JobObjectLimitFlags LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, JobObjectInfoClass infoClass, ref JobObjectExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
