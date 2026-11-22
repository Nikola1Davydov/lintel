using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IdsLib.IdsSchema.XsNodes;
using Lintel.Diagnostics;
using Lintel.Ifc;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Analysis;

/// <summary>
/// The checks against the IFC schema (SPEC.md, section 8): every class, predefined type, attribute, standard
/// property set, property, data type and unit exists in the IFC versions of its rule, and values fit their types.
/// An error is reported exactly where the official IDS audit would reject the file, and for the rules of the
/// language itself (a measure carries a unit, a value is written as its kind). Where IDS accepts a name that the
/// IFC schema does not have, such as in a prohibited requirement, the checker warns.
/// </summary>
public sealed class SchemaChecker
{
    private static readonly Regex NumberText = new(@"^[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?$", RegexOptions.CultureInvariant);
    private readonly RuleFile _file;
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<string, ListDefinition> _lists = new(StringComparer.Ordinal);

    /// <summary>Values that stand in for the values of a named list at the place where the list is used; they have no fix.</summary>
    private readonly HashSet<Literal> _fromLists = new();

    private SchemaChecker(RuleFile file, DiagnosticBag diagnostics)
    {
        _file = file;
        _diagnostics = diagnostics;
        foreach (var definition in file.Definitions)
        {
            _lists[definition.Name] = definition;
        }
    }

    public static void Check(RuleFile file, DiagnosticBag diagnostics) => new SchemaChecker(file, diagnostics).Run();

    private void Run()
    {
        foreach (var rule in _file.Rules)
        {
            _diagnostics.CurrentRule = rule.Label;
            var schemas = Resolver.SchemasOf(rule, _file);
            if (schemas.Count == 0)
            {
                continue;
            }

            var errors = ErrorCount();
            var selection = Resolver.Selection(rule.Entity, schemas);
            if (rule.Entity is not null)
            {
                CheckEntity(rule.Entity, schemas);
            }

            foreach (var facet in rule.Where)
            {
                CheckFacet(facet, schemas, selection, Cardinality.Required);
            }

            foreach (var requirement in rule.Requirements)
            {
                CheckFacet(requirement.Facet, schemas, selection, requirement.Cardinality);
            }

            // IDS compares the classes of the facets only when each facet is valid by itself.
            if (ErrorCount() == errors)
            {
                CheckCommonClasses(rule, schemas);
            }
        }

        _diagnostics.CurrentRule = null;
    }

    private int ErrorCount() => _diagnostics.Items.Count(d => d.Severity == Severity.Error);

    // ---------------------------------------------------------------- classes

    /// <summary>Checks a class list and its predefined types.</summary>
    private void CheckEntity(EntitySelector entity, IReadOnlyList<IfcSchema> schemas)
    {
        var classes = entity.Classes;
        if (classes.Kind == MatcherKind.Pattern)
        {
            var empty = schemas.FirstOrDefault(s => AuditRules.Classes(entity, schemas, s).Count == 0);
            if (empty is not null)
            {
                _diagnostics.Report(
                    Codes.UnknownClass,
                    classes.Span,
                    $"No class of {empty.Version} matches the pattern.",
                    hint: "IDS matches the pattern against the class name in upper case, such as IFCWALL.");
                return;
            }
        }
        else
        {
            if (classes.Names.Count == 0)
            {
                return;
            }

            var allKnown = true;
            foreach (var name in classes.Names)
            {
                var known = schemas.Select(s => s.FindClass(name.Text)).Where(c => c is not null).Select(c => c!).ToList();
                if (known.Count == 0)
                {
                    // IDS lets a file name an enumeration of IFC as a class. Nothing in a model has such a class.
                    var other = schemas.Select(s => s.AuditClass(name.Text)).FirstOrDefault(c => c is not null);
                    if (classes.Only && other is not null && other.Name == name.Text)
                    {
                        _diagnostics.Report(
                            Codes.NotInSchema,
                            name.Span,
                            $"{name.Text} is a list of values in IFC, not a class of objects: nothing in a model has this class.");
                        continue;
                    }

                    allKnown = false;
                    ReportUnknownClass(name, schemas);
                    continue;
                }

                if (classes.Only)
                {
                    if (known.All(c => c.IsAbstract))
                    {
                        _diagnostics.Report(
                            Codes.AbstractClass,
                            name.Span,
                            $"{name.Text} is abstract: no element has exactly this class, so nothing is selected.",
                            hint: $"Remove 'only' to select every subtype of {name.Text}.");
                    }

                    continue;
                }

                // A class that one version of the rule lacks: there the rule works through the subtypes that version has.
                foreach (var schema in schemas.Where(s => s.FindClass(name.Text) is null))
                {
                    var here = Resolver.ExpandClass(name.Text, schemas)
                        .Select(upper => schema.AuditClass(upper)?.Name)
                        .Where(n => n is not null)
                        .ToList();
                    if (here.Count > 0)
                    {
                        _diagnostics.Report(
                            Codes.NotInSchema,
                            name.Span,
                            $"{name.Text} is not a class of {schema.Version}. There the rule selects those of its subtypes that {schema.Version} has too: {Sample(here!)}.",
                            hint: "Give this version a rule of its own when it needs other classes.");
                    }
                }
            }

            if (!allKnown)
            {
                return;
            }

            var missing = schemas.FirstOrDefault(s => AuditRules.Classes(entity, schemas, s).Count == 0);
            if (missing is not null)
            {
                _diagnostics.Report(
                    Codes.ClassNotInVersion,
                    classes.Span,
                    $"{missing.Version} has none of these classes: {string.Join(", ", classes.Names.Select(n => n.Text))}.",
                    hint: "Add the class of this version to the list, or give this version a rule of its own.");
                return;
            }
        }

        if (entity.Types is not null)
        {
            CheckPredefinedTypes(entity, entity.Types, schemas);
        }
    }

