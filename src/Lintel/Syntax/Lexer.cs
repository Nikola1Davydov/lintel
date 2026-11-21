using System.Collections.Generic;
using System.Text;
using Lintel.Diagnostics;

namespace Lintel.Syntax;

/// <summary>Turns source text into tokens (SPEC.md, section 3). It never fails: a bad character becomes a token.</summary>
public sealed class Lexer
{
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<Token> _tokens = new();
    private int _pos;
    private int _line = 1;
    private int _column = 1;

    private Lexer(string text, DiagnosticBag diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
    }

    public static IReadOnlyList<Token> Tokenize(string text, DiagnosticBag diagnostics) =>
        new Lexer(text, diagnostics).Run();

    private IReadOnlyList<Token> Run()
    {
        while (true)
        {
            SkipTrivia();
            if (_pos >= _text.Length)
            {
                var mark = Mark();
                Add(TokenKind.EndOfFile, mark, string.Empty);
                return _tokens;
            }

            ReadToken();
        }
    }

    private void ReadToken()
    {
        var mark = Mark();
        var c = _text[_pos];

        if (char.IsLetter(c) || c == '_')
        {
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
            {
                Advance();
            }

            var word = Slice(mark);
            Add(Keywords.IsKeyword(word) ? TokenKind.Keyword : TokenKind.Name, mark, word);
            return;
        }

        if (StartsNumber())
        {
            ReadNumber(mark);
            return;
        }

        switch (c)
        {
            case '"':
                ReadString(mark);
                return;
            case '.':
                Single(TokenKind.Dot, mark);
                return;
            case ',':
                Single(TokenKind.Comma, mark);
                return;
            case '[':
                Single(TokenKind.LeftBracket, mark);
                return;
            case ']':
                Single(TokenKind.RightBracket, mark);
                return;
            case '/':
                if (!TryReadRegexLiteral(mark))
                {
                    Single(TokenKind.Slash, mark);
                }

                return;
            case '=':
                Advance();
                if (Peek() == '=')
                {
                    Advance();
                    Add(TokenKind.EqualEqual, mark, Slice(mark));
                }
                else if (Peek() == '~')
                {
                    Advance();
                    Add(TokenKind.Unknown, mark, Slice(mark));
                }
                else
                {
                    Add(TokenKind.Equal, mark, Slice(mark));
                }

                return;
            case '<':
                Advance();
                if (Peek() == '=')
                {
                    Advance();
                    Add(TokenKind.LessEqual, mark, Slice(mark));
                }
                else if (Peek() == '>')
                {
                    Advance();
                    Add(TokenKind.NotEqual, mark, Slice(mark));
                }
                else
                {
                    Add(TokenKind.Less, mark, Slice(mark));
                }

                return;
            case '>':
                Advance();
                if (Peek() == '=')
                {
                    Advance();
                    Add(TokenKind.GreaterEqual, mark, Slice(mark));
                }
                else
                {
                    Add(TokenKind.Greater, mark, Slice(mark));
                }

                return;
            case '!':
                Advance();
                if (Peek() == '=')
                {
                    Advance();
                    Add(TokenKind.NotEqual, mark, Slice(mark));
                }
                else
                {
                    Add(TokenKind.Unknown, mark, Slice(mark));
                }

                return;
            default:
                Advance();
                Add(TokenKind.Unknown, mark, Slice(mark));
                return;
        }
    }

    private bool StartsNumber()
    {
        var c = _text[_pos];
        if (char.IsDigit(c))
        {
            return true;
        }

        var next = _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';
        if (c == '.')
        {
            return char.IsDigit(next);
        }

        if (c == '+' || c == '-')
        {
            var afterNext = _pos + 2 < _text.Length ? _text[_pos + 2] : '\0';
            return char.IsDigit(next) || (next == '.' && char.IsDigit(afterNext));
        }

        return false;
    }

    private void ReadNumber(LexerMark mark)
    {
        if (Peek() == '+' || Peek() == '-')
        {
            Advance();
        }

        var digits = 0;
        while (char.IsDigit(Peek()))
        {
            Advance();
            digits++;
        }

        if (Peek() == '.' && !IsNameStart(PeekAt(1)))
        {
            Advance();
            while (char.IsDigit(Peek()))
            {
                Advance();
                digits++;
            }
        }

        if (Peek() == 'e' || Peek() == 'E')
        {
            var sign = PeekAt(1) == '+' || PeekAt(1) == '-' ? 1 : 0;
            if (char.IsDigit(PeekAt(1 + sign)))
            {
                Advance();
                if (sign == 1)
                {
                    Advance();
                }

                while (char.IsDigit(Peek()))
                {
                    Advance();
                }
            }
        }

        var text = Slice(mark);
        var token = Add(TokenKind.Number, mark, text);
        if (digits == 0)
        {
            _diagnostics.Report(Codes.MalformedNumber, token.Span, $"'{text}' is not a number.");
        }
    }

