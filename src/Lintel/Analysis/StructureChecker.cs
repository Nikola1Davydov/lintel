using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Lintel.Diagnostics;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Analysis;

/// <summary>
/// The checks that need no IFC schema (SPEC.md, sections 5 and 8): the combinations IDS allows,
/// the form of patterns, lists and metadata. The checks against the IFC schema are a later stage.
/// </summary>
public sealed class StructureChecker
{
    private static readonly Regex Email = new(@"^[^@]+@[^\.]+\..+$", RegexOptions.CultureInvariant);
    private static readonly Regex Date = new(@"^\d{4}-\d{2}-\d{2}(Z|([+-]\d{2}:\d{2}))?$", RegexOptions.CultureInvariant);
    private static readonly Regex BareName = new(@"^[\p{L}_][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant);

    private readonly string _source;
    private readonly RuleFile _file;
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, ListDefinition> _lists = new(StringComparer.Ordinal);

    private StructureChecker(string source, RuleFile file, DiagnosticBag diagnostics)
    {
        _source = source;
        _file = file;
        _diagnostics = diagnostics;
    }

    public static void Check(string source, RuleFile file, DiagnosticBag diagnostics) =>
        new StructureChecker(source, file, diagnostics).Run();

    private void Run()
    {
        CheckHeader();
        CheckDefinitions();

        var identifiers = new Dictionary<string, Rule>(StringComparer.Ordinal);
        foreach (var rule in _file.Rules)
        {
            _diagnostics.CurrentRule = rule.Label;
            CheckRule(rule);

            if (rule.Id is not null)
            {
                if (identifiers.ContainsKey(rule.Id))
                {
                    _diagnostics.Report(
                        Codes.DuplicateIdentifier,
                        rule.Span,
                        $"Two rules have the identifier '{rule.Id}'.",
                        hint: "Give each rule its own identifier, so that results can be traced.");
                }
                else
                {
                    identifiers[rule.Id] = rule;
                }
            }
        }

        _diagnostics.CurrentRule = null;
    }

    private void CheckHeader()
    {
        var header = _file.Header;
        if (header.Texts.TryGetValue("author", out var author) && !Email.IsMatch(author.Value))
        {
            _diagnostics.Report(
                Codes.InvalidMetadata,
                author.Span,
                "IDS stores the author as an e-mail address.",
                hint: "Write: author \"name@example.com\".");
        }

        if (header.Texts.TryGetValue("date", out var date) && !Date.IsMatch(date.Value))
        {
            _diagnostics.Report(
                Codes.InvalidMetadata,
                date.Span,
                "The date is written as year-month-day.",
                hint: "Write: date \"2026-11-21\".");
        }
    }

    private void CheckDefinitions()
    {
        foreach (var definition in _file.Definitions)
        {
            if (_lists.ContainsKey(definition.Name))
            {
                _diagnostics.Report(
                    Codes.DuplicateDefinition,
                    definition.NameSpan,
                    $"The list '{definition.Name}' is defined twice.");
                continue;
            }

            _lists[definition.Name] = definition;
            CheckListValues(definition.Values, definition.NameSpan);
        }
    }

    private void CheckRule(Rule rule)
    {
        if (rule.Ifc is null ? (_file.Header.Ifc is null || _file.Header.Ifc.Count == 0) : rule.Ifc.Count == 0)
        {
            _diagnostics.Report(
                Codes.MissingIfcVersion,
                rule.Span,
                "The rule has no IFC version.",
                hint: "Add a line at the top of the file: ifc IFC4.",
                expected: IfcVersions.All);
        }

        if (rule.HasFor && rule.Entity is null && rule.Where.Count == 0)
        {
            _diagnostics.Report(
                Codes.MissingWhere,
                rule.ForSpan,
                "'for any' needs at least one condition after 'where'.",
                hint: "Add a condition, or name a class: for IfcElement selects every element.");
        }

        if (rule.Entity is not null)
        {
            CheckEntity(rule.Entity);
        }

        for (var i = 0; i < rule.Where.Count; i++)
        {
            CheckPredefinedTypeAttribute(rule, i);
        }

        foreach (var facet in rule.Where)
        {
            if (facet is EntityFacet)
            {
                _diagnostics.Report(
                    Codes.MisplacedStatement,
                    facet.Span,
                    "The class of the selected elements is stated after 'for', not after 'where'.",
                    hint: "Write: for IfcWall where ...");
            }

            CheckFacet(facet);
        }

        if (rule.Count == RuleCount.None && rule.Requirements.Count > 0)
        {
            _diagnostics.Report(
                Codes.RequirementNotAllowed,
                rule.Requirements[0].KeywordSpan,
                "A rule with 'require count = 0' has no other requirement: IDS ignores them.",
                hint: "Remove the requirements, or move the condition to 'where'.");
        }

        if (rule.Count == RuleCount.Any && rule.Requirements.Count == 0 && rule.HasFor)
        {
            _diagnostics.Report(
                Codes.MissingRequirement,
                rule.Span,
                "The rule requires nothing.",
                hint: "Add a requirement, such as: require property Pset_WallCommon.FireRating.",
                expected: new[] { "require", "optional", "prohibit", "require count >= 1", "require count = 0" });
        }

        foreach (var requirement in rule.Requirements)
        {
            CheckRequirement(requirement);
            CheckFacet(requirement.Facet);
        }
    }

    /// <summary>
    /// "where attribute PredefinedType = FLOOR" is what authors write for "type FLOOR". It is valid IDS, but it
    /// compares the attribute of the element only; the predefined type of IDS also looks at the type object.
    /// </summary>
    private void CheckPredefinedTypeAttribute(Rule rule, int index)
    {
        if (rule.Where[index] is not AttributeFacet attribute
            || attribute.Name.Kind != MatcherKind.Exact
            || attribute.Name.Names.Count != 1
            || attribute.Name.Names[0].Text != "PredefinedType")
        {
            return;
        }

        IReadOnlyList<TextEdit>? fix = null;
        var hint = "State the predefined type with the class: for IfcSlab type FLOOR.";
        if (rule.Entity is { Types: null } entity
            && attribute.Value?.Equal is StringLiteral value
            && index < rule.WhereKeywordSpans.Count)
        {
            var type = Regex.IsMatch(value.Value, "^[A-Z][A-Z0-9_]*$") ? value.Value : "\"" + value.Value.Replace("\"", "\"\"") + "\"";
            hint = $"Write the predefined type with the class: type {type}.";
            var classesEnd = entity.Classes.Span.AtEnd;
            var insert = new TextEdit(classesEnd, " type " + type);
            if (rule.Where.Count == 1)
            {
                // "for X where attribute PredefinedType = V" becomes "for X type V"
                fix = new[] { new TextEdit(SourceSpan.Join(classesEnd, attribute.Span), " type " + type) };
            }
            else if (index == 0)
            {
                // drop the facet and the "and" behind it
                var next = rule.Where[1].Span;
                fix = new[] { insert, new TextEdit(new SourceSpan(attribute.Span.Start, next.Start, attribute.Span.Line, attribute.Span.Column, next.Line, next.Column), string.Empty) };
            }
            else
            {
                // drop the "and" in front of the facet and the facet
                var previous = rule.Where[index - 1].Span.AtEnd;
                fix = new[] { insert, new TextEdit(SourceSpan.Join(previous, attribute.Span), string.Empty) };
            }
        }

        var diagnostic = _diagnostics.Report(
            Codes.PredefinedTypeAsAttribute,
            attribute.Span,
            "This compares the attribute of the element itself. It misses a predefined type that comes from the type object, and user-defined types.",
            hint: hint);
        diagnostic.Fix = fix;
    }

    /// <summary>The combinations of facet and cardinality that IDS 1.0 allows (SPEC.md, section 5.3).</summary>
    private void CheckRequirement(Requirement requirement)
    {
        var facet = requirement.Facet;
        var word = requirement.Cardinality switch
        {
            Cardinality.Optional => "optional",
            Cardinality.Prohibited => "prohibit",
            _ => "require",
        };

        switch (facet)
        {
            case EntityFacet when requirement.Cardinality != Cardinality.Required:
                Report(
                    $"IDS has no '{word}' for the class of an element.",
                    requirement.Cardinality == Cardinality.Prohibited
                        ? "To forbid elements of a class, select them and write: require count = 0."
                        : "Use 'require entity ...'.");
                break;

            case PartOfFacet when requirement.Cardinality == Cardinality.Optional:
                Report("IDS has no optional 'part of': it would check nothing.", "Use 'require' or 'prohibit'.");
                break;

            case AttributeFacet { Value: null } when requirement.Cardinality == Cardinality.Optional:
            case ClassificationFacet { Value: null } when requirement.Cardinality == Cardinality.Optional:
            case MaterialFacet { Value: null } when requirement.Cardinality == Cardinality.Optional:
                Report(
                    "An optional requirement without a constraint checks nothing.",
                    "Add what the value must be when it is present, such as: in [\"A\", \"B\"].");
                break;

            case PropertyFacet { Value: null, DataType: null } when requirement.Cardinality == Cardinality.Optional:
                Report(
                    "An optional property without a constraint checks nothing.",
                    "Add what the value must be when it is present, or its data type: datatype IfcLabel.");
                break;

            case PropertyFacet property when requirement.Cardinality == Cardinality.Prohibited
                                             && (property.Value is not null || property.DataType is not null):
                Report(
                    "IDS prohibits a property as a whole, not a value or a data type of it.",
                    "Remove the constraint, or state the allowed values with 'require' or 'optional'.");
                break;
        }

        if (requirement.Uri is not null && facet is not (PropertyFacet or ClassificationFacet or MaterialFacet))
        {
            _diagnostics.Report(
                Codes.UriNotAllowed,
                requirement.UriSpan,
                $"IDS has no URI for the facet '{facet.Keyword}'.",
                expected: new[] { "property", "classification", "material" });
        }

        void Report(string message, string hint) =>
            _diagnostics.Report(Codes.InvalidCardinality, requirement.KeywordSpan, message, hint: hint);
    }

    private void CheckFacet(Facet facet)
    {
        switch (facet)
        {
            case EntityFacet entity:
                CheckEntity(entity.Entity);
                break;

            case AttributeFacet attribute:
                CheckName(attribute.Name, quotedByDefault: false);
                CheckConstraint(attribute.Value);
                break;

            case PropertyFacet property:
                CheckName(property.PropertySet, quotedByDefault: false);
                CheckName(property.Name, quotedByDefault: false);
                CheckConstraint(property.Value);
                break;

            case ClassificationFacet classification:
                CheckName(classification.System, quotedByDefault: true);
                CheckConstraint(classification.Value);
                break;

            case MaterialFacet material:
                CheckConstraint(material.Value);
                break;

            case PartOfFacet partOf:
                CheckEntity(partOf.Entity);
                break;
        }
    }

    private void CheckEntity(EntitySelector entity)
    {
        CheckName(entity.Classes, quotedByDefault: false);
        if (entity.Types is not null)
        {
            foreach (var pattern in entity.Types.Patterns)
            {
                CheckPattern(pattern);
            }
        }
    }

    /// <summary>
    /// IFC names are written bare when they can be; a classification system is data and is always quoted.
    /// The other spelling is accepted with a warning and a fix.
    /// </summary>
    private void CheckName(Matcher matcher, bool quotedByDefault)
    {
        foreach (var pattern in matcher.Patterns)
        {
            CheckPattern(pattern);
        }

        foreach (var name in matcher.Names)
        {
            if (quotedByDefault && !name.Quoted)
            {
                _diagnostics.Report(
                    Codes.QuotedName,
                    name.Span,
                    "A classification system is written in quotes.",
                    fix: new TextEdit(name.Span, "\"" + name.Text + "\""));
            }
            else if (!quotedByDefault && name.Quoted && BareName.IsMatch(name.Text) && !Keywords.IsKeyword(name.Text))
            {
                _diagnostics.Report(
                    Codes.QuotedName,
                    name.Span,
                    $"The name needs no quotes: {name.Text}.",
                    fix: new TextEdit(name.Span, name.Text));
            }
        }
    }

    private void CheckConstraint(Constraint? constraint)
    {
        if (constraint is null)
        {
            return;
        }

        BoundTerm? lower = null;
        BoundTerm? upper = null;

        foreach (var term in constraint.Terms)
        {
            switch (term)
            {
                case InTerm { ListName: not null } named:
                    if (!_lists.ContainsKey(named.ListName))
                    {
                        var candidates = _lists.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                        var nearest = Nearest(named.ListName, candidates);
                        _diagnostics.Report(
                            Codes.UnknownList,
                            named.ListNameSpan,
                            $"No list named '{named.ListName}' is defined.",
                            hint: nearest is not null
                                ? $"Did you mean '{nearest}'?"
                                : $"Define it: define {named.ListName} = [\"A\", \"B\"]. A single text value is written in quotes after '='.",
                            expected: candidates.Count > 0 ? candidates : null,
                            fix: nearest is null ? null : new TextEdit(named.ListNameSpan, nearest));
                    }

                    break;

                case InTerm { Values: not null } inline:
                    CheckListValues(inline.Values, inline.Span);
                    break;

                case MatchesTerm matches:
                    foreach (var pattern in matches.Patterns)
                    {
                        CheckPattern(pattern);
                    }

                    break;

                case BetweenTerm between:
                    if (TryCompare(between.Min, between.Max, out var order) && order > 0)
                    {
                        _diagnostics.Report(
                            Codes.InvalidRange,
                            between.Span,
                            "The range is empty: the first number is greater than the second.");
                    }

                    break;

                case BoundTerm bound:
                    var isLower = bound.Kind is BoundKind.Greater or BoundKind.GreaterOrEqual;
                    if ((isLower && lower is not null) || (!isLower && upper is not null))
                    {
                        _diagnostics.Report(
                            Codes.InvalidRange,
                            bound.Span,
                            isLower ? "The value has two lower bounds." : "The value has two upper bounds.");
                    }
                    else if (isLower)
                    {
                        lower = bound;
                    }
                    else
                    {
                        upper = bound;
                    }

                    break;
            }
        }

        if (lower is not null && upper is not null)
        {
            if (TryCompare(lower.Value, upper.Value, out var order) && order > 0)
            {
                _diagnostics.Report(
                    Codes.InvalidRange,
                    SourceSpan.Join(lower.Span, upper.Span),
                    "The range is empty: the lower bound is greater than the upper bound.");
            }
            else if (lower.Kind == BoundKind.GreaterOrEqual
                     && upper.Kind == BoundKind.LessOrEqual
                     && constraint.Terms.Count == 2
                     && constraint.Terms[0] == lower)
            {
                var span = SourceSpan.Join(lower.Span, upper.Span);
                var replacement = $"between {Text(lower.Value.Span)} and {Text(upper.Value.Span)}";
                _diagnostics.Report(
                    Codes.RangeAsTwoBounds,
                    span,
                    "A range with both ends included is written with 'between'.",
                    fix: new TextEdit(span, replacement));
            }
        }
    }

    private void CheckPattern(StringLiteral pattern)
    {
        var problem = XsdPattern.Check(pattern.Value);
        if (problem is null)
        {
            return;
        }

        var descriptor = problem.Kind switch
        {
            PatternProblemKind.Anchor => Codes.AnchoredPattern,
            PatternProblemKind.DoubleBackslash => Codes.DoubleBackslash,
            PatternProblemKind.NonPortableEscape => Codes.NonPortableEscape,
            _ => Codes.InvalidPattern,
        };

        _diagnostics.Report(
            descriptor,
            pattern.Span,
            problem.Message,
            hint: problem.Hint,
            fix: problem.Repaired is null
                ? null
                : new TextEdit(pattern.Span, "\"" + problem.Repaired.Replace("\"", "\"\"") + "\""));
    }

    private void CheckListValues(List<Literal> values, SourceSpan span)
    {
        if (values.Count == 0)
        {
            _diagnostics.Report(Codes.InvalidList, span, "The list is empty.");
            return;
        }

        var text = values.Any(v => v is StringLiteral);
        var other = values.Any(v => v is not StringLiteral);
        if (text && other)
        {
            _diagnostics.Report(
                Codes.InvalidList,
                span,
                "A list holds text or numbers, not both.",
                hint: "Write every value in quotes, or none.");
        }
    }

    private string Text(SourceSpan span) => _source.Substring(span.Start, span.Length);

    private static bool TryCompare(NumberLiteral a, NumberLiteral b, out int order)
    {
        order = 0;
        if (!string.Equals(a.Unit, b.Unit, StringComparison.Ordinal)
            || !double.TryParse(a.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(b.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return false;
        }

        order = x.CompareTo(y);
        return true;
    }

    private static string? Nearest(string written, IReadOnlyList<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = EditDistance.Of(written.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best is not null && bestDistance <= Math.Max(2, best.Length / 3) ? best : null;
    }
}