    private void ReportUnknownClass(NameItem name, IReadOnlyList<IfcSchema> schemas)
    {
        var versions = string.Join(", ", schemas.Select(s => s.Version));
        var candidates = schemas.SelectMany(s => s.ClassNames).Distinct(StringComparer.Ordinal).ToList();
        var nearest = Suggestions.Nearest(name.Text, candidates);

        // The class may belong to a version the rule does not name.
        var elsewhere = IfcVersions.All
            .Where(v => schemas.All(s => s.Version != v) && IfcSchema.Get(v)?.FindClass(name.Text) is not null)
            .ToList();

        string? hint = null;
        if (nearest is not null)
        {
            hint = $"Did you mean {nearest}?";
        }
        else if (elsewhere.Count > 0)
        {
            hint = $"{name.Text} exists in {string.Join(", ", elsewhere)} only.";
        }

        _diagnostics.Report(
            Codes.UnknownClass,
            name.Span,
            $"{name.Text} is not a class of {versions}.",
            hint: hint,
            expected: Suggestions.Top(name.Text, candidates),
            fix: nearest is null ? null : new TextEdit(name.Span, nearest));
    }

    /// <summary>
    /// IDS takes any text as a predefined type when every selected class has a predefined type with the value
    /// USERDEFINED; otherwise only the values that all of them share. The language adds that a value of the
    /// standard is written bare and any other text in quotes.
    /// </summary>
    private void CheckPredefinedTypes(EntitySelector entity, Matcher types, IReadOnlyList<IfcSchema> schemas)
    {
        var reported = new HashSet<NameItem>();
        foreach (var schema in schemas)
        {
            var selected = AuditRules.Classes(entity, schemas, schema);
            if (selected.Count == 0)
            {
                continue;
            }

            var common = AuditRules.PredefinedTypes(selected, schema, out var without);
            if (common.Contains("USERDEFINED"))
            {
                continue;
            }

            var expected = common.OrderBy(v => v, StringComparer.Ordinal).ToList();
            var reason = without is not null
                ? $"{without} has no predefined type in {schema.Version}"
                : $"the selected classes share no user-defined type in {schema.Version}";
            if (types.Kind == MatcherKind.Pattern)
            {
                if (AuditRules.Match(types, common).Count == 0)
                {
                    _diagnostics.Report(
                        Codes.UnknownPredefinedType,
                        types.Span,
                        $"No predefined type matches the pattern: {reason}.",
                        hint: without is not null ? "Remove 'type', or name only classes that have a predefined type." : null,
                        expected: expected);
                    return;
                }

                continue;
            }

            foreach (var type in types.Names.Where(t => !common.Contains(t.Text) && reported.Add(t)))
            {
                var nearest = Suggestions.Nearest(type.Text, expected);
                _diagnostics.Report(
                    Codes.UnknownPredefinedType,
                    type.Span,
                    $"{type.Text} cannot be a predefined type here: {reason}.",
                    hint: nearest is not null
                        ? $"Did you mean {nearest}?"
                        : without is not null ? "Remove 'type', or name only classes that have a predefined type." : null,
                    expected: expected,
                    fix: nearest is null ? null : new TextEdit(type.Span, nearest));
            }
        }

        if (types.Kind == MatcherKind.Pattern)
        {
            return;
        }

        var standard = Resolver.StandardPredefinedTypes(entity, schemas);
        var listed = standard.OrderBy(v => v, StringComparer.Ordinal).ToList();
        foreach (var type in types.Names.Where(t => !reported.Contains(t)))
        {
            if (type.Quoted && standard.Contains(type.Text))
            {
                _diagnostics.Report(
                    Codes.QuotedName,
                    type.Span,
                    $"{type.Text} is a predefined type of the standard and needs no quotes.",
                    fix: new TextEdit(type.Span, type.Text));
            }
            else if (!type.Quoted && standard.Contains(type.Text))
            {
                // A value of one selected class that another selected class does not have.
                var lacking = schemas
                    .SelectMany(schema => AuditRules.Classes(entity, schemas, schema)
                        .Where(c => schema.AuditClass(c)?.PredefinedTypeValues?.Contains(type.Text) != true)
                        .Select(c => schema.AuditClass(c)?.Name ?? c))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (lacking.Count > 0)
                {
                    _diagnostics.Report(
                        Codes.NotInSchema,
                        type.Span,
                        $"{type.Text} is not a predefined type of {Sample(lacking)}. For such an element it can only match a type that its author named {type.Text}.",
                        hint: "Give these classes a rule of their own when they need another type.");
                }
            }
            else if (!type.Quoted && !standard.Contains(type.Text))
            {
                var nearest = Suggestions.Nearest(type.Text, listed);
                _diagnostics.Report(
                    Codes.UnknownPredefinedType,
                    type.Span,
                    $"{type.Text} is not a predefined type of {string.Join(", ", entity.Classes.Names.Select(n => n.Text))}.",
                    hint: nearest is not null
                        ? $"Did you mean {nearest}?"
                        : $"A type of your own is written in quotes: type \"{type.Text}\".",
                    expected: listed,
                    fix: nearest is null ? null : new TextEdit(type.Span, nearest));
            }
        }
    }

    // ---------------------------------------------------------------- facets

    private void CheckFacet(Facet facet, IReadOnlyList<IfcSchema> schemas, Dictionary<IfcSchema, HashSet<string>>? selection, Cardinality cardinality)
    {
        switch (facet)
        {
            case EntityFacet entity:
                CheckEntity(entity.Entity, schemas);
                break;

            case AttributeFacet attribute:
                CheckAttribute(attribute, schemas, selection, cardinality);
                break;

            case PropertyFacet property:
                CheckProperty(property, schemas, selection, cardinality);
                break;

            case ClassificationFacet classification:
                CheckValue(classification.Value, ValueType.Text("a classification code"));
                break;

            case MaterialFacet material:
                CheckValue(material.Value, ValueType.Text("a material"));
                break;

            case PartOfFacet partOf:
                var errors = ErrorCount();
                CheckEntity(partOf.Entity, schemas);
                if (cardinality == Cardinality.Required && ErrorCount() == errors)
                {
                    CheckRelationTarget(partOf, schemas);
                }

                break;
        }
    }

