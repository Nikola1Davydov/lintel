using System;
using System.Collections.Generic;
using System.Linq;
using Lintel.Diagnostics;
using Lintel.Ids;
using Xunit;
using Xunit.Abstractions;

namespace Lintel.Tests;

/// <summary>
/// The compiler against the official IDS audit of buildingSMART, which is the judge of what a valid IDS file is.
/// Three claims: the compiler never writes a file that the audit rejects; a file that the audit accepts can be
/// written in Lintel and comes back unchanged; a file that the audit rejects does not get through.
/// The files are generated, so that the claims are tried on combinations nobody wrote by hand.
/// </summary>
public class AuditAgreementTests
{
    /// <summary>
    /// What Lintel cannot keep, and says so when it reads the file (SPEC.md, section 7): restrictions on a name
    /// other than a list or patterns, a base type other than text where the value is text, notes on single values
    /// of a list, and a property value without a data type, which the audit lets pass when an empty text meets it.
    /// </summary>
    private static readonly string[] KnownLimits =
    {
        "A name cannot be restricted with",
        "A name is restricted with a pattern and a list at once",
        "A name is text; the restriction has the base",
        "The restriction has the base",
        "The annotation of the single value",
        "The property has a value and no dataType",
    };

    private readonly ITestOutputHelper _output;

    public AuditAgreementTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Generated_files_are_judged_the_same_by_the_compiler_and_the_audit()
    {
        const int count = 300;
        var failures = new List<string>();
        var valid = 0;
        var noted = 0;

        for (var seed = 1; seed <= count; seed++)
        {
            var ids = new IdsGenerator(seed).Document();
            var passes = IdsXml.Audit(ids).Count == 0;
            var imported = LintelCompiler.Import(ids);
            var compiled = LintelCompiler.Compile(imported.Source);
            var error = compiled.Check.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Error);

            // The printed text is in the grammar, whatever the file contained.
            var grammar = compiled.Check.Diagnostics.FirstOrDefault(d => d.Code.StartsWith("E1", StringComparison.Ordinal) || d.Code.StartsWith("E2", StringComparison.Ordinal));
            if (grammar is not null)
            {
                failures.Add($"seed {seed}: the printed text is not in the grammar: {grammar}");
                continue;
            }

            if (!passes)
            {
                // A file that the audit rejects does not get through silently.
                if (error is null && imported.Notes.Count == 0)
                {
                    failures.Add($"seed {seed}: the audit rejects the file, the compiler accepts it: {IdsXml.Audit(ids)[0]}");
                }

                continue;
            }

            valid++;
            if (imported.Notes.Count > 0)
            {
                // The reader says what it cannot keep; it must be one of the known limits.
                noted++;
                var unknown = imported.Notes.FirstOrDefault(n => !KnownLimits.Any(limit => n.Message.StartsWith(limit, StringComparison.Ordinal)));
                if (unknown is not null)
                {
                    failures.Add($"seed {seed}: a valid file is not kept: {unknown.Message}");
                }

                continue;
            }

            // A file that the audit accepts has no errors in Lintel and comes back as the same IDS.
            if (error is not null)
            {
                failures.Add($"seed {seed}: the audit accepts the file, the compiler rejects it: {error}");
            }
            else if (IdsCanonical.FirstDifference(IdsCanonical.Of(ids), IdsCanonical.Of(compiled.Ids!)) is { } difference)
            {
                failures.Add($"seed {seed}: the file comes back different: {difference}");
            }
        }

        _output.WriteLine($"{count} generated files: {valid} valid for the audit, of which {noted} with a known limit");
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));

        // The generator must keep writing files of both kinds, or the test says nothing.
        Assert.InRange(valid, count / 10, count * 9 / 10);
    }

    [Theory]
    [InlineData("ifc IFC4\ndate \"2026-13-45\"\nrule \"x\"\n  for IfcWall\n  require attribute Name", 2)]
    [InlineData("ifc IFC4\nrule \"x\"\n  for any\n  where attribute CreationDate matches \"2026.*\"\n  require attribute Name", 4)]
    public void What_only_the_audit_knows_is_reported_at_its_place(string source, int line)
    {
        var result = LintelCompiler.Compile(source);

        var rejected = Assert.Single(result.Check.Diagnostics, d => d.Code == "E499");
        Assert.Equal(line, rejected.Span.Line);
        Assert.StartsWith("The official IDS audit rejects", rejected.Message);
        Assert.DoesNotContain("position", rejected.Message);
        Assert.Null(result.Ids);
    }

    [Fact]
    public void A_file_without_rules_has_no_errors_and_no_IDS()
    {
        var result = LintelCompiler.Compile("ifc IFC4\ndefine Status = [\"New\", \"Old\"]\n");

        Assert.True(result.Check.Ok, result.Check.Describe());
        Assert.Null(result.Ids);
    }

    [Theory]
    [InlineData("for IfcWall\n  where classification matches \".*\"\n  require attribute Name")]
    [InlineData("for IfcWall\n  optional attribute Name matches \".*\"")]
    [InlineData("for IfcWall\n  optional material in [\"\", \"Beton\"]")]
    [InlineData("for IfcWall\n  optional classification \"DIN 276\" length = 0")]
    public void A_constraint_that_an_empty_text_meets_counts_as_missing(string rule)
    {
        var result = LintelCompiler.Check("ifc IFC4\nrule \"x\"\n  " + rule);

        Assert.Contains("E317", result.Codes());
        Assert.DoesNotContain("E499", result.Codes());
    }

    [Theory]
    [InlineData("for IfcWall\n  where classification matches \".+\"\n  require attribute Name")]
    [InlineData("for IfcWall\n  optional attribute Name matches \".+\"")]
    [InlineData("for IfcWall\n  optional attribute Name length >= 0")]
    [InlineData("for IfcDoor\n  optional attribute OverallHeight matches \".*\"")]
    [InlineData("for IfcWall\n  require attribute Name matches \".*\"")]
    public void A_constraint_that_needs_a_character_counts(string rule)
    {
        var result = LintelCompiler.Check("ifc IFC4\nrule \"x\"\n  " + rule);

        Assert.True(result.Ok, result.Describe());
    }
}
