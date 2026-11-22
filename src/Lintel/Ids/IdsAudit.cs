using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using IdsLib;
using IdsLib.IdsSchema.IdsNodes;
using Lintel.Diagnostics;
using Lintel.Model;
using Lintel.Syntax;
using Microsoft.Extensions.Logging;

namespace Lintel.Ids;

/// <summary>
/// The official IDS audit of buildingSMART as the last check of the compiler. The checks of the compiler report
/// most problems with a place, a hint and a fix of their own; whatever they miss is found here, so that the
/// compiler never writes an IDS file that the audit rejects.
/// </summary>
public static class IdsAudit
{
    private static readonly Regex Prefix = new(@"^(Error|Warning) \d+: ", RegexOptions.CultureInvariant);
    private static readonly Regex Place = new(@",? on `[^`]*` element at line \d+, position \d+", RegexOptions.CultureInvariant);
    private static readonly Regex LineAndColumn = new(@"line (\d+), position (\d+)", RegexOptions.CultureInvariant);

    /// <summary>One problem that the audit reports.</summary>
    public sealed class Finding
    {
        internal Finding(string message, int line, int column)
        {
            Message = message;
            Line = line;
            Column = column;
        }

        /// <summary>The message of the audit, as it writes it.</summary>
        public string Message { get; }

        /// <summary>The line of the element in the IDS text; 0 when the audit names none.</summary>
        public int Line { get; }

        public int Column { get; }
    }

    /// <summary>Runs the audit on the text of an IDS file. An empty result means that the file passes.</summary>
    public static IReadOnlyList<Finding> Run(string ids)
    {
        var collector = new Collector();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ids));
        var status = Audit.Run(stream, new SingleAuditOptions(), collector);
        if (status != Audit.Status.Ok && collector.Findings.Count == 0)
        {
            collector.Findings.Add(new Finding("The audit ends with the status " + status + ".", 0, 0));
        }

        return collector.Findings;
    }

    /// <summary>
    /// Writes a checked rule file as IDS and audits it. What the audit rejects is reported at the place of the
    /// source that the rejected element was written from. Returns the IDS text.
    /// </summary>
    internal static string Check(RuleFile file, DiagnosticBag diagnostics)
    {
        var document = IdsEmitter.Emit(file);
        var text = IdsEmitter.Text(document);
        var findings = Run(text);
        if (findings.Count == 0)
        {
            return text;
        }

        // The audit names an element by its line and column in the text. The text read again has the same
        // elements in the same order as the document, and the document knows where each one comes from.
        var origins = new Dictionary<(int Line, int Column), XElement>();
        var written = document.Descendants().ToList();
        var read = XDocument.Parse(text, LoadOptions.SetLineInfo).Descendants().ToList();
        for (var i = 0; i < Math.Min(written.Count, read.Count); i++)
        {
            var info = (IXmlLineInfo)read[i];
            origins[(info.LineNumber, info.LinePosition)] = written[i];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var origin = OriginOf(ElementAt(origins, finding.Line, finding.Column));
            var message = Place.Replace(Prefix.Replace(finding.Message, string.Empty), string.Empty).Trim();
            if (!seen.Add(message + "@" + (origin?.Span.Start ?? -1)))
            {
                continue;
            }

            diagnostics.CurrentRule = origin?.Rule;
            diagnostics.Report(
                Codes.RejectedByAudit,
                origin?.Span ?? new SourceSpan(0, 0, 1, 1, 1, 1),
                "The official IDS audit rejects what this compiles to: " + message,
                hint: "This is a rule of IDS for which the compiler has no message of its own.");
        }

        diagnostics.CurrentRule = null;
        return text;
    }

    /// <summary>
    /// The element that starts at a place of the text; for a place inside an element, such as the end of its
    /// content, the last element that starts before it.
    /// </summary>
    private static XElement? ElementAt(Dictionary<(int Line, int Column), XElement> elements, int line, int column)
    {
        if (line <= 0)
        {
            return null;
        }

        if (elements.TryGetValue((line, column), out var exact))
        {
            return exact;
        }

        XElement? best = null;
        var bestPlace = (Line: 0, Column: 0);
        foreach (var entry in elements)
        {
            var place = entry.Key;
            var before = place.Line < line || (place.Line == line && place.Column <= column);
            var later = place.Line > bestPlace.Line || (place.Line == bestPlace.Line && place.Column > bestPlace.Column);
            if (before && later)
            {
                best = entry.Value;
                bestPlace = place;
            }
        }

        return best;
    }

    private static IdsEmitter.Origin? OriginOf(XElement? element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.Annotation<IdsEmitter.Origin>() is { } origin)
            {
                return origin;
            }
        }

        return null;
    }

    private sealed class Collector : ILogger
    {
        public List<Finding> Findings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            var message = formatter(state, exception);
            var line = 0;
            var column = 0;

            // The audit passes the place as a value of the message; when it does not, the text of the message has it.
            if (state is IEnumerable<KeyValuePair<string, object?>> values
                && values.Select(v => v.Value).OfType<NodeIdentification>().FirstOrDefault() is { } node)
            {
                line = node.StartLineNumber;
                column = node.StartLinePosition;
            }
            else if (LineAndColumn.Match(message) is { Success: true } match)
            {
                line = int.Parse(match.Groups[1].Value);
                column = int.Parse(match.Groups[2].Value);
            }

            Findings.Add(new Finding(message, line, column));
        }
    }
}
