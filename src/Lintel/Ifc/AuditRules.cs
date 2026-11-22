using System;
using System.Collections.Generic;
using System.Linq;
using IdsLib.IfcSchema;
using Lintel.Model;
using Lintel.Syntax;

namespace Lintel.Ifc;

/// <summary>
/// The rules of the official IDS audit that depend on the IFC schema, over the rules of a Lintel file.
/// The audit works with sets of classes: every facet stands for the classes that could meet it, and a rule is
/// invalid when no class can meet all its required facets at once. A facet that is optional or prohibited
/// narrows nothing. The checker asks here, so that it reports an error exactly where the audit does.
/// </summary>
internal static class AuditRules
{
    /// <summary>The candidates that a name position matches, the way the audit matches them.</summary>
    public static List<string> Match(Matcher matcher, IEnumerable<string> candidates)
    {
        if (matcher.Kind == MatcherKind.Pattern)
        {
            var patterns = matcher.Patterns
                .Select(p => XsdPattern.ToAuditRegex(p.Value))
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();
            return candidates.Where(c => patterns.Any(p => p.IsMatch(c))).ToList();
        }

        var names = new HashSet<string>(matcher.Names.Select(n => n.Text), StringComparer.Ordinal);
        return candidates.Where(names.Contains).ToList();
    }

    /// <summary>
    /// The classes of one schema that a class list names once it is written to IDS, in upper case.
    /// A class stands for itself and its subtypes; with <c>only</c>, and for a pattern, the names are taken as they are.
    /// </summary>
    public static HashSet<string> Classes(EntitySelector entity, IReadOnlyList<IfcSchema> schemas, IfcSchema schema)
    {
        if (entity.Classes.Kind == MatcherKind.Pattern)
        {
            return new HashSet<string>(Match(entity.Classes, schema.AuditClassNames), StringComparer.Ordinal);
        }

        return new HashSet<string>(
            Resolver.ExpandClasses(entity.Classes, schemas).Where(name => schema.Info[name] is not null),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The predefined types that every one of the classes has in the schema. When they include USERDEFINED,
    /// IDS takes any text as a predefined type; otherwise only these values.
    /// </summary>
    public static HashSet<string> PredefinedTypes(IEnumerable<string> classes, IfcSchema schema, out string? without)
    {
        HashSet<string>? common = null;
        without = null;
        foreach (var name in classes)
        {
            var info = schema.Info[name];
            var values = info?.PredefinedTypeValues?.ToList() ?? new List<string>();
            if (values.Count == 0)
            {
                without ??= info?.Name ?? name;
            }

            if (common is null)
            {
                common = new HashSet<string>(values, StringComparer.Ordinal);
            }
            else
            {
                common.IntersectWith(values);
            }
        }

        return common ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>The attribute names of the schema that the name of an attribute facet matches.</summary>
    public static List<string> AttributeNames(AttributeFacet facet, IfcSchema schema) =>
        Match(facet.Name, facet.Value is null ? schema.AuditAttributeNames : schema.Info.GetAttributeNames());

    /// <summary>The XML base types of the attributes of these names anywhere in the schema.</summary>
    public static List<string> AttributeBases(IEnumerable<string> attributeNames, IfcSchema schema) =>
        schema.Info.GetAttributesXsdTypes(attributeNames).ToList();

    /// <summary>The standard property sets of the schema that the set name of a property facet matches.</summary>
    public static List<string> StandardSets(PropertyFacet facet, IfcSchema schema) =>
        Match(facet.PropertySet, schema.PropertySetNames);

    /// <summary>The properties of the given standard sets that the property name of a facet matches.</summary>
    public static List<string> StandardProperties(PropertyFacet facet, IReadOnlyList<string> sets, IfcSchema schema)
    {
        var candidates = sets
            .SelectMany(set => schema.FindPropertySet(set)?.PropertyNames ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal);
        return Match(facet.Name, candidates);
    }

    /// <summary>The data types, in upper case, that the standard gives the properties of these names in these sets.</summary>
    public static HashSet<string> Measures(IReadOnlyList<string> sets, IReadOnlyList<string> properties, IfcSchema schema)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in StandardDefinitions(sets, properties, schema))
        {
            if (property.HasDataTypes(out var types))
            {
                result.UnionWith(types.Select(t => t.ToUpperInvariant()));
            }
        }

        return result;
    }

    /// <summary>
    /// The values the standard allows when every one of the properties is an enumeration; <c>null</c> when one of
    /// them takes any value.
    /// </summary>
    public static HashSet<string>? AllowedValues(IReadOnlyList<string> sets, IReadOnlyList<string> properties, IfcSchema schema)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in StandardDefinitions(sets, properties, schema))
        {
            if (property is not EnumerationPropertyType enumeration)
            {
                return null;
            }

            result.UnionWith(enumeration.EnumerationValues);
        }

        return result;
    }

    private static IEnumerable<IPropertyTypeInfo> StandardDefinitions(IReadOnlyList<string> sets, IReadOnlyList<string> properties, IfcSchema schema) =>
        schema.Info.PropertySets
            .Where(set => sets.Contains(set.Name))
            .SelectMany(set => set.Properties.Where(p => properties.Contains(p.Name)));

    /// <summary>The relationship of a <c>part of</c> facet in the schema: the class of the whole and the class of the part.</summary>
    public static PartOfRelationInformation? Relation(PartOfFacet facet, IfcSchema schema)
    {
        if (facet.Relation is null)
        {
            return null;
        }

        var upper = facet.Relation.ToUpperInvariant();
        return schema.Info.AllPartOfRelations.FirstOrDefault(r => r.RelationIfcName == upper);
    }

    /// <summary>The classes of the schema that can have instances and are the named class or a subtype of it, in upper case.</summary>
    public static HashSet<string> ConcreteClasses(string topClass, IfcSchema schema) =>
        new(
            (schema.Info[topClass]?.MatchingConcreteClasses ?? Enumerable.Empty<ClassInfo>()).Select(c => c.Name.ToUpperInvariant()),
            StringComparer.Ordinal);

    /// <summary>
    /// The classes of one schema that could meet a facet, in upper case; <c>null</c> when the facet narrows nothing.
    /// </summary>
    public static HashSet<string>? Filter(Facet facet, bool required, IReadOnlyList<IfcSchema> schemas, IfcSchema schema)
    {
        switch (facet)
        {
            case EntityFacet entity:
                return Classes(entity.Entity, schemas, schema);

            case AttributeFacet attribute when required:
                var names = AttributeNames(attribute, schema);
                return names.Count == 0
                    ? null
                    : new HashSet<string>(names.SelectMany(n => schema.Info.GetAttributeClasses(n)).Select(c => c.ToUpperInvariant()), StringComparer.Ordinal);

            case PropertyFacet property when required:
                var sets = StandardSets(property, schema);
                return sets.Count == 0
                    ? null
                    : new HashSet<string>(sets.SelectMany(schema.AuditClassesOf), StringComparer.Ordinal);

            case ClassificationFacet when required:
                return new HashSet<string>(schema.Info.GetRelAsssignClassificationClasses().Select(c => c.ToUpperInvariant()), StringComparer.Ordinal);

            case MaterialFacet when required:
                return new HashSet<string>(schema.Info.GetRelAsssignClasses().Select(c => c.ToUpperInvariant()), StringComparer.Ordinal);

            case PartOfFacet partOf when required:
                var relation = Relation(partOf, schema);
                return relation is null ? null : ConcreteClasses(relation.PartIfcType, schema);

            default:
                return null;
        }
    }
}
