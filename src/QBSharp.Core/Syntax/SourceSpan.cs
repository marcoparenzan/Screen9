namespace QBSharp.Core.Syntax;

/// <summary>Coordinates are one-based; offsets and lengths count UTF-16 code units.</summary>
public sealed record SourcePosition
{
    public int Offset { get; }
    public int Line { get; }
    public int Column { get; }

    public SourcePosition(int offset, int line, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(column, 1);
        Offset = offset;
        Line = line;
        Column = column;
    }
}

/// <summary>Half-open range in a named source; no filesystem access is required.</summary>
public sealed record SourceSpan
{
    public string File { get; }
    public SourcePosition Start { get; }
    public int Length { get; }

    public SourceSpan(string file, SourcePosition start, int length)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        File = file;
        Start = start;
        Length = length;
    }

    public override string ToString() => $"{File}({Start.Line},{Start.Column})";
}
