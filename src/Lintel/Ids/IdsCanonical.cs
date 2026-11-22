using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Lintel.Ids;

/// <summary>
/// An IDS document in a form in which two documents that say the same are the same text. It is used to show that
/// an IDS file comes back from Lintel unchanged.
/// </summary>
public static class IdsCanonical
{
    private static readonly XNamespace Ids = "http://standards.buildingsmart.org/IDS";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    /// <summary>
    /// The canonical text of an IDS document: no namespace prefixes, attributes in order, the defaults of the IDS
    /// schema written out, white space around text removed, the facets of a restriction in a fixed order,
    /// a class list of one name as that name, and no empty 'requirements' element.
    /// </summary>
    public static string Of(string xml)
    {
        var text = new StringBuilder();
        Write(XDocument.Parse(xml).Root!, text, 0);
        return text.ToString();
    }

    /// <summary>The first line in which two canonical texts differ, as "first | second"; <c>null</c> when they are equal.</summary>
    public static string? FirstDifference(string first, string second)
    {
        if (first == second)
        {
            return null;
        }

        var a = first.Split('\n');
        var b = second.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i].Trim() : "(nothing)";
            var y = i < b.Length ? b[i].Trim() : "(nothing)";
            if (x != y)
            {
                return x + " | " + y;
            }
        }

        return null;
    }

    private static void Write(XElement element, StringBuilder text, int depth)
    {
        var attributes = element.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName != "schemaLocation")
            .ToDictionary(a => a.Name.LocalName, a => a.Value);

        if (element.Name == Ids + "applicability")
        {
            // the defaults of xs:occurs
            if (!attributes.ContainsKey("minOccurs"))
            {
                attributes["minOccurs"] = "1";
            }

            if (!attributes.ContainsKey("maxOccurs"))
            {
                attributes["maxOccurs"] = "1";
            }
        }
        else if (element.Parent?.Name == Ids + "requirements" && element.Name != Ids + "entity" && !attributes.ContainsKey("cardinality"))
        {
            attributes["cardinality"] = "required";
        }

        var children = element.Elements().ToList();
        if (element.Name == Xs + "restriction")
        {
            // the order of the facets of a restriction has no meaning; values of one kind keep their order
            children = children.OrderBy(c => c.Name.LocalName, StringComparer.Ordinal).ToList();
        }

        // 'requirements' without a description and without facets says nothing.
        children = children.Where(c => !(c.Name == Ids + "requirements" && !c.HasElements && !c.HasAttributes)).ToList();

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
}
