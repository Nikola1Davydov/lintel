using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using IdsLib;
using Microsoft.Extensions.Logging;

namespace Lintel.Tests;

/// <summary>Helpers for tests that compare and audit IDS files.</summary>
internal static class IdsXml
{
    private static readonly XNamespace Ids = "http://standards.buildingsmart.org/IDS";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    /// <summary>
    /// An IDS document in a form in which two documents with the same content are equal text:
    /// no namespace prefixes, attributes in order, defaults written out, white space around text removed,
    /// the facets of a restriction in a fixed order.
    /// </summary>
    public static string Canonical(string xml)
    {
        var text = new StringBuilder();
        Write(XDocument.Parse(xml).Root!, text, 0);
        return text.ToString();
    }

    private static void Write(XElement element, StringBuilder text, int depth)
    {
        var attributes = element.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "schemaLocation")
            .ToDictionary(a => a.Name.LocalName, a => a.Value);

        if (element.Name == Ids + "applicability")
        {
            // the defaults of xs:occurs
            attributes.TryAdd("minOccurs", "1");
            attributes.TryAdd("maxOccurs", "1");
        }
        else if (element.Parent?.Name == Ids + "requirements" && element.Name != Ids + "entity")
        {
            attributes.TryAdd("cardinality", "required");
        }

        var children = element.Elements().ToList();
        if (element.Name == Xs + "restriction")
        {
            // the order of the facets of a restriction has no meaning; values of one kind keep their order
            children = children.OrderBy(c => c.Name.LocalName, StringComparer.Ordinal).ToList();
        }

        // A class list of one name says the same as the name alone, and Lintel writes the name alone.
        if (element.Name == Ids + "name"
            && element.Parent?.Name == Ids + "entity"
            && children.Count == 1
            && children[0].Name == Xs + "restriction"
            && children[0].Elements().Count() == 1
            && children[0].Elements().Single().Name == Xs + "enumeration")
        {
            children = new List<XElement> { new(Ids + "simpleValue", (string?)children[0].Elements().Single().Attribute("value")) };
        }

        text.Append(' ', depth * 2).Append(element.Name.LocalName);
        foreach (var attribute in attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            text.Append(' ').Append(attribute.Key).Append("=\"").Append(attribute.Value).Append('"');
        }

        if (children.Count == 0)
        {
            var value = element.Value.Replace("\r\n", "\n").Trim();
            if (value.Length > 0)
            {
                text.Append(" = ").Append(value.Replace("\n", "\\n"));
            }
        }

        text.Append('\n');
        foreach (var child in children)
        {
            Write(child, text, depth + 1);
        }
    }

    /// <summary>Runs the official IDS audit of buildingSMART on a document. Returns the problems it reports; empty when the document passes.</summary>
    public static IReadOnlyList<string> Audit(string xml)
    {
        var logger = new CollectingLogger();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var status = IdsLib.Audit.Run(stream, new SingleAuditOptions(), logger);
        var problems = logger.Messages.ToList();
        if (status != IdsLib.Audit.Status.Ok && problems.Count == 0)
        {
            problems.Add("audit status: " + status);
        }

        return problems;
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
