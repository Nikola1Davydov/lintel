using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Text;

/// <summary>
/// Writes a <see cref="RuleFile"/> in the canonical layout: one statement per line, two spaces of indentation
/// inside a rule, names bare where they can be. Comments are not part of the model, so the printer is used for
/// imported and generated rule sets, not to reformat a file that a person wrote.
/// </summary>
public static class Printer
{
    private const int MaxLineLength = 110;
    private static readonly Regex BareName = new(@"^[\p{L}_][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant);
    private static readonly string[] HeaderOrder = { "title", "description", "version", "author", "date", "purpose", "milestone", "copyright" };

    public static string Print(RuleFile file)
    {
        var text = new StringBuilder();

        foreach (var keyword in HeaderOrder)
        {
            if (file.Header.Get(keyword) is { } value)
            {
                Line(text, 0, keyword + " " + Text(value));
            }
        }

        if (file.Header.Ifc is { Count: > 0 } versions)
        {
            Line(text, 0, "ifc " + string.Join(", ", versions));
        }

        foreach (var import in file.Header.Imports)
        {
            Line(text, 0, "import " + Text(import.Value));
        }

        foreach (var item in file.Items)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            if (item is ListDefinition definition)
            {
                Line(text, 0, $"define {definition.Name} = " + List(definition.Values, "define ".Length + definition.Name.Length + 3, 0));
            }
            else if (item is Rule rule)
            {
                PrintRule(text, rule);
            }
        }

        return text.ToString();
    }

    private static void PrintRule(StringBuilder text, Rule rule)
    {
        Line(text, 0, "rule " + (rule.Id is null ? string.Empty : Name(rule.Id) + " ") + Text(rule.Name));

        if (rule.Source is not null)
        {
            Line(text, 1, "source " + Text(rule.Source));
        }

        if (rule.Description is not null)
        {
            Line(text, 1, "description " + Text(rule.Description));
        }

        if (rule.Instructions is not null)
        {
            Line(text, 1, "instructions " + Text(rule.Instructions));
        }

        if (rule.Ifc is { Count: > 0 })
        {
            Line(text, 1, "ifc " + string.Join(", ", rule.Ifc));
        }

        Line(text, 1, "for " + (rule.Entity is null ? "any" : Entity(rule.Entity)));

        for (var i = 0; i < rule.Where.Count; i++)
        {
            Line(text, i == 0 ? 1 : 2, (i == 0 ? "where " : "and ") + Facet(rule.Where[i], i == 0 ? 1 : 2));
        }

        if (rule.Count == RuleCount.AtLeastOne)
        {
            Line(text, 1, "require count >= 1");
        }
        else if (rule.Count == RuleCount.None)
        {
            Line(text, 1, "require count = 0");
        }

        foreach (var requirement in rule.Requirements)
        {
            var keyword = requirement.Cardinality switch
            {
                Cardinality.Optional => "optional",
                Cardinality.Prohibited => "prohibit",
                _ => "require",
            };

            Line(text, 1, keyword + " " + Facet(requirement.Facet, 1));
            if (requirement.Uri is not null)
            {
                Line(text, 2, "uri " + Quote(requirement.Uri));
            }

            if (requirement.Instructions is not null)
            {
                Line(text, 2, "instructions " + Text(requirement.Instructions));
            }
        }
    }

    private static string Facet(Facet facet, int indent)
    {
        switch (facet)
        {
            case EntityFacet entity:
                return "entity " + Entity(entity.Entity);

            case AttributeFacet attribute:
                return "attribute " + Matcher(attribute.Name, quoted: false) + Constraint(attribute.Value, indent);

            case PropertyFacet property:
                return "property " + Matcher(property.PropertySet, quoted: false) + "." + Matcher(property.Name, quoted: false)
                    + (property.DataType is null ? string.Empty : " datatype " + property.DataType)
                    + Constraint(property.Value, indent);

            case ClassificationFacet classification:
                return "classification " + Matcher(classification.System, quoted: true) + Constraint(classification.Value, indent);

            case MaterialFacet material:
                return "material" + Constraint(material.Value, indent);

            case PartOfFacet partOf:
                return "part of " + Entity(partOf.Entity) + (partOf.Relation is null ? string.Empty : " via " + partOf.Relation);

            default:
                return string.Empty;
        }
    }

