using System.Runtime.InteropServices;
using System.Text;

namespace Asa.App.Services;

internal static class NativeMethods
{
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint ProcessSuspendResume = 0x0800;
    public const uint ProcessTerminate = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    [DllImport("ntdll.dll")]
    public static extern int NtSuspendProcess(IntPtr process);

    [DllImport("ntdll.dll")]
    public static extern int NtResumeProcess(IntPtr process);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    public static string? GetProcessPath(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var builder = new StringBuilder(1024);
            int size = builder.Capacity;
            return QueryFullProcessImageName(handle, 0, builder, ref size) ? builder.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static ulong? GetWrittenBytes(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return GetProcessIoCounters(handle, out IoCounters counters) ? counters.WriteTransferCount : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static bool SuspendProcess(int processId)
    {
        IntPtr handle = OpenProcess(ProcessSuspendResume, false, processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NtSuspendProcess(handle) == 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static bool ResumeProcess(int processId)
    {
        IntPtr handle = OpenProcess(ProcessSuspendResume, false, processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NtResumeProcess(handle) == 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
