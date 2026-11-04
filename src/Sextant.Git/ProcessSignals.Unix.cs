using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sextant.Git;

/// <summary>
/// macOS and Linux. SIGTERM lets git unlink index.lock. This file is not part of the Windows build.
/// </summary>
internal static class ProcessSignals
{
    private const int SigTerm = 15;

    internal static bool TryTerminate(Process process) => Signal(process.Id, SigTerm) == 0;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Signal(int pid, int signal);
}