    private static string Entity(EntitySelector entity)
    {
        var classes = entity.Classes;
        string text;
        if (classes.Kind == MatcherKind.Pattern)
        {
            text = "matches " + Patterns(classes.Patterns);
        }
        else
        {
            text = (classes.Only ? "only " : string.Empty) + string.Join(", ", classes.Names.Select(n => Name(n.Text)));
        }

        text += Note(classes.Note);

        if (entity.Types is { } types)
        {
            text += " type ";
            text += types.Kind == MatcherKind.Pattern
                ? "matches " + Patterns(types.Patterns)
                : string.Join(", ", types.Names.Select(n => n.Quoted || !BareName.IsMatch(n.Text) || Keywords.IsKeyword(n.Text) ? Quote(n.Text) : n.Text));
            text += Note(types.Note);
        }

        return text;
    }

    private static string Matcher(Matcher matcher, bool quoted)
    {
        string Item(NameItem item) => quoted ? Quote(item.Text) : Name(item.Text);

        return matcher.Kind switch
        {
            MatcherKind.Pattern => "matches " + Patterns(matcher.Patterns) + Note(matcher.Note),
            MatcherKind.AnyOf => "[" + string.Join(", ", matcher.Names.Select(Item)) + "]" + Note(matcher.Note),
            _ => matcher.Names.Count == 0 ? string.Empty : Item(matcher.Names[0]),
        };
    }

    private static string Constraint(Constraint? constraint, int indent)
    {
        if (constraint is null)
        {
            return string.Empty;
        }

        if (constraint.Equal is not null)
        {
            return " = " + Literal(constraint.Equal);
        }

        var terms = constraint.Terms.Select(term => term switch
        {
            InTerm { ListName: not null } named => "in " + named.ListName,
            InTerm inline => "in " + List(inline.Values ?? new List<Literal>(), 40, indent),
            MatchesTerm matches => "matches " + Patterns(matches.Patterns),
            BetweenTerm between => "between " + Literal(between.Min) + " and " + Literal(between.Max),
            BoundTerm bound => Operator(bound.Kind) + " " + Literal(bound.Value),
            LengthTerm length => "length " + (length.Kind == LengthKind.Exactly ? "=" : length.Kind == LengthKind.AtLeast ? ">=" : "<=") + " " + length.Value,
            _ => string.Empty,
        });

        return " " + string.Join(" and ", terms) + Note(constraint.Note);
    }

    private static string Operator(BoundKind kind) => kind switch
    {
        BoundKind.Greater => ">",
        BoundKind.GreaterOrEqual => ">=",
        BoundKind.Less => "<",
        _ => "<=",
    };

    private static string Patterns(List<StringLiteral> patterns) =>
        patterns.Count == 1 ? Quote(patterns[0].Value) : "[" + string.Join(", ", patterns.Select(p => Quote(p.Value))) + "]";

    private static string Note(string? note) => note is null ? string.Empty : " note " + Text(note);

    /// <summary>A list on one line, or one value per line when the line would become too long.</summary>
    private static string List(List<Literal> values, int used, int indent)
    {
        var items = values.Select(Literal).ToList();
        var oneLine = "[" + string.Join(", ", items) + "]";
        if (used + oneLine.Length <= MaxLineLength || items.Count < 2)
        {
            return oneLine;
        }

        var inner = new string(' ', (indent + 1) * 2);
        return "[\n" + string.Join(",\n", items.Select(item => inner + item)) + "\n" + new string(' ', indent * 2) + "]";
    }

    private static string Literal(Literal literal) => literal switch
    {
        StringLiteral text => Quote(text.Value),
        BooleanLiteral boolean => boolean.Value ? "true" : "false",
        NumberLiteral number => number.Unit is null ? number.Text : number.Text + " " + number.Unit,
        _ => string.Empty,
    };

    /// <summary>A name: bare when it is a plain word that is not a keyword, quoted otherwise.</summary>
    private static string Name(string name) =>
        BareName.IsMatch(name) && !Keywords.IsKeyword(name) ? name : Quote(name);

    /// <summary>Text: a string, or a text block when it has line breaks.</summary>
    private static string Text(string value) =>
        value.IndexOf('\n') < 0 ? Quote(value) : "\"\"\"\n" + value.Replace("\r\n", "\n") + "\n\"\"\"";

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static void Line(StringBuilder text, int indent, string content)
    {
        text.Append(' ', indent * 2).Append(content).Append('\n');
    }
}
