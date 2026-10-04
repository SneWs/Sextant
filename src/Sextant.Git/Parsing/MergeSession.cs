using System.Text;

namespace Sextant.Git.Parsing;

/// <summary>One conflict in the three columns. Offsets are into the column text the editor shows.</summary>
public readonly record struct MergeRange(
    int Index,
    int ResultOffset,
    int ResultLength,
    int OursOffset,
    int OursLength,
    int TheirsOffset,
    int TheirsLength,
    int BaseOffset,
    int BaseLength);

/// <summary>
/// The working-tree conflict, split into context and conflict regions.
/// The result is editable. Ours, theirs, and base stay aligned with it by padding shorter columns.
/// </summary>
public sealed class MergeSession
{
    private readonly List<Segment> _segments = [];
    private string _newline = "\n";

    private MergeSession()
    {
    }

    public string Result { get; private set; } = "";

    public string Ours { get; private set; } = "";

    public string Theirs { get; private set; } = "";

    public string Base { get; private set; } = "";

    public bool AnyBase { get; private set; }

    public IReadOnlyList<MergeRange> Conflicts { get; private set; } = [];

    public int ConflictCount => Conflicts.Count;

    public event Action? Changed;

    public static MergeSession FromPieces(IReadOnlyList<ConflictPiece> pieces)
    {
        var session = new MergeSession();
        var raw = new StringBuilder();
        foreach (var piece in pieces)
            raw.Append(piece.IsConflict ? piece.Result : piece.Context);
        session._newline = NewlineOf(raw.ToString());
        if (pieces.Count == 0)
        {
            session._segments.Add(new Segment());
        }
        else
        {
            foreach (var piece in pieces)
            {
                session._segments.Add(new Segment
                {
                    Conflict = piece.IsConflict,
                    Text = ToNewlines(piece.IsConflict ? piece.Result : piece.Context),
                    Ours = ToNewlines(piece.Ours),
                    Theirs = ToNewlines(piece.Theirs),
                    Base = ToNewlines(piece.Base ?? ""),
                    HasBase = piece.Base is not null,
                });
            }
        }

        session.Publish();
        return session;
    }

    /// <summary>The result, with the file's original newline style restored.</summary>
    public string TextForDisk() => _newline == "\n" ? Result : Result.Replace("\n", _newline);

    public bool TryRange(int index, out MergeRange range)
    {
        if ((uint)index < (uint)Conflicts.Count)
        {
            range = Conflicts[index];
            return true;
        }

        range = default;
        return false;
    }

    /// <summary>The conflict whose result contains this caret, or -1 when the caret is in context.</summary>
    public int ConflictAt(int offset)
    {
        foreach (var range in Conflicts)
        {
            if (offset >= range.ResultOffset && offset <= range.ResultOffset + range.ResultLength)
                return range.Index;
        }

        return -1;
    }

    public bool TakeOurs(int conflictIndex) => Take(conflictIndex, ours: true);

    public bool TakeTheirs(int conflictIndex) => Take(conflictIndex, ours: false);

    /// <summary>Applies an edit to the result. Offsets match the result editor, which uses \n.</summary>
    public void ApplyEdit(int offset, int removed, string? inserted)
    {
        inserted ??= "";
        if (_segments.Count == 0)
            _segments.Add(new Segment());

        var length = Result.Length;
        if (offset < 0)
            offset = 0;
        if (offset > length)
            offset = length;
        if (removed < 0)
            removed = 0;
        if (offset + removed > length)
            removed = length - offset;
        if (removed == 0 && inserted.Length == 0)
            return;

        var end = offset + removed;
        var cursor = 0;
        var first = -1;
        var last = -1;
        var insertAt = -1;
        for (var i = 0; i < _segments.Count; i++)
        {
            var start = cursor;
            var segEnd = cursor + _segments[i].Text.Length;
            if (removed == 0)
            {
                // An empty resolution still has a caret. Typing there stays in that conflict.
                if (start == offset && _segments[i].Conflict && insertAt < 0)
                    insertAt = i;
                if (start <= offset && (offset < segEnd || (offset == segEnd && i == _segments.Count - 1)))
                    first = last = i;
            }
            else if (start < end && segEnd > offset)
            {
                if (first < 0)
                    first = i;
                last = i;
            }

            cursor = segEnd;
        }

        if (removed == 0 && insertAt >= 0)
            first = last = insertAt;
        if (first < 0)
            return;

        if (first == last)
        {
            var segment = _segments[first];
            var local = offset - StartOf(first);
            if (local < 0)
                local = 0;
            if (local > segment.Text.Length)
                local = segment.Text.Length;
            var take = Math.Min(removed, segment.Text.Length - local);
            segment.Text = string.Concat(segment.Text.AsSpan(0, local), inserted, segment.Text.AsSpan(local + take));
        }
        else
        {
            var local = Math.Clamp(offset - StartOf(first), 0, _segments[first].Text.Length);
            var lastStart = StartOf(last);
            var tailAt = Math.Clamp(end - lastStart, 0, _segments[last].Text.Length);
            var merged = new Segment
            {
                Text = string.Concat(
                    _segments[first].Text.AsSpan(0, local),
                    inserted,
                    _segments[last].Text.AsSpan(tailAt)),
            };
            _segments[first] = merged;
            _segments.RemoveRange(first + 1, last - first);
        }

        Publish();
    }

