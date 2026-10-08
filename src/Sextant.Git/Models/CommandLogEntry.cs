namespace Sextant.Git.Models;

public sealed record CommandLogEntry(
    DateTimeOffset At,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    TimeSpan Duration,
    string StandardError);