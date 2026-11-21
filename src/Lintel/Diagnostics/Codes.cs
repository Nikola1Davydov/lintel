namespace Lintel.Diagnostics;

/// <summary>
/// Every problem the compiler reports (SPEC.md, section 9). The first digit is the stage that finds it:
/// 1 characters, 2 grammar, 3 structure without the IFC schema, 4 IFC schema, 5 warnings, 6 information.
/// </summary>
public static class Codes
{
    // Characters
    public static readonly DiagnosticDescriptor UnterminatedString = Error("E101", "unterminated-string");
    public static readonly DiagnosticDescriptor UnexpectedCharacter = Error("E102", "unexpected-character");
    public static readonly DiagnosticDescriptor MalformedNumber = Error("E103", "malformed-number");
    public static readonly DiagnosticDescriptor UnterminatedTextBlock = Error("E104", "unterminated-text-block");

    // Grammar
    public static readonly DiagnosticDescriptor UnexpectedToken = Error("E201", "unexpected-token");
    public static readonly DiagnosticDescriptor UnknownKeyword = Error("E202", "unknown-keyword");
    public static readonly DiagnosticDescriptor MisplacedStatement = Error("E203", "misplaced-statement");
    public static readonly DiagnosticDescriptor DuplicateStatement = Error("E204", "duplicate-statement");
    public static readonly DiagnosticDescriptor MissingFor = Error("E205", "missing-for");
    public static readonly DiagnosticDescriptor UnsupportedOperator = Error("E206", "unsupported-operator");
    public static readonly DiagnosticDescriptor OneFacetPerRequirement = Error("E207", "one-facet-per-requirement");
    public static readonly DiagnosticDescriptor UnsupportedPhrase = Error("E208", "unsupported-phrase");
    public static readonly DiagnosticDescriptor MissingFacetKeyword = Error("E209", "missing-facet-keyword");
    public static readonly DiagnosticDescriptor PatternInSlashes = Error("E210", "pattern-in-slashes");
    public static readonly DiagnosticDescriptor ReservedWord = Error("E211", "reserved-word");

    // Structure, no IFC schema needed
    public static readonly DiagnosticDescriptor AnchoredPattern = Error("E301", "anchored-pattern");
    public static readonly DiagnosticDescriptor InvalidPattern = Error("E302", "invalid-pattern");
    public static readonly DiagnosticDescriptor InvalidCardinality = Error("E303", "invalid-cardinality");
    public static readonly DiagnosticDescriptor UnsupportedCount = Error("E304", "unsupported-count");
    public static readonly DiagnosticDescriptor RequirementNotAllowed = Error("E305", "requirement-not-allowed");
    public static readonly DiagnosticDescriptor MissingRequirement = Error("E306", "missing-requirement");
    public static readonly DiagnosticDescriptor MissingWhere = Error("E307", "missing-where");
    public static readonly DiagnosticDescriptor UnknownList = Error("E308", "unknown-list");
    public static readonly DiagnosticDescriptor DuplicateDefinition = Error("E309", "duplicate-definition");
    public static readonly DiagnosticDescriptor InvalidIfcVersion = Error("E310", "invalid-ifc-version");
    public static readonly DiagnosticDescriptor MissingIfcVersion = Error("E311", "missing-ifc-version");
    public static readonly DiagnosticDescriptor InvalidList = Error("E312", "invalid-list");
    public static readonly DiagnosticDescriptor InvalidMetadata = Error("E313", "invalid-metadata");
    public static readonly DiagnosticDescriptor InvalidRange = Error("E314", "invalid-range");
    public static readonly DiagnosticDescriptor UriNotAllowed = Error("E315", "uri-not-allowed");
    public static readonly DiagnosticDescriptor InvalidRelation = Error("E316", "invalid-relation");

    // Warnings
    public static readonly DiagnosticDescriptor PredefinedTypeAsAttribute = Warning("W501", "predefined-type-as-attribute");
    public static readonly DiagnosticDescriptor QuotedName = Warning("W502", "quoted-name");
    public static readonly DiagnosticDescriptor DoubleBackslash = Warning("W503", "double-backslash");
    public static readonly DiagnosticDescriptor RangeAsTwoBounds = Warning("W504", "range-as-two-bounds");
    public static readonly DiagnosticDescriptor DuplicateIdentifier = Warning("W505", "duplicate-identifier");

    private static DiagnosticDescriptor Error(string code, string name) => new(code, name, Severity.Error);

    private static DiagnosticDescriptor Warning(string code, string name) => new(code, name, Severity.Warning);
}
