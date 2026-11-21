using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lintel.Diagnostics;

/// <summary>Applies the fixes that diagnostics carry.</summary>
public static class Fixer
{
    /// <summary>
    /// The source with the fixes applied, or <c>null</c> when there is nothing to apply.
    /// The edits of one diagnostic are applied together or not at all; a fix that overlaps an earlier one waits
    /// for the next pass.
    /// </summary>
    public static string? Apply(string source, IEnumerable<Diagnostic> diagnostics)
    {
        var accepted = new List<TextEdit>();
        foreach (var diagnostic in diagnostics.Where(d => d.Fix is { Count: > 0 }).OrderBy(d => d.Fix![0].Span.Start))
        {
            var edits = diagnostic.Fix!;
            if (edits.All(edit => accepted.All(other => !Overlaps(edit, other))))
            {
                accepted.AddRange(edits);
            }
        }

        if (accepted.Count == 0)
        {
            return null;
        }

        var result = new StringBuilder(source);
        foreach (var edit in accepted.OrderByDescending(e => e.Span.Start).ThenByDescending(e => e.Span.End))
        {
            result.Remove(edit.Span.Start, edit.Span.Length);
            result.Insert(edit.Span.Start, edit.Text);
        }

        return result.ToString();
    }

    private static bool Overlaps(TextEdit a, TextEdit b)
    {
        // Two insertions at the same place would depend on their order.
        if (a.Span.Length == 0 && b.Span.Length == 0)
        {
            return a.Span.Start == b.Span.Start;
        }

        return a.Span.Start < b.Span.End && b.Span.Start < a.Span.End
            || (a.Span.Length == 0 && a.Span.Start > b.Span.Start && a.Span.Start < b.Span.End)
            || (b.Span.Length == 0 && b.Span.Start > a.Span.Start && b.Span.Start < a.Span.End);
    }
}
