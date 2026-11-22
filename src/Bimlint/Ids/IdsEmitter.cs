using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Bimlint.Ifc;
using Bimlint.Model;
using Bimlint.Syntax;

namespace Bimlint.Ids;

/// <summary>
/// Writes a rule file as IDS 1.0 (SPEC.md, section 7). The file must have passed the checks: the writer
/// resolves names, data types and units the way the checker does and does not report problems itself.
/// </summary>
public sealed class IdsEmitter
{
    private static readonly XNamespace Ids = "http://standards.buildingsmart.org/IDS";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    // The order of the elements of 'info' in the IDS schema.
    private static readonly string[] InfoOrder = { "title", "copyright", "version", "description", "author", "date", "purpose", "milestone" };

    // The order of the facets of 'applicability' in the IDS schema; 'entity' comes first and is written apart.
    private static readonly Type[] ApplicabilityOrder =
    {
        typeof(PartOfFacet), typeof(ClassificationFacet), typeof(AttributeFacet), typeof(PropertyFacet), typeof(MaterialFacet),
    };

    private readonly RuleFile _file;
    private readonly Dictionary<string, ListDefinition> _lists = new(StringComparer.Ordinal);

    private IReadOnlyList<IfcSchema> _schemas = Array.Empty<IfcSchema>();
    private Dictionary<IfcSchema, HashSet<string>>? _selection;

    private IdsEmitter(RuleFile file)
    {
        _file = file;
        foreach (var definition in file.Definitions)
        {
            _lists[definition.Name] = definition;
        }
    }

    /// <summary>The rule file as an IDS document.</summary>
    public static XDocument Emit(RuleFile file) => new IdsEmitter(file).Write();

    /// <summary>The rule file as the text of an IDS file: UTF-8, indented.</summary>
    public static string EmitText(RuleFile file) => Text(Emit(file));

    /// <summary>An IDS document as text: UTF-8, indented.</summary>
    public static string Text(XDocument document)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>The place in the source that an element of the document was written from, and the rule it belongs to.</summary>
    internal sealed class Origin
    {
        public Origin(SourceSpan span, string? rule)
        {
            Span = span;
            Rule = rule;
        }

        public SourceSpan Span { get; }

        public string? Rule { get; }
    }

    private string? _rule;

    private XElement From(XElement element, SourceSpan span)
    {
        element.AddAnnotation(new Origin(span, _rule));
        return element;
    }

