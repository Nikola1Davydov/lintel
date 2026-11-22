using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lintel.Diagnostics;
using Lintel.Ifc;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Analysis;

/// <summary>
/// The checks against the IFC schema (SPEC.md, section 8): every class, predefined type, attribute, standard
/// property set, property, data type and unit exists in the IFC versions of its rule, and values fit their types.
/// The rules follow the official IDS audit, so that what passes here passes there.
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

            var selection = rule.Entity is null ? null : CheckEntity(rule.Entity, schemas);
            foreach (var facet in rule.Where)
            {
                CheckFacet(facet, schemas, selection, Cardinality.Required);
            }

            foreach (var requirement in rule.Requirements)
            {
                CheckFacet(requirement.Facet, schemas, selection, requirement.Cardinality);
            }
        }

        _diagnostics.CurrentRule = null;
    }

    // ---------------------------------------------------------------- classes

    /// <summary>
    /// Checks a class list and its predefined types. Returns the classes it selects in each schema,
    /// or <c>null</c> when that is not known (a pattern, or an unknown class).
    /// </summary>
    private Dictionary<IfcSchema, HashSet<string>>? CheckEntity(EntitySelector entity, IReadOnlyList<IfcSchema> schemas)
    {
        var classes = entity.Classes;
        if (classes.Kind == MatcherKind.Pattern)
        {
            var matched = Resolver.Selection(entity, schemas);
            var empty = matched?.FirstOrDefault(m => m.Value.Count == 0).Key;
            if (empty is not null)
            {
                _diagnostics.Report(
                    Codes.UnknownClass,
                    classes.Span,
                    $"No class of {empty.Version} matches the pattern.",
                    hint: "IDS matches the pattern against the class name in upper case, such as IFCWALL.");
            }

            return matched;
        }

        if (classes.Names.Count == 0)
        {
            return null;
        }

        var allKnown = true;
        foreach (var name in classes.Names)
        {
            var known = schemas.Select(s => s.FindClass(name.Text)).Where(c => c is not null).Select(c => c!).ToList();
            if (known.Count == 0)
            {
                allKnown = false;
                ReportUnknownClass(name, schemas);
                continue;
            }

            if (classes.Only && known.All(c => c.IsAbstract))
            {
                _diagnostics.Report(
                    Codes.AbstractClass,
                    name.Span,
                    $"{name.Text} is abstract: no element has exactly this class.",
                    hint: $"Remove 'only' to select every subtype of {name.Text}.");
            }
        }

        if (allKnown)
        {
            foreach (var schema in schemas)
            {
                if (classes.Names.All(n => schema.FindClass(n.Text) is null))
                {
                    _diagnostics.Report(
                        Codes.ClassNotInVersion,
                        classes.Span,
                        $"{schema.Version} has none of these classes: {string.Join(", ", classes.Names.Select(n => n.Text))}.",
                        hint: "Add the class of this version to the list, or give this version a rule of its own.");
                }
            }
        }

        if (entity.Types is { Kind: not MatcherKind.Pattern } types)
        {
            CheckPredefinedTypes(types, classes, schemas);
        }

        return Resolver.Selection(entity, schemas);
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

    private void CheckPredefinedTypes(Matcher types, Matcher classes, IReadOnlyList<IfcSchema> schemas)
    {
        var owners = classes.Names
            .SelectMany(n => schemas.Select(s => s.FindClass(n.Text)))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
        if (owners.Count == 0)
        {
            return;
        }

        var without = owners.FirstOrDefault(c => c.PredefinedTypes.Count == 0);
        if (without is not null)
        {
            _diagnostics.Report(
                Codes.UnknownPredefinedType,
                types.Span,
                $"{without.Name} has no predefined type.",
                hint: "Remove 'type', or select by another facet.");
            return;
        }

        // A value must be valid for every named class.
        var common = owners
            .Select(c => (IEnumerable<string>)c.PredefinedTypes)
            .Aggregate((a, b) => a.Intersect(b, StringComparer.Ordinal))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        foreach (var type in types.Names)
        {
            if (type.Quoted)
            {
                if (common.Contains(type.Text))
                {
                    _diagnostics.Report(
                        Codes.QuotedName,
                        type.Span,
                        $"{type.Text} is a predefined type of the standard and needs no quotes.",
                        fix: new TextEdit(type.Span, type.Text));
                }
                else if (!common.Contains("USERDEFINED"))
                {
                    _diagnostics.Report(
                        Codes.UnknownPredefinedType,
                        type.Span,
                        $"{owners[0].Name} has no user-defined types.",
                        expected: common);
                }

                continue;
            }

            if (common.Contains(type.Text))
            {
                continue;
            }

            var nearest = Suggestions.Nearest(type.Text, common);
            _diagnostics.Report(
                Codes.UnknownPredefinedType,
                type.Span,
                $"{type.Text} is not a predefined type of {string.Join(", ", classes.Names.Select(n => n.Text))}.",
                hint: nearest is not null
                    ? $"Did you mean {nearest}?"
                    : $"A type of your own is written in quotes: type \"{type.Text}\".",
                expected: common,
                fix: nearest is null ? null : new TextEdit(type.Span, nearest));
        }
    }

    // ---------------------------------------------------------------- facets

    private void CheckFacet(Facet facet, IReadOnlyList<IfcSchema> schemas, Dictionary<IfcSchema, HashSet<string>>? selection, Cardinality cardinality)
    {
        switch (facet)
        {
            case EntityFacet entity:
                var required = CheckEntity(entity.Entity, schemas);
                if (required is not null && selection is not null
                    && schemas.Any(s => selection[s].Count > 0 && !selection[s].Overlaps(required[s])))
                {
                    _diagnostics.Report(
                        Codes.IncompatibleEntity,
                        facet.Span,
                        "No selected element can have this class: it is not among the classes after 'for'.",
                        hint: "A requirement on the class makes sense when 'for' selects several classes or none.");
                }

                break;

            case AttributeFacet attribute:
                CheckAttribute(attribute, schemas, selection);
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
                var whole = CheckEntity(partOf.Entity, schemas);
                if (partOf.Relation is not null && whole is not null && cardinality == Cardinality.Required)
                {
                    CheckRelationTarget(partOf, schemas, whole);
                }

                break;
        }
    }

    private void CheckRelationTarget(PartOfFacet partOf, IReadOnlyList<IfcSchema> schemas, Dictionary<IfcSchema, HashSet<string>> whole)
    {
        foreach (var schema in schemas)
        {
            var owner = schema.OwnerClassOf(partOf.Relation!);
            if (owner is null || whole[schema].Count == 0)
            {
                continue;
            }

            if (!whole[schema].Any(name => schema.FindClass(name)?.IsA(owner) == true))
            {
                _diagnostics.Report(
                    Codes.InvalidRelationTarget,
                    partOf.Entity.Classes.Span,
                    $"With {partOf.Relation} the whole is a kind of {owner}; {string.Join(", ", partOf.Entity.Classes.Names.Select(n => n.Text))} is not.",
                    hint: "Name another class, or another relationship after 'via'.");
                return;
            }
        }
    }

    private void CheckAttribute(AttributeFacet facet, IReadOnlyList<IfcSchema> schemas, Dictionary<IfcSchema, HashSet<string>>? selection)
    {
        var found = new List<IfcAttribute>();
        if (facet.Name.Kind == MatcherKind.Pattern)
        {
            found = Resolver.AttributesOf(facet.Name, selection, schemas);
            if (found.Count == 0)
            {
                _diagnostics.Report(
                    Codes.UnknownAttribute,
                    facet.Name.Span,
                    selection is null ? "No attribute matches the pattern." : "No attribute of the selected classes matches the pattern.",
                    expected: Suggestions.Top(string.Empty, AttributeNames(schemas, selection)));
                return;
            }
        }

        foreach (var name in facet.Name.Names)
        {
            var here = Resolver.AttributesOf(Matcher.Single(name.Text, name.Quoted, name.Span), selection, schemas);
            if (here.Count > 0)
            {
                found.AddRange(here);
                continue;
            }

            var candidates = AttributeNames(schemas, selection);
            var nearest = Suggestions.Nearest(name.Text, candidates);
            var subject = selection is null ? "No class" : "None of the selected classes";
            _diagnostics.Report(
                Codes.UnknownAttribute,
                name.Span,
                $"{subject} has an attribute {name.Text}.",
                hint: nearest is not null
                    ? $"Did you mean {nearest}?"
                    : "Attributes are the fixed fields of an IFC class, such as Name or Description. Other data is a property: property Set.Name.",
                expected: Suggestions.Top(name.Text, candidates),
                fix: nearest is null ? null : new TextEdit(name.Span, nearest));
        }

        if (facet.Value is null || found.Count == 0)
        {
            return;
        }

        // An attribute of this name may be a single value on one class and a list or an object on another.
        // It is an error only when no selected class has it as a single value.
        var comparable = found.Where(a => !a.IsCollection && a.XmlBase.Length > 0).ToList();
        if (comparable.Count == 0)
        {
            var sample = found[0];
            _diagnostics.Report(
                Codes.ValueTypeMismatch,
                facet.Value.Span,
                $"The value of {sample.Name} is {(sample.IsCollection ? "a list" : "an object (" + sample.IfcType + ")")} and cannot be compared.",
                hint: "IDS compares single values only. Require the attribute without a constraint.");
            return;
        }

        // The same attribute name has the same type on almost every class; when it does not, the value is not checked.
        var types = comparable.Select(a => a.IfcType).Distinct(StringComparer.Ordinal).ToList();
        var bases = comparable.Select(a => a.XmlBase).Distinct(StringComparer.Ordinal).ToList();
        if (bases.Count != 1)
        {
            return;
        }

        var type = types.Count == 1 ? IfcDataTypes.Find(types[0]) : null;
        CheckValue(facet.Value, type is not null ? ValueType.Of(type) : new ValueType("the attribute " + comparable[0].Name, bases[0], null));
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
        IfcProperty? standard = null;
        var standardKnown = true;

        if (Resolver.IsSingleName(facet.PropertySet))
        {
            var setName = facet.PropertySet.Names[0];
            var reserved = setName.Text.StartsWith("Pset_", StringComparison.Ordinal) || setName.Text.StartsWith("Qto_", StringComparison.Ordinal);
            foreach (var schema in schemas)
            {
                var set = schema.FindPropertySet(setName.Text);
                if (set is null)
                {
                    if (reserved)
                    {
                        standardKnown = false;
                        ReportUnknownPropertySet(setName, schema, selection);
                        break;
                    }

                    continue;
                }

                if (selection is not null && selection[schema].Count > 0 && !selection[schema].Overlaps(schema.ClassesOf(set)))
                {
                    _diagnostics.Report(
                        Codes.PropertySetNotApplicable,
                        setName.Span,
                        $"{set.Name} is defined for {string.Join(", ", set.ApplicableClasses)}, not for the selected classes.",
                        hint: "Use the property set of the selected class.",
                        expected: SetsFor(schema, selection[schema]));
                    standardKnown = false;
                    break;
                }

                if (!Resolver.IsSingleName(facet.Name))
                {
                    continue;
                }

                var propertyName = facet.Name.Names[0];
                var property = set.FindProperty(propertyName.Text);
                if (property is null)
                {
                    standardKnown = false;
                    var nearest = Suggestions.Nearest(propertyName.Text, set.PropertyNames);
                    _diagnostics.Report(
                        Codes.UnknownProperty,
                        propertyName.Span,
                        $"{set.Name} has no property {propertyName.Text} in {schema.Version}.",
                        hint: nearest is not null ? $"Did you mean {nearest}?" : "A property of your own belongs to a property set of your own.",
                        expected: set.PropertyNames,
                        fix: nearest is null ? null : new TextEdit(propertyName.Span, nearest));
                    break;
                }

                standard = property;
            }
        }

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
            else
            {
                var missing = schemas.FirstOrDefault(s => !written.IsValidIn(s));
                if (missing is not null)
                {
                    _diagnostics.Report(
                        Codes.UnknownDataType,
                        facet.DataTypeSpan,
                        $"{written.Name} does not exist in {missing.Version}.");
                }
            }
        }

        if (!standardKnown)
        {
            return;
        }

        var standardType = Resolver.StandardDataType(facet, schemas);
        if (written is not null && standardType is not null && written.Name != standardType.Name)
        {
            _diagnostics.Report(
                Codes.DataTypeMismatch,
                facet.DataTypeSpan,
                $"The standard defines {facet.PropertySet.Names[0].Text}.{facet.Name.Names[0].Text} as {standardType.Name}.",
                hint: "Remove 'datatype': the compiler takes the type of a standard property from the standard.",
                expected: new[] { standardType.Name },
                fix: new TextEdit(facet.DataTypeSpan, standardType.Name));
            return;
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
                standard is { DataTypes.Count: > 1 }
                    ? $"The property has several data types ({string.Join(", ", standard.DataTypes)}); say which one the value has."
                    : "A number with a unit is a measure, and the data type of this property is not known.",
                hint: "Write the data type after the property: datatype IfcLengthMeasure.",
                expected: new[] { "IfcLengthMeasure", "IfcAreaMeasure", "IfcVolumeMeasure", "IfcPlaneAngleMeasure", "IfcMassMeasure", "IfcReal", "IfcInteger" });
            return;
        }

        if (origin == DataTypeOrigin.Value && type.IsNumeric)
        {
            _diagnostics.Report(
                Codes.InferredNumberType,
                facet.Value.Span,
                $"The data type is taken from the number: {type.Name}. A model that stores this property with another type fails the rule.",
                hint: $"Write the type when you know it: datatype {type.Name}, or a measure such as IfcLengthMeasure with a unit.");
        }

        CheckValue(facet.Value, ValueType.Of(type));

        if (standard?.AllowedValues is { } allowed && cardinality == Cardinality.Required)
        {
            CheckAllowedValues(facet.Value, allowed, facet);
        }
    }

    private void ReportUnknownPropertySet(NameItem name, IfcSchema schema, Dictionary<IfcSchema, HashSet<string>>? selection)
    {
        var nearest = Suggestions.Nearest(name.Text, schema.PropertySetNames);
        var expected = selection is not null && selection[schema].Count > 0
            ? SetsFor(schema, selection[schema])
            : Suggestions.Top(name.Text, schema.PropertySetNames);
        _diagnostics.Report(
            Codes.UnknownPropertySet,
            name.Span,
            $"{name.Text} is not a property set of {schema.Version}. Names that start with Pset_ or Qto_ are reserved for the standard.",
            hint: nearest is not null ? $"Did you mean {nearest}?" : "Give a property set of your own another name, such as ACME_Common.",
            expected: expected,
            fix: nearest is null ? null : new TextEdit(name.Span, nearest));
    }

    /// <summary>The standard property sets that may be attached to any of the classes.</summary>
    private static IReadOnlyList<string> SetsFor(IfcSchema schema, HashSet<string> classes) =>
        schema.PropertySetNames
            .Where(n => classes.Overlaps(schema.ClassesOf(schema.FindPropertySet(n)!)))
            .Take(30)
            .ToList();

    /// <summary>An enumerated property of the standard takes one of the values the standard lists.</summary>
    private void CheckAllowedValues(Constraint constraint, IReadOnlyList<string> allowed, PropertyFacet facet)
    {
        foreach (var literal in Literals(constraint).OfType<StringLiteral>())
        {
            if (allowed.Contains(literal.Value))
            {
                continue;
            }

            var nearest = Suggestions.Nearest(literal.Value, allowed);
            _diagnostics.Report(
                Codes.ValueTypeMismatch,
                literal.Span,
                $"\"{literal.Value}\" is not a value of {facet.PropertySet.Names[0].Text}.{facet.Name.Names[0].Text}; the standard lists the values.",
                hint: nearest is not null ? $"Did you mean \"{nearest}\"?" : null,
                expected: allowed,
                fix: Fix(literal, literal.Span, nearest is null ? null : "\"" + nearest + "\""));
        }
    }

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

    private void CheckValue(Constraint? constraint, ValueType type)
    {
        if (constraint is null)
        {
            return;
        }

        switch (type.XmlBase)
        {
            case "xs:double":
            case "xs:integer":
                CheckNumber(constraint, type);
                break;
            case "xs:boolean":
                CheckBoolean(constraint, type);
                break;
            default:
                CheckText(constraint, type);
                break;
        }
    }

    private void CheckText(Constraint constraint, ValueType type)
    {
        foreach (var term in constraint.Terms)
        {
            if (term is BoundTerm or BetweenTerm)
            {
                _diagnostics.Report(
                    Codes.ValueTypeMismatch,
                    term.Span,
                    $"{type.Name} is text and cannot be compared with a bound.",
                    hint: "Use a list ('in [...]'), a pattern ('matches') or 'length'.");
            }
        }

        foreach (var literal in Literals(constraint))
        {
            if (literal is StringLiteral)
            {
                continue;
            }

            var text = literal is NumberLiteral number ? number.Text : ((BooleanLiteral)literal).Value ? "true" : "false";
            _diagnostics.Report(
                Codes.ValueTypeMismatch,
                literal.Span,
                $"{type.Name} is text; a text value is written in quotes.",
                fix: Fix(literal, literal.Span, literal is NumberLiteral { Unit: not null } ? null : "\"" + text + "\""));
        }
    }

    private void CheckBoolean(Constraint constraint, ValueType type)
    {
        if (constraint.Equal is null)
        {
            _diagnostics.Report(
                Codes.ValueTypeMismatch,
                constraint.Span,
                $"{type.Name} is true or false.",
                expected: new[] { "= true", "= false" });
            return;
        }

        if (constraint.Equal is BooleanLiteral)
        {
            return;
        }

        var text = (constraint.Equal as StringLiteral)?.Value.ToLowerInvariant();
        _diagnostics.Report(
            Codes.ValueTypeMismatch,
            constraint.Equal.Span,
            $"{type.Name} is true or false, without quotes.",
            expected: new[] { "true", "false" },
            fix: Fix(constraint.Equal, constraint.Equal.Span, text is "true" or "false" ? text : null));
    }

    private void CheckNumber(Constraint constraint, ValueType type)
    {
        foreach (var term in constraint.Terms)
        {
            if (term is MatchesTerm or LengthTerm)
            {
                _diagnostics.Report(
                    Codes.ValueTypeMismatch,
                    term.Span,
                    $"{type.Name} is a number; patterns and lengths apply to text.",
                    hint: "Use '=', a bound, 'between' or a list of numbers.");
            }
        }

        var units = type.DataType is null ? Array.Empty<string>() : Units.For(type.DataType);
        foreach (var literal in Literals(constraint))
        {
            if (literal is not NumberLiteral number)
            {
                var text = (literal as StringLiteral)?.Value;
                _diagnostics.Report(
                    Codes.ValueTypeMismatch,
                    literal.Span,
                    $"{type.Name} is a number; a number is written without quotes.",
                    fix: Fix(literal, literal.Span, text is not null && NumberText.IsMatch(text) && units.Count == 0 ? text : null));
                continue;
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

                continue;
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
    }

    /// <summary>Every value of a constraint, the values of a named list included.</summary>
    private IEnumerable<Literal> Literals(Constraint constraint)
    {
        if (constraint.Equal is not null)
        {
            yield return constraint.Equal;
        }

        foreach (var term in constraint.Terms)
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
