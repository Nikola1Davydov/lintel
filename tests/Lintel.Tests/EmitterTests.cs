using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Lintel.Tests;

/// <summary>The IDS that the compiler writes: the mapping of SPEC.md, section 7, construct by construct.</summary>
public class EmitterTests
{
    private static readonly XNamespace Ids = "http://standards.buildingsmart.org/IDS";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    /// <summary>Compiles a source and returns its first specification.</summary>
    private static XElement Specification(string rules, string header = "ifc IFC4")
    {
        var result = LintelCompiler.Compile(header + "\n\n" + rules);
        Assert.True(result.Check.Ok, result.Check.Describe());
        return XDocument.Parse(result.Ids!).Root!.Element(Ids + "specifications")!.Elements(Ids + "specification").First();
    }

    private static XElement Requirement(string line, string forLine = "for IfcWall") =>
        Specification($"rule \"x\"\n  {forLine}\n  {line}").Element(Ids + "requirements")!.Elements().Single();

    private static string? Simple(XElement? parent) => parent?.Element(Ids + "simpleValue")?.Value;

    private static List<string> Facets(XElement? parent, string facet) =>
        parent!.Element(Xs + "restriction")!.Elements(Xs + facet).Select(e => (string)e.Attribute("value")!).ToList();

    [Fact]
    public void A_class_is_written_with_its_subtypes()
    {
        var entity = Specification("rule \"x\"\n  for IfcWall\n  require attribute Name").Element(Ids + "applicability")!.Element(Ids + "entity")!;

        Assert.Equal(new[] { "IFCWALL", "IFCWALLELEMENTEDCASE", "IFCWALLSTANDARDCASE" }, Facets(entity.Element(Ids + "name"), "enumeration"));
        Assert.Equal("xs:string", (string)entity.Element(Ids + "name")!.Element(Xs + "restriction")!.Attribute("base")!);
    }

    [Fact]
    public void Only_and_a_class_without_subtypes_are_written_as_one_name()
    {
        var only = Specification("rule \"x\"\n  for only IfcWall\n  require attribute Name");
        Assert.Equal("IFCWALL", Simple(only.Element(Ids + "applicability")!.Element(Ids + "entity")!.Element(Ids + "name")));

        var space = Specification("rule \"x\"\n  for IfcSpace\n  require attribute Name");
        Assert.Equal("IFCSPACE", Simple(space.Element(Ids + "applicability")!.Element(Ids + "entity")!.Element(Ids + "name")));
    }

    [Fact]
    public void The_subtypes_of_every_version_of_the_rule_are_joined()
    {
        var both = Specification("rule \"x\"\n  for IfcWall\n  require attribute Name", "ifc IFC2X3, IFC4");
        Assert.Equal("IFC2X3 IFC4", (string)both.Attribute("ifcVersion")!);
        Assert.Equal(3, Facets(both.Element(Ids + "applicability")!.Element(Ids + "entity")!.Element(Ids + "name"), "enumeration").Count);

        var old = Specification("rule \"x\"\n  ifc IFC2X3\n  for IfcWall\n  require attribute Name");
        Assert.Equal("IFC2X3", (string)old.Attribute("ifcVersion")!);
        Assert.Equal(new[] { "IFCWALL", "IFCWALLSTANDARDCASE" }, Facets(old.Element(Ids + "applicability")!.Element(Ids + "entity")!.Element(Ids + "name"), "enumeration"));
    }

    [Fact]
    public void Predefined_types_patterns_and_no_class()
    {
        var typed = Specification("rule \"x\"\n  for IfcSlab type FLOOR, \"Podium\"\n  require attribute Name").Element(Ids + "applicability")!.Element(Ids + "entity")!;
        Assert.Equal(new[] { "FLOOR", "Podium" }, Facets(typed.Element(Ids + "predefinedType"), "enumeration"));

        var pattern = Specification("rule \"x\"\n  for matches \"IFC.*TYPE\" note \"every type\"\n  require attribute Name").Element(Ids + "applicability")!.Element(Ids + "entity")!;
        Assert.Equal(new[] { "IFC.*TYPE" }, Facets(pattern.Element(Ids + "name"), "pattern"));
        Assert.Equal("every type", pattern.Descendants(Xs + "documentation").Single().Value);

        var any = Specification("rule \"x\"\n  for any\n  where material = \"Beton\"\n  require attribute Name").Element(Ids + "applicability")!;
        Assert.Null(any.Element(Ids + "entity"));
        Assert.Equal("Beton", Simple(any.Element(Ids + "material")!.Element(Ids + "value")));
    }

