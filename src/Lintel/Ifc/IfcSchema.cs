using System;
using System.Collections.Generic;
using System.Linq;
using IdsLib.IfcSchema;

namespace Lintel.Ifc;

/// <summary>
/// One version of the IFC schema as the compiler needs it: classes with their subtypes, predefined types and
/// attributes, the standard property sets, and the data types. The data comes from ids-lib, the library of the
/// official IDS audit, so that the compiler and the audit agree on what a name means.
/// </summary>
public sealed class IfcSchema
{
    private static readonly Dictionary<string, IfcSchema> Loaded = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    private readonly SchemaInfo _info;
    private readonly Dictionary<string, IfcClass> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IfcPropertySet> _propertySets = new(StringComparer.Ordinal);
    private readonly Lazy<IReadOnlyList<string>> _attributeNames;
    private readonly Lazy<IReadOnlyList<string>> _auditClassNames;
    private readonly Lazy<IReadOnlyList<string>> _auditAttributeNames;
    private readonly Dictionary<string, HashSet<string>> _applicable = new(StringComparer.Ordinal);

    private IfcSchema(string version, SchemaInfo info, IfcSchemaVersions flag)
    {
        Version = version;
        Flag = flag;
        _info = info;

        foreach (var classInfo in info.Where(c => c.Type != ClassType.Enumeration))
        {
            _classes[classInfo.Name] = new IfcClass(classInfo);
        }

        foreach (var set in info.PropertySets)
        {
            _propertySets[set.Name] = new IfcPropertySet(set);
        }

        ClassNames = _classes.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        PropertySetNames = _propertySets.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        _attributeNames = new Lazy<IReadOnlyList<string>>(() => info.GetAttributeNames().OrderBy(n => n, StringComparer.Ordinal).ToList());
        _auditClassNames = new Lazy<IReadOnlyList<string>>(() => info.Select(c => c.Name.ToUpperInvariant()).ToList());
        _auditAttributeNames = new Lazy<IReadOnlyList<string>>(() => SchemaInfo.AllAttributes
            .Where(a => (a.ValidSchemaVersions & flag) == flag)
            .Select(a => a.IfcAttributeName)
            .Distinct(StringComparer.Ordinal)
            .ToList());
    }

    /// <summary>The version as IDS names it: IFC2X3, IFC4 or IFC4X3_ADD2.</summary>
    public string Version { get; }

    internal IfcSchemaVersions Flag { get; }

    /// <summary>The schema as the official IDS audit knows it. The checks ask it wherever they must agree with the audit.</summary>
    internal SchemaInfo Info => _info;

    /// <summary>
    /// Every name the audit takes for a class, in upper case. Besides the entity classes these are the
    /// enumerations: the audit lets an IDS file name one, although no object of a model has such a class.
    /// </summary>
    internal IReadOnlyList<string> AuditClassNames => _auditClassNames.Value;

    /// <summary>The attribute names of this version as the audit lists them for a facet without a value.</summary>
    internal IReadOnlyList<string> AuditAttributeNames => _auditAttributeNames.Value;

    /// <summary>Every entity class, as the IFC documentation spells it.</summary>
    public IReadOnlyList<string> ClassNames { get; }

    public IReadOnlyList<string> PropertySetNames { get; }

    /// <summary>The name of every attribute of any class.</summary>
    public IReadOnlyList<string> AttributeNames => _attributeNames.Value;