    /// <summary>With a relationship, the whole must be of the class that the relationship takes as the whole.</summary>
    private void CheckRelationTarget(PartOfFacet partOf, IReadOnlyList<IfcSchema> schemas)
    {
        foreach (var schema in schemas)
        {
            var relation = AuditRules.Relation(partOf, schema);
            if (relation is null)
            {
                continue;
            }

            var whole = AuditRules.Classes(partOf.Entity, schemas, schema);
            if (whole.Count > 0 && !whole.Overlaps(AuditRules.ConcreteClasses(relation.OwnerIfcType, schema)))
            {
                var owner = schema.AuditClass(relation.OwnerIfcType)?.Name ?? relation.OwnerIfcType;
                var written = partOf.Entity.Classes.Kind == MatcherKind.Pattern
                    ? "no class of the pattern"
                    : string.Join(", ", partOf.Entity.Classes.Names.Select(n => n.Text)) + " is not";
                _diagnostics.Report(
                    Codes.InvalidRelationTarget,
                    partOf.Entity.Classes.Span,
                    $"With {partOf.Relation} the whole is a kind of {owner}; {written}.",
                    hint: "Name another class, or another relationship after 'via'.");
                return;
            }
        }
    }

    private void CheckAttribute(
        AttributeFacet facet,
        IReadOnlyList<IfcSchema> schemas,
        Dictionary<IfcSchema, HashSet<string>>? selection,
        Cardinality cardinality)
    {
        foreach (var schema in schemas)
        {
            var matched = AuditRules.AttributeNames(facet, schema);
            if (matched.Count == 0)
            {
                ReportUnknownAttribute(facet, schema, schemas, selection);
                return;
            }

            if (facet.Value is not null && AuditRules.AttributeBases(matched, schema).Count == 0)
            {
                _diagnostics.Report(
                    Codes.ValueTypeMismatch,
                    facet.Value.Span,
                    $"{string.Join(", ", matched)} holds an object or a list in {schema.Version} and cannot be compared.",
                    hint: "IDS compares single values only. Require the attribute without a constraint.");
                return;
            }
        }

        // One name of a list that no class has, next to names that exist: IDS accepts the list.
        foreach (var name in facet.Name.Names.Where(n => schemas.All(s => !s.AuditAttributeNames.Contains(n.Text))))
        {
            var candidates = AttributeNames(schemas, selection);
            var nearest = Suggestions.Nearest(name.Text, candidates);
            _diagnostics.Report(
                Codes.NotInSchema,
                name.Span,
                $"No class has an attribute {name.Text}. IDS accepts the list because of its other names.",
                hint: nearest is not null ? $"Did you mean {nearest}?" : "Remove the name.",
                expected: Suggestions.Top(name.Text, candidates),
                fix: nearest is null ? null : new TextEdit(name.Span, nearest));
        }

        if (facet.Value is null)
        {
            return;
        }

        var typing = Resolver.AttributeType(facet.Name, selection, schemas);

        // An optional requirement needs a constraint; one on text that an empty text meets does not count.
        if (cardinality == Cardinality.Optional
            && (typing is null || typing.XmlBase == "xs:string")
            && EmptyText.Meets(facet.Value, _lists))
        {
            EmptyText.Report(_diagnostics, facet.Value);
            return;
        }

        if (typing is { Checked: true })
        {
            var subject = facet.Name.Names.Count == 1 ? "The attribute " + facet.Name.Names[0].Text : "The attribute";
            CheckValue(facet.Value, new ValueType(typing.Type?.Name ?? subject, typing.XmlBase, typing.Type));
        }
    }

    private void ReportUnknownAttribute(
        AttributeFacet facet,
        IfcSchema schema,
        IReadOnlyList<IfcSchema> schemas,
        Dictionary<IfcSchema, HashSet<string>>? selection)
    {
        var candidates = AttributeNames(schemas, selection);
        if (facet.Name.Kind == MatcherKind.Pattern || facet.Name.Names.Count == 0)
        {
            _diagnostics.Report(
                Codes.UnknownAttribute,
                facet.Name.Span,
                $"No attribute of {schema.Version} matches the pattern.",
                expected: Suggestions.Top(string.Empty, candidates));
            return;
        }

        foreach (var name in facet.Name.Names)
        {
            var nearest = Suggestions.Nearest(name.Text, candidates);
            _diagnostics.Report(
                Codes.UnknownAttribute,
                name.Span,
                $"No class of {schema.Version} has an attribute {name.Text}.",
                hint: nearest is not null
                    ? $"Did you mean {nearest}?"
                    : "Attributes are the fixed fields of an IFC class, such as Name or Description. Other data is a property: property Set.Name.",
                expected: Suggestions.Top(name.Text, candidates),
                fix: nearest is null ? null : new TextEdit(name.Span, nearest));
        }
    }

