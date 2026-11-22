using System.Collections.Generic;
using System.Linq;
using Lintel.Diagnostics;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Analysis;

/// <summary>
/// The official IDS audit takes a name or a constraint that an empty text can meet for a missing one.
/// A classification system must not be missing, and an optional requirement needs a constraint.
/// </summary>
internal static class EmptyText
{
    /// <summary>True when an empty text meets the name.</summary>
    public static bool Meets(Matcher name) => name.Kind switch
    {
        MatcherKind.Pattern => name.Patterns.Any(p => XsdPattern.ToAuditRegex(p.Value)?.IsMatch(string.Empty) == true),
        _ => name.Names.Any(n => n.Text.Length == 0),
    };

    /// <summary>
    /// True when an empty text meets a constraint on text, the way the audit decides it: a list with an empty
    /// text, a pattern that matches nothing, or the length 0. Bounds and the other lengths are not asked.
    /// </summary>
    public static bool Meets(Constraint constraint, IReadOnlyDictionary<string, ListDefinition> lists)
    {
        if (constraint.Equal is not null)
        {
            return constraint.Equal is StringLiteral { Value.Length: 0 };
        }

        var asked = false;
        foreach (var term in constraint.Terms)
        {
            switch (term)
            {
                case InTerm { Values: not null } inline:
                    asked = true;
                    if (!inline.Values.Any(v => v is StringLiteral { Value.Length: 0 }))
                    {
                        return false;
                    }

                    break;

                case InTerm { ListName: not null } named:
                    asked = true;
                    if (!lists.TryGetValue(named.ListName, out var list) || !list.Values.Any(v => v is StringLiteral { Value.Length: 0 }))
                    {
                        return false;
                    }

                    break;

                case MatchesTerm matches:
                    asked = true;
                    if (!matches.Patterns.Any(p => XsdPattern.ToAuditRegex(p.Value)?.IsMatch(string.Empty) == true))
                    {
                        return false;
                    }

                    break;

                case LengthTerm { Kind: LengthKind.Exactly } length:
                    asked = true;
                    if (length.Value.TrimStart('0').Length != 0)
                    {
                        return false;
                    }

                    break;
            }
        }

        return asked;
    }

    public static void Report(DiagnosticBag diagnostics, Constraint value) =>
        diagnostics.Report(
            Codes.EmptyMatch,
            value.Span,
            "An empty text also meets this constraint. IDS takes such a constraint for a missing one, and an optional requirement needs one.",
            hint: "Write a constraint that needs at least one character, such as matches \".+\".");
}
