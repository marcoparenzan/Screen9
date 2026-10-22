using System.Globalization;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;

namespace QBSharp.Core.Syntax;

public enum TokenKind { End, NewLine, Identifier, Number, String, Symbol }
public sealed record Token(TokenKind Kind, string Text, object? Value, SourceSpan Span);
public sealed record LexResult(IReadOnlyList<Token> Tokens, IReadOnlyList<ScriptDiagnostic> Diagnostics);

/// <summary>UTF-16 coordinates, culture-independent literals, cancellable scanning.</summary>
public sealed class Lexer
{
    public LexResult Scan(ScriptSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var tokens = new List<Token>(); var errors = new List<ScriptDiagnostic>(); var text = source.Text;
        int offset = 0, line = 1, column = 1;
        SourcePosition Position() => new(offset, line, column);
        void Advance() { cancellationToken.ThrowIfCancellationRequested(); offset++; column++; }
        SourceSpan Span(SourcePosition start) => new(source.FileName, start, offset - start.Offset);
        while (offset < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested(); var start = Position(); char c = text[offset];
            if (c is '\r' or '\n')
            {
                Advance(); if (c == '\r' && offset < text.Length && text[offset] == '\n') Advance();
                tokens.Add(new(TokenKind.NewLine, "\n", null, Span(start))); line++; column = 1;
            }
            else if (char.IsWhiteSpace(c)) Advance();
            else if (c == '\'') { while (offset < text.Length && text[offset] is not ('\r' or '\n')) Advance(); }
            else if (char.IsLetter(c) || c == '_')
            {
                Advance(); while (offset < text.Length && (char.IsLetterOrDigit(text[offset]) || text[offset] == '_')) Advance();
                tokens.Add(new(TokenKind.Identifier, text[start.Offset..offset], null, Span(start)));
            }
            else if (char.IsAsciiDigit(c))
            {
                Advance(); while (offset < text.Length && char.IsAsciiDigit(text[offset])) Advance();
                bool real = offset + 1 < text.Length && text[offset] == '.' && char.IsAsciiDigit(text[offset + 1]);
                if (real) { Advance(); while (offset < text.Length && char.IsAsciiDigit(text[offset])) Advance(); }
                var literal = text[start.Offset..offset]; object? value = null;
                if (real && double.TryParse(literal, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) value = d;
                else if (!real && long.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) value = n <= int.MaxValue ? (object)(int)n : n;
                else errors.Add(new("QBS1001", "Numeric literal out of range.", Span(start)));
                tokens.Add(new(TokenKind.Number, literal, value, Span(start)));
            }
            else if (c == '"')
            {
                Advance(); var value = new System.Text.StringBuilder(); bool closed = false;
                while (offset < text.Length && text[offset] is not ('\r' or '\n'))
                {
                    if (text[offset] == '"')
                    {
                        Advance();
                        if (offset < text.Length && text[offset] == '"') { value.Append('"'); Advance(); }
                        else { closed = true; break; }
                    }
                    else { value.Append(text[offset]); Advance(); }
                }
                if (!closed) errors.Add(new("QBS1002", "Unterminated string.", Span(start)));
                tokens.Add(new(TokenKind.String, text[start.Offset..offset], value.ToString(), Span(start)));
            }
            else
            {
                Advance();
                if (c is '<' or '>' && offset < text.Length && (text[offset] == '=' || c == '<' && text[offset] == '>')) Advance();
                var symbol = text[start.Offset..offset];
                if ("+-*/\\^&=<>(),.".Contains(c)) tokens.Add(new(TokenKind.Symbol, symbol, null, Span(start)));
                else errors.Add(new("QBS1003", $"Unexpected character '{c}'.", Span(start)));
            }
        }
        tokens.Add(new(TokenKind.End, "", null, new(source.FileName, Position(), 0))); return new(tokens, errors);
    }
}
