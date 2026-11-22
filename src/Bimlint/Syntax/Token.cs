namespace Bimlint.Syntax;

public enum TokenKind
{
    EndOfFile,
    Name,
    Keyword,
    String,
    TextBlock,
    Number,
    Dot,
    Comma,
    LeftBracket,
    RightBracket,
    Slash,
    Equal,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,

    // Not part of the language. The lexer keeps them apart so that the parser can say what to write instead.
    EqualEqual,
    NotEqual,
    RegexLiteral,
    Unknown,
}

public sealed class Token
{
    public Token(TokenKind kind, string text, string value, SourceSpan span)
    {
        Kind = kind;
        Text = text;
        Value = value;
        Span = span;
    }

    public TokenKind Kind { get; }

    /// <summary>The characters of the token as written.</summary>
    public string Text { get; }

    /// <summary>The content: a string without its quotes, a pattern without its slashes; otherwise the text.</summary>
    public string Value { get; }

    public SourceSpan Span { get; }

    public bool IsKeyword(string word) => Kind == TokenKind.Keyword && Text == word;

    public override string ToString() => $"{Kind} '{Text}' at {Span}";
}
