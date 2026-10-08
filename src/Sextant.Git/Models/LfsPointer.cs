namespace Sextant.Git.Models;

public sealed record LfsPointer(string Oid, long Size)
{
    public string Render() =>
        "version https://git-lfs.github.com/spec/v1\noid " + Oid + "\nsize " + Size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
}