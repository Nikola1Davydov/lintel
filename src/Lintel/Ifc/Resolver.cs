using System;
using System.Collections.Generic;
using System.Linq;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Ifc;

/// <summary>How the data type of a property facet was found.</summary>
public enum DataTypeOrigin
{
    /// <summary>No data type: the facet has none and none can be found.</summary>
    None,

    /// <summary><c>datatype</c> is written.</summary>
    Written,

    /// <summary>The property belongs to a standard property set, which fixes its type.</summary>
    Standard,

    /// <summary>From the kind of the value: text, whole number, number, true or false.</summary>
    Value,
}

/// <summary>
/// What a rule means once the IFC schema is known: which classes a name selects and which data type a property has.
/// The checker, the IDS output and the IDS import all ask here, so they cannot disagree.
/// </summary>
public static class Resolver
{
    /// <summary>The schemas of the IFC versions of a rule. Names that are not versions are left out.</summary>
    public static IReadOnlyList<IfcSchema> SchemasOf(Rule rule, RuleFile file) =>
        SchemasOf(rule.Ifc ?? file.Header.Ifc ?? new List<string>());

    public static IReadOnlyList<IfcSchema> SchemasOf(IEnumerable<string> versions) =>
        versions.Select(IfcSchema.Get).Where(s => s is not null).Select(s => s!).ToList();

