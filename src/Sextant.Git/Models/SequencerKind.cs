namespace Sextant.Git.Models;

public enum SequencerKind
{
    None,
    Merge,
    CherryPick,
    Revert,
    Rebase,
}