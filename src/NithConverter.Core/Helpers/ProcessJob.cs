using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NithConverter.Core.Helpers;

/// <summary>Windows closes the entire child family when this handle closes, including app crashes.</summary>
internal sealed class ProcessJob : SafeHandleZeroOrMinusOneIsInvalid
{
    private ProcessJob() : base(true) { }

    public static ProcessJob? TryCreate(Process process)
    {
        if (!OperatingSystem.IsWindows()) return null;
        ProcessJob job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); return null; }
        var information = new ExtendedLimitInformation
        {
            BasicLimitInformation = new BasicLimitInformation { LimitFlags = 0x00002000 } // KILL_ON_JOB_CLOSE
        };
        try
        {
            if (!SetInformationJobObject(job, 9, ref information, (uint)Marshal.SizeOf<ExtendedLimitInformation>())
                || !AssignProcessToJobObject(job, process.Handle))
            { job.Dispose(); return null; }
            return job;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { job.Dispose(); return null; }
    }

    public void Terminate()
    {
        if (!IsInvalid && !IsClosed) _ = TerminateJobObject(this, 1);
    }

    protected override bool ReleaseHandle() => CloseHandle(handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
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
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ProcessJob CreateJobObject(IntPtr securityAttributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(ProcessJob job, int informationClass,
        ref ExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(ProcessJob job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(ProcessJob job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
