using System.Diagnostics;

namespace Sextant.Git;

/// <summary>
/// Windows. There is no SIGTERM for a console git process, so the caller kills the tree.
/// This file is not part of the macOS or Linux build.
/// </summary>
internal static class ProcessSignals
{
    internal static bool TryTerminate(Process process)
    {
        _ = process;
        return false;
    }
}
