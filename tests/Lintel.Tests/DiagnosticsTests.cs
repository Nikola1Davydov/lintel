using System.Linq;
using Lintel.Diagnostics;
using Xunit;

namespace Lintel.Tests;

/// <summary>
/// Every diagnostic: what triggers it, and for the ones with a fix, the source after the fix.
/// </summary>
public class DiagnosticsTests
{
    private const string Rule = "rule \"x\"\n  for IfcWall\n  ";

    /// <summary>One requirement line inside a rule for IfcWall.</summary>
    private static CheckResult CheckLine(string line) => LintelCompiler.Check(TestSupport.WithHeader(Rule + line));

    private static CheckResult CheckRules(string rules) => LintelCompiler.Check(TestSupport.WithHeader(rules));

    // ---------------------------------------------------------------- other spellings, with a fix

    [Theory]
    [InlineData("require property A.B == 1", "E206", "require property A.B = 1")]
    [InlineData("require A.B = 1", "E209", "require property A.B = 1")]
    [InlineData("require \"My Set\".\"My Prop\"", "E209", "require property \"My Set\".\"My Prop\"")]
    [InlineData("require Name", "E209", "require attribute Name")]
    [InlineData("require attribute Name matches \"^D[0-9]{3}$\"", "W509", "require attribute Name matches \"D[0-9]{3}\"")]
    [InlineData("require attribute Name matches /D\\d{3}/", "E210", "require attribute Name matches \"D\\d{3}\"")]
    [InlineData("require attribute Name matches /^D\\d{3}$/", "W509", "require attribute Name matches \"D\\d{3}\"")]
    [InlineData("require attribute Name matches \"D\\\\d{3}\"", "W503", "require attribute Name matches \"D\\d{3}\"")]
    [InlineData("forbid property A.B", "E202", "prohibit property A.B")]
    [InlineData("requires attribute Name", "E202", "require attribute Name")]
    [InlineData("REQUIRE attribute Name", "E202", "require attribute Name")]
    [InlineData("requier attribute Name", "E202", "require attribute Name")]
    [InlineData("must have attribute Name", "E202", "require attribute Name")]
    [InlineData("must not have property A.B", "E202", "prohibit property A.B")]
    [InlineData("may have attribute Name = \"x\"", "E202", "optional attribute Name = \"x\"")]
    [InlineData("require attribute Name and attribute Tag", "E207", "require attribute Name\n  require attribute Tag")]
    [InlineData("require attribute Name is present", "E208", "require attribute Name")]
    [InlineData("require attribute Name exists", "E208", "require attribute Name")]
    [InlineData("require classification \"DIN 276\" starts with \"3.1\"", "E208", "require classification \"DIN 276\" matches \"3\\.1.*\"")]
    [InlineData("require classification \"Uniclass 2015\" code matches \"Ss_.*\"", "E208", "require classification \"Uniclass 2015\" matches \"Ss_.*\"")]
    [InlineData("require material name contains \"beton\"", "E208", "require material matches \".*beton.*\"")]
    [InlineData("require attribute Name ends with \"-A\"", "E208", "require attribute Name matches \".*\\-A\"")]
    [InlineData("require attribute Name like \"D.*\"", "E202", "require attribute Name matches \"D.*\"")]
    [InlineData("require property A.B is one of [\"x\", \"y\"]", "E208", "require property A.B in [\"x\", \"y\"]")]
    [InlineData("require property A.B in \"x\", \"y\"", "E201", "require property A.B in [\"x\", \"y\"]")]
    [InlineData("require property A.B at most 5", "E208", "require property A.B <= 5")]
    [InlineData("require property A.B min 5", "E208", "require property A.B >= 5")]
    [InlineData("require property A.B is \"x\"", "E206", "require property A.B = \"x\"")]
    [InlineData("require property A.B = TRUE", "E202", "require property A.B = true")]
    [InlineData("require property A.B = New", "E201", "require property A.B = \"New\"")]
    [InlineData("require property A.B >= 1 and <= 5", "W504", "require property A.B between 1 and 5")]
    [InlineData("require property Qto_WallBaseQuantities.Width >= 150 mm and <= 400 mm", "W504", "require property Qto_WallBaseQuantities.Width between 150 mm and 400 mm")]
    [InlineData("require property A.B type IfcLabel", "E202", "require property A.B datatype IfcLabel")]
    [InlineData("require property A.B: IfcLabel = \"x\"", "E202", "require property A.B datatype IfcLabel = \"x\"")]
    [InlineData("require property A.B as IfcLabel", "E202", "require property A.B datatype IfcLabel")]
    [InlineData("require property A B", "E201", "require property A.B")]
    [InlineData("require property \"A\".B", "W502", "require property A.B")]
    [InlineData("require classification ETIM", "W502", "require classification \"ETIM\"")]
    [InlineData("require contained in IfcBuildingStorey", "E208", "require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure")]
    [InlineData("require partof IfcBuildingStorey", "E202", "require part of IfcBuildingStorey")]
    [InlineData("require part of IfcSpace via IfcRelContainedInSpatialStructur", "E316", "require part of IfcSpace via IfcRelContainedInSpatialStructure")]
    [InlineData("count >= 1", "E202", "require count >= 1")]
    [InlineData("require count > 0", "E304", "require count >= 1")]
    [InlineData("prohibit", "E208", "require count = 0")]
    public void Another_spelling_is_reported_with_its_fix(string line, string code, string fixedLine)
    {
        var result = CheckLine(line);

        Assert.Contains(code, result.Codes());
        Assert.Equal(TestSupport.WithHeader(Rule + fixedLine), result.Fixed);
        Assert.True(result.FixedOk, result.Describe());
    }

