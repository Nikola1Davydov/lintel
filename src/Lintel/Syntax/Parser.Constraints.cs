using System;
using System.Collections.Generic;
using Lintel.Diagnostics;
using Lintel.Model;

namespace Lintel.Syntax;

/// <summary>Constraints, lists and literals (SPEC.md, sections 5.6 to 5.8).</summary>
public sealed partial class Parser
{
    private Constraint? ParseConstraint()
    {
        DropPresencePhrase();
        var start = Current;

        if (At(TokenKind.NotEqual))
        {
            var op = Take();
            _diagnostics.Report(
                Codes.UnsupportedOperator,
                op.Span,
                "IDS has no 'not equal'.",
                hint: "List the allowed values with 'in [...]' or 'matches', or prohibit the value of an attribute, classification or material: prohibit material = \"...\".");
            ParseLiteral();
            return null;
        }

        if (At(TokenKind.Equal) || At(TokenKind.EqualEqual) || AtEqualityWord())
        {
            var op = Take();
            if (op.Kind != TokenKind.Equal)
            {
                _diagnostics.Report(
                    Codes.UnsupportedOperator,
                    op.Span,
                    $"'{op.Text}' is not an operator of the language.",
                    hint: "A value is compared with '='.",
                    fix: new TextEdit(op.Span, "="));
            }

            var literal = ParseLiteral();
            if (literal is null)
            {
                return null;
            }

            return new Constraint { Equal = literal, Span = SourceSpan.Join(start.Span, literal.Span) };
        }

        if (!AtTermStart(0))
        {
            return null;
        }

        var constraint = new Constraint();
        while (true)
        {
            var term = ParseTerm();
            if (term is null)
            {
                break;
            }

            constraint.Terms.Add(term);
            if (AtWord("and") && AtTermStart(1))
            {
                TakeWord("and");
                continue;
            }

            break;
        }

        if (constraint.Terms.Count == 0)
        {
            return null;
        }

        if (AtKeyword("note"))
        {
            Take();
            constraint.Note = ParseText("text in quotes")?.Value;
        }

        constraint.Span = SourceSpan.Join(start.Span, _tokens[Math.Max(_pos - 1, 0)].Span);
        return constraint;
    }

    private Term? ParseTerm()
    {
        var start = Current;
        Term? term;

        if (AtKeyword("in"))
        {
            Take();
            term = ParseIn();
        }
        else if (AtKeyword("matches"))
        {
            Take();
            var matches = new MatchesTerm();
            ParsePatterns(matches.Patterns);
            term = matches.Patterns.Count == 0 ? null : matches;
        }
        else if (AtKeyword("between"))
        {
            Take();
            var min = ParseNumber();
            if (AtWord("and"))
            {
                TakeWord("and");
            }
            else
            {
                ReportUnexpected("'and'");
            }

            var max = ParseNumber();
            term = min is null || max is null ? null : new BetweenTerm(min, max);
        }
        else if (BoundOf(Current.Kind) is { } bound)
        {
            Take();
            var number = ParseNumber();
            term = number is null ? null : new BoundTerm(bound, number);
        }
        else if (AtKeyword("length"))
        {
            Take();
            term = ParseLength();
        }
        else
        {
            term = ParseTermAlias();
        }

        if (term is not null)
        {
            term.Span = SourceSpan.Join(start.Span, _tokens[Math.Max(_pos - 1, 0)].Span);
        }

        return term;
    }

    private Term? ParseIn()
    {
        var term = new InTerm();

        if (IsName(Current, "list") && Next.Kind == TokenKind.Name)
        {
            var filler = Take();
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                filler.Span,
                "A named list follows 'in' directly.",
                fix: new TextEdit(WithFollowingSpace(filler.Span), string.Empty));
        }

        if (At(TokenKind.LeftBracket))
        {
            term.Values = ParseList("in [\"A\", \"B\"]");
            return term.Values is null ? null : term;
        }