    /// <summary>
    /// The classes a class list selects, in the upper-case spelling and the order in which they are written to IDS:
    /// each named class first, then its subtypes by name. With <c>only</c> the names are taken as written.
    /// For several IFC versions the subtypes of all of them are joined.
    /// </summary>
    public static IReadOnlyList<string> ExpandClasses(Matcher classes, IReadOnlyList<IfcSchema> schemas)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in classes.Names)
        {
            var expansion = classes.Only ? new[] { name.Text.ToUpperInvariant() } : ExpandClass(name.Text, schemas);
            foreach (var upper in expansion)
            {
                if (seen.Add(upper))
                {
                    result.Add(upper);
                }
            }
        }

        return result;
    }

    /// <summary>One class and its subtypes in the schemas, upper case; empty when no schema knows the class.</summary>
    public static IReadOnlyList<string> ExpandClass(string name, IReadOnlyList<IfcSchema> schemas)
    {
        var known = false;
        var self = false;
        var others = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var schema in schemas)
        {
            var found = schema.FindClass(name);
            if (found is null)
            {
                continue;
            }

            known = true;
            foreach (var concrete in found.ConcreteClasses)
            {
                if (concrete == found.Name)
                {
                    self = true;
                }
                else
                {
                    others.Add(concrete.ToUpperInvariant());
                }
            }
        }

        if (!known)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        if (self)
        {
            result.Add(name.ToUpperInvariant());
        }

        result.AddRange(others);
        return result;
    }

    /// <summary>
    /// The inverse of <see cref="ExpandClasses"/>: class names that select exactly the given upper-case classes in the
    /// given order, or <c>null</c> when there are none. The importer uses it to write <c>IfcWall</c> for a list
    /// that is IfcWall with all its subtypes, and <c>only ...</c> for any other list.
    /// </summary>
    public static IReadOnlyList<string>? FoldClasses(IReadOnlyList<string> upperNames, IReadOnlyList<IfcSchema> schemas)
    {
        if (upperNames.Count == 0 || schemas.Count == 0)
        {
            return null;
        }

        var wanted = new HashSet<string>(upperNames, StringComparer.Ordinal);
        if (wanted.Count != upperNames.Count)
        {
            return null;
        }

        // Every class whose subtypes all lie in the wanted set.
        var candidates = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var name in schemas.SelectMany(s => s.ClassNames).Distinct(StringComparer.Ordinal))
        {
            var expansion = ExpandClass(name, schemas);
            if (expansion.Count > 0 && expansion.All(wanted.Contains))
            {
                candidates[name] = expansion;
            }
        }

        var names = new List<string>();
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var upper in upperNames)
        {
            if (covered.Contains(upper))
            {
                continue;
            }

            // The widest class that covers this one; a class that is itself in the list before an abstract parent.
            var best = candidates
                .Where(c => c.Value.Contains(upper))
                .OrderByDescending(c => c.Value.Count)
                .ThenByDescending(c => c.Value[0] == upper)
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => c.Key)
                .FirstOrDefault();
            if (best is null)
            {
                return null;
            }

            names.Add(best);
            covered.UnionWith(candidates[best]);
        }

        // The names must give back the list exactly, order included; otherwise the list is kept as written.
        var check = new Matcher(names.Count > 1 ? MatcherKind.AnyOf : MatcherKind.Exact, default);
        check.Names.AddRange(names.Select(n => new NameItem(n, false, default)));
        return ExpandClasses(check, schemas).SequenceEqual(upperNames, StringComparer.Ordinal) ? names : null;
    }

    /// <summary>
    /// The data type of a property facet (SPEC.md, section 5.7): the written one, else the type of the standard
    /// property, else the kind of the value. <c>null</c> when there is none or it is not known.
    /// </summary>
    public static IfcDataType? DataTypeOf(
        PropertyFacet facet,
        IReadOnlyList<IfcSchema> schemas,
        IReadOnlyDictionary<string, ListDefinition> lists,
        out DataTypeOrigin origin)
    {
        if (facet.DataType is not null)
        {
            origin = DataTypeOrigin.Written;
            return IfcDataTypes.Find(facet.DataType);
        }

        var standard = StandardDataType(facet, schemas);
        if (standard is not null)
        {
            origin = DataTypeOrigin.Standard;
            return standard;
        }

        var inferred = facet.Value is null ? null : DataTypeOfValue(facet.Value, lists);
        origin = inferred is null ? DataTypeOrigin.None : DataTypeOrigin.Value;
        return inferred;
    }

    /// <summary>The data type that the standard gives the property, when every schema that knows the set agrees on one.</summary>
    public static IfcDataType? StandardDataType(PropertyFacet facet, IReadOnlyList<IfcSchema> schemas)
    {
        if (!IsSingleName(facet.PropertySet) || !IsSingleName(facet.Name))
        {
            return null;
        }

        string? found = null;
        foreach (var schema in schemas)
        {
            var property = schema.FindPropertySet(facet.PropertySet.Names[0].Text)?.FindProperty(facet.Name.Names[0].Text);
            if (property is null)
            {
                continue;
            }

            if (property.DataTypes.Count != 1 || (found is not null && found != property.DataTypes[0]))
            {
                return null;
            }

            found = property.DataTypes[0];
        }

        return found is null ? null : IfcDataTypes.Find(found);
    }

    /// <summary>
    /// The data type that the kind of a value calls for, as the IDS guide for implementers recommends:
    /// text is IfcLabel, a whole number IfcInteger, another number IfcReal, true and false IfcBoolean.
    /// A number with a unit is a measure, and which measure cannot be guessed.
    /// </summary>
    public static IfcDataType? DataTypeOfValue(Constraint constraint, IReadOnlyDictionary<string, ListDefinition> lists)
    {
        Literal? sample = constraint.Equal;
        if (sample is null)
        {
            foreach (var term in constraint.Terms)
            {
                switch (term)
                {
                    case MatchesTerm:
                    case LengthTerm:
                        return IfcDataTypes.Find("IfcLabel");
                    case InTerm { Values: { Count: > 0 } values }:
                        sample = values[0];
                        break;
                    case InTerm { ListName: not null } named when lists.TryGetValue(named.ListName, out var list) && list.Values.Count > 0:
                        sample = list.Values[0];
                        break;
                    case BoundTerm bound:
                        sample = bound.Value;
                        break;
                    case BetweenTerm between:
                        sample = between.Min;
                        break;
                }

                if (sample is not null)
                {
                    break;
                }
            }
        }

        return sample switch
        {
            StringLiteral => IfcDataTypes.Find("IfcLabel"),
            BooleanLiteral => IfcDataTypes.Find("IfcBoolean"),
            NumberLiteral { Unit: null } number => IfcDataTypes.Find(number.IsInteger ? "IfcInteger" : "IfcReal"),
            _ => null,
        };
    }

    public static bool IsSingleName(Matcher matcher) => matcher.Kind == MatcherKind.Exact && matcher.Names.Count == 1;

    /// <summary>
    /// The classes that a class list selects in each schema, as the schema spells them.
    /// <c>null</c> when that is not known: no class list, a pattern, or a name no schema knows.
    /// </summary>
    public static Dictionary<IfcSchema, HashSet<string>>? Selection(EntitySelector? entity, IReadOnlyList<IfcSchema> schemas)
    {
        if (entity is null)
        {
            return null;
        }

        var classes = entity.Classes;
        if (classes.Kind == MatcherKind.Pattern)
        {
            // IDS matches the pattern against the class name in upper case.
            var patterns = classes.Patterns.Select(p => XsdPattern.ToRegex(p.Value)).ToList();
            if (patterns.Count == 0 || patterns.Any(p => p is null))
            {
                return null;
            }

            return schemas.ToDictionary(
                schema => schema,
                schema => new HashSet<string>(
                    schema.ClassNames.Where(name => patterns.Any(p => p!.IsMatch(name.ToUpperInvariant()))),
                    StringComparer.Ordinal));
        }

        if (classes.Names.Count == 0)
        {
            return null;
        }

        if (classes.Names.Any(n => schemas.All(s => s.FindClass(n.Text) is null)))
        {
            return null;
        }

        var selection = new Dictionary<IfcSchema, HashSet<string>>();
        foreach (var schema in schemas)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in classes.Names)
            {
                var found = schema.FindClass(name.Text);
                if (found is null)
                {
                    continue;
                }

                if (classes.Only)
                {
                    selected.Add(found.Name);
                }
                else
                {
                    selected.UnionWith(found.ConcreteClasses);
                }
            }

            selection[schema] = selected;
        }

        return selection;
    }

    /// <summary>
    /// The attributes with one of the given names on the selected classes (on every class when the selection is not known).
    /// </summary>
    public static List<IfcAttribute> AttributesOf(
        Matcher name,
        Dictionary<IfcSchema, HashSet<string>>? selection,
        IReadOnlyList<IfcSchema> schemas)
    {
        var found = new List<IfcAttribute>();
        var patterns = name.Kind == MatcherKind.Pattern
            ? name.Patterns.Select(p => XsdPattern.ToRegex(p.Value)).Where(p => p is not null).Select(p => p!).ToList()
            : null;

        foreach (var schema in schemas)
        {
            var owners = selection is null ? schema.ClassNames : (IEnumerable<string>)selection[schema];
            foreach (var owner in owners)
            {
                var attributes = schema.FindClass(owner)?.Attributes;
                if (attributes is null)
                {
                    continue;
                }

                if (patterns is not null)
                {
                    found.AddRange(attributes.Values.Where(a => patterns.Any(p => p.IsMatch(a.Name))));
                    continue;
                }

                foreach (var item in name.Names)
                {
                    if (attributes.TryGetValue(item.Text, out var attribute))
                    {
                        found.Add(attribute);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// What the values of an attribute facet are compared as; <c>null</c> when that is not known.
    /// IDS fixes the XML type when every attribute of this name, on whatever class, has the same one: then a
    /// value of another kind is an error. Otherwise the type of the selected classes is taken when they agree.
    /// The data type, which gives the unit, comes from the selected classes.
    /// </summary>
    public static ValueTyping? AttributeType(
        Matcher name,
        Dictionary<IfcSchema, HashSet<string>>? selection,
        IReadOnlyList<IfcSchema> schemas)
    {
        var comparable = AttributesOf(name, selection, schemas).Where(a => !a.IsCollection && a.XmlBase.Length > 0).ToList();
        var bases = comparable.Select(a => a.XmlBase).Distinct(StringComparer.Ordinal).ToList();
        var types = comparable.Select(a => a.IfcType).Distinct(StringComparer.Ordinal).ToList();
        var dataType = types.Count == 1 ? IfcDataTypes.Find(types[0]) : null;

        // What the official audit expects, schema by schema.
        var expected = schemas
            .Select(schema => AuditRules.AttributeBases(AuditRules.Match(name, schema.Info.GetAttributeNames()), schema))
            .ToList();
        if (expected.Count > 0 && expected.All(e => e.Count == 1) && expected.All(e => e[0] == expected[0][0]))
        {
            var fixedBase = expected[0][0];
            return new ValueTyping(fixedBase, dataType?.XmlBase == fixedBase ? dataType : null, true);
        }

        if (bases.Count == 1 && expected.All(e => e.Contains(bases[0])))
        {
            return new ValueTyping(bases[0], dataType, false);
        }

        return null;
    }

    /// <summary>
    /// The predefined types that the standard lists for a selected class in any of the schemas. Such a value is
    /// written as a bare name; any other text is a type of the user and is written in quotes.
    /// </summary>
    public static HashSet<string> StandardPredefinedTypes(EntitySelector entity, IReadOnlyList<IfcSchema> schemas)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var schema in schemas)
        {
            foreach (var name in AuditRules.Classes(entity, schemas, schema))
            {
                result.UnionWith(schema.AuditClass(name)?.PredefinedTypeValues ?? Enumerable.Empty<string>());
            }
        }

        return result;
    }
}

/// <summary>What the values of a facet are compared as.</summary>
public sealed class ValueTyping
{
    public ValueTyping(string xmlBase, IfcDataType? type, bool isChecked)
    {
        XmlBase = xmlBase;
        Type = type;
        Checked = isChecked;
    }

    /// <summary>The XML base type: <c>xs:string</c>, <c>xs:double</c>, ...</summary>
    public string XmlBase { get; }

    /// <summary>The IFC data type when it is a single one; it gives the unit of a measure.</summary>
    public IfcDataType? Type { get; }

    /// <summary>True when IDS fixes the base type, so that a value of another kind makes the file invalid.</summary>
    public bool Checked { get; }
}