    /// <summary>The schema of a version, or <c>null</c> for a name that is not a version of IDS 1.0.</summary>
    public static IfcSchema? Get(string version)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(version, out var schema))
            {
                return schema;
            }

            schema = version switch
            {
                "IFC2X3" => new IfcSchema(version, SchemaInfo.SchemaIfc2x3, IfcSchemaVersions.Ifc2x3),
                "IFC4" => new IfcSchema(version, SchemaInfo.SchemaIfc4, IfcSchemaVersions.Ifc4),
                "IFC4X3_ADD2" => new IfcSchema(version, SchemaInfo.SchemaIfc4x3, IfcSchemaVersions.Ifc4x3),
                _ => null,
            };

            if (schema is not null)
            {
                Loaded[version] = schema;
            }

            return schema;
        }
    }

    /// <summary>The class with exactly this name.</summary>
    public IfcClass? FindClass(string name) => _classes.TryGetValue(name, out var found) ? found : null;

    /// <summary>The class with this name in any case; IDS writes class names in upper case.</summary>
    public IfcClass? FindClassIgnoreCase(string name)
    {
        if (_classes.TryGetValue(name, out var found))
        {
            return found;
        }

        return _classes.Values.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public IfcPropertySet? FindPropertySet(string name) => _propertySets.TryGetValue(name, out var found) ? found : null;

    /// <summary>
    /// The classes a standard property set may be attached to, in upper case: the classes it is defined for with
    /// their subtypes, and the type objects of those classes. This is the rule of the official audit, step by step.
    /// </summary>
    internal IReadOnlyCollection<string> AuditClassesOf(string setName)
    {
        lock (_applicable)
        {
            if (_applicable.TryGetValue(setName, out var known))
            {
                return known;
            }

            var result = new HashSet<string>(StringComparer.Ordinal);
            var set = _info.PropertySets.FirstOrDefault(p => p.Name == setName);
            if (set is not null)
            {
                var concrete = set.ApplicableClasses
                    .Select(name => _info[name])
                    .Where(c => c is not null)
                    .SelectMany(c => c!.MatchingConcreteClasses)
                    .Select(c => c.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                foreach (var name in concrete)
                {
                    result.Add(name.ToUpperInvariant());
                    foreach (var typeName in _info[name]?.RelationTypeClasses ?? Array.Empty<string>())
                    {
                        result.Add(typeName.ToUpperInvariant());

                        // The audit adds the direct subtypes of the type object, such as IfcDoorStyle for IfcTypeProduct.
                        foreach (var sub in _info[typeName]?.SubClasses ?? new List<ClassInfo>())
                        {
                            result.Add(sub.Name.ToUpperInvariant());
                        }
                    }
                }
            }

            _applicable[setName] = result;
            return result;
        }
    }

    /// <summary>The classes a standard property set may be attached to, as the IFC documentation spells them.</summary>
    public IReadOnlyCollection<string> ClassesOf(IfcPropertySet set) =>
        AuditClassesOf(set.Name).Select(upper => _info[upper]?.Name ?? upper).ToList();

    /// <summary>
    /// The class that the whole of a partOf relation must be: IfcGroup for IfcRelAssignsToGroup, and so on.
    /// <paramref name="relation"/> is written as in Lintel; <c>null</c> for an unknown relation.
    /// </summary>
    public string? OwnerClassOf(string relation)
    {
        var upper = relation.ToUpperInvariant();
        var owner = _info.AllPartOfRelations.FirstOrDefault(r => r.RelationIfcName == upper)?.OwnerIfcType;
        return owner is null ? null : FindClassIgnoreCase(owner)?.Name;
    }
}

/// <summary>An entity class of one IFC version.</summary>
public sealed class IfcClass
{
    private readonly ClassInfo _info;
    private readonly Lazy<IReadOnlyList<string>> _concrete;
    private readonly Lazy<IReadOnlyDictionary<string, IfcAttribute>> _attributes;

    internal IfcClass(ClassInfo info)
    {
        _info = info;
        _concrete = new Lazy<IReadOnlyList<string>>(() =>
        {
            // The class itself first, then its subtypes by name: the order in which the compiler writes them to IDS.
            var names = info.MatchingConcreteClasses.Select(c => c.Name).Distinct(StringComparer.Ordinal).ToList();
            var ordered = names.Where(n => n != info.Name).OrderBy(n => n.ToUpperInvariant(), StringComparer.Ordinal).ToList();
            if (names.Contains(info.Name))
            {
                ordered.Insert(0, info.Name);
            }

            return ordered;
        });
        _attributes = new Lazy<IReadOnlyDictionary<string, IfcAttribute>>(() =>
        {
            var attributes = new Dictionary<string, IfcAttribute>(StringComparer.Ordinal);
            foreach (var attribute in info.DirectAttributesInfo ?? Enumerable.Empty<AttributeInfo>())
            {
                attributes[attribute.Name] = new IfcAttribute(attribute);
            }

            return attributes;
        });
        PredefinedTypes = (info.PredefinedTypeValues ?? Enumerable.Empty<string>()).ToList();
        TypeClasses = info.RelationTypeClasses ?? Array.Empty<string>();
    }

    public string Name => _info.Name;

    public bool IsAbstract => _info.Type == ClassType.Abstract;

    /// <summary>The class itself when it can have instances, and every subtype that can.</summary>
    public IReadOnlyList<string> ConcreteClasses => _concrete.Value;

    /// <summary>The values of the predefined type of the class; empty when it has none.</summary>
    public IReadOnlyList<string> PredefinedTypes { get; }

    /// <summary>The classes of the type objects of this class, such as IFCWALLTYPE for IfcWall, as ids-lib spells them.</summary>
    internal IReadOnlyList<string> TypeClasses { get; }

    /// <summary>The attributes of the class, its own and the inherited ones, by name.</summary>
    public IReadOnlyDictionary<string, IfcAttribute> Attributes => _attributes.Value;

    /// <summary>True when the class is the named class or one of its subtypes.</summary>
    public bool IsA(string className) => _info.Is(className);
}

/// <summary>An attribute of an entity class.</summary>
public sealed class IfcAttribute
{
    internal IfcAttribute(AttributeInfo info)
    {
        Name = info.Name;
        IfcType = info.BaseType;
        // ids-lib leaves this empty for a type name that is not in upper case; ask the table of data types.
        XmlBase = IfcDataTypes.Find(info.BaseType)?.XmlBase ?? string.Empty;
        IsCollection = info.IsCollection;
    }

    public string Name { get; }

    /// <summary>The IFC type of the value, such as <c>IfcLabel</c>; for a reference, the class it points to.</summary>
    public string IfcType { get; }

    /// <summary>The XML base type of the value (<c>xs:string</c>, <c>xs:double</c>, ...); empty for a reference to an object.</summary>
    public string XmlBase { get; }

    public bool IsCollection { get; }
}

/// <summary>A property set or quantity set that the IFC standard defines.</summary>
public sealed class IfcPropertySet
{
    private readonly Dictionary<string, IfcProperty> _properties = new(StringComparer.Ordinal);

    internal IfcPropertySet(PropertySetInfo info)
    {
        Name = info.Name;
        ApplicableClasses = info.ApplicableClasses.ToList();
        foreach (var property in info.Properties)
        {
            _properties[property.Name] = new IfcProperty(property);
        }

        PropertyNames = _properties.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    public string Name { get; }

    /// <summary>The classes the set is defined for; their subtypes are included.</summary>
    public IReadOnlyList<string> ApplicableClasses { get; }

    public IReadOnlyList<string> PropertyNames { get; }

    public IfcProperty? FindProperty(string name) => _properties.TryGetValue(name, out var found) ? found : null;
}

/// <summary>A property of a standard property set.</summary>
public sealed class IfcProperty
{
    internal IfcProperty(IPropertyTypeInfo info)
    {
        Name = info.Name;
        DataTypes = info.HasDataTypes(out var types)
            ? types.Select(t => IfcDataTypes.Find(t)?.Name ?? t).ToList()
            : new List<string>();
        AllowedValues = info is EnumerationPropertyType enumeration ? enumeration.EnumerationValues.ToList() : null;
    }

    public string Name { get; }

    /// <summary>The data type of the value. A table property has two; then the author has to name one.</summary>
    public IReadOnlyList<string> DataTypes { get; }

    /// <summary>The values the standard allows for an enumerated property; <c>null</c> for other properties.</summary>
    public IReadOnlyList<string>? AllowedValues { get; }
}
