namespace Sextant;

public readonly record struct ShellLaunch(string FileName, string? Arguments, IReadOnlyList<string> ArgumentList, bool UseShellExecute);