using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Lintel.Syntax;

public enum PatternProblemKind
{
    /// <summary><c>^</c> at the start or <c>$</c> at the end: anchors of other regex dialects.</summary>
    Anchor,

    /// <summary>A construct that XML Schema patterns do not have, or a malformed pattern.</summary>
    Invalid,

    /// <summary><c>\\d</c> and the like: a doubled backslash that probably comes from JSON habits.</summary>
    DoubleBackslash,

    /// <summary>
    /// A backslash in front of a character that XML Schema does not let you escape, such as <c>\/</c>.
    /// Lenient checkers read it as the character itself; strict ones reject the pattern.
    /// </summary>
    NonPortableEscape,
}

public sealed class PatternProblem
{
    public PatternProblem(PatternProblemKind kind, string message, string? hint, string? repaired)
    {
        Kind = kind;
        Message = message;
        Hint = hint;
        Repaired = repaired;
    }

    public PatternProblemKind Kind { get; }

    public string Message { get; }

    public string? Hint { get; }

    /// <summary>The pattern without the problem, when it can be repaired without changing what is matched.</summary>
    public string? Repaired { get; }
}

/// <summary>
/// The regular expressions of XML Schema, which IDS uses for <c>xs:pattern</c>. The whole value must match,
/// so there are no anchors, and lazy quantifiers, look-around and back-references do not exist.
/// </summary>
public static class XsdPattern
{
    private const string Metacharacters = "\\|.-^?*+{}()[]";

    // \n \r \t and the characters that may be escaped, then the class escapes; \p and \P are handled apart.
    private const string SingleEscapes = "nrt\\|.?*+(){}-[]^";
    private const string ClassEscapes = "sSiIcCdDwW";

    /// <summary>Text as a pattern that matches exactly this text.</summary>
    public static string Escape(string text)
    {
        var result = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (Metacharacters.IndexOf(c) >= 0)
            {
                result.Append('\\');
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>The first problem of the pattern, or <c>null</c> when it is a valid XML Schema pattern.</summary>
    public static PatternProblem? Check(string pattern)
    {
        if (pattern.Length == 0)
        {
            return null;
        }

        var startsWithAnchor = pattern[0] == '^';
        var endsWithAnchor = EndsWithUnescapedDollar(pattern);
        if (startsWithAnchor || endsWithAnchor)
        {
            var repaired = pattern.Substring(startsWithAnchor ? 1 : 0);
            if (endsWithAnchor)
            {
                repaired = repaired.Substring(0, repaired.Length - 1);
            }

            return new PatternProblem(
                PatternProblemKind.Anchor,
                "A pattern always matches the whole value; '^' and '$' are not anchors here and would be taken literally.",
                "Remove them. For a literal character write '\\^' or '[$]'.",
                repaired);
        }

        var depth = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '\\':
                    if (i + 1 >= pattern.Length)
                    {
                        return Invalid("The pattern ends with a backslash.");
                    }

                    var next = pattern[i + 1];
                    if (next == '\\' && i + 2 < pattern.Length && ClassEscapes.IndexOf(pattern[i + 2]) >= 0)
                    {
                        var repaired = pattern.Remove(i, 1);
                        return new PatternProblem(
                            PatternProblemKind.DoubleBackslash,
                            $"'\\\\{pattern[i + 2]}' matches a backslash followed by the letter '{pattern[i + 2]}'.",
                            $"Strings are raw: write '\\{pattern[i + 2]}'.",
                            repaired);
                    }

                    if (next == 'p' || next == 'P')
                    {
                        var close = pattern.IndexOf('}', i);
                        if (i + 2 >= pattern.Length || pattern[i + 2] != '{' || close < 0)
                        {
                            return Invalid($"'\\{next}' must be followed by a category in braces, such as \\p{{Lu}}.");
                        }

                        i = close;
                        break;
                    }

                    if (SingleEscapes.IndexOf(next) < 0 && ClassEscapes.IndexOf(next) < 0)
                    {
                        if (!char.IsLetterOrDigit(next))
                        {
                            // "\/" means "/" for lenient checkers and is an error for strict ones; "/" works for both.
                            var plain = next == '$' ? "[$]" : next.ToString();
                            return new PatternProblem(
                                PatternProblemKind.NonPortableEscape,
                                $"'\\{next}' is not an escape of XML Schema patterns; some checkers reject it.",
                                $"Write '{plain}'.",
                                pattern.Substring(0, i) + plain + pattern.Substring(i + 2));
                        }

                        return Invalid(
                            $"'\\{next}' does not exist in XML Schema patterns.",
                            HintForEscape(next));
                    }

                    i++;
                    break;

                case '[':
                    var end = SkipCharacterClass(pattern, i);
                    if (end < 0)
                    {
                        return Invalid("A '[' is not closed.");
                    }

                    i = end;
                    break;

                case '(':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                    {
                        return Invalid(
                            "'(?' groups (non-capturing groups, look-ahead, look-behind, flags) do not exist in XML Schema patterns.",
                            "Use a plain group: (...).");
                    }

                    depth++;
                    break;

                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        return Invalid("A ')' has no '('.");
                    }

                    break;

                case '*':
                case '+':
                case '?':
                case '}':
                    if (i + 1 < pattern.Length && (pattern[i + 1] == '?' || pattern[i + 1] == '+') && c != '}')
                    {
                        return Invalid(
                            $"'{c}{pattern[i + 1]}' (a lazy or possessive quantifier) does not exist in XML Schema patterns.",
                            "Use the plain quantifier; the whole value must match anyway.");
                    }

                    if (c == '}' && i + 1 < pattern.Length && pattern[i + 1] == '?')
                    {
                        return Invalid(
                            "A lazy quantifier does not exist in XML Schema patterns.",
                            "Use the plain quantifier; the whole value must match anyway.");
                    }

                    break;
            }
        }

