using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Lintel.Diagnostics;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Ids;

/// <summary>
/// Reads an IDS 1.0 file into a <see cref="RuleFile"/> (SPEC.md, section 7, right to left).
/// This first version needs no IFC schema: classes are imported as written (<c>only</c>), and a value gets
/// its kind and unit from the data type of the facet. What it cannot represent is reported, never dropped silently.
/// </summary>
public sealed class IdsImporter
{
    private static readonly XNamespace Ids = "http://standards.buildingsmart.org/IDS";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private static readonly Regex Number = new(@"^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex UpperName = new(@"^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly string[] InfoElements = { "title", "copyright", "version", "description", "author", "date", "purpose", "milestone" };

    private static readonly Dictionary<string, string> RelationNames = new(StringComparer.Ordinal)
    {
        ["IFCRELAGGREGATES"] = "IfcRelAggregates",
        ["IFCRELASSIGNSTOGROUP"] = "IfcRelAssignsToGroup",
        ["IFCRELCONTAINEDINSPATIALSTRUCTURE"] = "IfcRelContainedInSpatialStructure",
        ["IFCRELNESTS"] = "IfcRelNests",
        ["IFCRELVOIDSELEMENT IFCRELFILLSELEMENT"] = "IfcRelVoidsElement IfcRelFillsElement",
    };

    private readonly DiagnosticBag _diagnostics;

    private IdsImporter(DiagnosticBag diagnostics) => _diagnostics = diagnostics;

    public static RuleFile Import(string xml, DiagnosticBag diagnostics) =>
        Import(XDocument.Parse(xml, LoadOptions.SetLineInfo), diagnostics);

    public static RuleFile Import(XDocument document, DiagnosticBag diagnostics) =>
        new IdsImporter(diagnostics).Read(document);

    private RuleFile Read(XDocument document)
    {
        var file = new RuleFile();
        var root = document.Root;
        if (root is null || root.Name != Ids + "ids")
        {
            throw new FormatException("The document is not an IDS file: the root element must be 'ids' in the IDS namespace.");
        }

        var info = root.Element(Ids + "info");
        if (info is not null)
        {
            foreach (var name in InfoElements)
            {
                var element = info.Element(Ids + name);
                if (element is not null)
                {
                    file.Header.Texts[name] = new HeaderText(Normalize(element.Value), SpanOf(element));
                }
            }
        }

        var specifications = root.Element(Ids + "specifications")?.Elements(Ids + "specification").ToList() ?? new List<XElement>();

        // The versions most specifications share go to the header; the others stay with their rule.
        var versions = specifications.Select(s => Versions(s)).ToList();
        var common = versions
            .GroupBy(v => string.Join(" ", v))
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .FirstOrDefault();
        if (common is { Count: > 0 })
        {
            file.Header.Ifc = common;
        }

        for (var i = 0; i < specifications.Count; i++)
        {
            var rule = ReadSpecification(specifications[i]);
            if (common is null || !versions[i].SequenceEqual(common))
            {
                rule.Ifc = versions[i];
            }

            file.Items.Add(rule);
        }

        _diagnostics.CurrentRule = null;
        return file;
    }

    private static List<string> Versions(XElement specification) =>
        ((string?)specification.Attribute("ifcVersion") ?? string.Empty)
            .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private Rule ReadSpecification(XElement specification)
    {
        var rule = new Rule((string?)specification.Attribute("name") ?? string.Empty, SpanOf(specification))
        {
            Id = (string?)specification.Attribute("identifier"),
            Description = (string?)specification.Attribute("description"),
            Instructions = (string?)specification.Attribute("instructions"),
            HasFor = true,
        };
        _diagnostics.CurrentRule = rule.Label;

        var applicability = specification.Element(Ids + "applicability");
        if (applicability is not null)
        {
            rule.Count = ReadOccurs(applicability);
            foreach (var element in applicability.Elements())
            {
                if (element.Name == Ids + "entity")
                {
                    rule.Entity = ReadEntity(element);
                }
                else if (ReadFacet(element) is { } facet)
                {
                    rule.Where.Add(facet);
                    rule.WhereKeywordSpans.Add(facet.Span);
                }
            }
        }

        var requirements = specification.Element(Ids + "requirements");
        if (requirements is not null)
        {
            rule.Source = (string?)requirements.Attribute("description");
            foreach (var element in requirements.Elements())
            {
                if (ReadFacet(element) is not { } facet)
                {
                    continue;
                }

                var cardinality = ((string?)element.Attribute("cardinality")) switch
                {
                    "optional" => Cardinality.Optional,
                    "prohibited" => Cardinality.Prohibited,
                    _ => Cardinality.Required,
                };

                rule.Requirements.Add(new Requirement(cardinality, facet, facet.Span)
                {
                    Uri = (string?)element.Attribute("uri"),
                    UriSpan = facet.Span,
                    Instructions = (string?)element.Attribute("instructions"),
                });
            }
        }

        return rule;
    }

    private RuleCount ReadOccurs(XElement applicability)
    {
        // The defaults of xs:occurs are 1 and 1.
        var min = (string?)applicability.Attribute("minOccurs") ?? "1";
        var max = (string?)applicability.Attribute("maxOccurs") ?? "1";

        if (min == "0" && max == "0")
        {
            return RuleCount.None;
        }

        if (min == "0" && max == "unbounded")
        {
            return RuleCount.Any;
        }

        if (min == "1" && max == "unbounded")
        {
            return RuleCount.AtLeastOne;
        }

        Unsupported(applicability, $"IDS 1.0 allows the occurrences 0..0, 0..unbounded and 1..unbounded; found {min}..{max}.");
        return max == "0" ? RuleCount.None : min == "0" ? RuleCount.Any : RuleCount.AtLeastOne;
    }

    private Facet? ReadFacet(XElement element)
    {
        Facet facet;
        switch (element.Name.LocalName)
        {
            case "entity":
                facet = new EntityFacet(ReadEntity(element));
                break;

            case "attribute":
                facet = new AttributeFacet(ReadName(element.Element(Ids + "name"), NameKind.Plain))
                {
                    Value = ReadValue(element.Element(Ids + "value"), null),
                };
                break;

            case "property":
                var upper = (string?)element.Attribute("dataType");
                var type = upper is null ? null : IfcNames.DataType(upper);
                facet = new PropertyFacet(
                    ReadName(element.Element(Ids + "propertySet"), NameKind.Plain),
                    ReadName(element.Element(Ids + "baseName"), NameKind.Plain))
                {
                    DataType = upper is null ? null : type?.Name ?? upper,
                    Value = ReadValue(element.Element(Ids + "value"), type),
                };
                break;

            case "classification":
                facet = new ClassificationFacet(ReadName(element.Element(Ids + "system"), NameKind.Text))
                {
                    Value = ReadValue(element.Element(Ids + "value"), TextType),
                };
                break;

            case "material":
                facet = new MaterialFacet { Value = ReadValue(element.Element(Ids + "value"), TextType) };
                break;

            case "partOf":
                var entity = element.Element(Ids + "entity");
                var partOf = new PartOfFacet(entity is null ? new EntitySelector(new Matcher(MatcherKind.Exact, SpanOf(element))) : ReadEntity(entity));
                var relation = (string?)element.Attribute("relation");
                if (relation is not null)
                {
                    if (RelationNames.TryGetValue(relation, out var name))
                    {
                        partOf.Relation = name;
                    }
                    else
                    {
                        Unsupported(element, $"'{relation}' is not a relation of IDS 1.0.");
                    }
                }

                facet = partOf;
                break;

            default:
                Unsupported(element, $"'{element.Name.LocalName}' is not a facet of IDS 1.0.");
                return null;
        }

        facet.Span = SpanOf(element);
        return facet;
    }

    private EntitySelector ReadEntity(XElement element)
    {
        var classes = ReadName(element.Element(Ids + "name"), NameKind.Class);
        classes.Only = classes.Kind != MatcherKind.Pattern;
        var selector = new EntitySelector(classes);
        if (element.Element(Ids + "predefinedType") is { } predefinedType)
        {
            selector.Types = ReadName(predefinedType, NameKind.PredefinedType);
        }

        return selector;
    }

    private enum NameKind
    {
        /// <summary>An attribute, a property set or a property.</summary>
        Plain,

        /// <summary>An IFC class: IDS writes it in upper case.</summary>
        Class,

        /// <summary>A predefined type: a value of an enumeration is written bare, other text is quoted.</summary>
        PredefinedType,

        /// <summary>Data that is always quoted: a classification system.</summary>
        Text,
    }

    private Matcher ReadName(XElement? element, NameKind kind)
    {
        var span = element is null ? default : SpanOf(element);

        NameItem Item(string text) => kind switch
        {
            NameKind.Class => new NameItem(IfcNames.Class(text), false, span),
            NameKind.PredefinedType => new NameItem(text, !UpperName.IsMatch(text), span),
            NameKind.Text => new NameItem(text, true, span),
            _ => new NameItem(text, false, span),
        };

        if (element is null)
        {
            return new Matcher(MatcherKind.Exact, span);
        }

        if (element.Element(Ids + "simpleValue") is { } simple)
        {
            var exact = new Matcher(MatcherKind.Exact, span);
            exact.Names.Add(Item(simple.Value));
            return exact;
        }

        var restriction = element.Element(Xs + "restriction");
        if (restriction is null)
        {
            Unsupported(element, "The value has neither a simpleValue nor a restriction.");
            return new Matcher(MatcherKind.Exact, span);
        }

        var patterns = restriction.Elements(Xs + "pattern").Select(p => (string?)p.Attribute("value") ?? string.Empty).ToList();
        var values = restriction.Elements(Xs + "enumeration").Select(e => (string?)e.Attribute("value") ?? string.Empty).ToList();
        foreach (var other in restriction.Elements().Where(e => e.Name != Xs + "pattern" && e.Name != Xs + "enumeration" && e.Name != Xs + "annotation"))
        {
            Unsupported(other, $"A name cannot be restricted with '{other.Name.LocalName}'.");
        }

        Matcher matcher;
        if (patterns.Count > 0)
        {
            if (values.Count > 0)
            {
                Unsupported(restriction, "A name is restricted with a pattern and a list at once; the list is ignored.");
            }

            matcher = new Matcher(MatcherKind.Pattern, span);
            matcher.Patterns.AddRange(patterns.Select(p => new StringLiteral(p) { Span = span }));
        }
        else
        {
            matcher = new Matcher(values.Count == 1 ? MatcherKind.Exact : MatcherKind.AnyOf, span);
            matcher.Names.AddRange(values.Select(Item));
            if (values.Count == 1 && kind != NameKind.Class)
            {
                // A list of one is still a list in IDS; keep it as one.
                matcher = new Matcher(MatcherKind.AnyOf, span);
                matcher.Names.Add(Item(values[0]));
            }
        }

        matcher.Note = Documentation(restriction);
        return matcher;
    }

    private static readonly IfcDataType TextType = new("IfcLabel", "xs:string", null);

    /// <summary>Reads a value element. <paramref name="type"/> is the data type of the facet when it is known.</summary>
    private Constraint? ReadValue(XElement? element, IfcDataType? type)
    {
        if (element is null)
        {
            return null;
        }

        var span = SpanOf(element);
        if (element.Element(Ids + "simpleValue") is { } simple)
        {
            return new Constraint { Equal = Literal(simple.Value, type?.XmlBase, type?.Unit, simple), Span = span };
        }

        var restriction = element.Element(Xs + "restriction");
        if (restriction is null)
        {
            Unsupported(element, "The value has neither a simpleValue nor a restriction.");
            return null;
        }

        var xmlBase = (string?)restriction.Attribute("base") ?? type?.XmlBase;
        var unit = type?.Unit;
        var constraint = new Constraint { Span = span, Note = Documentation(restriction) };

        var values = restriction.Elements(Xs + "enumeration").ToList();
        if (values.Count > 0)
        {
            constraint.Terms.Add(new InTerm
            {
                Values = values.Select(v => Literal((string?)v.Attribute("value") ?? string.Empty, xmlBase, unit, v)).ToList(),
                Span = span,
            });
        }

        var patterns = restriction.Elements(Xs + "pattern").ToList();
        if (patterns.Count > 0)
        {
            var matches = new MatchesTerm { Span = span };
            matches.Patterns.AddRange(patterns.Select(p => new StringLiteral((string?)p.Attribute("value") ?? string.Empty) { Span = SpanOf(p) }));
            constraint.Terms.Add(matches);
        }

        var minInclusive = NumberOf(restriction, "minInclusive", unit);
        var maxInclusive = NumberOf(restriction, "maxInclusive", unit);
        var minExclusive = NumberOf(restriction, "minExclusive", unit);
        var maxExclusive = NumberOf(restriction, "maxExclusive", unit);

        if (minInclusive is not null && maxInclusive is not null && minExclusive is null && maxExclusive is null)
        {
            constraint.Terms.Add(new BetweenTerm(minInclusive, maxInclusive) { Span = span });
        }
        else
        {
            AddBound(constraint, BoundKind.Greater, minExclusive, span);
            AddBound(constraint, BoundKind.GreaterOrEqual, minInclusive, span);
            AddBound(constraint, BoundKind.Less, maxExclusive, span);
            AddBound(constraint, BoundKind.LessOrEqual, maxInclusive, span);
        }

        AddLength(constraint, restriction, "length", LengthKind.Exactly, span);
        AddLength(constraint, restriction, "minLength", LengthKind.AtLeast, span);
        AddLength(constraint, restriction, "maxLength", LengthKind.AtMost, span);

        foreach (var other in restriction.Elements().Where(e => e.Name.Namespace != Xs || !KnownFacets.Contains(e.Name.LocalName)))
        {
            Unsupported(other, $"The restriction '{other.Name.LocalName}' is not part of IDS 1.0.");
        }

        if (constraint.Terms.Count == 0)
        {
            Unsupported(restriction, "The restriction is empty.");
            return null;
        }

        return constraint;
    }

    private static readonly HashSet<string> KnownFacets = new(StringComparer.Ordinal)
    {
        "annotation", "enumeration", "pattern", "minInclusive", "maxInclusive", "minExclusive", "maxExclusive",
        "length", "minLength", "maxLength",
    };

    private static void AddBound(Constraint constraint, BoundKind kind, NumberLiteral? value, SourceSpan span)
    {
        if (value is not null)
        {
            constraint.Terms.Add(new BoundTerm(kind, value) { Span = span });
        }
    }

    private void AddLength(Constraint constraint, XElement restriction, string facet, LengthKind kind, SourceSpan span)
    {
        var element = restriction.Element(Xs + facet);
        if (element is null)
        {
            return;
        }

        var value = (string?)element.Attribute("value") ?? string.Empty;
        if (!Regex.IsMatch(value, @"^\d+$"))
        {
            Unsupported(element, $"The length '{value}' is not a whole number.");
            return;
        }

        constraint.Terms.Add(new LengthTerm(kind, value) { Span = span });
    }

    private NumberLiteral? NumberOf(XElement restriction, string facet, string? unit)
    {
        var element = restriction.Element(Xs + facet);
        if (element is null)
        {
            return null;
        }

        var value = (string?)element.Attribute("value") ?? string.Empty;
        if (!Number.IsMatch(value))
        {
            Unsupported(element, $"The bound '{value}' is not a number.");
            return null;
        }

        return new NumberLiteral(value, unit) { Span = SpanOf(element) };
    }

    /// <summary>A value as the literal its XML base type calls for; text when the base is unknown or the value does not fit.</summary>
    private Literal Literal(string value, string? xmlBase, string? unit, XElement origin)
    {
        var span = SpanOf(origin);
        switch (xmlBase)
        {
            case "xs:double":
            case "xs:integer":
            case "xs:decimal":
            case "xs:float":
                if (Number.IsMatch(value))
                {
                    return new NumberLiteral(value, unit) { Span = span };
                }

                Unsupported(origin, $"'{value}' is not a number; it is kept as text.");
                break;

            case "xs:boolean":
                if (value == "true" || value == "false")
                {
                    return new BooleanLiteral(value == "true") { Span = span };
                }

                Unsupported(origin, $"'{value}' is not 'true' or 'false'; it is kept as text.");
                break;
        }

        return new StringLiteral(value) { Span = span };
    }

    private static string? Documentation(XElement restriction)
    {
        var documentation = restriction.Element(Xs + "annotation")?.Element(Xs + "documentation");
        return documentation is null ? null : Normalize(documentation.Value);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    private void Unsupported(XElement element, string message) =>
        _diagnostics.Report(Codes.IdsNotRepresentable, SpanOf(element), message);

    private static SourceSpan SpanOf(XElement element)
    {
        var info = (IXmlLineInfo)element;
        return info.HasLineInfo()
            ? new SourceSpan(0, 0, info.LineNumber, info.LinePosition, info.LineNumber, info.LinePosition)
            : default;
    }
}
