namespace Bimlint.Diagnostics;

/// <summary>
/// Every problem the compiler reports (SPEC.md, section 9). The first digit is the stage that finds it:
/// 1 characters, 2 grammar, 3 structure without the IFC schema, 4 IFC schema, 5 warnings, 7 import of IDS.
/// An error is something the official IDS audit rejects, or a rule of the language itself; what is valid IDS
/// but probably not what the author means is a warning.
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
    // E301 was anchored-pattern. Anchors are valid IDS; they are reported as W509.
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
    public static readonly DiagnosticDescriptor InvalidLength = Error("E314", "invalid-length");
    public static readonly DiagnosticDescriptor UriNotAllowed = Error("E315", "uri-not-allowed");
    public static readonly DiagnosticDescriptor InvalidRelation = Error("E316", "invalid-relation");
    public static readonly DiagnosticDescriptor EmptyMatch = Error("E317", "empty-match");

    // IFC schema
    public static readonly DiagnosticDescriptor UnknownClass = Error("E401", "unknown-class");
    public static readonly DiagnosticDescriptor UnknownAttribute = Error("E402", "unknown-attribute");
    public static readonly DiagnosticDescriptor UnknownPropertySet = Error("E403", "unknown-property-set");
    public static readonly DiagnosticDescriptor UnknownProperty = Error("E404", "unknown-property");
    public static readonly DiagnosticDescriptor UnknownPredefinedType = Error("E405", "unknown-predefined-type");
    public static readonly DiagnosticDescriptor UnknownDataType = Error("E406", "unknown-datatype");
    public static readonly DiagnosticDescriptor DataTypeMismatch = Error("E407", "datatype-mismatch");
    public static readonly DiagnosticDescriptor ValueTypeMismatch = Error("E408", "value-type-mismatch");
    public static readonly DiagnosticDescriptor UnitRequired = Error("E409", "unit-required");
    public static readonly DiagnosticDescriptor UnknownUnit = Error("E410", "unknown-unit");
    public static readonly DiagnosticDescriptor PropertySetNotApplicable = Error("E411", "property-set-not-applicable");
    public static readonly DiagnosticDescriptor ClassNotInVersion = Error("E412", "class-not-in-version");
    public static readonly DiagnosticDescriptor DataTypeRequired = Error("E413", "datatype-required");
    public static readonly DiagnosticDescriptor IncompatibleEntity = Error("E414", "incompatible-entity");
    public static readonly DiagnosticDescriptor InvalidRelationTarget = Error("E415", "invalid-relation-target");
    public static readonly DiagnosticDescriptor NoCommonClass = Error("E416", "no-common-class");

    /// <summary>The official IDS audit rejects the IDS that the file compiles to, for a reason no other code covers.</summary>
    public static readonly DiagnosticDescriptor RejectedByAudit = Error("E499", "rejected-by-ids-audit");

    // Warnings
    public static readonly DiagnosticDescriptor PredefinedTypeAsAttribute = Warning("W501", "predefined-type-as-attribute");
    public static readonly DiagnosticDescriptor QuotedName = Warning("W502", "quoted-name");
    public static readonly DiagnosticDescriptor DoubleBackslash = Warning("W503", "double-backslash");
    public static readonly DiagnosticDescriptor RangeAsTwoBounds = Warning("W504", "range-as-two-bounds");
    public static readonly DiagnosticDescriptor DuplicateIdentifier = Warning("W505", "duplicate-identifier");
    public static readonly DiagnosticDescriptor NonPortableEscape = Warning("W506", "non-portable-escape");
    public static readonly DiagnosticDescriptor InferredNumberType = Warning("W507", "inferred-number-type");
    public static readonly DiagnosticDescriptor AbstractClass = Warning("W508", "abstract-class");
    public static readonly DiagnosticDescriptor AnchoredPattern = Warning("W509", "anchored-pattern");
    public static readonly DiagnosticDescriptor NonStandardPattern = Warning("W510", "non-standard-pattern");
    public static readonly DiagnosticDescriptor NotInSchema = Warning("W511", "not-in-schema");
    public static readonly DiagnosticDescriptor PatternOnNumber = Warning("W512", "pattern-on-number");
    public static readonly DiagnosticDescriptor OddRange = Warning("W513", "odd-range");

    // Import of IDS
    public static readonly DiagnosticDescriptor IdsNotRepresentable = Warning("W701", "ids-not-representable");

    private static DiagnosticDescriptor Error(string code, string name) => new(code, name, Severity.Error);

    private static DiagnosticDescriptor Warning(string code, string name) => new(code, name, Severity.Warning);
}