    private static List<string> AttributeNames(IReadOnlyList<IfcSchema> schemas, Dictionary<IfcSchema, HashSet<string>>? selection)
    {
        if (selection is null)
        {
            return schemas.SelectMany(s => s.AttributeNames).Distinct(StringComparer.Ordinal).ToList();
        }

        return schemas
            .SelectMany(s => selection[s].SelectMany(c => s.FindClass(c)?.Attributes.Keys ?? Enumerable.Empty<string>()))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    private void CheckProperty(PropertyFacet facet, IReadOnlyList<IfcSchema> schemas, Dictionary<IfcSchema, HashSet<string>>? selection, Cardinality cardinality)
    {
        // IDS checks the names of a property against the standard only in a requirement that must be met.
        // In an optional or prohibited one the same problems are warnings.
        var required = cardinality == Cardinality.Required;

        // The data type as written.
        IfcDataType? written = null;
        if (facet.DataType is not null)
        {
            written = IfcDataTypes.Find(facet.DataType);
            if (written is null || written.Name != facet.DataType)
            {
                var nearest = written?.Name ?? Suggestions.Nearest(facet.DataType, IfcDataTypes.Names);
                _diagnostics.Report(
                    Codes.UnknownDataType,
                    facet.DataTypeSpan,
                    $"{facet.DataType} is not an IFC data type.",
                    hint: nearest is null ? null : $"Did you mean {nearest}?",
                    expected: Suggestions.Top(facet.DataType, IfcDataTypes.Names),
                    fix: nearest is null ? null : new TextEdit(facet.DataTypeSpan, nearest));
                if (written is null)
                {
                    return;
                }
            }
            else if (schemas.FirstOrDefault(s => !written.IsValidIn(s)) is { } missing)
            {
                _diagnostics.Report(
                    Codes.UnknownDataType,
                    facet.DataTypeSpan,
                    $"{written.Name} does not exist in {missing.Version}.");
                return;
            }
        }

        var warned = false;
        foreach (var schema in schemas)
        {
            var sets = AuditRules.StandardSets(facet, schema);
            if (sets.Count == 0)
            {
                if (ReportReservedName(facet, schema, selection, required, ref warned))
                {
                    return;
                }

                continue;
            }

            var properties = AuditRules.StandardProperties(facet, sets, schema);
            if (properties.Count == 0)
            {
                if (required || !warned)
                {
                    ReportUnknownProperty(facet, sets, schema, required);
                    warned = true;
                }

                if (required)
                {
                    return;
                }

                continue;
            }

            var measures = AuditRules.Measures(sets, properties, schema);
            if (written is not null && !measures.Contains(written.Name.ToUpperInvariant()) && (required || !warned))
            {
                var names = measures.Select(m => IfcDataTypes.Find(m)?.Name ?? m).OrderBy(n => n, StringComparer.Ordinal).ToList();
                _diagnostics.Report(
                    required ? Codes.DataTypeMismatch : Codes.NotInSchema,
                    facet.DataTypeSpan,
                    $"The standard defines {Describe(facet)} as {string.Join(" or ", names)} in {schema.Version}." + (required ? string.Empty : NotChecked(cardinality)),
                    hint: names.Count == 1 ? "Remove 'datatype': the compiler takes the type of a standard property from the standard." : null,
                    expected: names,
                    fix: names.Count == 1 ? new TextEdit(facet.DataTypeSpan, names[0]) : null);
                warned = true;
                if (required)
                {
                    return;
                }
            }
        }

        if (facet.Value is null)
        {
            return;
        }

        var type = Resolver.DataTypeOf(facet, schemas, _lists, out var origin);
        if (type is null)
        {
            _diagnostics.Report(
                Codes.DataTypeRequired,
                facet.Value.Span,
                "A number with a unit is a measure, and the data type of this property is not known.",
                hint: "Write the data type after the property: datatype IfcLengthMeasure.",
                expected: new[] { "IfcLengthMeasure", "IfcAreaMeasure", "IfcVolumeMeasure", "IfcPlaneAngleMeasure", "IfcMassMeasure", "IfcReal", "IfcInteger" });
            return;
        }

        // The type that the compiler found must be one the standard gives the property, in every version of the rule.
        if (required && written is null)
        {
            foreach (var schema in schemas)
            {
                var sets = AuditRules.StandardSets(facet, schema);
                var properties = sets.Count == 0 ? new List<string>() : AuditRules.StandardProperties(facet, sets, schema);
                if (properties.Count == 0)
                {
                    continue;
                }

                var measures = AuditRules.Measures(sets, properties, schema);
                if (measures.Contains(type.Name.ToUpperInvariant()))
                {
                    continue;
                }

                var names = measures.Select(m => IfcDataTypes.Find(m)?.Name ?? m).OrderBy(n => n, StringComparer.Ordinal).ToList();
                if (origin == DataTypeOrigin.Value)
                {
                    _diagnostics.Report(
                        Codes.DataTypeRequired,
                        facet.Value.Span,
                        $"The property has several data types in the standard ({string.Join(", ", names)}); say which one the value has.",
                        hint: $"Write the data type after the property: datatype {names.FirstOrDefault() ?? "IfcLabel"}.",
                        expected: names);
                }
                else
                {
                    _diagnostics.Report(
                        Codes.DataTypeMismatch,
                        facet.Name.Span,
                        $"{schema.Version} defines {Describe(facet)} as {string.Join(" or ", names)}, another version of the rule as {type.Name}.",
                        hint: "IDS takes one data type for a property: give each IFC version a rule of its own.",
                        expected: names);
                }

                return;
            }
        }

        if (origin == DataTypeOrigin.Value && type.IsNumeric)
        {
            _diagnostics.Report(
                Codes.InferredNumberType,
                facet.Value.Span,
                $"The data type is taken from the number: {type.Name}. A model that stores this property with another type fails the rule.",
                hint: $"Write the type when you know it: datatype {type.Name}, or a measure such as IfcLengthMeasure with a unit.");
        }

        var before = ErrorCount();
        CheckValue(facet.Value, ValueType.Of(type));
        if (required && ErrorCount() == before)
        {
            CheckAllowedValues(facet, schemas);
        }
    }

    private static string Describe(PropertyFacet facet)
    {
        var set = facet.PropertySet.Names.Count == 1 ? facet.PropertySet.Names[0].Text : "the property set";
        var name = facet.Name.Names.Count == 1 ? facet.Name.Names[0].Text : "the property";
        return facet.PropertySet.Names.Count == 1 && facet.Name.Names.Count == 1 ? set + "." + name : name + " of " + set;
    }

    private static string NotChecked(Cardinality cardinality) =>
        cardinality == Cardinality.Prohibited
            ? " IDS does not check this in a prohibited requirement; as written, the requirement always passes."
            : " IDS does not check this in an optional requirement; as written, the requirement applies to nothing.";

    /// <summary>
    /// A set that the standard does not have. The prefix Pset_ is reserved: IDS rejects it in a requirement that
    /// must be met. Returns true when an error was reported.
    /// </summary>
    private bool ReportReservedName(
        PropertyFacet facet,
        IfcSchema schema,
        Dictionary<IfcSchema, HashSet<string>>? selection,
        bool required,
        ref bool warned)
    {
        if (facet.PropertySet.Kind == MatcherKind.Pattern)
        {
            return false;
        }

        var reserved = facet.PropertySet.Names.FirstOrDefault(n => n.Text.StartsWith("Pset_", StringComparison.Ordinal));
        var quantity = facet.PropertySet.Names.FirstOrDefault(n => n.Text.StartsWith("Qto_", StringComparison.Ordinal));
        var name = reserved ?? quantity;
        if (name is null || (warned && !(required && reserved is not null)))
        {
            return false;
        }

        var error = required && reserved is not null;
        var nearest = Suggestions.Nearest(name.Text, schema.PropertySetNames);
        var expected = selection is not null && selection[schema].Count > 0
            ? SetsFor(schema, new HashSet<string>(selection[schema].Select(c => c.ToUpperInvariant()), StringComparer.Ordinal))
            : Suggestions.Top(name.Text, schema.PropertySetNames);
        var message = reserved is not null
            ? $"{name.Text} is not a property set of {schema.Version}. Names that start with Pset_ are reserved for the standard."
            : $"{name.Text} is not a quantity set of {schema.Version}. Qto_ is the prefix of the quantity sets of the standard.";
        _diagnostics.Report(
            error ? Codes.UnknownPropertySet : Codes.NotInSchema,
            name.Span,
            error || reserved is null ? message : message + " IDS checks this only in a requirement that must be met.",
            hint: nearest is not null ? $"Did you mean {nearest}?" : "Give a property set of your own another name, such as ACME_Common.",
            expected: expected,
            fix: nearest is null ? null : new TextEdit(name.Span, nearest));
        warned = true;
        return error;
    }

    private void ReportUnknownProperty(PropertyFacet facet, IReadOnlyList<string> sets, IfcSchema schema, bool required)
    {
        var candidates = sets
            .SelectMany(set => schema.FindPropertySet(set)?.PropertyNames ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var where = sets.Count == 1 ? sets[0] : "These property sets";
        var verb = sets.Count == 1 ? "has" : "have";
        var suffix = required ? string.Empty : " IDS checks this only in a requirement that must be met.";

        if (facet.Name.Kind == MatcherKind.Pattern || facet.Name.Names.Count == 0)
        {
            _diagnostics.Report(
                required ? Codes.UnknownProperty : Codes.NotInSchema,
                facet.Name.Span,
                $"{where} {verb} no property that matches the pattern in {schema.Version}.{suffix}",
                expected: candidates);
            return;
        }

        var name = facet.Name.Names[0];
        var nearest = Suggestions.Nearest(name.Text, candidates);
        _diagnostics.Report(
            required ? Codes.UnknownProperty : Codes.NotInSchema,
            facet.Name.Names.Count == 1 ? name.Span : facet.Name.Span,
            facet.Name.Names.Count == 1
                ? $"{where} {verb} no property {name.Text} in {schema.Version}.{suffix}"
                : $"{where} {verb} none of these properties in {schema.Version}.{suffix}",
            hint: nearest is not null ? $"Did you mean {nearest}?" : "A property of your own belongs to a property set of your own.",
            expected: candidates,
            fix: nearest is null || facet.Name.Names.Count != 1 ? null : new TextEdit(name.Span, nearest));
    }

    /// <summary>The standard property sets that may be attached to any of the classes, given in upper case.</summary>
    private static IReadOnlyList<string> SetsFor(IfcSchema schema, HashSet<string> classes) =>
        schema.PropertySetNames
            .Where(n => classes.Overlaps(schema.AuditClassesOf(n)))
            .Take(30)
            .ToList();

    /// <summary>
    /// An enumerated property of the standard takes the values the standard lists. IDS rejects a constraint that
    /// none of them can meet; a single value of a list that is not one of them is a warning.
    /// </summary>
    private void CheckAllowedValues(PropertyFacet facet, IReadOnlyList<IfcSchema> schemas)
    {
        var constraint = facet.Value!;
        foreach (var schema in schemas)
        {
            var sets = AuditRules.StandardSets(facet, schema);
            var properties = sets.Count == 0 ? new List<string>() : AuditRules.StandardProperties(facet, sets, schema);
            var allowed = properties.Count == 0 ? null : AuditRules.AllowedValues(sets, properties, schema);
            if (allowed is null)
            {
                continue;
            }

            var listed = allowed.OrderBy(v => v, StringComparer.Ordinal).ToList();
            if (constraint.Equal is not null)
            {
                if (!allowed.Contains(TextOf(constraint.Equal)))
                {
                    ReportValueNotAllowed(constraint.Equal, facet, listed, Codes.ValueTypeMismatch);
                    return;
                }

                continue;
            }

            foreach (var term in constraint.Terms)
            {
                switch (term)
                {
                    case InTerm:
                        var values = TermLiterals(term).ToList();
                        var unknown = values.Where(v => !allowed.Contains(TextOf(v))).ToList();
                        if (values.Count > 0 && unknown.Count == values.Count)
                        {
                            ReportValueNotAllowed(unknown[0], facet, listed, Codes.ValueTypeMismatch);
                            return;
                        }

                        foreach (var value in unknown)
                        {
                            ReportValueNotAllowed(value, facet, listed, Codes.NotInSchema);
                        }

                        if (unknown.Count > 0)
                        {
                            return;
                        }

                        break;

                    case MatchesTerm matches:
                        var patterns = matches.Patterns.Select(p => XsdPattern.ToAuditRegex(p.Value)).Where(p => p is not null).ToList();
                        if (patterns.Count > 0 && !allowed.Any(a => patterns.Any(p => p!.IsMatch(a))))
                        {
                            _diagnostics.Report(
                                Codes.ValueTypeMismatch,
                                matches.Span,
                                $"No value that the standard lists for {Describe(facet)} matches the pattern.",
                                expected: listed);
                            return;
                        }

                        break;
                }
            }
        }
    }

    private void ReportValueNotAllowed(Literal literal, PropertyFacet facet, IReadOnlyList<string> allowed, DiagnosticDescriptor descriptor)
    {
        var text = TextOf(literal);
        var nearest = Suggestions.Nearest(text, allowed);

        // A value is data of the author. Only another spelling of the same word is repaired; a word that merely
        // looks like a value of the standard ("neu" and "NEW") is for the author to translate.
        var sameWord = nearest is not null && string.Equals(nearest, text, StringComparison.OrdinalIgnoreCase);
        var hint = sameWord
            ? $"The standard writes it \"{nearest}\"."
            : (nearest is not null ? $"Did you mean \"{nearest}\"? " : string.Empty)
              + "Write the value of the standard that means the same; the wording of the requirement stays in 'source'. "
              + "For values of your own use a property of your own.";
        _diagnostics.Report(
            descriptor,
            literal.Span,
            $"\"{text}\" is not a value of {Describe(facet)}; the standard lists the values.",
            hint: hint,
            expected: allowed,
            fix: Fix(literal, literal.Span, sameWord ? "\"" + nearest + "\"" : null));
    }

    private static string TextOf(Literal literal) => literal switch
    {
        StringLiteral text => text.Value,
        BooleanLiteral boolean => boolean.Value ? "true" : "false",
        NumberLiteral number => number.Text,
        _ => string.Empty,
    };

    // ---------------------------------------------------------------- classes that can meet all facets

    /// <summary>
    /// IDS rejects a rule when no class can meet all of its conditions and required requirements at once:
    /// an attribute that the selected classes do not have, a property set of the standard that is made for other
    /// classes, a class in a requirement that is not among the selected ones. The same in an optional or
    /// prohibited requirement is valid IDS, and a warning here.
    /// </summary>
    private void CheckCommonClasses(Rule rule, IReadOnlyList<IfcSchema> schemas)
    {
        var warned = new HashSet<Facet>();
        foreach (var schema in schemas)
        {
            HashSet<string>? running = rule.Entity is null ? null : AuditRules.Classes(rule.Entity, schemas, schema);
            var steps = rule.Where.Select(f => (Facet: f, Required: true))
                .Concat(rule.Requirements.Select(r => (r.Facet, Required: r.Cardinality == Cardinality.Required || r.Facet is EntityFacet)));

            foreach (var (facet, required) in steps)
            {
                var filter = AuditRules.Filter(facet, required: true, schemas, schema);
                if (filter is null)
                {
                    continue;
                }

                if (!required)
                {
                    if (running is not null && !running.Overlaps(filter) && warned.Add(facet))
                    {
                        ReportNoCommonClass(facet, running, schema, warning: true);
                    }

                    continue;
                }

                if (running is not null && !running.Overlaps(filter))
                {
                    ReportNoCommonClass(facet, running, schema, warning: false);
                    return;
                }

                if (running is null)
                {
                    running = new HashSet<string>(filter, StringComparer.Ordinal);
                }
                else
                {
                    running.IntersectWith(filter);
                }
            }
        }
    }

    private void ReportNoCommonClass(Facet facet, HashSet<string> selected, IfcSchema schema, bool warning)
    {
        var suffix = warning ? " IDS accepts this in a requirement that is optional or prohibited, but it then checks nothing." : string.Empty;
        switch (facet)
        {
            case AttributeFacet attribute:
                var candidates = selected
                    .SelectMany(c => schema.FindClassIgnoreCase(c)?.Attributes.Keys ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                var single = attribute.Name.Kind == MatcherKind.Exact && attribute.Name.Names.Count == 1 ? attribute.Name.Names[0] : null;
                var nearest = single is null ? null : Suggestions.Nearest(single.Text, candidates);
                _diagnostics.Report(
                    warning ? Codes.NotInSchema : Codes.UnknownAttribute,
                    single?.Span ?? attribute.Name.Span,
                    (single is not null
                        ? $"None of the selected classes has an attribute {single.Text} in {schema.Version}."
                        : $"None of the selected classes has such an attribute in {schema.Version}.") + suffix,
                    hint: nearest is not null
                        ? $"Did you mean {nearest}?"
                        : "Attributes are the fixed fields of an IFC class, such as Name or Description. Other data is a property: property Set.Name.",
                    expected: single is null ? candidates.Take(30).ToList() : Suggestions.Top(single.Text, candidates),
                    fix: nearest is null ? null : new TextEdit(single!.Span, nearest));
                break;

            case PropertyFacet property:
                var sets = AuditRules.StandardSets(property, schema);
                var made = sets.SelectMany(s => schema.FindPropertySet(s)?.ApplicableClasses ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
                _diagnostics.Report(
                    warning ? Codes.NotInSchema : Codes.PropertySetNotApplicable,
                    property.PropertySet.Span,
                    $"{(sets.Count == 1 ? sets[0] : "The property set")} is defined for {Sample(made)}, not for the selected classes." + suffix,
                    hint: "Use the property set of the selected class.",
                    expected: SetsFor(schema, selected));
                break;

            case EntityFacet:
                _diagnostics.Report(
                    Codes.IncompatibleEntity,
                    facet.Span,
                    "No selected element can have this class: it is not among the classes after 'for'.",
                    hint: "A requirement on the class makes sense when 'for' selects several classes or none.");
                break;

            case PartOfFacet partOf:
                var part = AuditRules.Relation(partOf, schema)?.PartIfcType;
                _diagnostics.Report(
                    warning ? Codes.NotInSchema : Codes.NoCommonClass,
                    facet.Span,
                    $"With {partOf.Relation} the part is a kind of {schema.AuditClass(part ?? string.Empty)?.Name ?? part}; none of the selected classes is." + suffix,
                    hint: "Name another relationship after 'via', or leave it out.");
                break;

            default:
                var what = facet is ClassificationFacet ? "a classification" : "a material";
                _diagnostics.Report(
                    warning ? Codes.NotInSchema : Codes.NoCommonClass,
                    facet.Span,
                    $"IFC has no way to give the selected classes {what} in {schema.Version}." + suffix,
                    hint: "Select objects, such as IfcElement, or drop the facet.");
                break;
        }
    }

    private static string Sample(IReadOnlyList<string> names) =>
        names.Count <= 6 ? string.Join(", ", names) : string.Join(", ", names.Take(5)) + $" and {names.Count - 5} more";

    // ---------------------------------------------------------------- values

    /// <summary>What kind of value a facet takes.</summary>
    private sealed class ValueType
    {
        public ValueType(string name, string xmlBase, IfcDataType? dataType)
        {
            Name = name;
            XmlBase = xmlBase;
            DataType = dataType;
        }

        public string Name { get; }

        public string XmlBase { get; }

        public IfcDataType? DataType { get; }

        public static ValueType Of(IfcDataType type) => new(type.Name, type.XmlBase, type);

        public static ValueType Text(string name) => new(name, "xs:string", null);
    }

    private enum ValueKind
    {
        Unknown,
        Text,
        Number,
        Boolean,

        /// <summary>A date, a time, a date with a time, or a duration: written as text, and ordered.</summary>
        Moment,
    }

    private static ValueKind KindOf(string xmlBase) => xmlBase switch
    {
        "xs:string" or "xs:anyURI" => ValueKind.Text,
        "xs:double" or "xs:integer" or "xs:decimal" or "xs:float" => ValueKind.Number,
        "xs:boolean" => ValueKind.Boolean,
        "xs:date" or "xs:dateTime" or "xs:time" or "xs:duration" => ValueKind.Moment,
        _ => ValueKind.Unknown,
    };

    /// <summary>
    /// The terms that IDS allows for each kind of value, and the way each value is written.
    /// Text: lists, patterns and lengths. Numbers, dates and times: lists, patterns and bounds.
    /// True or false: one value or a pattern.
    /// </summary>
    private void CheckValue(Constraint? constraint, ValueType type)
    {
        if (constraint is null)
        {
            return;
        }

        var kind = KindOf(type.XmlBase);
        if (kind == ValueKind.Unknown)
        {
            return;
        }

        foreach (var term in constraint.Terms)
        {
            switch (term)
            {
                case BoundTerm or BetweenTerm when kind == ValueKind.Text:
                    _diagnostics.Report(
                        Codes.ValueTypeMismatch,
                        term.Span,
                        $"{type.Name} is text and cannot be compared with a bound.",
                        hint: "Use a list ('in [...]'), a pattern ('matches') or 'length'.");
                    return;

                case BoundTerm or BetweenTerm or InTerm when kind == ValueKind.Boolean:
                    _diagnostics.Report(
                        Codes.ValueTypeMismatch,
                        term.Span,
                        $"{type.Name} is true or false.",
                        expected: new[] { "= true", "= false" });
                    return;

                case LengthTerm when kind != ValueKind.Text:
                    _diagnostics.Report(
                        Codes.ValueTypeMismatch,
                        term.Span,
                        $"{type.Name} is not text; 'length' counts the characters of a text.",
                        hint: kind == ValueKind.Boolean ? "Write '= true' or '= false'." : "Use '=', a bound, 'between' or a list.");
                    return;

                case MatchesTerm when kind != ValueKind.Text:
                    _diagnostics.Report(
                        Codes.PatternOnNumber,
                        term.Span,
                        $"{type.Name} is not text. The pattern is compared with the way a checker writes the value, which IDS does not fix.",
                        hint: kind == ValueKind.Boolean ? "Write '= true' or '= false'." : "Use '=', a bound, 'between' or a list.");
                    break;
            }
        }

        foreach (var literal in Literals(constraint))
        {
            switch (kind)
            {
                case ValueKind.Text:
                    CheckTextValue(literal, type);
                    break;
                case ValueKind.Number:
                    CheckNumberValue(literal, type);
                    break;
                case ValueKind.Boolean:
                    CheckBooleanValue(literal, type);
                    break;
                default:
                    CheckMomentValue(literal, type);
                    break;
            }
        }
    }

    private void CheckTextValue(Literal literal, ValueType type)
    {
        if (literal is StringLiteral)
        {
            return;
        }

        var text = TextOf(literal);
        _diagnostics.Report(
            Codes.ValueTypeMismatch,
            literal.Span,
            $"{type.Name} is text; a text value is written in quotes.",
            fix: Fix(literal, literal.Span, literal is NumberLiteral { Unit: not null } ? null : "\"" + text + "\""));
    }

    private void CheckBooleanValue(Literal literal, ValueType type)
    {
        switch (literal)
        {
            case BooleanLiteral:
                return;

            // IDS also takes 0 and 1 for false and true; they are written as text, so that they stay what they are.
            case StringLiteral { Value: "0" or "1" }:
                return;

            case StringLiteral text:
                var lower = text.Value.ToLowerInvariant();
                _diagnostics.Report(
                    Codes.ValueTypeMismatch,
                    literal.Span,
                    $"{type.Name} is true or false, without quotes.",
                    expected: new[] { "true", "false" },
                    fix: Fix(literal, literal.Span, lower is "true" or "false" ? lower : null));
                return;

            case NumberLiteral number:
                _diagnostics.Report(
                    Codes.ValueTypeMismatch,
                    literal.Span,
                    $"{type.Name} is true or false.",
                    expected: new[] { "true", "false" },
                    fix: Fix(literal, literal.Span, number is { Text: "1", Unit: null } ? "true" : number is { Text: "0", Unit: null } ? "false" : null));
                return;
        }
    }

    private void CheckMomentValue(Literal literal, ValueType type)
    {
        var (what, example) = type.XmlBase switch
        {
            "xs:date" => ("a date", "\"2026-11-22\""),
            "xs:dateTime" => ("a date with a time", "\"2026-11-22T10:00:00\""),
            "xs:time" => ("a time", "\"10:00:00\""),
            _ => ("a duration", "\"P1D\" for one day or \"PT2H\" for two hours"),
        };

        if (literal is StringLiteral text && XsTypes.IsValid(text.Value, XsTypes.GetBaseFrom(type.XmlBase)))
        {
            return;
        }

        _diagnostics.Report(
            Codes.ValueTypeMismatch,
            literal.Span,
            $"{type.Name} is {what}, written as text in the form of ISO 8601.",
            hint: $"Write, for example, {example}.");
    }

    private void CheckNumberValue(Literal literal, ValueType type)
    {
        var units = type.DataType is null ? Array.Empty<string>() : Units.For(type.DataType);
        if (literal is not NumberLiteral number)
        {
            // A number that only IDS can spell, such as NaN or +INF, is written as text and passed on as it is.
            if (literal is StringLiteral special
                && !NumberText.IsMatch(special.Value)
                && XsTypes.IsValid(special.Value, XsTypes.GetBaseFrom(type.XmlBase)))
            {
                return;
            }

            var text = (literal as StringLiteral)?.Value;
            _diagnostics.Report(
                Codes.ValueTypeMismatch,
                literal.Span,
                $"{type.Name} is a number; a number is written without quotes.",
                fix: Fix(literal, literal.Span, text is not null && NumberText.IsMatch(text) && units.Count == 0 ? text : null));
            return;
        }

        if (type.XmlBase == "xs:integer" && !number.IsInteger)
        {
            _diagnostics.Report(
                Codes.ValueTypeMismatch,
                number.Span,
                $"{type.Name} is a whole number; IDS rejects {number.Text}.",
                hint: "Write the number without a decimal point.");
        }

        if (units.Count == 0)
        {
            if (number.Unit is not null)
            {
                _diagnostics.Report(
                    Codes.UnknownUnit,
                    number.UnitSpan,
                    $"{type.Name} has no unit.",
                    hint: type.DataType is { IsNumeric: true, Unit: null }
                        ? "Remove the unit, or name the measure: datatype IfcLengthMeasure."
                        : "Remove the unit.");
            }

            return;
        }

        if (number.Unit is null)
        {
            _diagnostics.Report(
                Codes.UnitRequired,
                number.Span.AtEnd,
                $"{type.Name} is a measure: write the unit of the number. IDS stores it in {units[0]}.",
                hint: $"Write, for example, {number.Text} {units[0]}.",
                expected: units);
        }
        else if (!units.Contains(number.Unit))
        {
            var nearest = units.FirstOrDefault(u => string.Equals(u, number.Unit, StringComparison.OrdinalIgnoreCase));
            _diagnostics.Report(
                Codes.UnknownUnit,
                number.UnitSpan,
                $"'{number.Unit}' is not a unit of {type.Name}.",
                hint: nearest is null ? null : $"Did you mean {nearest}?",
                expected: units,
                fix: Fix(number, number.UnitSpan, nearest));
        }
    }

    /// <summary>Every value of a constraint, the values of a named list included.</summary>
    private IEnumerable<Literal> Literals(Constraint constraint)
    {
        if (constraint.Equal is not null)
        {
            yield return constraint.Equal;
        }

        foreach (var literal in constraint.Terms.SelectMany(TermLiterals))
        {
            yield return literal;
        }
    }

    private IEnumerable<Literal> TermLiterals(Term term)
    {
        switch (term)
        {
            case InTerm { Values: not null } inline:
                foreach (var value in inline.Values)
                {
                    yield return value;
                }

                break;

            case InTerm { ListName: not null } named when _lists.TryGetValue(named.ListName, out var list):
                foreach (var value in list.Values)
                {
                    // A problem in a named list is reported where the list is used: there its type is known.
                    yield return Relocate(value, named.ListNameSpan);
                }

                break;

            case BoundTerm bound:
                yield return bound.Value;
                break;

            case BetweenTerm between:
                yield return between.Min;
                yield return between.Max;
                break;
        }
    }

    private Literal Relocate(Literal literal, SourceSpan span)
    {
        var copy = Copy(literal, span);
        _fromLists.Add(copy);
        return copy;
    }

    private static Literal Copy(Literal literal, SourceSpan span) => literal switch
    {
        StringLiteral text => new StringLiteral(text.Value) { Span = span },
        BooleanLiteral boolean => new BooleanLiteral(boolean.Value) { Span = span },
        NumberLiteral number => new NumberLiteral(number.Text, number.Unit) { Span = span, UnitSpan = span },
        _ => literal,
    };

    private TextEdit? Fix(Literal literal, SourceSpan span, string? text) =>
        text is null || _fromLists.Contains(literal) ? null : new TextEdit(span, text);
}
