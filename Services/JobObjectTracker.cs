using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mosaic.Services;

/// <summary>
/// Tracks a launched process and its entire descendant tree via a Windows Job Object
/// with an associated IO completion port. Completes <see cref="WaitForTreeExitAsync"/>
/// only when the LAST process in the job exits, so launcher-spawns-game cases are
/// measured correctly.
/// </summary>
public sealed class JobObjectTracker : IDisposable
{
    private const int JobObjectAssociateCompletionPortInformation = 7;
    private const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO = 4;
    private const int ERROR_ABANDONED_WAIT_0 = 735;

    private readonly SafeJobHandle _job;
    private readonly SafeCompletionPortHandle _port;
    private bool _disposed;

    public JobObjectTracker()
    {
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");

        _port = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 1);
        if (_port.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateIoCompletionPort failed.");

        var assoc = new JOBOBJECT_ASSOCIATE_COMPLETION_PORT
        {
            CompletionKey = IntPtr.Zero,
            CompletionPort = _port.DangerousGetHandle(),
        };

        if (!SetInformationJobObject(_job, JobObjectAssociateCompletionPortInformation,
                ref assoc, (uint)Marshal.SizeOf<JOBOBJECT_ASSOCIATE_COMPLETION_PORT>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed.");
        }
    }

    /// <summary>Assigns a process (and thus its future descendants) to the tracked job.</summary>
    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_job, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed.");
    }

    /// <summary>
    /// Completes when every process in the job has exited. Runs the blocking
    /// completion-port wait on a dedicated background thread.
    /// </summary>
    public Task WaitForTreeExitAsync(CancellationToken cancellationToken = default)
    {
        return Task.Factory.StartNew(() => WaitLoop(cancellationToken),
            cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void WaitLoop(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1s timeout so cancellation is observed promptly.
            bool ok = GetQueuedCompletionStatus(_port, out uint code, out _, out _, 1000);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == ERROR_ABANDONED_WAIT_0)
                    return; // port closed during disposal
                continue;   // WAIT_TIMEOUT (258) or transient: loop again
            }

            if (code == JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO)
                return;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _port.Dispose();
        _job.Dispose();
    }

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_ASSOCIATE_COMPLETION_PORT
    {
        public IntPtr CompletionKey;
        public IntPtr CompletionPort;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle hJob, int infoClass,
        ref JOBOBJECT_ASSOCIATE_COMPLETION_PORT info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeCompletionPortHandle CreateIoCompletionPort(
        IntPtr fileHandle, IntPtr existingPort, UIntPtr completionKey, uint concurrentThreads);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetQueuedCompletionStatus(SafeCompletionPortHandle port,
        out uint numberOfBytes, out UIntPtr completionKey, out IntPtr overlapped, uint milliseconds);

    private sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private sealed class SafeCompletionPortHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
