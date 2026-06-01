using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mosaic.Services;

/// <summary>
/// Starts a process in a suspended state via the Win32 <c>CreateProcess</c> API so the
/// caller can assign it to a Job Object BEFORE it executes a single instruction, then
/// resume it. This closes the race where a thin launcher could spawn the real game and
/// exit before a post-start <c>AssignProcessToJobObject</c> call completes — leaving the
/// real game outside the job and the session mismeasured.
/// </summary>
/// <remarks>
/// All process-creation P/Invoke is isolated here (mirroring <see cref="JobObjectTracker"/>'s
/// isolation of the Job Object interop); callers see only the managed
/// <see cref="SuspendedProcess"/> surface.
/// </remarks>
public static class SuspendedProcessLauncher
{
    private const uint CREATE_SUSPENDED = 0x00000004;

    /// <summary>
    /// Creates the process suspended. Returns a <see cref="SuspendedProcess"/> whose
    /// primary thread is still suspended; the caller assigns it to a job and then calls
    /// <see cref="SuspendedProcess.Resume"/>.
    /// </summary>
    /// <exception cref="Win32Exception">The process could not be created.</exception>
    public static SuspendedProcess LaunchSuspended(string executablePath, string? arguments, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path is required.", nameof(executablePath));

        // CreateProcessW may write into lpCommandLine in place, so pass a mutable buffer.
        // Quote the executable path (it may contain spaces); pass it also as lpApplicationName
        // so the quoted path is unambiguous regardless of the command line.
        var command = string.IsNullOrWhiteSpace(arguments)
            ? $"\"{executablePath}\""
            : $"\"{executablePath}\" {arguments}";
        var commandLine = new StringBuilder(command, command.Length + 1);

        var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };

        bool created = CreateProcess(
            lpApplicationName: executablePath,
            lpCommandLine: commandLine,
            lpProcessAttributes: IntPtr.Zero,
            lpThreadAttributes: IntPtr.Zero,
            bInheritHandles: false,
            dwCreationFlags: CREATE_SUSPENDED,
            lpEnvironment: IntPtr.Zero,                       // inherit the parent's environment
            lpCurrentDirectory: string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            lpStartupInfo: ref startupInfo,
            lpProcessInformation: out PROCESS_INFORMATION pi);

        if (!created)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcess failed for '{executablePath}'.");

        var mainThread = new SafeThreadHandle(pi.hThread);
        try
        {
            // Obtain a managed Process (it opens its own handle) BEFORE releasing the raw
            // process handle. The process is suspended, hence alive, so its id cannot be
            // reused and GetProcessById is safe; we then close the raw handle we no longer need.
            var process = Process.GetProcessById((int)pi.dwProcessId);
            CloseHandle(pi.hProcess);
            return new SuspendedProcess(process, mainThread);
        }
        catch
        {
            // Don't leave an orphaned, permanently-suspended process behind on failure.
            mainThread.Resume();
            mainThread.Dispose();
            CloseHandle(pi.hProcess);
            throw;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>
/// A process created in a suspended state. Assign <see cref="Process"/> to a Job Object,
/// then call <see cref="Resume"/> to let it run. Owns the primary thread handle.
/// </summary>
public sealed class SuspendedProcess
{
    private readonly SafeThreadHandle _mainThread;
    private bool _resumed;

    internal SuspendedProcess(Process process, SafeThreadHandle mainThread)
    {
        Process = process;
        _mainThread = mainThread;
    }

    /// <summary>The created process. Its primary thread is suspended until <see cref="Resume"/>.</summary>
    public Process Process { get; }

    /// <summary>
    /// Resumes the primary thread so the process begins executing. Idempotent and
    /// best-effort: a failure to resume (effectively impossible for our own freshly
    /// created thread) is swallowed rather than aborting an already-recorded launch.
    /// </summary>
    public void Resume()
    {
        if (_resumed)
            return;
        _resumed = true;
        _mainThread.Resume();
        _mainThread.Dispose();
    }
}

/// <summary>SafeHandle for a Win32 thread handle.</summary>
internal sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeThreadHandle(IntPtr handle) : base(ownsHandle: true) => SetHandle(handle);

    /// <summary>Resumes the thread. Best-effort: ignores the (near-impossible) failure case.</summary>
    public void Resume()
    {
        if (!IsInvalid && !IsClosed)
            ResumeThread(this);
    }

    protected override bool ReleaseHandle() => CloseHandle(handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeThreadHandle hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