    [Theory]
    [InlineData("requirement R1 \"x\"\n  for IfcWall\n  require attribute Name", "rule R1 \"x\"\n  for IfcWall\n  require attribute Name")]
    [InlineData("spec \"x\"\n  for IfcWall\n  require attribute Name", "rule \"x\"\n  for IfcWall\n  require attribute Name")]
    [InlineData("rule R1: \"x\"\n  for IfcWall\n  require attribute Name", "rule R1 \"x\"\n  for IfcWall\n  require attribute Name")]
    [InlineData("rule \"x\"\n  applies to IfcWall\n  require attribute Name", "rule \"x\"\n  for IfcWall\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for entity IfcWall\n  require attribute Name", "rule \"x\"\n  for IfcWall\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for IfcWall or IfcSlab\n  require attribute Name", "rule \"x\"\n  for IfcWall, IfcSlab\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for all where material\n  require attribute Name", "rule \"x\"\n  for any where material\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for property A.B\n  require count = 0", "rule \"x\"\n  for any where property A.B\n  require count = 0")]
    [InlineData("rule \"x\"\n  for IfcWall with property A.B = true\n  require attribute Name", "rule \"x\"\n  for IfcWall where property A.B = true\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for IfcWall\n  where material\n  where property A.B\n  require attribute Name", "rule \"x\"\n  for IfcWall\n  where material\n  and property A.B\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for IfcSlab with predefined type FLOOR\n  require attribute Name", "rule \"x\"\n  for IfcSlab type FLOOR\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for IfcSlab where PredefinedType = FLOOR\n  require attribute Name", "rule \"x\"\n  for IfcSlab type FLOOR\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for IfcSlab\n  where attribute PredefinedType = \"FLOOR\"\n    and material\n  require attribute Name", "rule \"x\"\n  for IfcSlab type FLOOR\n  where material\n  require attribute Name")]
    [InlineData("rule \"x\"\n  for IfcSlab\n  where material\n    and attribute PredefinedType = \"Podium slab\"\n  require attribute Name", "rule \"x\"\n  for IfcSlab type \"Podium slab\"\n  where material\n  require attribute Name")]
    [InlineData("define L = [\"a\"]\nrule \"x\"\n  for IfcWall\n  require property A.B in LL", "define L = [\"a\"]\nrule \"x\"\n  for IfcWall\n  require property A.B in L")]
    [InlineData("list L = [\"a\"]\nrule \"x\"\n  for IfcWall\n  require property A.B in list L", "define L = [\"a\"]\nrule \"x\"\n  for IfcWall\n  require property A.B in L")]
    public void Another_spelling_of_a_rule_is_repaired(string rules, string fixedRules)
    {
        var result = CheckRules(rules);

        Assert.Equal(TestSupport.WithHeader(fixedRules), result.Fixed);
        Assert.True(result.FixedOk, result.Describe());
    }

    [Theory]
    [InlineData("schema IFC4", "ifc IFC4")]
    [InlineData("ifc IFC4X3", "ifc IFC4X3_ADD2")]
    [InlineData("ifc IFC4.3", "ifc IFC4X3_ADD2")]
    [InlineData("ifc ifc2x3", "ifc IFC2X3")]
    [InlineData("ifc IFC2X3 IFC4", "ifc IFC2X3, IFC4")]
    [InlineData("IFC IFC4", "ifc IFC4")]
    public void Another_spelling_of_the_ifc_version_is_repaired(string header, string fixedHeader)
    {
        const string rule = "\nrule \"x\"\n  for IfcWall\n  require attribute Name";

        var result = LintelCompiler.Check(header + rule);

        Assert.Equal(fixedHeader + rule, result.Fixed);
        Assert.True(result.FixedOk, result.Describe());
    }