    [Theory]
    [InlineData("require attribute Name", "0", "unbounded", 1)]
    [InlineData("require count >= 1", "1", "unbounded", 0)]
    [InlineData("require count >= 1\n  require attribute Name", "1", "unbounded", 1)]
    [InlineData("require count = 0", "0", "0", 0)]
    public void Count_becomes_the_occurrence_of_the_applicability(string requirements, string min, string max, int facets)
    {
        var specification = Specification($"rule \"x\"\n  for IfcWall\n  {requirements}");
        var applicability = specification.Element(Ids + "applicability")!;

        Assert.Equal(min, (string)applicability.Attribute("minOccurs")!);
        Assert.Equal(max, (string)applicability.Attribute("maxOccurs")!);
        Assert.Equal(facets, specification.Element(Ids + "requirements")?.Elements().Count() ?? 0);
    }

    [Fact]
    public void Metadata_of_the_file_and_of_the_rule()
    {
        var result = LintelCompiler.Compile("""
            title "Fire & safety"
            author "bim@example.com"
            date "2026-11-21"
            version "1.0"
            ifc IFC4

            rule "AIA 4.2.1" "Fire rating <walls>"
              source "Alle tragenden Wände müssen ..."
              description "why"
              instructions "how"
              for IfcWall
              require attribute Name
            """);
        var root = XDocument.Parse(result.Ids!).Root!;

        Assert.Equal(new[] { "title", "version", "author", "date" }, root.Element(Ids + "info")!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("Fire & safety", root.Element(Ids + "info")!.Element(Ids + "title")!.Value);
        var specification = root.Descendants(Ids + "specification").Single();
        Assert.Equal("Fire rating <walls>", (string)specification.Attribute("name")!);
        Assert.Equal("AIA 4.2.1", (string)specification.Attribute("identifier")!);
        Assert.Equal("why", (string)specification.Attribute("description")!);
        Assert.Equal("how", (string)specification.Attribute("instructions")!);
        Assert.Equal("Alle tragenden Wände müssen ...", (string)specification.Element(Ids + "requirements")!.Attribute("description")!);
        Assert.Equal("Untitled", XDocument.Parse(LintelCompiler.Compile("ifc IFC4\nrule \"x\"\n  for IfcWall\n  require attribute Name").Ids!).Descendants(Ids + "title").Single().Value);
    }

    [Fact]
    public void The_source_of_a_rule_without_requirements_is_kept()
    {
        var specification = Specification("rule \"x\"\n  source \"Keine Platzhalter.\"\n  for IfcBuildingElementProxy\n  require count = 0");

        var requirements = specification.Element(Ids + "requirements")!;
        Assert.Equal("Keine Platzhalter.", (string)requirements.Attribute("description")!);
        Assert.Empty(requirements.Elements());
    }

    [Theory]
    [InlineData("require attribute Name", "required")]
    [InlineData("optional attribute Name = \"x\"", "optional")]
    [InlineData("prohibit attribute Name", "prohibited")]
    public void Cardinality(string line, string expected)
    {
        Assert.Equal(expected, (string)Requirement(line).Attribute("cardinality")!);
    }

    [Fact]
    public void A_standard_property_gets_its_data_type_and_measures_are_converted_to_SI()
    {
        var rating = Requirement("require property Pset_WallCommon.FireRating matches \"F[0-9]{2}\"");
        Assert.Equal("IFCLABEL", (string)rating.Attribute("dataType")!);
        Assert.Equal("Pset_WallCommon", Simple(rating.Element(Ids + "propertySet")));
        Assert.Equal("FireRating", Simple(rating.Element(Ids + "baseName")));
        Assert.Equal(new[] { "F[0-9]{2}" }, Facets(rating.Element(Ids + "value"), "pattern"));

        var width = Requirement("require property Qto_WallBaseQuantities.Width between 150 mm and 40 cm");
        Assert.Equal("IFCLENGTHMEASURE", (string)width.Attribute("dataType")!);
        Assert.Equal("xs:double", (string)width.Descendants(Xs + "restriction").Single().Attribute("base")!);
        Assert.Equal(new[] { "0.15" }, Facets(width.Element(Ids + "value"), "minInclusive"));
        Assert.Equal(new[] { "0.4" }, Facets(width.Element(Ids + "value"), "maxInclusive"));

        var transmittance = Requirement("require property Pset_WallCommon.ThermalTransmittance <= 0.24 W/m2K");
        Assert.Equal("IFCTHERMALTRANSMITTANCEMEASURE", (string)transmittance.Attribute("dataType")!);
        Assert.Equal(new[] { "0.24" }, Facets(transmittance.Element(Ids + "value"), "maxInclusive"));

        Assert.Equal("true", Simple(Requirement("require property Pset_WallCommon.LoadBearing = true").Element(Ids + "value")));
        Assert.Null(Requirement("require property Pset_WallCommon.FireRating").Attribute("dataType"));
    }

    [Theory]
    [InlineData("require property ACME.Status = \"New\"", "IFCLABEL")]
    [InlineData("require property ACME.Status in [\"New\", \"Old\"]", "IFCLABEL")]
    [InlineData("require property ACME.Flag = false", "IFCBOOLEAN")]
    [InlineData("require property ACME.Count = 3", "IFCINTEGER")]
    [InlineData("require property ACME.Ratio <= 0.5", "IFCREAL")]
    [InlineData("require property ACME.Height datatype IfcLengthMeasure >= 2.2 m", "IFCLENGTHMEASURE")]
    [InlineData("require property ACME.Note datatype IfcText", "IFCTEXT")]
    [InlineData("optional property ACME.Note datatype IfcText", "IFCTEXT")]
    public void A_custom_property_gets_the_data_type_of_its_value_or_the_written_one(string line, string expected)
    {
        Assert.Equal(expected, (string)Requirement(line).Attribute("dataType")!);
    }

    [Fact]
    public void Bounds_lists_lengths_and_notes()
    {
        var bounds = Requirement("require property ACME.Ratio datatype IfcReal > 0 and <= 1 note \"a share\"");
        Assert.Equal(new[] { "0" }, Facets(bounds.Element(Ids + "value"), "minExclusive"));
        Assert.Equal(new[] { "1" }, Facets(bounds.Element(Ids + "value"), "maxInclusive"));
        Assert.Equal("a share", bounds.Descendants(Xs + "documentation").Single().Value);

        var patterns = Requirement("require attribute Name matches [\"A.*\", \"B.*\"] and length >= 2 and length <= 12");
        Assert.Equal(new[] { "A.*", "B.*" }, Facets(patterns.Element(Ids + "value"), "pattern"));
        Assert.Equal(new[] { "2" }, Facets(patterns.Element(Ids + "value"), "minLength"));
        Assert.Equal(new[] { "12" }, Facets(patterns.Element(Ids + "value"), "maxLength"));
        Assert.Equal(new[] { "4" }, Facets(Requirement("require attribute Name length = 4").Element(Ids + "value"), "length"));

        var named = Specification("define Status = [\"New\", \"Old\"]\nrule \"x\"\n  for IfcWall\n  require property ACME.Status in Status")
            .Element(Ids + "requirements")!.Elements().Single();
        Assert.Equal(new[] { "New", "Old" }, Facets(named.Element(Ids + "value"), "enumeration"));
    }

    [Fact]
    public void Names_as_lists_and_patterns()
    {
        var sets = Requirement("require property [Pset_WallCommon, Pset_SlabCommon].IsExternal = true", "for IfcWall, IfcSlab");
        Assert.Equal(new[] { "Pset_WallCommon", "Pset_SlabCommon" }, Facets(sets.Element(Ids + "propertySet"), "enumeration"));

        var names = Requirement("require attribute matches \".*Name\"", "for IfcSpace");
        Assert.Equal(new[] { ".*Name" }, Facets(names.Element(Ids + "name"), "pattern"));
    }

    [Fact]
    public void Classification_material_part_of_and_entity()
    {
        var classification = Requirement("require classification \"Uniclass 2015\" matches \"Ss_25.*\" uri \"https://example.org/uniclass\"");
        Assert.Equal(new[] { "value", "system" }, classification.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("Uniclass 2015", Simple(classification.Element(Ids + "system")));
        Assert.Equal("https://example.org/uniclass", (string)classification.Attribute("uri")!);

        var material = Requirement("require material in [\"Concrete\", \"Masonry\"] instructions \"ask the engineer\"");
        Assert.Equal(new[] { "Concrete", "Masonry" }, Facets(material.Element(Ids + "value"), "enumeration"));
        Assert.Equal("ask the engineer", (string)material.Attribute("instructions")!);
        Assert.Empty(Requirement("require material").Elements());

        var partOf = Requirement("require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure");
        Assert.Equal("IFCRELCONTAINEDINSPATIALSTRUCTURE", (string)partOf.Attribute("relation")!);
        Assert.Equal("IFCBUILDINGSTOREY", Simple(partOf.Element(Ids + "entity")!.Element(Ids + "name")));
        Assert.Equal("IFCRELVOIDSELEMENT IFCRELFILLSELEMENT", (string)Requirement("require part of IfcWall via IfcRelVoidsElement IfcRelFillsElement", "for IfcDoor").Attribute("relation")!);
        Assert.Null(Requirement("prohibit part of IfcSpace").Attribute("relation"));

        var entity = Requirement("require entity IfcBuildingElementProxy", "for any\n  where attribute Name = \"Nullpunkt\"");
        Assert.Null(entity.Attribute("cardinality"));
        Assert.Equal("IFCBUILDINGELEMENTPROXY", Simple(entity.Element(Ids + "name")));
    }

    [Fact]
    public void The_conditions_are_written_in_the_order_of_the_IDS_schema()
    {
        var applicability = Specification("""
            rule "x"
              for IfcWall
              where material = "Beton"
                and property ACME.A = "a"
                and attribute Name = "n"
                and classification "DIN 276" = "330"
                and part of IfcBuildingStorey
                and property ACME.B = "b"
              require attribute Tag
            """).Element(Ids + "applicability")!;

        Assert.Equal(
            new[] { "entity", "partOf", "classification", "attribute", "property", "property", "material" },
            applicability.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(new[] { "A", "B" }, applicability.Elements(Ids + "property").Select(p => Simple(p.Element(Ids + "baseName"))));
    }

    [Fact]
    public void A_source_with_errors_gives_no_IDS()
    {
        var result = LintelCompiler.Compile("ifc IFC4\nrule \"x\"\n  for IfcWal\n  require attribute Name");

        Assert.False(result.Check.Ok);
        Assert.Null(result.Ids);
    }

    public static IEnumerable<object[]> Sources()
    {
        var root = TestSupport.RepositoryRoot;
        foreach (var path in Directory.GetFiles(Path.Combine(root, "examples"), "*.lintel"))
        {
            yield return new object[] { Path.GetFileName(path), File.ReadAllText(path) };
        }

        // The rule files that language models wrote in experiment 2.
        foreach (var path in Directory.GetFiles(Path.Combine(root, "docs", "experiments", "02-writing-with-the-description", "raw"), "answer-*.md"))
        {
            var code = Regex.Match(File.ReadAllText(path), "```[a-z]*\n(.*?)```", RegexOptions.Singleline).Groups[1].Value;
            yield return new object[] { Path.GetFileName(path), code };
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void What_the_compiler_writes_passes_the_official_audit(string name, string source)
    {
        var result = LintelCompiler.Compile(source);
        Assert.True(result.Check.Ok, name + "\n" + result.Check.Describe());

        var problems = IdsXml.Audit(result.Ids!);

        Assert.True(problems.Count == 0, name + "\n" + string.Join("\n", problems));
    }

    [Fact]
    public void An_IDS_file_goes_to_lintel_and_back()
    {
        var first = LintelCompiler.Compile(File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot, "examples", "showcase.lintel"))).Ids!;

        var imported = LintelCompiler.Import(first);
        var second = LintelCompiler.Compile(imported.Source);

        Assert.Empty(imported.Notes);
        Assert.True(second.Check.Ok, second.Check.Describe());
        Assert.Equal(IdsXml.Canonical(first), IdsXml.Canonical(second.Ids!));
    }
}