    private void ReadString(LexerMark mark)
    {
        if (PeekAt(1) == '"' && PeekAt(2) == '"' && RestOfLineIsBlank(_pos + 3))
        {
            ReadTextBlock(mark);
            return;
        }

        Advance();
        var value = new StringBuilder();
        while (true)
        {
            if (_pos >= _text.Length || Peek() == '\n' || Peek() == '\r')
            {
                var broken = Add(TokenKind.String, mark, Slice(mark), value.ToString());
                _diagnostics.Report(
                    Codes.UnterminatedString,
                    broken.Span,
                    "The string is not closed before the end of the line.",
                    hint: "Close it with a double quote. A quote inside a string is written twice: \"a \"\"b\"\" c\".");
                return;
            }

            if (Peek() == '"')
            {
                if (PeekAt(1) == '"')
                {
                    value.Append('"');
                    Advance();
                    Advance();
                    continue;
                }

                Advance();
                Add(TokenKind.String, mark, Slice(mark), value.ToString());
                return;
            }

            value.Append(Peek());
            Advance();
        }
    }

    private void ReadTextBlock(LexerMark mark)
    {
        // The opening delimiter and the rest of its line.
        while (_pos < _text.Length && Peek() != '\n')
        {
            Advance();
        }

        if (_pos < _text.Length)
        {
            Advance();
        }

        var lines = new List<string>();
        while (_pos < _text.Length)
        {
            var lineStart = _pos;
            while (_pos < _text.Length && Peek() != '\n')
            {
                Advance();
            }

            var line = _text.Substring(lineStart, _pos - lineStart).TrimEnd('\r');
            if (line.Trim() == "\"\"\"")
            {
                Add(TokenKind.TextBlock, mark, Slice(mark), string.Join("\n", lines));
                return;
            }

            lines.Add(line);
            if (_pos < _text.Length)
            {
                Advance();
            }
        }

        var broken = Add(TokenKind.TextBlock, mark, Slice(mark), string.Join("\n", lines));
        _diagnostics.Report(
            Codes.UnterminatedTextBlock,
            broken.Span.AtStart,
            "The text block is not closed.",
            hint: "Close it with \"\"\" alone on a line.");
    }

    /// <summary>
    /// Reads <c>/.../</c> after <c>matches</c>. Patterns are strings in this language; the token exists
    /// so that the parser can say so instead of reporting every character of the pattern.
    /// </summary>
    private bool TryReadRegexLiteral(LexerMark mark)
    {
        if (_tokens.Count == 0 || !_tokens[_tokens.Count - 1].IsKeyword("matches"))
        {
            return false;
        }

        var end = _pos + 1;
        while (end < _text.Length && _text[end] != '\n' && _text[end] != '\r')
        {
            if (_text[end] == '\\' && end + 1 < _text.Length && _text[end + 1] != '\n')
            {
                end += 2;
                continue;
            }

            if (_text[end] == '/')
            {
                var value = _text.Substring(_pos + 1, end - _pos - 1).Replace("\\/", "/");
                while (_pos <= end)
                {
                    Advance();
                }

                Add(TokenKind.RegexLiteral, mark, Slice(mark), value);
                return true;
            }

            end++;
        }

        return false;
    }

    private void SkipTrivia()
    {
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (c == '#')
            {
                while (_pos < _text.Length && _text[_pos] != '\n')
                {
                    Advance();
                }
            }
            else if (char.IsWhiteSpace(c) || c == '﻿')
            {
                Advance();
            }
            else
            {
                return;
            }
        }
    }

    private bool RestOfLineIsBlank(int from)
    {
        for (var i = from; i < _text.Length; i++)
        {
            if (_text[i] == '\n')
            {
                return true;
            }

            if (!char.IsWhiteSpace(_text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

    private char PeekAt(int offset) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private void Advance()
    {
        var c = _text[_pos];
        _pos++;
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
    }

    private void Single(TokenKind kind, LexerMark mark)
    {
        Advance();
        Add(kind, mark, Slice(mark));
    }

    private LexerMark Mark() => new(_pos, _line, _column);

    private string Slice(LexerMark mark) => _text.Substring(mark.Position, _pos - mark.Position);

    private Token Add(TokenKind kind, LexerMark mark, string text) => Add(kind, mark, text, text);

    private Token Add(TokenKind kind, LexerMark mark, string text, string value)
    {
        var span = new SourceSpan(mark.Position, _pos, mark.Line, mark.Column, _line, _column);
        var token = new Token(kind, text, value, span);
        _tokens.Add(token);
        return token;
    }

    private readonly struct LexerMark
    {
        public LexerMark(int position, int line, int column)
        {
            Position = position;
            Line = line;
            Column = column;
        }

        public int Position { get; }

        public int Line { get; }

        public int Column { get; }
    }
}