    // ---------------------------------------------------------------- errors without a fix

    [Theory]
    [InlineData("require property A.B != 1", "E206")]
    [InlineData("require attribute Name matches \"(a|b\"", "E302")]
    [InlineData("optional attribute Name", "E303")]
    [InlineData("optional material", "E303")]
    [InlineData("optional classification \"DIN 276\"", "E303")]
    [InlineData("optional property A.B", "E303")]
    [InlineData("optional part of IfcSpace", "E303")]
    [InlineData("optional entity IfcWall", "E303")]
    [InlineData("prohibit entity IfcWall", "E303")]
    [InlineData("prohibit property A.B = 1", "E303")]
    [InlineData("prohibit property A.B datatype IfcLabel", "E303")]
    [InlineData("require count >= 2", "E304")]
    [InlineData("require count = 0\n  require attribute Name", "E305")]
    [InlineData("require property A.B in Missing", "E308")]
    [InlineData("require property A.B in []", "E312")]
    [InlineData("require property A.B in [\"a\", 1]", "E312")]
    [InlineData("require attribute Name length <= 2.5", "E314")]
    [InlineData("require attribute Name uri \"https://example.org\"", "E315")]
    [InlineData("require part of IfcSpace via IfcRelDefinesByType", "E316")]
    [InlineData("require attribute Name; require attribute Tag", "E102")]
    [InlineData("require attribute Name = 'x'", "E102")]
    [InlineData("require material in {\"a\", \"b\"}", "E102")]
    [InlineData("require attribute Name = \"x", "E101")]
    [InlineData("require property A.B or property A.C", "E211")]
    [InlineData("require property A.B if present", "E211")]
    public void Errors(string line, string code)
    {
        var result = CheckLine(line);

        Assert.False(result.Ok);
        Assert.Contains(code, result.Codes());
    }

    [Theory]
    [InlineData("optional attribute Name = \"x\"")]
    [InlineData("optional property A.B datatype IfcLabel")]
    [InlineData("optional property A.B in [\"x\"]")]
    [InlineData("optional material = \"Steel\"")]
    [InlineData("optional classification \"DIN 276\" matches \"3.*\"")]
    [InlineData("prohibit attribute Name")]
    [InlineData("prohibit attribute Name = \"TBD\"")]
    [InlineData("prohibit property A.B")]
    [InlineData("prohibit material")]
    [InlineData("prohibit material = \"Asbestos\"")]
    [InlineData("prohibit classification \"DIN 276\"")]
    [InlineData("prohibit part of IfcSpace")]
    [InlineData("require attribute Name matches \"[$]\\^\\d+\\p{Lu}[a-z-[aeiou]]\"")]
    [InlineData("require property \"count\".\"in\"")]
    public void Combinations_that_IDS_allows(string line)
    {
        var result = CheckLine(line);

        Assert.True(result.Diagnostics.Count == 0, result.Describe());
    }

    // What IDS accepts is not an error, even when it is probably not what the author means.
    [Theory]
    [InlineData("require attribute Name matches \"a.*?\"", "W510")]
    [InlineData("require attribute Name matches \"(?i)abc\"", "W510")]
    [InlineData("require attribute Name matches \"\\bword\\b\"", "W510")]
    [InlineData("require attribute Name matches \"^x\"", "W509")]
    [InlineData("require property A.B between 5 and 1", "W513")]
    [InlineData("require property A.B > 5 and < 1", "W513")]
    [InlineData("require property A.B > 1 and >= 2", "W513")]
    public void Warnings(string line, string code)
    {
        var result = CheckLine(line);

        Assert.True(result.Ok, result.Describe());
        Assert.Contains(code, result.Codes());
    }

    [Fact]
    public void Structure_of_a_rule()
    {
        Assert.Contains("E205", CheckRules("rule \"x\"\n  require attribute Name").Codes());
        Assert.Contains("E306", CheckRules("rule \"x\"\n  for IfcWall").Codes());
        Assert.Contains("E307", CheckRules("rule \"x\"\n  for any\n  require attribute Name").Codes());
        Assert.Contains("E203", CheckRules("rule \"x\"\n  for IfcWall\n  require attribute Name\n  where material").Codes());
        Assert.Contains("E203", CheckRules("rule \"x\"\n  for IfcWall\n  description \"late\"\n  require attribute Name").Codes());
        Assert.Contains("E203", CheckRules("rule \"x\"\n  for IfcWall\n  where entity IfcSlab\n  require attribute Name").Codes());
        Assert.Contains("E203", CheckRules("rule \"x\"\n  for IfcWall\n  require attribute Name\ndefine L = [1]\ntitle \"late\"").Codes());
        Assert.Contains("E203", CheckRules("for IfcWall\nrule \"x\"\n  for IfcWall\n  require attribute Name").Codes());
        Assert.Contains("E204", CheckRules("rule \"x\"\n  for IfcWall\n  for IfcSlab\n  require attribute Name").Codes());
        Assert.Contains("E204", CheckRules("rule \"x\"\n  source \"a\"\n  source \"b\"\n  for IfcWall\n  require attribute Name").Codes());
        Assert.Contains("E201", CheckRules("rule R1\n  for IfcWall\n  require attribute Name").Codes());
        Assert.Contains("E309", CheckRules("define L = [1]\ndefine L = [2]\nrule \"x\"\n  for IfcWall\n  require attribute Name").Codes());
        Assert.Contains("W505", CheckRules("rule R1 \"x\"\n  for IfcWall\n  require attribute Name\nrule R1 \"y\"\n  for IfcSlab\n  require attribute Name").Codes());
    }

