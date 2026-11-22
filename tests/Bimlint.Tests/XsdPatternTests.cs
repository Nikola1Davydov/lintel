using Bimlint.Syntax;
using Xunit;

namespace Bimlint.Tests;

/// <summary>Patterns are XML Schema regular expressions: the whole value must match.</summary>
public class XsdPatternTests
{
    [Theory]
    [InlineData("F[0-9]{2}", "F30", true)]
    [InlineData("F[0-9]{2}", "F3", false)]
    [InlineData("F[0-9]{2}", "xF30", false)]
    [InlineData("F[0-9]{2}", "F30x", false)]
    [InlineData("(F|REI)[0-9]{2,3}", "REI120", true)]
    [InlineData("IFC.*TYPE", "IFCWALLTYPE", true)]
    [InlineData("IFC.*TYPE", "IFCWALL", false)]
    [InlineData("\\d+", "2026", true)]
    [InlineData("\\d+", "20a6", false)]
    [InlineData("[a-z-[aeiou]]+", "xyz", true)]
    [InlineData("[a-z-[aeiou]]+", "xay", false)]
    [InlineData("a.c", "abc", true)]
    [InlineData("a.c", "a\nc", false)]
    [InlineData("\\w+", "Wand", true)]
    [InlineData("\\w+", "Wand_1", false)]
    [InlineData("^a$", "a", false)]
    [InlineData("^a$", "^a$", true)]
    [InlineData("3\\.1.*", "3.14", true)]
    [InlineData("3\\.1.*", "3x14", false)]
    public void The_whole_value_must_match(string pattern, string value, bool expected)
    {
        Assert.Equal(expected, XsdPattern.ToRegex(pattern)!.IsMatch(value));
    }

    /// <summary>
    /// The official audit reads a pattern as the .NET expression "^pattern$". That accepts what XML Schema does not
    /// have, and it does not group an alternative: "a|b" is read as "^a" or "b$".
    /// </summary>
    [Theory]
    [InlineData("F[0-9]{2}", "F30", true)]
    [InlineData("F[0-9]{2}", "F300", false)]
    [InlineData("^F[0-9]{2}$", "F30", true)]
    [InlineData("(?i)f30", "F30", true)]
    [InlineData("F.*?", "F30", true)]
    [InlineData("\\d+", "2026", true)]
    [InlineData("\\d+", "20a6", false)]
    [InlineData("\\i\\c*", "ifc:Wall", true)]
    [InlineData("IFCDOOR|IFCWINDOW", "IFCDOOR", true)]
    [InlineData("IFCDOOR|IFCWINDOW", "IFCDOORSTYLE", true)]
    [InlineData("(IFCDOOR|IFCWINDOW)", "IFCDOORSTYLE", false)]
    [InlineData("x\\", "x$", true)]
    public void The_audit_reads_a_pattern_its_own_way(string pattern, string value, bool expected)
    {
        Assert.Equal(expected, XsdPattern.ToAuditRegex(pattern)!.IsMatch(value));
    }

    [Theory]
    [InlineData("(a|b")]
    [InlineData("[a-")]
    [InlineData("a{2,1}")]
    public void What_the_audit_cannot_read_is_no_pattern(string pattern)
    {
        Assert.Null(XsdPattern.ToAuditRegex(pattern));
        Assert.Contains("E302", BimlintCompiler.Check("ifc IFC4\nrule \"x\"\n  for IfcWall\n  require attribute Name matches \"" + pattern + "\"").Codes());
    }

    [Theory]
    [InlineData("Ss_25", "Ss_25")]
    [InlineData("3.1", "3\\.1")]
    [InlineData("a-b", "a\\-b")]
    [InlineData("(x)", "\\(x\\)")]
    public void Text_is_escaped_to_a_pattern_that_matches_it(string text, string pattern)
    {
        Assert.Equal(pattern, XsdPattern.Escape(text));
        Assert.Matches(XsdPattern.ToRegex(pattern)!, text);
    }
}
