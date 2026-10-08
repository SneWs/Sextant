namespace Sextant.Git.Models;

public sealed record BlobLoad(byte[] Bytes, bool TooLarge)
{
    public static BlobLoad OverLimit { get; } = new([], true);

    public static BlobLoad Empty { get; } = new([], false);
}