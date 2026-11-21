using System.Collections.Generic;
using Lintel.Syntax;

namespace Lintel.Model;

/// <summary>One piece of information about an element: an IDS facet.</summary>
public abstract class Facet
{
    public SourceSpan Span { get; set; }

    /// <summary>The keyword of the facet: entity, attribute, property, classification, material, part of.</summary>
    public abstract string Keyword { get; }
}

public sealed class EntityFacet : Facet
{
    public EntityFacet(EntitySelector entity) => Entity = entity;

    public EntitySelector Entity { get; }

    public override string Keyword => "entity";
}

public sealed class AttributeFacet : Facet
{
    public AttributeFacet(Matcher name) => Name = name;

    public Matcher Name { get; }

    public Constraint? Value { get; set; }

    public override string Keyword => "attribute";
}

public sealed class PropertyFacet : Facet
{
    public PropertyFacet(Matcher propertySet, Matcher name)
    {
        PropertySet = propertySet;
        Name = name;
    }

    public Matcher PropertySet { get; }

    public Matcher Name { get; }

    /// <summary>The IFC data type as written, such as <c>IfcLabel</c>; <c>null</c> when it is to be inferred.</summary>
    public string? DataType { get; set; }

    public SourceSpan DataTypeSpan { get; set; }

    public Constraint? Value { get; set; }

    public override string Keyword => "property";
}

public sealed class ClassificationFacet : Facet
{
    public ClassificationFacet(Matcher system) => System = system;

    public Matcher System { get; }

    public Constraint? Value { get; set; }

    public override string Keyword => "classification";
}

public sealed class MaterialFacet : Facet
{
    public Constraint? Value { get; set; }

    public override string Keyword => "material";
}

public sealed class PartOfFacet : Facet
{
    public PartOfFacet(EntitySelector entity) => Entity = entity;

    public EntitySelector Entity { get; }

    /// <summary>
    /// The relationship after <c>via</c>, such as <c>IfcRelAggregates</c> or
    /// <c>IfcRelVoidsElement IfcRelFillsElement</c>; <c>null</c> for any relationship.
    /// </summary>
    public string? Relation { get; set; }

    public override string Keyword => "part of";
}

/// <summary>Classes and an optional predefined type: the content of an IDS entity facet.</summary>
public sealed class EntitySelector
{
    public EntitySelector(Matcher classes) => Classes = classes;

    public Matcher Classes { get; }

    /// <summary>The predefined types after <c>type</c>.</summary>
    public Matcher? Types { get; set; }
}

public enum MatcherKind
{
    /// <summary>One name.</summary>
    Exact,

    /// <summary>One of several names.</summary>
    AnyOf,

    /// <summary>A name that matches a pattern.</summary>
    Pattern,
}

/// <summary>How a name is given: a class, a predefined type, an attribute, a property set, a property or a classification system.</summary>
public sealed class Matcher
{
    public Matcher(MatcherKind kind, SourceSpan span)
    {
        Kind = kind;
        Span = span;
    }

    public MatcherKind Kind { get; }

    public SourceSpan Span { get; set; }

    /// <summary>The names, for <see cref="MatcherKind.Exact"/> and <see cref="MatcherKind.AnyOf"/>.</summary>
    public List<NameItem> Names { get; } = new();

    /// <summary>The patterns, for <see cref="MatcherKind.Pattern"/>. Several patterns are alternatives.</summary>
    public List<StringLiteral> Patterns { get; } = new();

    /// <summary>For classes: <c>only</c> was written, so subtypes are not included.</summary>
    public bool Only { get; set; }

    /// <summary>A sentence that explains the list or the pattern to a reader.</summary>
    public string? Note { get; set; }

    public static Matcher Single(string name, bool quoted, SourceSpan span)
    {
        var matcher = new Matcher(MatcherKind.Exact, span);
        matcher.Names.Add(new NameItem(name, quoted, span));
        return matcher;
    }
}

/// <summary>A name as written: bare (<c>FireRating</c>) or quoted (<c>"Fire Rating"</c>).</summary>
public sealed class NameItem
{
    public NameItem(string text, bool quoted, SourceSpan span)
    {
        Text = text;
        Quoted = quoted;
        Span = span;
    }

    public string Text { get; }

    public bool Quoted { get; }

    public SourceSpan Span { get; }
}
