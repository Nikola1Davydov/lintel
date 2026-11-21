using System;
using System.Collections.Generic;
using System.Linq;
using Lintel.Diagnostics;
using Lintel.Model;

namespace Lintel.Syntax;

/// <summary>
/// Reads tokens into a <see cref="RuleFile"/> (SPEC.md, section 4). Hand-written recursive descent.
/// It never stops at the first problem: it reports, resumes at the next statement, and where an author
/// wrote a known other spelling it reports that spelling with a fix and reads on as if the right one was there.
/// </summary>
public sealed partial class Parser
{
    private readonly string _source;
    private readonly IReadOnlyList<Token> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _pos;

    private Parser(string source, IReadOnlyList<Token> tokens, DiagnosticBag diagnostics)
    {
        _source = source;
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    public static RuleFile Parse(string source, DiagnosticBag diagnostics)
    {
        var tokens = Lexer.Tokenize(source, diagnostics);
        return new Parser(source, tokens, diagnostics).ParseFile();
    }

    // ---------------------------------------------------------------- file

    private RuleFile ParseFile()
    {
        var file = new RuleFile();
        var seenRule = false;

        while (!At(TokenKind.EndOfFile))
        {
            if (!TryPeekStatement(out var statement))
            {
                ReportUnexpected("a statement", TopLevelStatements);
                SkipToStatement();
                continue;
            }

            var keyword = TakeStatement(statement);
            switch (statement.Word)
            {
                case "rule":
                    ParseRule(file, keyword);
                    seenRule = true;
                    break;
                case "define":
                    ParseDefine(file);
                    break;
                case "for":
                case "where":
                case "require":
                case "optional":
                case "prohibit":
                case "source":
                case "instructions":
                    _diagnostics.Report(
                        Codes.MisplacedStatement,
                        keyword.Span,
                        $"'{statement.Word}' belongs to a rule, and no rule is open here.",
                        hint: "Start a rule first: rule \"name\".");
                    SkipToStatement();
                    break;
                default:
                    if (seenRule)
                    {
                        _diagnostics.Report(
                            Codes.MisplacedStatement,
                            keyword.Span,
                            $"'{statement.Word}' belongs to the file header and must come before the first rule.");
                    }

                    ParseHeaderItem(file.Header, statement.Word, keyword);
                    break;
            }
        }

        return file;
    }

    private void ParseHeaderItem(Header header, string word, Token keyword)
    {
        if (word == "ifc")
        {
            var versions = ParseVersions();
            if (header.Ifc is not null)
            {
                ReportDuplicate(keyword, "ifc");
            }
            else
            {
                header.Ifc = versions;
                header.IfcSpan = keyword.Span;
            }

            return;
        }

        var text = ParseText(word == "import" ? "a file path in quotes" : "text in quotes");
        if (text is null)
        {
            SkipToStatement();
            return;
        }

        var item = new HeaderText(text.Value, SourceSpan.Join(keyword.Span, text.Span));
        if (word == "import")
        {
            header.Imports.Add(item);
        }
        else if (header.Texts.ContainsKey(word))
        {
            ReportDuplicate(keyword, word);
        }
        else
        {
            header.Texts[word] = item;
        }
    }

    private List<string> ParseVersions()
    {
        var versions = new List<string>();
        while (true)
        {
            if (!At(TokenKind.Name))
            {
                ReportUnexpected("an IFC version", IfcVersions.All);
                SkipToStatement();
                return versions;
            }

            var token = Take();
            var written = token.Text;
            var span = token.Span;

            // "IFC4.3" is read as the name IFC4 and the number .3
            if (At(TokenKind.Number) && Current.Span.Start == span.End && Current.Text.StartsWith(".", StringComparison.Ordinal))
            {
                var fraction = Take();
                written += fraction.Text;
                span = SourceSpan.Join(span, fraction.Span);
            }

            var version = IfcVersions.Resolve(written);
            if (version is null)
            {
                _diagnostics.Report(
                    Codes.InvalidIfcVersion,
                    span,
                    $"'{written}' is not an IFC version of IDS 1.0.",
                    expected: IfcVersions.All);
            }
            else
            {
                if (version != written)
                {
                    _diagnostics.Report(
                        Codes.InvalidIfcVersion,
                        span,
                        $"'{written}' is not an IFC version of IDS 1.0.",
                        hint: $"Write {version}.",
                        expected: IfcVersions.All,
                        fix: new TextEdit(span, version));
                }

                if (!versions.Contains(version))
                {
                    versions.Add(version);
                }
            }

            if (At(TokenKind.Comma))
            {
                Take();
                continue;
            }

            if (At(TokenKind.Name) && Current.Text.StartsWith("IFC", StringComparison.OrdinalIgnoreCase))
            {
                _diagnostics.Report(
                    Codes.UnexpectedToken,
                    Current.Span.AtStart,
                    "IFC versions are separated by commas.",
                    fix: new TextEdit(new SourceSpan(span.End, Current.Span.Start, span.EndLine, span.EndColumn, Current.Span.Line, Current.Span.Column), ", "));
                continue;
            }

            return versions;
        }
    }

    private void ParseDefine(RuleFile file)
    {
        if (!At(TokenKind.Name))
        {
            ReportUnexpected("the name of the list");
            SkipToStatement();
            return;
        }

        var name = Take();
        if (At(TokenKind.Equal))
        {
            Take();
        }
        else
        {
            ReportUnexpected("'='");
            SkipToStatement();
            return;
        }

        var values = ParseList("define " + name.Text + " = [\"A\", \"B\"]");
        if (values is not null)
        {
            file.Items.Add(new ListDefinition(name.Text, name.Span, values));
        }
    }

    // ---------------------------------------------------------------- rule

    private void ParseRule(RuleFile file, Token keyword)
    {
        string? id = null;
        string? name = null;
        var end = keyword.Span;

        if (At(TokenKind.Name))
        {
            var token = Take();
            id = token.Text;
            end = token.Span;
        }

        if (At(TokenKind.Unknown) && Current.Text == ":")
        {
            var colon = Take();
            _diagnostics.Report(
                Codes.UnexpectedCharacter,
                colon.Span,
                "A rule is written: rule ID \"name\", without a colon.",
                fix: new TextEdit(colon.Span, string.Empty));
        }

        if (At(TokenKind.String))
        {
            var first = Take();
            end = first.Span;
            if (id is null && At(TokenKind.String))
            {
                id = first.Value;
                var second = Take();
                name = second.Value;
                end = second.Span;
            }
            else
            {
                name = first.Value;
            }
        }

        var span = SourceSpan.Join(keyword.Span, end);
        if (name is null)
        {
            _diagnostics.Report(
                Codes.UnexpectedToken,
                span.AtEnd,
                "A rule needs a name in quotes.",
                hint: id is null ? "Write: rule \"name\" or rule ID \"name\"." : $"Write: rule {id} \"name\".");
            name = id ?? string.Empty;
        }

        var rule = new Rule(name, span) { Id = id };
        _diagnostics.CurrentRule = rule.Label;
        var missingForReported = false;
        var whereSeen = false;

        while (!At(TokenKind.EndOfFile))
        {
            if (AtKeyword("count"))
            {
                _diagnostics.Report(
                    Codes.UnknownKeyword,
                    Current.Span.AtStart,
                    "'count' is written with 'require'.",
                    expected: new[] { "require count >= 1", "require count = 0" },
                    fix: new TextEdit(Current.Span.AtStart, "require "));
                ParseCount(rule, Cardinality.Required, Current);
                continue;
            }

            if (!TryPeekStatement(out var statement))
            {
                ReportUnexpected("a statement", RuleStatements);
                SkipToStatement();
                continue;
            }

            if (statement.Word == "rule" || statement.Word == "define")
            {
                break;
            }

            var token = TakeStatement(statement);
            switch (statement.Word)
            {
                case "source":
                case "description":
                case "ifc":
                    ParseRuleMetadata(rule, statement.Word, token);
                    break;

                case "instructions":
                    if (rule.HasFor)
                    {
                        _diagnostics.Report(
                            Codes.MisplacedStatement,
                            token.Span,
                            "'instructions' follows a requirement, or comes before 'for' when it is about the whole rule.");
                        ParseText("text in quotes");
                    }
                    else
                    {
                        ParseRuleMetadata(rule, statement.Word, token);
                    }

                    break;

                case "for":
                    if (rule.HasFor)
                    {
                        ReportDuplicate(token, "for", "A rule has one 'for'. Start another rule for other elements.");
                        ParseFor(new Rule(string.Empty, token.Span), token);
                    }
                    else
                    {
                        ParseFor(rule, token);
                    }

                    break;

                case "where":
                    if (!rule.HasFor && !missingForReported)
                    {
                        ReportMissingFor(token);
                        missingForReported = true;
                    }

                    if (whereSeen)
                    {
                        _diagnostics.Report(
                            Codes.DuplicateStatement,
                            token.Span,
                            "A rule has one 'where'. Further conditions are joined with 'and'.",
                            fix: new TextEdit(token.Span, "and"));
                    }
                    else if (rule.Requirements.Count > 0 || rule.Count != RuleCount.Any)
                    {
                        _diagnostics.Report(
                            Codes.MisplacedStatement,
                            token.Span,
                            "'where' comes directly after 'for', before the requirements.");
                    }

                    whereSeen = true;
                    ParseWhere(rule, token.Span);
                    break;

                case "require":
                case "optional":
                case "prohibit":
                    if (!rule.HasFor && !missingForReported)
                    {
                        ReportMissingFor(token);
                        missingForReported = true;
                    }

                    ParseRequirement(rule, CardinalityOf(statement.Word), token);
                    break;

                default:
                    _diagnostics.Report(
                        Codes.MisplacedStatement,
                        token.Span,
                        $"'{statement.Word}' belongs to the file header and must come before the first rule.");
                    if (statement.Word == "ifc")
                    {
                        ParseVersions();
                    }
                    else
                    {
                        ParseText("text in quotes");
                    }

                    break;
            }
        }

        if (!rule.HasFor && !missingForReported)
        {
            ReportMissingFor(keyword);
        }

        _diagnostics.CurrentRule = null;
        file.Items.Add(rule);
    }

    private void ParseRuleMetadata(Rule rule, string word, Token keyword)
    {
        if (rule.HasFor)
        {
            _diagnostics.Report(
                Codes.MisplacedStatement,
                keyword.Span,
                $"'{word}' describes the rule and must come before 'for'.");
        }

        if (word == "ifc")
        {
            var versions = ParseVersions();
            if (rule.Ifc is not null)
            {
                ReportDuplicate(keyword, word);
            }
            else
            {
                rule.Ifc = versions;
            }

            return;
        }

        var text = ParseText("text in quotes");
        if (text is null)
        {
            SkipToStatement();
            return;
        }

        switch (word)
        {
            case "source" when rule.Source is null:
                rule.Source = text.Value;
                break;
            case "description" when rule.Description is null:
                rule.Description = text.Value;
                break;
            case "instructions" when rule.Instructions is null:
                rule.Instructions = text.Value;
                break;
            default:
                ReportDuplicate(keyword, word);
                break;
        }
    }

    private void ParseFor(Rule rule, Token keyword)
    {
        rule.HasFor = true;
        rule.ForSpan = keyword.Span;

        if (AtKeyword("any"))
        {
            Take();
            return;
        }

        if ((At(TokenKind.Name) && AnyAliases.Contains(Current.Text)) || (At(TokenKind.Unknown) && Current.Text == "*"))
        {
            var token = Take();
            _diagnostics.Report(
                Codes.UnknownKeyword,
                token.Span,
                $"'{token.Text}' is not a keyword.",
                hint: "Write 'for any' and state the condition after 'where', or name an IFC class such as IfcElement.",
                fix: new TextEdit(token.Span, "any"));
            return;
        }

        if (AtKeyword("entity"))
        {
            var filler = Take();
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                filler.Span,
                "The class follows 'for' directly.",
                hint: "Write: for IfcWall.",
                fix: new TextEdit(WithFollowingSpace(filler.Span), string.Empty));
        }
        else if (Current.Kind == TokenKind.Keyword && Keywords.Facets.Contains(Current.Text))
        {
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                Current.Span.AtStart,
                "After 'for' comes a class, or 'any' when the elements are selected by conditions alone.",
                hint: "Write: for any where ...",
                fix: new TextEdit(Current.Span.AtStart, "any where "));
            ParseWhere(rule, Current.Span.AtStart);
            return;
        }

