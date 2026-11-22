using System;
using System.Collections.Generic;
using System.Linq;
using Bimlint.Diagnostics;

namespace Bimlint.Syntax;

/// <summary>Statement detection, the known other spellings, and error reporting.</summary>
public sealed partial class Parser
{
    private static readonly IReadOnlyList<string> TopLevelStatements = new[] { "rule", "define", "ifc", "title", "import" };

    private static readonly IReadOnlyList<string> RuleStatements = new[]
    {
        "source", "description", "instructions", "ifc", "for", "where", "require", "optional", "prohibit", "rule",
    };

    private static readonly IReadOnlyList<string> FacetStarts = new[]
    {
        "entity", "attribute", "property", "classification", "material", "part of",
    };

    /// <summary>Words that authors write for a statement keyword. Found in the syntax experiment and in related languages.</summary>
    private static readonly Dictionary<string, string> StatementAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["requirement"] = "rule",
        ["spec"] = "rule",
        ["specification"] = "rule",
        ["check"] = "rule",
        ["applicable"] = "for",
        ["foreach"] = "for",
        ["with"] = "where",
        ["having"] = "where",
        ["when"] = "where",
        ["requires"] = "require",
        ["required"] = "require",
        ["must"] = "require",
        ["shall"] = "require",
        ["ensure"] = "require",
        ["assert"] = "require",
        ["expect"] = "require",
        ["may"] = "optional",
        ["optionally"] = "optional",
        ["forbid"] = "prohibit",
        ["forbids"] = "prohibit",
        ["forbidden"] = "prohibit",
        ["prohibited"] = "prohibit",
        ["prohibits"] = "prohibit",
        ["deny"] = "prohibit",
        ["disallow"] = "prohibit",
        ["let"] = "define",
        ["const"] = "define",
        ["def"] = "define",
        ["list"] = "define",
        ["enum"] = "define",
        ["ids"] = "title",
        ["schema"] = "ifc",
        ["ifcversion"] = "ifc",
        ["ifc_version"] = "ifc",
        ["src"] = "source",
        ["reference"] = "source",
        ["desc"] = "description",
        ["rationale"] = "description",
        ["instruction"] = "instructions",
        ["guidance"] = "instructions",
    };

    /// <summary>Phrases of several words that stand for a statement keyword. Longer phrases come first.</summary>
    private static readonly (string[] Words, string Keyword)[] StatementPhrases =
    {
        (new[] { "must", "not", "have" }, "prohibit"),
        (new[] { "must", "not", "be" }, "prohibit"),
        (new[] { "must", "have" }, "require"),
        (new[] { "must", "be" }, "require"),
        (new[] { "may", "have" }, "optional"),
        (new[] { "applies", "to" }, "for"),
    };

    private static readonly HashSet<string> AnyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "anything", "everything", "element", "elements", "model",
    };

    private static readonly HashSet<string> ContainmentAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "contained", "containment", "located",
    };

    private static readonly HashSet<string> ClassificationFillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "value", "reference", "identification",
    };

    private static readonly HashSet<string> MaterialFillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "category", "value",
    };

    private static readonly HashSet<string> MatchesAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "like", "regex", "pattern", "matching", "match",
    };

    private static readonly HashSet<string> PresenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "present", "set", "defined", "given", "populated", "filled",
    };

    private int _lastReportedPosition = -1;

    private readonly struct StatementStart
    {
        public StatementStart(string word, int tokens, bool exact)
        {
            Word = word;
            Tokens = tokens;
            Exact = exact;
        }

        /// <summary>The canonical keyword.</summary>
        public string Word { get; }

        /// <summary>How many tokens the author wrote for it.</summary>
        public int Tokens { get; }

        /// <summary>False when the author wrote another spelling.</summary>
        public bool Exact { get; }
    }

    /// <summary>
    /// True when a statement starts at the current token: its keyword, the keyword in another case,
    /// a known synonym, or a near miss. Call it only where a statement is expected.
    /// </summary>
    private bool TryPeekStatement(out StatementStart statement)
    {
        var token = Current;
        statement = default;

        if (token.Kind == TokenKind.Keyword)
        {
            if (Keywords.Statements.Contains(token.Text))
            {
                statement = new StatementStart(token.Text, 1, true);
                return true;
            }

            return false;
        }

        if (token.Kind != TokenKind.Name)
        {
            return false;
        }

        foreach (var phrase in StatementPhrases)
        {
            var matches = true;
            for (var i = 0; i < phrase.Words.Length && matches; i++)
            {
                var candidate = PeekAt(i);
                matches = candidate.Kind is TokenKind.Name or TokenKind.Keyword
                    && string.Equals(candidate.Text, phrase.Words[i], StringComparison.OrdinalIgnoreCase);
            }

            if (matches)
            {
                statement = new StatementStart(phrase.Keyword, phrase.Words.Length, false);
                return true;
            }
        }

        var lower = token.Text.ToLowerInvariant();
        if (Keywords.Statements.Contains(lower))
        {
            statement = new StatementStart(lower, 1, false);
            return true;
        }

        if (StatementAliases.TryGetValue(token.Text, out var canonical))
        {
            statement = new StatementStart(canonical, 1, false);
            return true;
        }

        var nearest = NearMiss(lower, Keywords.Statements);
        if (nearest is not null)
        {
            statement = new StatementStart(nearest, 1, false);
            return true;
        }

        return false;
    }

    /// <summary>Consumes the tokens of a statement keyword and reports another spelling with its fix.</summary>
    private Token TakeStatement(StatementStart statement)
    {
        var first = Current;
        var last = first;
        for (var i = 0; i < statement.Tokens; i++)
        {
            last = Take();
        }

        if (!statement.Exact)
        {
            var span = SourceSpan.Join(first.Span, last.Span);
            var written = _source.Substring(span.Start, span.Length);
            var message = string.Equals(written, statement.Word, StringComparison.OrdinalIgnoreCase)
                ? "Keywords are lower case."
                : $"'{written}' is not a keyword.";
            _diagnostics.Report(
                Codes.UnknownKeyword,
                span,
                message,
                hint: $"Write '{statement.Word}'.",
                fix: new TextEdit(span, statement.Word));
        }

        return first;
    }

    /// <summary>True for the keyword or the same word in another case; <see cref="TakeWord"/> reports the other case.</summary>
    private bool AtWord(string keyword) => AtKeyword(keyword) || IsName(Current, keyword);

    private Token TakeWord(string keyword)
    {
        var token = Take();
        if (token.Kind == TokenKind.Name)
        {
            _diagnostics.Report(
                Codes.UnknownKeyword,
                token.Span,
                "Keywords are lower case.",
                hint: $"Write '{keyword}'.",
                fix: new TextEdit(token.Span, keyword));
        }

        return token;
    }

    /// <summary>Skips to the next token that certainly starts a statement.</summary>
    private void SkipToStatement()
    {
        while (!At(TokenKind.EndOfFile))
        {
            if (Current.Kind == TokenKind.Keyword && Keywords.Statements.Contains(Current.Text))
            {
                return;
            }

            // Another spelling of a statement keyword counts only at the start of a line.
            if (IsFirstOnLine() && TryPeekStatement(out _))
            {
                return;
            }

            Take();
        }
    }

    private bool IsFirstOnLine() => _pos == 0 || _tokens[_pos - 1].Span.EndLine < Current.Span.Line;

    private void ReportUnexpected(string what, IReadOnlyList<string>? expected = null)
    {
        // One report for one place: the callers further up would only repeat it.
        if (_lastReportedPosition == _pos)
        {
            return;
        }

        _lastReportedPosition = _pos;
        var token = Current;

        if (token.Kind == TokenKind.Unknown)
        {
            _diagnostics.Report(
                Codes.UnexpectedCharacter,
                token.Span,
                $"Unexpected character '{token.Text}'. Expected {what}.",
                hint: HintForCharacter(token.Text),
                expected: expected);
            return;
        }

        if (token.Kind == TokenKind.Keyword && Keywords.IsFuture(token.Text))
        {
            _diagnostics.Report(
                Codes.ReservedWord,
                token.Span,
                $"'{token.Text}' is reserved for a later version of the language. Expected {what}.",
                hint: HintForReservedWord(token.Text),
                expected: expected);
            return;
        }

        _diagnostics.Report(
            Codes.UnexpectedToken,
            token.Span,
            $"Expected {what}, found {Describe(token)}.",
            expected: expected);
    }

    private void ReportDuplicate(Token keyword, string word, string? message = null) =>
        _diagnostics.Report(
            Codes.DuplicateStatement,
            keyword.Span,
            message ?? $"'{word}' is written twice.");

    private void ReportMissingFor(Token at) =>
        _diagnostics.Report(
            Codes.MissingFor,
            at.Span.AtStart,
            "The rule does not say which elements it is for.",
            hint: "Add a line before the requirements: for IfcWall, or: for any where ...");

    private void ReportMissingFacetKeyword(Token token, string keyword) =>
        _diagnostics.Report(
            Codes.MissingFacetKeyword,
            token.Span.AtStart,
            $"A facet starts with its keyword; this looks like {(keyword == "entity" ? "a class" : keyword == "attribute" ? "an attribute" : "a property")}.",
            hint: $"Write '{keyword}' in front of it.",
            expected: FacetStarts,
            fix: new TextEdit(token.Span.AtStart, keyword + " "));

    private static string? HintForCharacter(string text) => text switch
    {
        "'" => "Strings are written in double quotes.",
        "{" or "}" => "Lists are written in square brackets: [\"A\", \"B\"].",
        "(" or ")" => "The language has no parentheses.",
        ";" => "Statements are not ended by a semicolon.",
        ":" => "The language has no colon.",
        "\\" => "A backslash is allowed inside a string only.",
        "^" or "$" => "A pattern is written in double quotes after 'matches'.",
        _ => null,
    };

    private static string? HintForReservedWord(string word) => word switch
    {
        "or" => "IDS has no 'or'. Use a list ('in [...]'), a pattern, or a second rule.",
        "not" => "IDS has no 'not'. Use 'prohibit', or list what is allowed.",
        "if" or "then" or "else" => "A condition is written with 'for ... where ...'; 'optional' checks a value only when it is present.",
        "exists" => "A facet without a constraint already means 'is present'.",
        _ => null,
    };

    private static string Describe(Token token) => token.Kind switch
    {
        TokenKind.EndOfFile => "the end of the file",
        TokenKind.String => "the text " + Shorten(token.Text),
        TokenKind.TextBlock => "a text block",
        _ => "'" + Shorten(token.Text) + "'",
    };

    private static string Shorten(string text) => text.Length <= 30 ? text : text.Substring(0, 27) + "...";

    /// <summary>The candidate nearest to what was written, when it is near enough to be a slip of the same word.</summary>
    private static string? Suggest(string written, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = EditDistance.Of(written.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best is not null && bestDistance <= Math.Max(2, best.Length / 3) ? best : null;
    }

    /// <summary>A keyword that differs by one edit (two for long words) from what was written.</summary>
    private static string? NearMiss(string written, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (keyword.Length < 5)
            {
                continue;
            }

            var allowed = keyword.Length >= 7 ? 2 : 1;
            if (Math.Abs(keyword.Length - written.Length) <= allowed && EditDistance.Of(written, keyword) <= allowed)
            {
                return keyword;
            }
        }

        return null;
    }

    /// <summary>The range with the white space in front of it on the same line.</summary>
    private SourceSpan WithPrecedingSpace(SourceSpan span)
    {
        var start = span.Start;
        while (start > 0 && (_source[start - 1] == ' ' || _source[start - 1] == '\t'))
        {
            start--;
        }

        var shift = span.Start - start;
        return new SourceSpan(start, span.End, span.Line, span.Column - shift, span.EndLine, span.EndColumn);
    }

    /// <summary>The range with the white space behind it on the same line.</summary>
    private SourceSpan WithFollowingSpace(SourceSpan span)
    {
        var end = span.End;
        while (end < _source.Length && (_source[end] == ' ' || _source[end] == '\t'))
        {
            end++;
        }

        var shift = end - span.End;
        return new SourceSpan(span.Start, end, span.Line, span.Column, span.EndLine, span.EndColumn + shift);
    }

    private SourceSpan TrimLeadingSpace(SourceSpan span) => WithPrecedingSpace(span);

    /// <summary>
    /// The range from a token to the last token read; the empty range in front of the token when nothing was read,
    /// which happens after an error.
    /// </summary>
    private SourceSpan SpanFrom(Token start)
    {
        var last = _tokens[Math.Max(_pos - 1, 0)];
        return last.Span.Start >= start.Span.Start ? SourceSpan.Join(start.Span, last.Span) : start.Span.AtStart;
    }

    /// <summary>The white space at the start of the line the range begins on.</summary>
    private string IndentOf(SourceSpan span)
    {
        var lineStart = span.Start == 0 ? 0 : _source.LastIndexOf('\n', span.Start - 1) + 1;
        var end = lineStart;
        while (end < span.Start && (_source[end] == ' ' || _source[end] == '\t'))
        {
            end++;
        }

        return _source.Substring(lineStart, end - lineStart);
    }
}

