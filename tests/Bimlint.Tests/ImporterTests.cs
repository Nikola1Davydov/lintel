using System.Linq;
using Bimlint.Ids;
using Xunit;

namespace Bimlint.Tests;

/// <summary>Reading IDS: what is written as which Bimlint, what comes back unchanged, and what is reported as not kept.</summary>
public class ImporterTests
{
    private const string Location = " xsi:schemaLocation=\"http://standards.buildingsmart.org/IDS http://standards.buildingsmart.org/IDS/1.0/ids.xsd\"";

    /// <summary>An IDS file for IFC4 with one specification: walls, and the given requirements.</summary>
    private static string Ids(string requirements, string applicability = "<entity><name><simpleValue>IFCWALL</simpleValue></name></entity>", string location = Location) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <ids xmlns="http://standards.buildingsmart.org/IDS" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"{location}>
          <info><title>t</title></info>
          <specifications>
            <specification name="s" ifcVersion="IFC4">
              <applicability minOccurs="0" maxOccurs="unbounded">{applicability}</applicability>
              <requirements>{requirements}</requirements>
            </specification>
          </specifications>
        </ids>
        """;

    private static string Property(string dataType, string value, string cardinality = "required") =>
        $"<property dataType=\"{dataType}\" cardinality=\"{cardinality}\"><propertySet><simpleValue>ACME</simpleValue></propertySet><baseName><simpleValue>P</simpleValue></baseName><value>{value}</value></property>";

    private static string Restriction(string xmlBase, string facets) => $"<xs:restriction base=\"{xmlBase}\">{facets}</xs:restriction>";

    /// <summary>Imports a file that the official audit accepts; it must come back unchanged, without notes.</summary>
    private static string Kept(string ids)
    {
        Assert.Empty(IdsXml.Audit(ids));
        var imported = BimlintCompiler.Import(ids);
        Assert.True(imported.Notes.Count == 0, string.Join("\n", imported.Notes.Select(n => n.Message)));

        var compiled = BimlintCompiler.Compile(imported.Source);
        Assert.True(compiled.Check.Ok, compiled.Check.Describe());
        Assert.Equal(IdsCanonical.Of(ids), IdsCanonical.Of(compiled.Ids!));
        return imported.Source;
    }

    [Theory]
    [InlineData("IFCDATE", "<xs:minInclusive value=\"2026-01-01\"/>", "xs:date", "require property ACME.P datatype IfcDate >= \"2026-01-01\"")]
    [InlineData("IFCDATE", "<xs:minInclusive value=\"2026-01-01\"/><xs:maxInclusive value=\"2026-12-31\"/>", "xs:date", "between \"2026-01-01\" and \"2026-12-31\"")]
    [InlineData("IFCDURATION", "<xs:enumeration value=\"P1D\"/><xs:enumeration value=\"PT2H\"/>", "xs:duration", "in [\"P1D\", \"PT2H\"]")]
    [InlineData("IFCLENGTHMEASURE", "<xs:minExclusive value=\"0\"/><xs:maxInclusive value=\"2.5\"/>", "xs:double", "> 0 m and <= 2.5 m")]
    [InlineData("IFCLENGTHMEASURE", "<xs:pattern value=\"1.*\"/>", "xs:double", "matches \"1.*\"")]
    [InlineData("IFCREAL", "<xs:enumeration value=\"NaN\"/><xs:enumeration value=\"1.5\"/>", "xs:double", "in [\"NaN\", 1.5]")]
    [InlineData("IFCINTEGER", "<xs:minInclusive value=\"+3\"/>", "xs:integer", ">= +3")]
    [InlineData("IFCBOOLEAN", "<xs:pattern value=\"t.*\"/>", "xs:boolean", "datatype IfcBoolean matches \"t.*\"")]
    [InlineData("IFCLABEL", "<xs:minLength value=\"2\"/><xs:maxLength value=\"12\"/>", "xs:string", "length >= 2 and length <= 12")]
    [InlineData("IFCLABEL", "<xs:pattern value=\"^F[0-9]{2}$\"/>", "xs:string", "matches \"^F[0-9]{2}$\"")]
    [InlineData("IFCLABEL", "<xs:pattern value=\"(?i)f90\"/>", "xs:string", "matches \"(?i)f90\"")]
    public void A_restriction_comes_back_unchanged(string dataType, string facets, string xmlBase, string expected)
    {
        var source = Kept(Ids(Property(dataType, Restriction(xmlBase, facets))));

        Assert.Contains(expected, source);
    }

    [Theory]
    [InlineData("IFCBOOLEAN", "1", "= \"1\"")]
    [InlineData("IFCBOOLEAN", "true", "= true")]
    [InlineData("IFCREAL", "NaN", "= \"NaN\"")]
    [InlineData("IFCREAL", "1e3", "= 1e3")]
    [InlineData("IFCREAL", ".5", "= .5")]
    [InlineData("IFCLENGTHMEASURE", "0.150", "= 0.150 m")]
    [InlineData("IFCDATETIME", "2026-11-22T10:00:00Z", "= \"2026-11-22T10:00:00Z\"")]
    [InlineData("IFCLABEL", "a \"quoted\" word", "= \"a \"\"quoted\"\" word\"")]
    [InlineData("IFCLABEL", "  padded  ", "= \"  padded  \"")]
    public void A_value_comes_back_digit_for_digit(string dataType, string value, string expected)
    {
        var source = Kept(Ids(Property(dataType, $"<simpleValue>{System.Security.SecurityElement.Escape(value)}</simpleValue>")));

        Assert.Contains(expected, source);
    }

    [Fact]
    public void Names_that_IDS_does_not_check_are_kept_with_a_warning()
    {
        // an abstract class named exactly, a property that the standard set does not have in a prohibited
        // requirement, a quantity set of one's own: the official audit accepts all three
        var source = Kept(Ids(
            "<property cardinality=\"prohibited\"><propertySet><simpleValue>Pset_WallCommon</simpleValue></propertySet><baseName><simpleValue>Nope</simpleValue></baseName></property>"
            + "<property><propertySet><simpleValue>Qto_Own</simpleValue></propertySet><baseName><simpleValue>X</simpleValue></baseName></property>",
            "<entity><name><simpleValue>IFCBUILDINGELEMENT</simpleValue></name></entity>"));

        Assert.Contains("for only IfcBuildingElement", source);
        var codes = BimlintCompiler.Check(source).Codes();
        Assert.Contains("W508", codes);
        Assert.Contains("W511", codes);
    }

    [Theory]
    [InlineData("<classification><system><xs:restriction base=\"xs:string\"><xs:minLength value=\"3\"/></xs:restriction></system></classification>", "A name cannot be restricted with 'minLength'")]
    [InlineData("<material><value><xs:restriction base=\"xs:integer\"><xs:minInclusive value=\"1\"/></xs:restriction></value></material>", "The restriction has the base xs:integer")]
    [InlineData("<material><value><xs:restriction base=\"xs:string\"><xs:enumeration value=\"a\"><xs:annotation><xs:documentation>first</xs:documentation></xs:annotation></xs:enumeration></xs:restriction></value></material>", "The annotation of the single value")]
    public void What_cannot_be_kept_is_reported(string requirement, string note)
    {
        var ids = Ids(requirement);
        Assert.Empty(IdsXml.Audit(ids));

        var imported = BimlintCompiler.Import(ids);

        Assert.Contains(imported.Notes, n => n.Code == "W701" && n.Message.StartsWith(note));
    }

    [Theory]
    [InlineData("", "does not say which version")]
    [InlineData(" xsi:schemaLocation=\"http://standards.buildingsmart.org/IDS http://standards.buildingsmart.org/IDS/0.9.7/ids.xsd\"", "it is read as IDS 1.0")]
    public void A_file_of_another_version_of_IDS_is_reported(string location, string note)
    {
        var imported = BimlintCompiler.Import(Ids("<attribute><name><simpleValue>Name</simpleValue></name></attribute>", location: location));

        Assert.Contains(imported.Notes, n => n.Message.Contains(note));
    }

    [Fact]
    public void A_file_that_does_not_come_back_unchanged_says_so()
    {
        // two values where IDS allows one: the reader takes the first, and its own check finds the difference
        var ids = Ids("<attribute><name><simpleValue>Name</simpleValue></name><value><simpleValue>a</simpleValue></value><value><simpleValue>b</simpleValue></value></attribute>");

        var imported = BimlintCompiler.Import(ids);

        Assert.Contains(imported.Notes, n => n.Message.StartsWith("The file does not come back from Bimlint unchanged"));
    }

    [Fact]
    public void A_class_list_is_folded_into_the_class_with_its_subtypes()
    {
        var folded = Kept(Ids(
            "<attribute><name><simpleValue>Name</simpleValue></name></attribute>",
            "<entity><name><xs:restriction base=\"xs:string\"><xs:enumeration value=\"IFCWALL\"/><xs:enumeration value=\"IFCWALLELEMENTEDCASE\"/><xs:enumeration value=\"IFCWALLSTANDARDCASE\"/></xs:restriction></name></entity>"));
        Assert.Contains("for IfcWall\n", folded);

        var exact = Kept(Ids(
            "<attribute><name><simpleValue>Name</simpleValue></name></attribute>",
            "<entity><name><xs:restriction base=\"xs:string\"><xs:enumeration value=\"IFCWALL\"/><xs:enumeration value=\"IFCWALLSTANDARDCASE\"/></xs:restriction></name></entity>"));
        Assert.Contains("for only IfcWall, IfcWallStandardCase", exact);
    }
}
