namespace Lintel.Syntax;

/// <summary>
/// A range in the source text. Lines and columns start at 1 and count UTF-16 units;
/// the end position is the first character after the range.
/// </summary>
public readonly struct SourceSpan
{
    public SourceSpan(int start, int end, int line, int column, int endLine, int endColumn)
    {
        Start = start;
        End = end;
        Line = line;
        Column = column;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    /// <summary>Offset of the first character.</summary>
    public int Start { get; }

    /// <summary>Offset of the first character after the range.</summary>
    public int End { get; }

    public int Line { get; }

    public int Column { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    public int Length => End - Start;

    /// <summary>The empty range in front of this one.</summary>
    public SourceSpan AtStart => new(Start, Start, Line, Column, Line, Column);

    /// <summary>The empty range behind this one.</summary>
    public SourceSpan AtEnd => new(End, End, EndLine, EndColumn, EndLine, EndColumn);

    /// <summary>The range from the start of <paramref name="first"/> to the end of <paramref name="last"/>.</summary>
    public static SourceSpan Join(SourceSpan first, SourceSpan last) =>
        new(first.Start, last.End, first.Line, first.Column, last.EndLine, last.EndColumn);

    public override string ToString() => $"{Line}:{Column}-{EndLine}:{EndColumn}";
}
