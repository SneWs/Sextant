namespace Sextant.Git.Models;

public sealed record LfsFileNote(string Path, LfsPointer? Before, LfsPointer? After, long? LocalBytes);