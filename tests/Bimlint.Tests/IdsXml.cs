using System.Collections.Generic;
using System.Linq;
using Bimlint.Ids;

namespace Bimlint.Tests;

/// <summary>Helpers for tests that compare and audit IDS files.</summary>
internal static class IdsXml
{
    /// <summary>An IDS document in the form in which two documents with the same content are equal text.</summary>
    public static string Canonical(string xml) => IdsCanonical.Of(xml);

    /// <summary>Runs the official IDS audit of buildingSMART on a document. Returns the problems it reports; empty when the document passes.</summary>
    public static IReadOnlyList<string> Audit(string xml) => IdsAudit.Run(xml).Select(f => f.Message).ToList();
}
