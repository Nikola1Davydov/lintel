using System.Collections.Generic;
using System.Linq;
using Bimlint.Diagnostics;
using Bimlint.Syntax;
using Xunit;

namespace Bimlint.Tests;

public class LexerTests
{
    private static IReadOnlyList<Token> Tokens(string text, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        return Lexer.Tokenize(text, diagnostics);
    }

    private static IReadOnlyList<Token> Tokens(string text) => Tokens(text, out _);

    [Fact]
    public void Keywords_and_names_are_told_apart_by_case()
    {
        var tokens = Tokens("for IfcWall Description description");

        Assert.Equal(
            new[] { TokenKind.Keyword, TokenKind.Name, TokenKind.Name, TokenKind.Keyword, TokenKind.EndOfFile },
            tokens.Select(t => t.Kind));
    }

    [Fact]
    public void A_string_is_raw_and_a_quote_is_written_twice()
    {
        var tokens = Tokens("\"F\\d{2,3}\" \"a \"\"b\"\" c\"");

        Assert.Equal("F\\d{2,3}", tokens[0].Value);
        Assert.Equal("a \"b\" c", tokens[1].Value);
    }

    [Fact]
    public void A_string_ends_at_the_end_of_the_line()
    {
        var tokens = Tokens("title \"no end\nifc IFC4", out var diagnostics);

        Assert.Equal("E101", Assert.Single(diagnostics.Items).Code);
        Assert.True(tokens.Any(t => t.IsKeyword("ifc")), "the next line is still read");
    }

    [Fact]
    public void A_text_block_keeps_its_lines()
    {
        var tokens = Tokens("description \"\"\"\nfirst line\n  second \"line\"\n\"\"\"\nifc IFC4");

        Assert.Equal(TokenKind.TextBlock, tokens[1].Kind);
        Assert.Equal("first line\n  second \"line\"", tokens[1].Value);
        Assert.True(tokens[2].IsKeyword("ifc"));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("-3")]
    [InlineData("0.15")]
    [InlineData("1e3")]
    [InlineData("2.5E-4")]
    [InlineData("0.")]
    [InlineData(".5")]
    [InlineData("+7")]
    public void Numbers(string text)
    {
        var tokens = Tokens(text);

        Assert.Equal(TokenKind.Number, tokens[0].Kind);
        Assert.Equal(text, tokens[0].Text);
        Assert.Equal(TokenKind.EndOfFile, tokens[1].Kind);
    }

    [Fact]
    public void A_unit_is_a_name_after_a_number()
    {
        var tokens = Tokens("150 mm 150mm 0.24 W/m2K");

        Assert.Equal(
            new[] { "150", "mm", "150", "mm", "0.24", "W", "/", "m2K" },
            tokens.Take(8).Select(t => t.Text));
    }

    [Fact]
    public void A_dot_between_names_is_not_part_of_a_number()
    {
        var tokens = Tokens("Pset_WallCommon.FireRating");

        Assert.Equal(new[] { TokenKind.Name, TokenKind.Dot, TokenKind.Name }, tokens.Take(3).Select(t => t.Kind));
    }

    [Fact]
    public void Comments_run_to_the_end_of_the_line()
    {
        var tokens = Tokens("ifc IFC4 # the version\n# a whole line\nrule \"x\"");

        Assert.Equal(new[] { "ifc", "IFC4", "rule", "\"x\"" }, tokens.Take(4).Select(t => t.Text));
    }

    [Fact]
    public void Positions_start_at_one_and_the_end_is_exclusive()
    {
        var tokens = Tokens("ifc IFC4\n  rule \"Wände\"");

        var rule = tokens[2];
        Assert.Equal((2, 3, 2, 7), (rule.Span.Line, rule.Span.Column, rule.Span.EndLine, rule.Span.EndColumn));
        var name = tokens[3];
        Assert.Equal((2, 8, 2, 15), (name.Span.Line, name.Span.Column, name.Span.EndLine, name.Span.EndColumn));
    }

    [Fact]
    public void A_pattern_between_slashes_is_one_token_after_matches()
    {
        var tokens = Tokens("matches /^(F|REI)\\d{2,3}$/ 5 / 6");

        Assert.Equal(TokenKind.RegexLiteral, tokens[1].Kind);
        Assert.Equal("^(F|REI)\\d{2,3}$", tokens[1].Value);
        Assert.Equal(TokenKind.Slash, tokens[3].Kind);
    }

    [Fact]
    public void Operators()
    {
        var tokens = Tokens("= == != < <= > >=");

        Assert.Equal(
            new[]
            {
                TokenKind.Equal, TokenKind.EqualEqual, TokenKind.NotEqual, TokenKind.Less, TokenKind.LessEqual,
                TokenKind.Greater, TokenKind.GreaterEqual,
            },
            tokens.Take(7).Select(t => t.Kind));
    }
}
