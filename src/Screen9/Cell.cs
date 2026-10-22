namespace Screen9;

/// <summary>Una cella: un carattere BMP a larghezza singola e due colori Console.</summary>
public readonly record struct Cell
{
    public char Character { get; }
    public ConsoleColor Foreground { get; }
    public ConsoleColor Background { get; }

    public Cell(char character, ConsoleColor foreground = ConsoleColor.Gray,
        ConsoleColor background = ConsoleColor.Black)
    {
        if (char.IsControl(character) || char.IsSurrogate(character))
            throw new ArgumentException("Usare un carattere BMP stampabile a larghezza singola.", nameof(character));
        if (!Enum.IsDefined(foreground)) throw new ArgumentOutOfRangeException(nameof(foreground));
        if (!Enum.IsDefined(background)) throw new ArgumentOutOfRangeException(nameof(background));
        Character = character;
        Foreground = foreground;
        Background = background;
    }

    // default(Cell) viene interpretata come spazio nero dal renderer.
    public static Cell Empty => new(' ');
}
