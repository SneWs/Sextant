namespace Sextant.Git.Tests;

/// <summary>
/// Headless Avalonia uses one process-wide dispatcher. Tests in this collection run one at a time
/// so a second session cannot clear that dispatcher while another window is being created.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HeadlessCollection
{
    public const string Name = "Headless";
}
