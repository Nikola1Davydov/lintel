using System;
using System.Collections.Generic;
using System.Linq;
using Lintel.Syntax;

namespace Lintel.Analysis;

/// <summary>"Did you mean": the names nearest to what was written.</summary>
internal static class Suggestions
{
    /// <summary>The candidate that is the same word in another case, or near enough to be a slip of the pen; else <c>null</c>.</summary>
    public static string? Nearest(string written, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        var lower = written.ToLowerInvariant();
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate, written, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            if (Math.Abs(candidate.Length - written.Length) >= bestDistance)
            {
                continue;
            }

            var distance = EditDistance.Of(lower, candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best is not null && bestDistance <= Math.Max(2, written.Length / 4) ? best : null;
    }

    /// <summary>The candidates to show as "expected": the nearest ones first, at most <paramref name="count"/>.</summary>
    public static IReadOnlyList<string> Top(string written, IEnumerable<string> candidates, int count = 12)
    {
        var lower = written.ToLowerInvariant();
        return candidates
            .Distinct(StringComparer.Ordinal)
            .Select(c => (Name: c, Distance: Score(lower, c.ToLowerInvariant())))
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Take(count)
            .Select(c => c.Name)
            .ToList();
    }

    private static int Score(string written, string candidate)
    {
        // A candidate that contains what was written, or the other way round, is nearer than its length suggests.
        if (candidate.Contains(written) || written.Contains(candidate))
        {
            return Math.Abs(candidate.Length - written.Length) / 4;
        }

        return EditDistance.Of(written, candidate);
    }
}