        if (At(TokenKind.Name) && !IsBooleanName(Current))
        {
            var name = Take();
            term.ListName = name.Text;
            term.ListNameSpan = name.Span;
            return term;
        }

        if (At(TokenKind.String) || At(TokenKind.Number) || AtBoolean())
        {
            // in "A", "B": the brackets are missing
            var first = Current;
            var values = new List<Literal>();
            while (true)
            {
                var literal = ParseLiteral();
                if (literal is null)
                {
                    break;
                }

                values.Add(literal);
                if (At(TokenKind.Comma) && (Next.Kind is TokenKind.String or TokenKind.Number || Next.IsKeyword("true") || Next.IsKeyword("false")))
                {
                    Take();
                    continue;
                }

                break;
            }

            var last = _tokens[Math.Max(_pos - 1, 0)];
            var diagnostic = _diagnostics.Report(
                Codes.UnexpectedToken,
                SourceSpan.Join(first.Span, last.Span),
                "A list is written in square brackets.",
                hint: "Write: in [\"A\", \"B\"].");
            diagnostic.Fix = new[] { new TextEdit(first.Span.AtStart, "["), new TextEdit(last.Span.AtEnd, "]") };
            term.Values = values;
            return term;
        }

        ReportUnexpected("a list in square brackets or the name of a defined list");
        return null;
    }

    private Term? ParseLength()
    {
        LengthKind? kind = Current.Kind switch
        {
            TokenKind.Equal or TokenKind.EqualEqual => LengthKind.Exactly,
            TokenKind.GreaterEqual => LengthKind.AtLeast,
            TokenKind.LessEqual => LengthKind.AtMost,
            _ => null,
        };

        if (kind is null)
        {
            if (Current.Kind is TokenKind.Less or TokenKind.Greater)
            {
                _diagnostics.Report(
                    Codes.UnsupportedOperator,
                    Current.Span,
                    "A length is compared with '=', '>=' or '<='.",
                    expected: new[] { "length = 4", "length >= 2", "length <= 12" });
                Take();
                ParseNumber();
                return null;
            }

            ReportUnexpected("'=', '>=' or '<='", new[] { "length = 4", "length >= 2", "length <= 12" });
            return null;
        }

        var op = Take();
        if (op.Kind == TokenKind.EqualEqual)
        {
            _diagnostics.Report(
                Codes.UnsupportedOperator,
                op.Span,
                "'==' is not an operator of the language.",
                fix: new TextEdit(op.Span, "="));
        }

        var number = ParseNumber();
        if (number is null)
        {
            return null;
        }

        if (!number.IsInteger || number.Unit is not null || number.Text.StartsWith("-", StringComparison.Ordinal))
        {
            _diagnostics.Report(
                Codes.InvalidRange,
                number.Span,
                "A length is a whole number of characters.");
        }

        return new LengthTerm(kind.Value, number.Text);
    }

    /// <summary>Phrases that authors write for a term and that the language spells differently.</summary>
    private Term? ParseTermAlias()
    {
        var first = Current;

        // starts with "x" / ends with "x" / contains "x"
        var affix = AffixOf(first, Next);
        if (affix is not null)
        {
            var words = affix.Value.Words;
            for (var i = 0; i < words; i++)
            {
                Take();
            }

            if (!At(TokenKind.String))
            {
                ReportUnexpected("text in quotes");
                return null;
            }

            var text = Take();
            var escaped = XsdPattern.Escape(text.Value);
            var pattern = affix.Value.Kind switch
            {
                "starts" => escaped + ".*",
                "ends" => ".*" + escaped,
                _ => ".*" + escaped + ".*",
            };

            var span = SourceSpan.Join(first.Span, text.Span);
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                span,
                $"'{_source.Substring(first.Span.Start, _tokens[_pos - 2].Span.End - first.Span.Start)}' is not part of the language.",
                hint: "Write a pattern after 'matches'.",
                fix: new TextEdit(span, "matches " + Quote(pattern)));
            var term = new MatchesTerm();
            term.Patterns.Add(new StringLiteral(pattern) { Span = text.Span });
            return term;
        }

        // like "x" / regex "x" / =~ "x" / ~ "x"
        if ((first.Kind == TokenKind.Name && MatchesAliases.Contains(first.Text)) || IsMatchOperator(first))
        {
            Take();
            _diagnostics.Report(
                first.Kind == TokenKind.Name ? Codes.UnknownKeyword : Codes.UnsupportedOperator,
                first.Span,
                $"'{first.Text}' is not part of the language.",
                hint: "A pattern follows 'matches'.",
                fix: new TextEdit(first.Span, "matches"));
            var term = new MatchesTerm();
            ParsePatterns(term.Patterns);
            return term.Patterns.Count == 0 ? null : term;
        }

        // one of [...] / is one of [...]
        var oneOf = OneOfWords(0);
        if (oneOf > 0)
        {
            var last = first;
            for (var i = 0; i < oneOf; i++)
            {
                last = Take();
            }

            var span = SourceSpan.Join(first.Span, last.Span);
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                span,
                "A list of allowed values follows 'in'.",
                fix: new TextEdit(span, "in"));
            return ParseIn();
        }

        // at most 5 / at least 5 / max 5 / min 5
        var limit = LimitOf(first, Next);
        if (limit is not null)
        {
            var last = first;
            for (var i = 0; i < limit.Value.Words; i++)
            {
                last = Take();
            }

            var span = SourceSpan.Join(first.Span, last.Span);
            var op = limit.Value.Kind == BoundKind.LessOrEqual ? "<=" : ">=";
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                span,
                $"'{_source.Substring(span.Start, span.Length)}' is not part of the language.",
                hint: $"Write '{op}'.",
                fix: new TextEdit(span, op));
            var number = ParseNumber();
            return number is null ? null : new BoundTerm(limit.Value.Kind, number);
        }

        return null;
    }

    private void ParsePatterns(List<StringLiteral> target)
    {
        if (At(TokenKind.LeftBracket))
        {
            Take();
            while (!At(TokenKind.RightBracket) && !At(TokenKind.EndOfFile))
            {
                if (!TakePattern(target))
                {
                    break;
                }

                if (At(TokenKind.Comma))
                {
                    Take();
                }
                else
                {
                    break;
                }
            }

            if (At(TokenKind.RightBracket))
            {
                Take();
            }
            else
            {
                ReportUnexpected("']'");
            }

            return;
        }

        TakePattern(target);
    }

    private bool TakePattern(List<StringLiteral> target)
    {
        if (At(TokenKind.String))
        {
            var token = Take();
            target.Add(new StringLiteral(token.Value) { Span = token.Span });
            return true;
        }

        if (At(TokenKind.RegexLiteral))
        {
            var token = Take();
            _diagnostics.Report(
                Codes.PatternInSlashes,
                token.Span,
                "A pattern is written in double quotes, not between slashes.",
                fix: new TextEdit(token.Span, Quote(token.Value)));
            target.Add(new StringLiteral(token.Value) { Span = token.Span });
            return true;
        }

        ReportUnexpected("a pattern in quotes");
        return false;
    }

    private List<Literal>? ParseList(string example)
    {
        if (!At(TokenKind.LeftBracket))
        {
            ReportUnexpected("a list in square brackets", new[] { example });
            SkipToStatement();
            return null;
        }

        Take();
        var values = new List<Literal>();
        while (!At(TokenKind.RightBracket) && !At(TokenKind.EndOfFile))
        {
            var literal = ParseLiteral();
            if (literal is null)
            {
                SkipToStatement();
                return values;
            }

            values.Add(literal);
            if (At(TokenKind.Comma))
            {
                Take();
            }
            else
            {
                break;
            }
        }

        if (At(TokenKind.RightBracket))
        {
            Take();
        }
        else
        {
            ReportUnexpected("',' or ']'");
            SkipToStatement();
        }

        return values;
    }

    private Literal? ParseLiteral()
    {
        var token = Current;

        if (token.Kind == TokenKind.String)
        {
            Take();
            return new StringLiteral(token.Value) { Span = token.Span };
        }

        if (token.Kind == TokenKind.Number)
        {
            return ParseNumber();
        }

        if (token.IsKeyword("true") || token.IsKeyword("false"))
        {
            Take();
            return new BooleanLiteral(token.Text == "true") { Span = token.Span };
        }

        if (IsBooleanName(token))
        {
            Take();
            var lower = token.Text.ToLowerInvariant();
            _diagnostics.Report(
                Codes.UnknownKeyword,
                token.Span,
                "Keywords are lower case.",
                hint: $"Write '{lower}'.",
                fix: new TextEdit(token.Span, lower));
            return new BooleanLiteral(lower == "true") { Span = token.Span };
        }

        if (token.Kind == TokenKind.Name)
        {
            Take();
            _diagnostics.Report(
                Codes.UnexpectedToken,
                token.Span,
                $"A text value is written in quotes: \"{token.Text}\".",
                fix: new TextEdit(token.Span, Quote(token.Text)));
            return new StringLiteral(token.Text) { Span = token.Span };
        }

        ReportUnexpected("a value: text in quotes, a number, true or false");
        return null;
    }

    private NumberLiteral? ParseNumber()
    {
        if (!At(TokenKind.Number))
        {
            ReportUnexpected("a number");
            return null;
        }

        var number = Take();
        string? unit = null;
        var end = number.Span;
        var unitSpan = number.Span.AtEnd;

        // A unit stands on the line of its number; a name on the next line starts something else.
        if (At(TokenKind.Name) && Current.Span.Line == number.Span.EndLine)
        {
            var first = Take();
            unit = first.Text;
            unitSpan = first.Span;
            end = first.Span;
            if (At(TokenKind.Slash) && Next.Kind == TokenKind.Name)
            {
                Take();
                var second = Take();
                unit += "/" + second.Text;
                unitSpan = SourceSpan.Join(first.Span, second.Span);
                end = second.Span;
            }
        }

        return new NumberLiteral(number.Text, unit)
        {
            Span = SourceSpan.Join(number.Span, end),
            UnitSpan = unitSpan,
        };
    }

    private Token? ParseText(string what)
    {
        if (At(TokenKind.String) || At(TokenKind.TextBlock))
        {
            return Take();
        }

        ReportUnexpected(what);
        return null;
    }

    // ---------------------------------------------------------------- recognisers

    private static BoundKind? BoundOf(TokenKind kind) => kind switch
    {
        TokenKind.Greater => BoundKind.Greater,
        TokenKind.GreaterEqual => BoundKind.GreaterOrEqual,
        TokenKind.Less => BoundKind.Less,
        TokenKind.LessEqual => BoundKind.LessOrEqual,
        _ => null,
    };

    /// <summary>True when the token at the offset starts a term of a constraint, in the canonical or a known other spelling.</summary>
    private bool AtTermStart(int offset)
    {
        var token = PeekAt(offset);
        var next = PeekAt(offset + 1);
        if (token.IsKeyword("in") || token.IsKeyword("matches") || token.IsKeyword("between") || token.IsKeyword("length"))
        {
            return true;
        }

        if (BoundOf(token.Kind) is not null || IsMatchOperator(token))
        {
            return true;
        }

        if (token.Kind == TokenKind.Name && MatchesAliases.Contains(token.Text) && next.Kind is TokenKind.String or TokenKind.RegexLiteral)
        {
            return true;
        }

        return AffixOf(token, next) is not null || OneOfWords(offset) > 0 || LimitOf(token, next) is not null;
    }

    private bool AtEqualityWord()
    {
        if (!(IsName(Current, "is") || IsName(Current, "equals") || IsName(Current, "eq")))
        {
            return false;
        }

        if (OneOfWords(0) > 0)
        {
            return false;
        }

        return Next.Kind is TokenKind.String or TokenKind.Number || Next.IsKeyword("true") || Next.IsKeyword("false") || IsBooleanName(Next);
    }

    private bool AtBoolean() => Current.IsKeyword("true") || Current.IsKeyword("false") || IsBooleanName(Current);

    private static bool IsBooleanName(Token token) =>
        token.Kind == TokenKind.Name
        && (string.Equals(token.Text, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(token.Text, "false", StringComparison.OrdinalIgnoreCase));

    private static bool IsMatchOperator(Token token) =>
        token.Kind == TokenKind.Unknown && (token.Text == "=~" || token.Text == "~");

    private static (string Kind, int Words)? AffixOf(Token token, Token next)
    {
        if (token.Kind != TokenKind.Name)
        {
            return null;
        }

        if ((IsName(token, "starts") || IsName(token, "begins")) && IsName(next, "with"))
        {
            return ("starts", 2);
        }

        if (IsName(token, "ends") && IsName(next, "with"))
        {
            return ("ends", 2);
        }

        if (IsName(token, "startswith"))
        {
            return ("starts", 1);
        }

        if (IsName(token, "endswith"))
        {
            return ("ends", 1);
        }

        return IsName(token, "contains") && next.Kind == TokenKind.String ? ("contains", 1) : null;
    }

    /// <summary>The number of tokens of "one of" or "is one of" at the offset, or 0.</summary>
    private int OneOfWords(int offset)
    {
        var index = offset;
        if (IsName(PeekAt(index), "is"))
        {
            index++;
        }

        if (IsName(PeekAt(index), "one") && PeekAt(index + 1).IsKeyword("of"))
        {
            return index + 2 - offset;
        }

        return 0;
    }

    private static (BoundKind Kind, int Words)? LimitOf(Token token, Token next)
    {
        if (token.Kind != TokenKind.Name)
        {
            return null;
        }

        if (IsName(token, "at") && IsName(next, "most"))
        {
            return (BoundKind.LessOrEqual, 2);
        }

        if (IsName(token, "at") && IsName(next, "least"))
        {
            return (BoundKind.GreaterOrEqual, 2);
        }

        if ((IsName(token, "max") || IsName(token, "maximum")) && next.Kind == TokenKind.Number)
        {
            return (BoundKind.LessOrEqual, 1);
        }

        if ((IsName(token, "min") || IsName(token, "minimum")) && next.Kind == TokenKind.Number)
        {
            return (BoundKind.GreaterOrEqual, 1);
        }

        return null;
    }

    /// <summary>"is present", "exists" and the like after a facet say what the facet alone already says.</summary>
    private void DropPresencePhrase()
    {
        var first = Current;
        var words = 0;
        if (first.IsKeyword("exists") || IsName(first, "exist") || IsName(first, "present"))
        {
            words = 1;
        }
        else if (IsName(first, "is") && PresenceWords.Contains(Next.Text) && Next.Kind == TokenKind.Name)
        {
            words = 2;
        }

        if (words == 0)
        {
            return;
        }

        var last = first;
        for (var i = 0; i < words; i++)
        {
            last = Take();
        }

        var span = SourceSpan.Join(first.Span, last.Span);
        _diagnostics.Report(
            Codes.UnsupportedPhrase,
            span,
            "A facet without a constraint already means 'is present'.",
            hint: "Remove the words.",
            fix: new TextEdit(WithPrecedingSpace(span), string.Empty));
    }

    private void DropFillerWord(HashSet<string> words, string message)
    {
        if (At(TokenKind.Name) && words.Contains(Current.Text))
        {
            var filler = Take();
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                filler.Span,
                message,
                hint: $"Remove '{filler.Text}'.",
                fix: new TextEdit(WithFollowingSpace(filler.Span), string.Empty));
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
