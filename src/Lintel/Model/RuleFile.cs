using System.Collections.Generic;
using Lintel.Syntax;

namespace Lintel.Model;

/// <summary>One source file: a header, then named lists and rules in the order they were written.</summary>
public sealed class RuleFile
{
    public Header Header { get; } = new();

    /// <summary><see cref="ListDefinition"/> and <see cref="Rule"/> items in source order.</summary>
    public List<object> Items { get; } = new();

    public IEnumerable<ListDefinition> Definitions
    {
        get
        {
            foreach (var item in Items)
            {
                if (item is ListDefinition definition)
                {
                    yield return definition;
                }
            }
        }
    }

    public IEnumerable<Rule> Rules
    {
        get
        {
            foreach (var item in Items)
            {
                if (item is Rule rule)
                {
                    yield return rule;
                }
            }
        }
    }
}

/// <summary>The statements in front of the first rule. They become the <c>info</c> element of IDS.</summary>
public sealed class Header
{
    /// <summary>The IFC versions of every rule that does not name its own.</summary>
    public List<string>? Ifc { get; set; }

    public SourceSpan IfcSpan { get; set; }

    /// <summary>The text statements by keyword: title, description, version, author, date, purpose, milestone, copyright.</summary>
    public Dictionary<string, HeaderText> Texts { get; } = new();

    public List<HeaderText> Imports { get; } = new();

    public string? Get(string keyword) => Texts.TryGetValue(keyword, out var text) ? text.Value : null;
}

public sealed class HeaderText
{
    public HeaderText(string value, SourceSpan span)
    {
        Value = value;
        Span = span;
    }

    public string Value { get; }

    public SourceSpan Span { get; }
}

/// <summary><c>define Name = [...]</c>: a list of values with a name.</summary>
public sealed class ListDefinition
{
    public ListDefinition(string name, SourceSpan nameSpan, List<Literal> values)
    {
        Name = name;
        NameSpan = nameSpan;
        Values = values;
    }

    public string Name { get; }

    public SourceSpan NameSpan { get; }

    public List<Literal> Values { get; }
}

/// <summary>What <c>count</c> says about the number of selected elements.</summary>
public enum RuleCount
{
    /// <summary>No statement: the rule applies when elements are selected.</summary>
    Any,

    /// <summary><c>require count &gt;= 1</c></summary>
    AtLeastOne,

    /// <summary><c>require count = 0</c></summary>
    None,
}

/// <summary>One rule: an IDS specification.</summary>
public sealed class Rule
{
    public Rule(string name, SourceSpan span)
    {
        Name = name;
        Span = span;
    }

    public string Name { get; }

    /// <summary>The range of the rule's header: the keyword, the identifier and the name.</summary>
    public SourceSpan Span { get; }

    public string? Id { get; set; }

    /// <summary>The original wording of the requirement.</summary>
    public string? Source { get; set; }

    public string? Description { get; set; }

    public string? Instructions { get; set; }

    /// <summary>The IFC versions of this rule, when they differ from the header.</summary>
    public List<string>? Ifc { get; set; }

    /// <summary>The classes after <c>for</c>; <c>null</c> for <c>for any</c>.</summary>
    public EntitySelector? Entity { get; set; }

    /// <summary>False when the rule has no <c>for</c> at all, which is an error.</summary>
    public bool HasFor { get; set; }

    public SourceSpan ForSpan { get; set; }

    /// <summary>The facets after <c>where</c> and <c>and</c>.</summary>
    public List<Facet> Where { get; } = new();

    /// <summary>For each facet of <see cref="Where"/>, the range of the <c>where</c> or <c>and</c> in front of it.</summary>
    public List<SourceSpan> WhereKeywordSpans { get; } = new();

    public List<Requirement> Requirements { get; } = new();

    public RuleCount Count { get; set; } = RuleCount.Any;

    public SourceSpan CountSpan { get; set; }

    /// <summary>The identifier, or the name when there is none: how diagnostics refer to the rule.</summary>
    public string Label => Id ?? Name;
}

public enum Cardinality
{
    Required,
    Optional,
    Prohibited,
}

/// <summary><c>require</c>, <c>optional</c> or <c>prohibit</c> with one facet.</summary>
public sealed class Requirement
{
    public Requirement(Cardinality cardinality, Facet facet, SourceSpan keywordSpan)
    {
        Cardinality = cardinality;
        Facet = facet;
        KeywordSpan = keywordSpan;
    }

    public Cardinality Cardinality { get; }

    public Facet Facet { get; }

    public SourceSpan KeywordSpan { get; }

    /// <summary>A link to the definition of the property, class or material in a dictionary such as bSDD.</summary>
    public string? Uri { get; set; }

    public SourceSpan UriSpan { get; set; }

    /// <summary>Instructions for the author of the model.</summary>
    public string? Instructions { get; set; }
}