    private XDocument Write()
    {
        var info = new XElement(Ids + "info");
        foreach (var name in InfoOrder)
        {
            if (_file.Header.Texts.TryGetValue(name, out var text))
            {
                info.Add(From(new XElement(Ids + name, text.Value), text.Span));
            }
            else if (name == "title")
            {
                info.Add(new XElement(Ids + name, "Untitled"));
            }
        }

        var root = new XElement(
            Ids + "ids",
            new XAttribute(XNamespace.Xmlns + "xs", Xs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute(Xsi + "schemaLocation", "http://standards.buildingsmart.org/IDS http://standards.buildingsmart.org/IDS/1.0/ids.xsd"),
            info,
            new XElement(Ids + "specifications", _file.Rules.Select(Specification)));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private XElement Specification(Rule rule)
    {
        var versions = rule.Ifc ?? _file.Header.Ifc ?? new List<string>();
        _schemas = Resolver.SchemasOf(versions);
        _selection = Resolver.Selection(rule.Entity, _schemas);
        _rule = rule.Label;

        var specification = From(
            new XElement(
                Ids + "specification",
                new XAttribute("name", rule.Name),
                new XAttribute("ifcVersion", string.Join(" ", versions))),
            rule.Span);
        AddAttribute(specification, "identifier", rule.Id);
        AddAttribute(specification, "description", rule.Description);
        AddAttribute(specification, "instructions", rule.Instructions);

        var applicability = From(
            new XElement(
                Ids + "applicability",
                new XAttribute("minOccurs", rule.Count == RuleCount.AtLeastOne ? "1" : "0"),
                new XAttribute("maxOccurs", rule.Count == RuleCount.None ? "0" : "unbounded")),
            rule.HasFor ? rule.ForSpan : rule.Span);
        if (rule.Entity is not null)
        {
            applicability.Add(Entity(rule.Entity));
        }

        // The IDS schema fixes the order of the facets here; facets of one kind keep the order of the source.
        foreach (var kind in ApplicabilityOrder)
        {
            foreach (var facet in rule.Where.Where(f => f.GetType() == kind))
            {
                applicability.Add(Facet(facet));
            }
        }

        specification.Add(applicability);

        if (rule.Requirements.Count > 0 || rule.Source is not null)
        {
            var requirements = new XElement(Ids + "requirements");
            AddAttribute(requirements, "description", rule.Source);
            foreach (var requirement in rule.Requirements)
            {
                var element = Facet(requirement.Facet);
                if (requirement.Facet is not EntityFacet)
                {
                    element.Add(new XAttribute("cardinality", requirement.Cardinality switch
                    {
                        Cardinality.Optional => "optional",
                        Cardinality.Prohibited => "prohibited",
                        _ => "required",
                    }));
                }

                AddAttribute(element, "uri", requirement.Uri);
                AddAttribute(element, "instructions", requirement.Instructions);
                requirements.Add(element);
            }

            specification.Add(requirements);
        }

        return specification;
    }

    private XElement Facet(Facet facet) => From(FacetElement(facet), facet.Span);

    private XElement FacetElement(Facet facet)
    {
        switch (facet)
        {
            case EntityFacet entity:
                return Entity(entity.Entity);

            case AttributeFacet attribute:
                var attributeType = Resolver.AttributeType(attribute.Name, _selection, _schemas);
                var attributeElement = new XElement(Ids + "attribute", From(new XElement(Ids + "name", Name(attribute.Name)), attribute.Name.Span));
                AddValue(attributeElement, attribute.Value, attributeType?.XmlBase, attributeType?.Type);
                return attributeElement;

            case PropertyFacet property:
                var type = property.DataType is not null || property.Value is not null
                    ? Resolver.DataTypeOf(property, _schemas, _lists, out _)
                    : null;
                var propertyElement = new XElement(Ids + "property");
                if (type is not null)
                {
                    propertyElement.Add(new XAttribute("dataType", type.Name.ToUpperInvariant()));
                }
                else if (property.DataType is not null)
                {
                    propertyElement.Add(new XAttribute("dataType", property.DataType.ToUpperInvariant()));
                }

                propertyElement.Add(From(new XElement(Ids + "propertySet", Name(property.PropertySet)), property.PropertySet.Span));
                propertyElement.Add(From(new XElement(Ids + "baseName", Name(property.Name)), property.Name.Span));
                AddValue(propertyElement, property.Value, type?.XmlBase, type);
                return propertyElement;

            case ClassificationFacet classification:
                // The IDS schema puts the value in front of the system.
                var classificationElement = new XElement(Ids + "classification");
                AddValue(classificationElement, classification.Value, "xs:string", null);
                classificationElement.Add(From(new XElement(Ids + "system", Name(classification.System)), classification.System.Span));
                return classificationElement;

            case MaterialFacet material:
                var materialElement = new XElement(Ids + "material");
                AddValue(materialElement, material.Value, "xs:string", null);
                return materialElement;

            case PartOfFacet partOf:
                var partOfElement = new XElement(Ids + "partOf");
                if (partOf.Relation is not null)
                {
                    partOfElement.Add(new XAttribute("relation", partOf.Relation.ToUpperInvariant()));
                }

                partOfElement.Add(Entity(partOf.Entity));
                return partOfElement;

            default:
                throw new InvalidOperationException("Unknown facet " + facet.GetType().Name);
        }
    }

    private XElement Entity(EntitySelector entity)
    {
        var element = From(new XElement(Ids + "entity"), entity.Classes.Span);
        var classes = entity.Classes;
        if (classes.Kind == MatcherKind.Pattern)
        {
            element.Add(new XElement(Ids + "name", Patterns(classes)));
        }
        else
        {
            // A class stands for itself and its subtypes; IDS wants every class written out.
            var names = Resolver.ExpandClasses(classes, _schemas);
            element.Add(new XElement(Ids + "name", names.Count == 1 && classes.Note is null ? Simple(names[0]) : Enumeration(names, classes.Note)));
        }

        if (entity.Types is { } types)
        {
            element.Add(From(new XElement(Ids + "predefinedType", Name(types)), types.Span));
        }

        return element;
    }

    /// <summary>A name position: one name, a list of names, or patterns.</summary>
    private static XElement Name(Matcher matcher) => matcher.Kind switch
    {
        MatcherKind.Pattern => Patterns(matcher),
        MatcherKind.AnyOf => Enumeration(matcher.Names.Select(n => n.Text).ToList(), matcher.Note),
        _ => Simple(matcher.Names.Count > 0 ? matcher.Names[0].Text : string.Empty),
    };

    private static XElement Simple(string value) => new(Ids + "simpleValue", value);

    private static XElement Enumeration(IReadOnlyList<string> values, string? note)
    {
        var restriction = Restriction("xs:string", note);
        restriction.Add(values.Select(v => new XElement(Xs + "enumeration", new XAttribute("value", v))));
        return restriction;
    }

    private static XElement Patterns(Matcher matcher)
    {
        var restriction = Restriction("xs:string", matcher.Note);
        restriction.Add(matcher.Patterns.Select(p => new XElement(Xs + "pattern", new XAttribute("value", p.Value))));
        return restriction;
    }

    private static XElement Restriction(string xmlBase, string? note)
    {
        var restriction = new XElement(Xs + "restriction", new XAttribute("base", xmlBase));
        if (note is not null)
        {
            restriction.Add(new XElement(Xs + "annotation", new XElement(Xs + "documentation", note)));
        }

        return restriction;
    }

    /// <summary>Adds the 'value' element of a facet: a simpleValue for "= x", a restriction for terms.</summary>
    private void AddValue(XElement facet, Constraint? constraint, string? xmlBase, IfcDataType? type)
    {
        if (constraint is null)
        {
            return;
        }

        if (constraint.Equal is not null)
        {
            facet.Add(From(new XElement(Ids + "value", Simple(Text(constraint.Equal, type))), constraint.Span));
            return;
        }

        var restriction = Restriction(string.IsNullOrEmpty(xmlBase) ? BaseOf(constraint) : xmlBase!, constraint.Note);
        foreach (var term in constraint.Terms)
        {
            switch (term)
            {
                case InTerm { Values: not null } inline:
                    restriction.Add(inline.Values.Select(v => Facet("enumeration", Text(v, type))));
                    break;
                case InTerm { ListName: not null } named when _lists.TryGetValue(named.ListName, out var list):
                    restriction.Add(list.Values.Select(v => Facet("enumeration", Text(v, type))));
                    break;
                case MatchesTerm matches:
                    restriction.Add(matches.Patterns.Select(p => Facet("pattern", p.Value)));
                    break;
                case BetweenTerm between:
                    restriction.Add(Facet("minInclusive", Text(between.Min, type)));
                    restriction.Add(Facet("maxInclusive", Text(between.Max, type)));
                    break;
                case BoundTerm bound:
                    restriction.Add(Facet(
                        bound.Kind switch
                        {
                            BoundKind.Greater => "minExclusive",
                            BoundKind.GreaterOrEqual => "minInclusive",
                            BoundKind.Less => "maxExclusive",
                            _ => "maxInclusive",
                        },
                        Text(bound.Value, type)));
                    break;
                case LengthTerm length:
                    restriction.Add(Facet(
                        length.Kind switch
                        {
                            LengthKind.Exactly => "length",
                            LengthKind.AtLeast => "minLength",
                            _ => "maxLength",
                        },
                        length.Value));
                    break;
            }
        }

        facet.Add(From(new XElement(Ids + "value", restriction), constraint.Span));
    }

    private static XElement Facet(string name, string value) => new(Xs + name, new XAttribute("value", value));

    /// <summary>A value as IDS writes it: text as it is, true and false, a number in the SI unit of its data type.</summary>
    private static string Text(Literal literal, IfcDataType? type) => literal switch
    {
        StringLiteral text => text.Value,
        BooleanLiteral boolean => boolean.Value ? "true" : "false",
        NumberLiteral { Unit: not null } number when type is not null => Units.ToSi(number.Text, number.Unit, type) ?? number.Text,
        NumberLiteral number => number.Text,
        _ => string.Empty,
    };

    /// <summary>The XML base type of a restriction whose data type is not known: the kind of its values.</summary>
    private string BaseOf(Constraint constraint)
    {
        var literals = new List<Literal>();
        foreach (var term in constraint.Terms)
        {
            switch (term)
            {
                case MatchesTerm:
                case LengthTerm:
                    return "xs:string";
                case InTerm { Values: not null } inline:
                    literals.AddRange(inline.Values);
                    break;
                case InTerm { ListName: not null } named when _lists.TryGetValue(named.ListName, out var list):
                    literals.AddRange(list.Values);
                    break;
                case BetweenTerm between:
                    literals.Add(between.Min);
                    literals.Add(between.Max);
                    break;
                case BoundTerm bound:
                    literals.Add(bound.Value);
                    break;
            }
        }

        if (literals.Count > 0 && literals.All(l => l is NumberLiteral))
        {
            return literals.Cast<NumberLiteral>().All(n => n.IsInteger) ? "xs:integer" : "xs:double";
        }

        return literals.Count > 0 && literals.All(l => l is BooleanLiteral) ? "xs:boolean" : "xs:string";
    }

    private static void AddAttribute(XElement element, string name, string? value)
    {
        if (value is not null)
        {
            element.Add(new XAttribute(name, value));
        }
    }
}