        rule.Entity = ParseEntity();
    }

    private void ParseWhere(Rule rule, SourceSpan keyword)
    {
        while (true)
        {
            var facet = ParseFacet();
            if (facet is null)
            {
                SkipToStatement();
                return;
            }

            rule.Where.Add(facet);
            rule.WhereKeywordSpans.Add(keyword);
            if (!AtWord("and"))
            {
                return;
            }

            keyword = TakeWord("and").Span;
        }
    }

    private void ParseRequirement(Rule rule, Cardinality cardinality, Token keyword)
    {
        if (AtKeyword("count"))
        {
            ParseCount(rule, cardinality, keyword);
            return;
        }

        if (cardinality == Cardinality.Prohibited && (At(TokenKind.EndOfFile) || TryPeekStatement(out _)))
        {
            _diagnostics.Report(
                Codes.UnsupportedPhrase,
                keyword.Span,
                "'prohibit' needs a facet.",
                hint: "To say that no selected element may exist, write: require count = 0.",
                fix: new TextEdit(keyword.Span, "require count = 0"));
            rule.Count = RuleCount.None;
            rule.CountSpan = keyword.Span;
            return;
        }

        while (true)
        {
            var facet = ParseFacet();
            if (facet is null)
            {
                SkipToStatement();
                return;
            }

            var requirement = new Requirement(cardinality, facet, keyword.Span);
            while (true)
            {
                if (AtKeyword("uri"))
                {
                    var uriKeyword = Take();
                    var uri = ParseText("a URI in quotes");
                    if (requirement.Uri is not null)
                    {
                        ReportDuplicate(uriKeyword, "uri");
                    }
                    else if (uri is not null)
                    {
                        requirement.Uri = uri.Value;
                        requirement.UriSpan = SourceSpan.Join(uriKeyword.Span, uri.Span);
                    }
                }
                else if (AtKeyword("instructions"))
                {
                    var instructionsKeyword = Take();
                    var instructions = ParseText("text in quotes");
                    if (requirement.Instructions is not null)
                    {
                        ReportDuplicate(instructionsKeyword, "instructions");
                    }
                    else if (instructions is not null)
                    {
                        requirement.Instructions = instructions.Value;
                    }
                }
                else
                {
                    break;
                }
            }

            rule.Requirements.Add(requirement);

            if (!AtWord("and"))
            {
                return;
            }

            var and = Current;
            var fix = IsFirstOnLine()
                ? new TextEdit(and.Span, WordOf(cardinality))
                : new TextEdit(WithPrecedingSpace(and.Span), "\n" + IndentOf(keyword.Span) + WordOf(cardinality));
            _diagnostics.Report(
                Codes.OneFacetPerRequirement,
                and.Span,
                "Each requirement has its own statement.",
                hint: $"Start a new statement with '{WordOf(cardinality)}'.",
                fix: fix);
            Take();
        }
    }

    private void ParseCount(Rule rule, Cardinality cardinality, Token keyword)
    {
        var count = Take();
        var start = Current;
        RuleCount? parsed = null;
        TextEdit? fix = null;

        var op = Current.Kind;
        if (op is TokenKind.Equal or TokenKind.EqualEqual or TokenKind.GreaterEqual or TokenKind.Greater && Next.Kind == TokenKind.Number)
        {
            Take();
            var number = Take();
            var span = SourceSpan.Join(start.Span, number.Span);
            if ((op == TokenKind.Equal || op == TokenKind.EqualEqual) && number.Text == "0")
            {
                parsed = RuleCount.None;
                if (op == TokenKind.EqualEqual)
                {
                    fix = new TextEdit(span, "= 0");
                }
            }
            else if (op == TokenKind.GreaterEqual && number.Text == "1")
            {
                parsed = RuleCount.AtLeastOne;
            }
            else if (op == TokenKind.Greater && number.Text == "0")
            {
                parsed = RuleCount.AtLeastOne;
                fix = new TextEdit(span, ">= 1");
            }

            if (parsed is null)
            {
                _diagnostics.Report(
                    Codes.UnsupportedCount,
                    span,
                    "IDS 1.0 knows two counts only: at least one element, or none.",
                    expected: new[] { "require count >= 1", "require count = 0" });
                return;
            }

            if (fix is not null)
            {
                _diagnostics.Report(
                    Codes.UnsupportedCount,
                    span,
                    "The count is written '>= 1' or '= 0'.",
                    expected: new[] { "require count >= 1", "require count = 0" },
                    fix: fix);
            }
        }
        else
        {
            ReportUnexpected("'>= 1' or '= 0'", new[] { "require count >= 1", "require count = 0" });
            SkipToStatement();
            return;
        }

        if (cardinality != Cardinality.Required)
        {
            _diagnostics.Report(
                Codes.UnsupportedCount,
                keyword.Span,
                "'count' is written with 'require'.",
                expected: new[] { "require count >= 1", "require count = 0" },
                fix: new TextEdit(keyword.Span, "require"));
        }

        if (rule.Count != RuleCount.Any)
        {
            ReportDuplicate(count, "count");
            return;
        }

        rule.Count = parsed.Value;
        rule.CountSpan = SourceSpan.Join(keyword.Span, _tokens[_pos - 1].Span);
    }

    // ---------------------------------------------------------------- facets

    private Facet? ParseFacet()
    {
        var start = Current;
        Facet? facet;

        if (AtKeyword("entity"))
        {
            Take();
            facet = new EntityFacet(ParseEntity());
        }
        else if (AtKeyword("attribute"))
        {
            Take();
            facet = ParseAttribute();
        }
        else if (AtKeyword("property"))
        {
            Take();
            facet = ParseProperty();
        }
        else if (AtKeyword("classification"))
        {
            Take();
            facet = ParseClassification();
        }
        else if (AtKeyword("material"))
        {
            Take();
            facet = ParseMaterial();
        }
        else if (AtKeyword("part"))
        {
            Take();
            if (AtKeyword("of"))
            {
                Take();
            }
            else
            {
                ReportUnexpected("'of'");
            }

            facet = ParsePartOf(containment: false);
        }
        else
        {
            facet = ParseFacetWithoutKeyword();
        }

        if (facet is not null)
        {
            facet.Span = SourceSpan.Join(start.Span, _tokens[Math.Max(_pos - 1, 0)].Span);
        }

        return facet;
    }

    /// <summary>Recognises the facets that authors write without their keyword, and says which keyword is missing.</summary>
    private Facet? ParseFacetWithoutKeyword()
    {
        var token = Current;

        if (token.Kind == TokenKind.Name && string.Equals(token.Text, "partof", StringComparison.OrdinalIgnoreCase))
        {
            Take();
            _diagnostics.Report(
                Codes.UnknownKeyword,
                token.Span,
                $"'{token.Text}' is not a keyword.",
                hint: "Write 'part of'.",
                fix: new TextEdit(token.Span, "part of"));
            return ParsePartOf(containment: false);
        }

        if (token.Kind == TokenKind.Name && ContainmentAliases.Contains(token.Text) && Next.IsKeyword("in"))
        {
            Take();
            var inToken = Take();
            var phrase = SourceSpan.Join(token.Span, inToken.Span);
            var facet = ParsePartOf(containment: true);
            var edits = new List<TextEdit> { new(phrase, "part of") };
            if (facet.Relation is null)
            {
                facet.Relation = "IfcRelContainedInSpatialStructure";
                edits.Add(new TextEdit(_tokens[_pos - 1].Span.AtEnd, " via IfcRelContainedInSpatialStructure"));
            }

            var diagnostic = _diagnostics.Report(
                Codes.UnsupportedPhrase,
                phrase,
                $"'{token.Text} in' is not part of the language.",
                hint: "Write: part of <class> via IfcRelContainedInSpatialStructure.");
            diagnostic.Fix = edits;
            return facet;
        }

        if (token.Kind is TokenKind.Name or TokenKind.String)
        {
            if (Next.Kind == TokenKind.Dot)
            {
                ReportMissingFacetKeyword(token, "property");
                return ParseProperty();
            }

            if (token.Kind == TokenKind.Name && token.Text.StartsWith("Ifc", StringComparison.Ordinal))
            {
                ReportMissingFacetKeyword(token, "entity");
                return new EntityFacet(ParseEntity());
            }

            if (token.Kind == TokenKind.Name)
            {
                ReportMissingFacetKeyword(token, "attribute");
                return ParseAttribute();
            }
        }

        ReportUnexpected("a facet", FacetStarts);
        return null;
    }

    private AttributeFacet ParseAttribute()
    {
        var facet = new AttributeFacet(ParseName("the name of an attribute, such as Name"));
        facet.Value = ParseConstraint();
        return facet;
    }

    private PropertyFacet ParseProperty()
    {
        var propertySet = ParseName("a property set, such as Pset_WallCommon");
        Matcher name;
        if (At(TokenKind.Dot))
        {
            Take();
            name = ParseName("the name of a property");
        }
        else if ((At(TokenKind.Name) || At(TokenKind.String)) && Current.Span.Line == _tokens[_pos - 1].Span.EndLine)
        {
            // "property Pset_WallCommon FireRating": the dot is missing
            _diagnostics.Report(
                Codes.UnexpectedToken,
                Current.Span.AtStart,
                "A property is written: property set, dot, property name.",
                hint: "Write: property Pset_WallCommon.FireRating.",
                fix: new TextEdit(new SourceSpan(_tokens[_pos - 1].Span.End, Current.Span.Start, _tokens[_pos - 1].Span.EndLine, _tokens[_pos - 1].Span.EndColumn, Current.Span.Line, Current.Span.Column), "."));
            name = ParseName("the name of a property");
        }
        else
        {
            ReportUnexpected("'.' and the name of the property");
            name = new Matcher(MatcherKind.Exact, Current.Span.AtStart);
        }

        var facet = new PropertyFacet(propertySet, name);

        if (AtKeyword("datatype"))
        {
            Take();
            ParseDataType(facet);
        }
        else if (TryTakeDataTypeAlias())
        {
            ParseDataType(facet);
        }

        facet.Value = ParseConstraint();
        return facet;
    }

    private void ParseDataType(PropertyFacet facet)
    {
        if (At(TokenKind.Name))
        {
            var type = Take();
            facet.DataType = type.Text;
            facet.DataTypeSpan = type.Span;
        }
        else
        {
            ReportUnexpected("an IFC data type, such as IfcLabel");
        }
    }

    /// <summary>"type IfcLabel", "as IfcLabel", ": IfcLabel", "of type IfcLabel" after a property mean "datatype IfcLabel".</summary>
    private bool TryTakeDataTypeAlias()
    {
        var first = Current;
        var count = 0;
        if ((first.IsKeyword("type") || IsName(first, "as") || (first.Kind == TokenKind.Unknown && first.Text == ":")) && LooksLikeDataType(Next))
        {
            count = 1;
        }
        else if (first.IsKeyword("of") && Next.IsKeyword("type") && LooksLikeDataType(PeekAt(2)))
        {
            count = 2;
        }

        if (count == 0)
        {
            return false;
        }

        var last = first;
        for (var i = 0; i < count; i++)
        {
            last = Take();
        }

        var span = SourceSpan.Join(first.Span, last.Span);
        var needsSpace = first.Text == ":" && first.Span.Start > 0 && !char.IsWhiteSpace(_source[first.Span.Start - 1]);
        _diagnostics.Report(
            Codes.UnknownKeyword,
            span,
            "The data type of a property is written with 'datatype'.",
            hint: "Write: property Set.Name datatype IfcLabel.",
            fix: new TextEdit(span, needsSpace ? " datatype" : "datatype"));
        return true;
    }

    private ClassificationFacet ParseClassification()
    {
        var facet = new ClassificationFacet(ParseName("a classification system in quotes, such as \"Uniclass 2015\""));
        DropFillerWord(ClassificationFillers, "The constraint on the code follows the system directly.");
        facet.Value = ParseConstraint();
        return facet;
    }

    private MaterialFacet ParseMaterial()
    {
        var facet = new MaterialFacet();
        DropFillerWord(MaterialFillers, "The constraint follows 'material' directly; it applies to the name or the category.");
        facet.Value = ParseConstraint();
        return facet;
    }

    private PartOfFacet ParsePartOf(bool containment)
    {
        var facet = new PartOfFacet(ParseEntity());
        if (!AtKeyword("via"))
        {
            return facet;
        }

        Take();
        if (!At(TokenKind.Name))
        {
            ReportUnexpected("a relationship", Relations.All);
            return facet;
        }

        var first = Take();
        var relation = first.Text;
        var span = first.Span;
        if (At(TokenKind.Name) && Current.Text.StartsWith("IfcRel", StringComparison.Ordinal))
        {
            var second = Take();
            relation += " " + second.Text;
            span = SourceSpan.Join(span, second.Span);
        }

        if (Relations.All.Contains(relation))
        {
            facet.Relation = relation;
        }
        else
        {
            var suggestion = Suggest(relation, Relations.All);
            _diagnostics.Report(
                Codes.InvalidRelation,
                span,
                $"'{relation}' is not a relationship of the IDS partOf facet.",
                hint: suggestion is null ? null : $"Did you mean {suggestion}?",
                expected: Relations.All,
                fix: suggestion is null ? null : new TextEdit(span, suggestion));
            facet.Relation = suggestion;
        }

        return facet;
    }

    private EntitySelector ParseEntity()
    {
        var selector = new EntitySelector(ParseClasses());

        if (AtKeyword("type"))
        {
            Take();
            selector.Types = ParseTypes();
        }
        else if (TryTakePredefinedTypeAlias())
        {
            selector.Types = ParseTypes();
        }

        return selector;
    }

    /// <summary>"predefined type FLOOR", "with predefined type FLOOR" and "predefined FLOOR" mean "type FLOOR".</summary>
    private bool TryTakePredefinedTypeAlias()
    {
        var first = Current;
        var index = 0;
        if (IsName(PeekAt(index), "with"))
        {
            index++;
        }

        if (!IsName(PeekAt(index), "predefined") && !IsName(PeekAt(index), "predefinedtype"))
        {
            return false;
        }

        index++;
        if (PeekAt(index).IsKeyword("type"))
        {
            index++;
        }

        var last = first;
        for (var i = 0; i < index; i++)
        {
            last = Take();
        }

        var span = SourceSpan.Join(first.Span, last.Span);
        _diagnostics.Report(
            Codes.UnknownKeyword,
            span,
            "The predefined type follows the class after 'type'.",
            hint: "Write: IfcSlab type FLOOR.",
            fix: new TextEdit(span, "type"));
        return true;
    }

    private Matcher ParseClasses()
    {
        var start = Current;
        if (AtKeyword("matches"))
        {
            Take();
            var pattern = new Matcher(MatcherKind.Pattern, start.Span);
            ParsePatterns(pattern.Patterns);
            FinishMatcher(pattern, start);
            return pattern;
        }

        var only = false;
        if (AtKeyword("only"))
        {
            Take();
            only = true;
        }

        var names = new List<NameItem>();
        while (true)
        {
            if (At(TokenKind.Name))
            {
                var token = Take();
                names.Add(new NameItem(token.Text, false, token.Span));
            }
            else if (At(TokenKind.String))
            {
                var token = Take();
                names.Add(new NameItem(token.Value, true, token.Span));
            }
            else
            {
                ReportUnexpected("an IFC class, such as IfcWall");
                break;
            }

            if (At(TokenKind.Comma))
            {
                Take();
                continue;
            }

            if (Current.IsKeyword("or") || (At(TokenKind.Unknown) && Current.Text == "|"))
            {
                var or = Take();
                _diagnostics.Report(
                    Codes.UnsupportedOperator,
                    or.Span,
                    "Classes are separated by commas.",
                    fix: new TextEdit(TrimLeadingSpace(or.Span), ","));
                continue;
            }

            break;
        }

        var matcher = new Matcher(names.Count > 1 ? MatcherKind.AnyOf : MatcherKind.Exact, start.Span) { Only = only };
        matcher.Names.AddRange(names);
        FinishMatcher(matcher, start);
        return matcher;
    }

    private Matcher ParseTypes()
    {
        var start = Current;
        if (AtKeyword("matches"))
        {
            Take();
            var pattern = new Matcher(MatcherKind.Pattern, start.Span);
            ParsePatterns(pattern.Patterns);
            FinishMatcher(pattern, start);
            return pattern;
        }

        var names = new List<NameItem>();
        while (true)
        {
            if (At(TokenKind.Name))
            {
                var token = Take();
                names.Add(new NameItem(token.Text, false, token.Span));
            }
            else if (At(TokenKind.String))
            {
                var token = Take();
                names.Add(new NameItem(token.Value, true, token.Span));
            }
            else
            {
                ReportUnexpected("a predefined type, such as FLOOR, or a user-defined type in quotes");
                break;
            }

            if (!At(TokenKind.Comma))
            {
                break;
            }

            Take();
        }

        var matcher = new Matcher(names.Count > 1 ? MatcherKind.AnyOf : MatcherKind.Exact, start.Span);
        matcher.Names.AddRange(names);
        FinishMatcher(matcher, start);
        return matcher;
    }

    private Matcher ParseName(string what)
    {
        var start = Current;
        Matcher matcher;

        if (At(TokenKind.Name))
        {
            Take();
            return Matcher.Single(start.Text, false, start.Span);
        }

        if (At(TokenKind.String))
        {
            Take();
            return Matcher.Single(start.Value, true, start.Span);
        }

        if (AtKeyword("matches"))
        {
            Take();
            matcher = new Matcher(MatcherKind.Pattern, start.Span);
            ParsePatterns(matcher.Patterns);
        }
        else if (At(TokenKind.LeftBracket))
        {
            Take();
            matcher = new Matcher(MatcherKind.AnyOf, start.Span);
            while (!At(TokenKind.RightBracket) && !At(TokenKind.EndOfFile))
            {
                if (At(TokenKind.Name))
                {
                    var token = Take();
                    matcher.Names.Add(new NameItem(token.Text, false, token.Span));
                }
                else if (At(TokenKind.String))
                {
                    var token = Take();
                    matcher.Names.Add(new NameItem(token.Value, true, token.Span));
                }
                else
                {
                    ReportUnexpected("a name");
                    break;
                }

                if (At(TokenKind.Comma))
                {
                    Take();
                }
                else
                {
                    break;
                }
            }

            if (At(TokenKind.RightBracket))
            {
                Take();
            }
            else
            {
                ReportUnexpected("']'");
            }
        }
        else
        {
            ReportUnexpected(what);
            return new Matcher(MatcherKind.Exact, start.Span.AtStart);
        }

        FinishMatcher(matcher, start);
        return matcher;
    }

    private void FinishMatcher(Matcher matcher, Token start)
    {
        if (AtKeyword("note"))
        {
            Take();
            matcher.Note = ParseText("text in quotes")?.Value;
        }

        matcher.Span = SourceSpan.Join(start.Span, _tokens[Math.Max(_pos - 1, 0)].Span);
    }

    // ---------------------------------------------------------------- helpers shared by the parts of the parser

    private Token Current => _tokens[_pos];

    private Token Next => PeekAt(1);

    private Token PeekAt(int offset) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

    private bool At(TokenKind kind) => Current.Kind == kind;

    private bool AtKeyword(string word) => Current.IsKeyword(word);

    private Token Take()
    {
        var token = Current;
        if (token.Kind != TokenKind.EndOfFile)
        {
            _pos++;
        }

        return token;
    }

    private static bool IsName(Token token, string text) =>
        token.Kind == TokenKind.Name && string.Equals(token.Text, text, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeDataType(Token token) =>
        token.Kind == TokenKind.Name && token.Text.StartsWith("Ifc", StringComparison.OrdinalIgnoreCase);

    private static Cardinality CardinalityOf(string word) => word switch
    {
        "optional" => Cardinality.Optional,
        "prohibit" => Cardinality.Prohibited,
        _ => Cardinality.Required,
    };

    private static string WordOf(Cardinality cardinality) => cardinality switch
    {
        Cardinality.Optional => "optional",
        Cardinality.Prohibited => "prohibit",
        _ => "require",
    };
}
