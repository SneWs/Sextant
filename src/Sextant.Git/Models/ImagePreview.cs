namespace Sextant.Git.Models;

public sealed record ImagePreview(byte[]? Before, byte[]? After, string Notice)
{
    public string BeforeNotice { get; init; } = "";

    public string AfterNotice { get; init; } = "";
}