    [Fact]
    public void Header_problems()
    {
        const string rule = "\nrule \"x\"\n  for IfcWall\n  require attribute Name";

        Assert.Contains("E311", LintelCompiler.Check(rule).Codes());
        Assert.Contains("E310", LintelCompiler.Check("ifc IFC5" + rule).Codes());
        Assert.Contains("E313", LintelCompiler.Check("ifc IFC4\nauthor \"Jane Doe\"" + rule).Codes());
        Assert.Contains("E313", LintelCompiler.Check("ifc IFC4\ndate \"21.11.2026\"" + rule).Codes());
        Assert.Contains("E204", LintelCompiler.Check("ifc IFC4\ntitle \"a\"\ntitle \"b\"" + rule).Codes());
        Assert.Contains("E104", LintelCompiler.Check("ifc IFC4\ndescription \"\"\"\nno end" + rule).Codes());

        // a rule may bring its own version
        Assert.True(LintelCompiler.Check("rule \"x\"\n  ifc IFC2X3\n  for IfcWall\n  require attribute Name").Ok);
    }

    // ---------------------------------------------------------------- the shape of a diagnostic

    [Fact]
    public void A_diagnostic_says_where_what_and_how_to_repair()
    {
        var result = CheckLine("require property A.B == 1");

        var diagnostic = result.Single("E206");
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal("unsupported-operator", diagnostic.Name);
        Assert.Equal("x", diagnostic.Rule);
        Assert.Equal((5, 24, 5, 26), (diagnostic.Span.Line, diagnostic.Span.Column, diagnostic.Span.EndLine, diagnostic.Span.EndColumn));
        var edit = Assert.Single(diagnostic.Fix!);
        Assert.Equal("=", edit.Text);
        Assert.Equal((5, 24), (edit.Span.Line, edit.Span.Column));
    }

    [Fact]
    public void Expected_values_are_listed()
    {
        var version = LintelCompiler.Check("ifc IFC5\nrule \"x\"\n  for IfcWall\n  require attribute Name").Single("E310");
        Assert.Equal(new[] { "IFC2X3", "IFC4", "IFC4X3_ADD2" }, version.Expected);

        var relation = CheckLine("require part of IfcSpace via IfcRelDefinesByType").Single("E316");
        Assert.Contains("IfcRelContainedInSpatialStructure", relation.Expected!);

        var list = CheckRules("define Status = [\"a\"]\ndefine Phases = [\"b\"]\nrule \"x\"\n  for IfcWall\n  require property A.B in Statu").Single("E308");
        Assert.Equal(new[] { "Phases", "Status" }, list.Expected);
        Assert.Equal("Did you mean 'Status'?", list.Hint);
    }

    [Fact]
    public void Json()
    {
        var json = CheckLine("require property A.B == 1").ToJson();

        Assert.Contains("\"ok\": false", json);
        Assert.Contains("\"code\": \"E206\"", json);
        Assert.Contains("\"name\": \"unsupported-operator\"", json);
        Assert.Contains("\"line\": 5", json);
        Assert.Contains("\"rule\": \"x\"", json);
        Assert.Contains("\"fix\": [", json);
        Assert.Contains("\"fixed\": \"ifc IFC4\\n\\nrule \\\"x\\\"\\n  for IfcWall\\n  require property A.B = 1\"", json);
        Assert.Contains("\"fixedOk\": true", json);
    }

    [Fact]
    public void One_problem_does_not_hide_the_next()
    {
        var result = CheckRules("""
            rule "a"
              for IfcWall
              require property A.B =
            rule "b"
              for IfcSlab
              require attribute Name matches "^x$"
            """);

        Assert.Contains("E201", result.Codes());
        Assert.Contains("W509", result.Codes());
        Assert.Equal(2, result.File.Rules.Count());
    }
}
