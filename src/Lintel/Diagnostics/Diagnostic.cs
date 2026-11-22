using System.Collections.Generic;
using Lintel.Syntax;

namespace Lintel.Diagnostics;

public enum Severity
{
    /// <summary>The file cannot be compiled: the official IDS audit would reject the result, or a rule of the language is broken.</summary>
    Error,

    /// <summary>The file compiles to valid IDS, but the author probably meant something else or used a non-canonical spelling.</summary>
    Warning,
}

/// <summary>A replacement of a range of the source text.</summary>
public sealed class TextEdit
{
    public TextEdit(SourceSpan span, string text)
    {
        Span = span;
        Text = text;
    }

    public SourceSpan Span { get; }

    public string Text { get; }
}

/// <summary>The stable identity of a kind of problem: the code never changes its meaning.</summary>
public sealed class DiagnosticDescriptor
{
    public DiagnosticDescriptor(string code, string name, Severity severity)
    {
        Code = code;
        Name = name;
        Severity = severity;
    }

    public string Code { get; }

    public string Name { get; }

    public Severity Severity { get; }
}

/// <summary>One problem, as data: what, where, what to do, and the edit that repairs it when there is a safe one.</summary>
public sealed class Diagnostic
{
    public Diagnostic(DiagnosticDescriptor descriptor, SourceSpan span, string message)
    {
        Descriptor = descriptor;
        Span = span;
        Message = message;
    }

    public DiagnosticDescriptor Descriptor { get; }

    public Severity Severity => Descriptor.Severity;

    public string Code => Descriptor.Code;

    public string Name => Descriptor.Name;

    public string Message { get; }

    public SourceSpan Span { get; }

    /// <summary>The identifier or the name of the rule the problem is in.</summary>
    public string? Rule { get; set; }

    public string? Hint { get; set; }

    /// <summary>The values allowed at this place.</summary>
    public IReadOnlyList<string>? Expected { get; set; }

    /// <summary>Edits that repair the problem without changing what the author meant.</summary>
    public IReadOnlyList<TextEdit>? Fix { get; set; }

    public override string ToString() => $"{Code} {Span.Line}:{Span.Column} {Message}";
}

/// <summary>The problems found so far, in the order they were found.</summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = new();

    public IReadOnlyList<Diagnostic> Items => _items;

    /// <summary>The rule being read; it is attached to every problem reported while it is set.</summary>
    public string? CurrentRule { get; set; }

    public Diagnostic Report(
        DiagnosticDescriptor descriptor,
        SourceSpan span,
        string message,
        string? hint = null,
        IReadOnlyList<string>? expected = null,
        TextEdit? fix = null)
    {
        var diagnostic = new Diagnostic(descriptor, span, message)
        {
            Rule = CurrentRule,
            Hint = hint,
            Expected = expected,
            Fix = fix is null ? null : new[] { fix },
        };
        _items.Add(diagnostic);
        return diagnostic;
    }

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _items.AddRange(diagnostics);
}
