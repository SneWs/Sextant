namespace Sextant.Git.Models;

public static class HistoryLimits
{
    public const int FirstPage = 300;

    public const int Page = 500;

    public const int SoftCap = 50_000;

    public const int MaxDiffBytes = 1_000_000;

    public const int MaxDiffLines = 20_000;

    public const int MaxPreviewBytes = 8 * 1024 * 1024;

    public const int LfsPointerProbeBytes = 1024;
}