/// <summary>The IFC versions that IDS 1.0 knows.</summary>
public static class IfcVersions
{
    public static readonly IReadOnlyList<string> All = new[] { "IFC2X3", "IFC4", "IFC4X3_ADD2" };

    /// <summary>The IDS name of a version as authors write it, or <c>null</c>.</summary>
    public static string? Resolve(string written)
    {
        if (All.Contains(written))
        {
            return written;
        }

        var key = new string(written.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (key.StartsWith("IFC2X3", StringComparison.Ordinal) || key == "IFC23")
        {
            return "IFC2X3";
        }

        if (key.StartsWith("IFC4X3", StringComparison.Ordinal) || key == "IFC43")
        {
            return "IFC4X3_ADD2";
        }

        return key == "IFC4" || key.StartsWith("IFC4ADD", StringComparison.Ordinal) || key == "IFC40" ? "IFC4" : null;
    }
}

/// <summary>The relationships of the IDS partOf facet, spelled as the IFC classes.</summary>
public static class Relations
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "IfcRelAggregates",
        "IfcRelAssignsToGroup",
        "IfcRelContainedInSpatialStructure",
        "IfcRelNests",
        "IfcRelVoidsElement IfcRelFillsElement",
    };
}

/// <summary>How far two words are apart: the number of single letters to add, drop or change to turn one into the other.</summary>
public static class EditDistance
{
    public static int Of(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
