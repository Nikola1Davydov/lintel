using System.Collections.Generic;
using Lintel.Syntax;

namespace Lintel.Model;

/// <summary>
/// What a value must be: either exactly one value (an IDS simpleValue),
/// or one or more terms that must all hold (one xs:restriction).
/// </summary>
public sealed class Constraint
{
    public SourceSpan Span { get; set; }

    /// <summary>The value after <c>=</c>; <c>null</c> when the constraint is made of terms.</summary>
    public Literal? Equal { get; set; }

    public List<Term> Terms { get; } = new();

    /// <summary>A sentence that explains the terms to a reader.</summary>
    public string? Note { get; set; }
}

public abstract class Term
{
    public SourceSpan Span { get; set; }
}

/// <summary><c>in [...]</c> or <c>in Name</c>.</summary>
public sealed class InTerm : Term
{
    /// <summary>The values written in brackets; <c>null</c> when a named list is used.</summary>
    public List<Literal>? Values { get; set; }

    public string? ListName { get; set; }

    public SourceSpan ListNameSpan { get; set; }
}

/// <summary><c>matches "..."</c> or <c>matches ["...", "..."]</c>.</summary>
public sealed class MatchesTerm : Term
{
    public List<StringLiteral> Patterns { get; } = new();
}

public enum BoundKind
{
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}

/// <summary>
/// <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c> or <c>&lt;=</c> with a number; with a text for a date, a time or a duration.
/// </summary>
public sealed class BoundTerm : Term
{
    public BoundTerm(BoundKind kind, Literal value)
    {
        Kind = kind;
        Value = value;
    }

    public BoundKind Kind { get; }

    public Literal Value { get; }
}

/// <summary><c>between a and b</c>, both ends included.</summary>
public sealed class BetweenTerm : Term
{
    public BetweenTerm(Literal min, Literal max)
    {
        Min = min;
        Max = max;
    }

    public Literal Min { get; }

    public Literal Max { get; }
}

public enum LengthKind
{
    Exactly,
    AtLeast,
    AtMost,
}

/// <summary><c>length = n</c>, <c>length &gt;= n</c> or <c>length &lt;= n</c>.</summary>
public sealed class LengthTerm : Term
{
    public LengthTerm(LengthKind kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    public LengthKind Kind { get; }

    /// <summary>The number of characters, as written.</summary>
    public string Value { get; }
}

public abstract class Literal
{
    public SourceSpan Span { get; set; }
}

public sealed class StringLiteral : Literal
{
    public StringLiteral(string value) => Value = value;

    public string Value { get; }
}

public sealed class BooleanLiteral : Literal
{
    public BooleanLiteral(bool value) => Value = value;

    public bool Value { get; }
}

/// <summary>A number as written, with its unit when it has one.</summary>
public sealed class NumberLiteral : Literal
{
    public NumberLiteral(string text, string? unit)
    {
        Text = text;
        Unit = unit;
    }

    /// <summary>The digits as written. They are kept so that an imported IDS value is written back unchanged.</summary>
    public string Text { get; }

    public string? Unit { get; }

    public SourceSpan UnitSpan { get; set; }

    public bool IsInteger => Text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
}