        return depth != 0 ? Invalid("A '(' is not closed.") : null;
    }

    /// <summary>
    /// The pattern as a .NET regular expression that matches a whole value the way XML Schema does;
    /// <c>null</c> when the pattern cannot be translated.
    /// </summary>
    public static Regex? ToRegex(string pattern)
    {
        var body = new StringBuilder(pattern.Length + 16);
        var inClass = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                var next = pattern[++i];
                body.Append(next switch
                {
                    // The multi-character escapes of XML Schema, which differ from those of .NET.
                    'd' => "\\p{Nd}",
                    'D' => "\\P{Nd}",
                    's' => inClass > 0 ? " \\t\\n\\r" : "[ \\t\\n\\r]",
                    'S' => inClass > 0 ? "\\S" : "[^ \\t\\n\\r]",
                    'w' => inClass > 0 ? "\\w" : "[^\\p{P}\\p{Z}\\p{C}]",
                    'W' => inClass > 0 ? "\\W" : "[\\p{P}\\p{Z}\\p{C}]",
                    'i' => inClass > 0 ? "\\p{L}_:" : "[\\p{L}_:]",
                    'I' => inClass > 0 ? "\\W" : "[^\\p{L}_:]",
                    'c' => inClass > 0 ? "\\p{L}\\p{N}_:.\\-" : "[\\p{L}\\p{N}_:.\\-]",
                    'C' => inClass > 0 ? "\\W" : "[^\\p{L}\\p{N}_:.\\-]",
                    _ => "\\" + next,
                });
                continue;
            }

            if (c == '[')
            {
                inClass++;
            }
            else if (c == ']' && inClass > 0)
            {
                inClass--;
            }
            else if (inClass == 0)
            {
                if (c == '^' || c == '$')
                {
                    // Not anchors in XML Schema: the characters themselves.
                    body.Append('\\').Append(c);
                    continue;
                }

                if (c == '.')
                {
                    body.Append("[^\\n\\r]");
                    continue;
                }
            }

            body.Append(c);
        }

        try
        {
            return new Regex("\\A(?:" + body + ")\\z", RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The pattern as the official IDS audit compiles it: the .NET expression "^pattern$" with the character
    /// classes of XML. This decides what the audit accepts, which is more than XML Schema allows: there,
    /// anchors, look-around and lazy quantifiers work. <c>null</c> when the audit rejects the pattern.
    /// </summary>
    public static Regex? ToAuditRegex(string pattern)
    {
        var body = new StringBuilder(pattern.Length + 16).Append('^');
        var copied = 0;
        for (var i = 0; i < pattern.Length - 1; i++)
        {
            if (pattern[i] != '\\')
            {
                continue;
            }

            var next = pattern[i + 1];
            if (next == '\\')
            {
                // a backslash itself; what follows it is not an escape
                i++;
                continue;
            }

            var xml = next switch
            {
                'c' => "\\p{_xmlC}",
                'C' => "\\P{_xmlC}",
                'd' => "\\p{_xmlD}",
                'D' => "\\P{_xmlD}",
                'i' => "\\p{_xmlI}",
                'I' => "\\P{_xmlI}",
                'w' => "\\p{_xmlW}",
                'W' => "\\P{_xmlW}",
                _ => null,
            };
            if (xml is null)
            {
                continue;
            }

            body.Append(pattern, copied, i - copied).Append(xml);
            i++;
            copied = i + 1;
        }

        body.Append(pattern, copied, pattern.Length - copied).Append('$');
        try
        {
            return new Regex(body.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static PatternProblem Invalid(string message, string? hint = null) =>
        new(PatternProblemKind.Invalid, message, hint, null);

    private static string? HintForEscape(char c) => c switch
    {
        'b' or 'B' => "Word boundaries do not exist; the whole value must match.",
        'A' or 'Z' or 'z' or 'G' => "Anchors do not exist; the whole value must match.",
        'x' or 'u' => "Write the character itself.",
        >= '0' and <= '9' => "Back-references do not exist.",
        _ => null,
    };

    private static bool EndsWithUnescapedDollar(string pattern)
    {
        if (pattern[pattern.Length - 1] != '$')
        {
            return false;
        }

        // An odd number of backslashes in front of it would escape it (which XML Schema rejects anyway).
        var backslashes = 0;
        for (var i = pattern.Length - 2; i >= 0 && pattern[i] == '\\'; i--)
        {
            backslashes++;
        }

        if (backslashes % 2 == 1)
        {
            return false;
        }

        // "[$]" ends with "]", so a dollar sign at the very end is outside a character class.
        return true;
    }

    /// <summary>The index of the <c>]</c> that closes the character class starting at <paramref name="start"/>, or -1.</summary>
    private static int SkipCharacterClass(string pattern, int start)
    {
        var depth = 0;
        for (var i = start; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                i++;
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
