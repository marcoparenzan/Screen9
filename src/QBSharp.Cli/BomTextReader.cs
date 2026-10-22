namespace QBSharp.Cli;

/// <summary>Console.In does not reliably consume a redirected UTF-8 BOM. Strip it once at the input boundary.</summary>
internal sealed class BomTextReader(TextReader inner) : TextReader
{
    private bool first = true;
    private string Strip(string text)
    {
        if (!first) return text;
        first = false;
        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }
    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = await inner.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return line is null ? null : Strip(line);
    }
    public override async Task<string> ReadToEndAsync(CancellationToken cancellationToken) =>
        Strip(await inner.ReadToEndAsync(cancellationToken).ConfigureAwait(false));
}
