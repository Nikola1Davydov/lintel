using System;
using System.Collections.Generic;

namespace Lintel.Syntax;

/// <summary>The reserved words of the language (SPEC.md, section 10).</summary>
public static class Keywords
{
    /// <summary>Keywords of v0.1.</summary>
    public static readonly IReadOnlyCollection<string> Current = new HashSet<string>(StringComparer.Ordinal)
    {
        "and", "any", "attribute", "author", "between", "classification", "copyright", "count", "datatype", "date",
        "define", "description", "entity", "false", "for", "ifc", "import", "in", "instructions", "length", "matches",
        "material", "milestone", "note", "of", "only", "optional", "part", "prohibit", "property", "purpose",
        "require", "rule", "source", "title", "true", "type", "uri", "version", "via", "where",
    };

    /// <summary>Words kept for later levels of the language; they cannot be used as names.</summary>
    public static readonly IReadOnlyCollection<string> Future = new HashSet<string>(StringComparer.Ordinal)
    {
        "or", "not", "sum", "unique", "each", "exists", "if", "then", "else", "fix", "set", "warn",
    };

    /// <summary>Statements of the file header. Each may appear once, before the first rule.</summary>
    public static readonly IReadOnlyList<string> Header = new[]
    {
        "ifc", "title", "description", "version", "author", "date", "purpose", "milestone", "copyright", "import",
    };

    /// <summary>Words that start a facet.</summary>
    public static readonly IReadOnlyList<string> Facets = new[]
    {
        "entity", "attribute", "property", "classification", "material", "part",
    };

    /// <summary>Words that start a statement. The parser resumes at one of them after an error.</summary>
    public static readonly IReadOnlyCollection<string> Statements = new HashSet<string>(StringComparer.Ordinal)
    {
        "rule", "define", "for", "where", "require", "optional", "prohibit", "source", "description",
        "instructions", "ifc", "title", "version", "author", "date", "purpose", "milestone", "copyright", "import",
    };

    public static bool IsKeyword(string word) => ((HashSet<string>)Current).Contains(word) || IsFuture(word);

    public static bool IsFuture(string word) => ((HashSet<string>)Future).Contains(word);
}