    public static int LineIndex(string text, int offset)
    {
        if (offset < 0)
            offset = 0;
        if (offset > text.Length)
            offset = text.Length;
        var line = 0;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    private bool Take(int conflictIndex, bool ours)
    {
        var ordinal = 0;
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (!segment.Conflict)
                continue;
            if (ordinal == conflictIndex)
            {
                ApplyEdit(StartOf(i), segment.Text.Length, ours ? segment.Ours : segment.Theirs);
                return true;
            }

            ordinal++;
        }

        return false;
    }

    private int StartOf(int index)
    {
        var start = 0;
        for (var i = 0; i < index; i++)
            start += _segments[i].Text.Length;
        return start;
    }

    private void Publish()
    {
        var result = new StringBuilder();
        var ours = new StringBuilder();
        var theirs = new StringBuilder();
        var baseline = new StringBuilder();
        var ranges = new List<MergeRange>();
        var conflict = 0;
        var anyBase = false;
        foreach (var segment in _segments)
        {
            var resultStart = result.Length;
            var oursStart = ours.Length;
            var theirsStart = theirs.Length;
            var baseStart = baseline.Length;
            var oursText = segment.Conflict ? segment.Ours : segment.Text;
            var theirsText = segment.Conflict ? segment.Theirs : segment.Text;
            var baseText = segment.Conflict ? segment.Base : segment.Text;
            result.Append(segment.Text);
            ours.Append(oursText);
            theirs.Append(theirsText);
            baseline.Append(baseText);
            // Shorter columns gain blank lines so the next region starts on the same row.
            Align(ours, result);
            Align(theirs, result);
            Align(baseline, result);
            if (!segment.Conflict)
                continue;
            anyBase |= segment.HasBase;
            ranges.Add(new MergeRange(
                conflict,
                resultStart,
                segment.Text.Length,
                oursStart,
                oursText.Length,
                theirsStart,
                theirsText.Length,
                baseStart,
                baseText.Length));
            conflict++;
        }

        Result = result.ToString();
        Ours = ours.ToString();
        Theirs = theirs.ToString();
        Base = baseline.ToString();
        AnyBase = anyBase;
        Conflicts = ranges;
        Changed?.Invoke();
    }

    private static void Align(StringBuilder column, StringBuilder result)
    {
        while (Newlines(column) < Newlines(result))
            column.Append('\n');
    }

    private static int Newlines(StringBuilder text)
    {
        var count = 0;
        foreach (var chunk in text.GetChunks())
        {
            foreach (var character in chunk.Span)
            {
                if (character == '\n')
                    count++;
            }
        }

        return count;
    }

    private static string ToNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string NewlineOf(string text)
    {
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;
            if (i > 0 && text[i - 1] == '\r')
                crlf++;
            else
                lf++;
        }

        return crlf > lf ? "\r\n" : "\n";
    }

    private sealed class Segment
    {
        public bool Conflict;

        public string Text = "";

        public string Ours = "";

        public string Theirs = "";

        public string Base = "";

        public bool HasBase;
    }
}
