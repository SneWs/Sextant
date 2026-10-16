namespace Sextant.Git;

/// <summary>Minutes between background fetches of the active repository. Zero is off.</summary>
public static class FetchInterval
{
    public const int Maximum = 10080;

    public static int Normalize(int minutes) => Math.Clamp(minutes, 0, Maximum);
}
