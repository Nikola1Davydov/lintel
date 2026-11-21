using System.Linq;
using Lintel.Model;
using Xunit;

namespace Lintel.Tests;

public class ParserTests
{
    private static RuleFile Parse(string source)
    {
        var result = LintelCompiler.Check(source);
        Assert.True(result.Diagnostics.Count == 0, result.Describe());
        return result.File;
    }

    private static Rule ParseRule(string rule) => Parse(TestSupport.WithHeader(rule)).Rules.Single();

    [Fact]
    public void Header()
    {
        var file = Parse("""
            title "Fire protection"
            author "bim@example.com"
            date "2026-11-21"
            ifc IFC2X3, IFC4
            import "lists.lintel"

            rule "x"
              for IfcWall
              require attribute Name
            """);

        Assert.Equal("Fire protection", file.Header.Get("title"));
        Assert.Equal("bim@example.com", file.Header.Get("author"));
        Assert.Equal(new[] { "IFC2X3", "IFC4" }, file.Header.Ifc);
        Assert.Equal("lists.lintel", file.Header.Imports.Single().Value);
    }

    [Fact]
    public void Rule_header_with_and_without_identifier()
    {
        var file = Parse(TestSupport.WithHeader("""
            rule "No identifier"
              for IfcWall
              require attribute Name
            rule R1 "Name identifier"
              for IfcWall
              require attribute Name
            rule "AIA 4.2.1" "Text identifier"
              for IfcWall
              require attribute Name
            """));

        var rules = file.Rules.ToList();
        Assert.Equal((null, "No identifier"), (rules[0].Id, rules[0].Name));
        Assert.Equal(("R1", "Name identifier"), (rules[1].Id, rules[1].Name));
        Assert.Equal(("AIA 4.2.1", "Text identifier"), (rules[2].Id, rules[2].Name));
    }

    [Fact]
    public void Rule_metadata()
    {
        var rule = ParseRule("""
            rule R1 "Fire rating"
              source "AIA 4.2.1: Alle tragenden Wände ..."
              description "why"
              instructions "how"
              ifc IFC4X3_ADD2
              for IfcWall
              require attribute Name
            """);

        Assert.Equal("AIA 4.2.1: Alle tragenden Wände ...", rule.Source);
        Assert.Equal("why", rule.Description);
        Assert.Equal("how", rule.Instructions);
        Assert.Equal(new[] { "IFC4X3_ADD2" }, rule.Ifc);
    }

    [Fact]
    public void Classes_with_only_and_predefined_types()
    {
        var rule = ParseRule("""
            rule "x"
              for only IfcSlab, IfcRoof type FLOOR, "Podium"
              require attribute Name
            """);

        var entity = rule.Entity!;
        Assert.True(entity.Classes.Only);
        Assert.Equal(new[] { "IfcSlab", "IfcRoof" }, entity.Classes.Names.Select(n => n.Text));
        Assert.Equal(new[] { ("FLOOR", false), ("Podium", true) }, entity.Types!.Names.Select(n => (n.Text, n.Quoted)));
    }

    [Fact]
    public void For_any_with_conditions()
    {
        var rule = ParseRule("""
            rule "x"
              for any
              where classification "Uniclass 2015" matches "Ss_25.*"
                and material = "Concrete"
              require attribute Name
            """);

        Assert.Null(rule.Entity);
        Assert.Collection(
            rule.Where,
            facet =>
            {
                var classification = Assert.IsType<ClassificationFacet>(facet);
                Assert.Equal("Uniclass 2015", classification.System.Names.Single().Text);
                var matches = Assert.IsType<MatchesTerm>(classification.Value!.Terms.Single());
                Assert.Equal("Ss_25.*", matches.Patterns.Single().Value);
            },
            facet => Assert.Equal("Concrete", Assert.IsType<StringLiteral>(Assert.IsType<MaterialFacet>(facet).Value!.Equal).Value));
    }

    [Fact]
    public void Property_with_datatype_and_bound_with_unit()
    {
        var rule = ParseRule("""
            rule "x"
              for IfcSpace
              require property ACME_Fire.ClearHeight datatype IfcLengthMeasure >= 2.2 m
            """);

        var property = Assert.IsType<PropertyFacet>(rule.Requirements.Single().Facet);
        Assert.Equal("ACME_Fire", property.PropertySet.Names.Single().Text);
        Assert.Equal("ClearHeight", property.Name.Names.Single().Text);
        Assert.Equal("IfcLengthMeasure", property.DataType);
        var bound = Assert.IsType<BoundTerm>(property.Value!.Terms.Single());
        Assert.Equal(BoundKind.GreaterOrEqual, bound.Kind);
        Assert.Equal(("2.2", "m"), (bound.Value.Text, bound.Value.Unit));
    }

    [Fact]
    public void Constraint_terms()
    {
        var rule = ParseRule("""
            define Status = ["New", "Existing"]
            rule "x"
              for IfcWall
              require property A.B between 150 mm and 400 mm
              require property A.C > 0 m2 and <= 500 m2
              require property A.D in Status
              require property A.E in [1, 2, 3]
              require attribute Name matches ["A.*", "B.*"] and length <= 12 note "naming scheme"
              require property A.F = true
              require property A.G <= 0.24 W/m2K
            """);

        var values = rule.Requirements.Select(r => r.Facet).Select(f => f is PropertyFacet p ? p.Value! : ((AttributeFacet)f).Value!).ToList();

        var between = Assert.IsType<BetweenTerm>(values[0].Terms.Single());
        Assert.Equal(("150", "mm", "400", "mm"), (between.Min.Text, between.Min.Unit, between.Max.Text, between.Max.Unit));

        Assert.Equal(new[] { BoundKind.Greater, BoundKind.LessOrEqual }, values[1].Terms.Cast<BoundTerm>().Select(b => b.Kind));
        Assert.Equal("Status", Assert.IsType<InTerm>(values[2].Terms.Single()).ListName);
        Assert.Equal(new[] { "1", "2", "3" }, Assert.IsType<InTerm>(values[3].Terms.Single()).Values!.Cast<NumberLiteral>().Select(n => n.Text));

        Assert.Equal(2, values[4].Terms.Count);
        Assert.Equal(new[] { "A.*", "B.*" }, Assert.IsType<MatchesTerm>(values[4].Terms[0]).Patterns.Select(p => p.Value));
        var length = Assert.IsType<LengthTerm>(values[4].Terms[1]);
        Assert.Equal((LengthKind.AtMost, "12"), (length.Kind, length.Value));
        Assert.Equal("naming scheme", values[4].Note);

        Assert.True(Assert.IsType<BooleanLiteral>(values[5].Equal).Value);
        Assert.Equal("W/m2K", Assert.IsType<BoundTerm>(values[6].Terms.Single()).Value.Unit);
    }

    [Fact]
    public void Requirements_with_cardinality_uri_and_instructions()
    {
        var rule = ParseRule("""
            rule "x"
              for IfcDoor
              require property Pset_DoorCommon.FireRating
                uri "https://example.org/FireRating"
                instructions "ask the fire engineer"
              optional property Pset_DoorCommon.AcousticRating in ["Rw 32 dB"]
              prohibit material matches ".*[Aa]sbest.*"
              require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure
              prohibit part of IfcWall via IfcRelVoidsElement IfcRelFillsElement
              require entity IfcDoor type DOOR
            """);

        Assert.Equal(
            new[] { Cardinality.Required, Cardinality.Optional, Cardinality.Prohibited, Cardinality.Required, Cardinality.Prohibited, Cardinality.Required },
            rule.Requirements.Select(r => r.Cardinality));
        Assert.Equal("https://example.org/FireRating", rule.Requirements[0].Uri);
        Assert.Equal("ask the fire engineer", rule.Requirements[0].Instructions);
        Assert.Equal("IfcRelContainedInSpatialStructure", Assert.IsType<PartOfFacet>(rule.Requirements[3].Facet).Relation);
        Assert.Equal("IfcRelVoidsElement IfcRelFillsElement", Assert.IsType<PartOfFacet>(rule.Requirements[4].Facet).Relation);
        Assert.Equal("DOOR", Assert.IsType<EntityFacet>(rule.Requirements[5].Facet).Entity.Types!.Names.Single().Text);
    }

    [Theory]
    [InlineData("require count >= 1", RuleCount.AtLeastOne)]
    [InlineData("require count = 0", RuleCount.None)]
    public void Count(string statement, RuleCount expected)
    {
        var rule = ParseRule($"rule \"x\"\n  for IfcBuildingStorey\n  {statement}");

        Assert.Equal(expected, rule.Count);
        Assert.Empty(rule.Requirements);
    }

    [Fact]
    public void Names_as_lists_and_patterns()
    {
        var rule = ParseRule("""
            rule "x"
              for matches "IFC.*TYPE" note "every type object"
              where property [Pset_WallCommon, "My Set"].IsExternal = true
                and attribute matches ".*Name"
              require property matches "Pset_.*Common".FireRating
            """);

        Assert.Equal(MatcherKind.Pattern, rule.Entity!.Classes.Kind);
        Assert.Equal("every type object", rule.Entity.Classes.Note);
        var first = Assert.IsType<PropertyFacet>(rule.Where[0]);
        Assert.Equal(new[] { ("Pset_WallCommon", false), ("My Set", true) }, first.PropertySet.Names.Select(n => (n.Text, n.Quoted)));
        Assert.Equal(MatcherKind.Pattern, Assert.IsType<AttributeFacet>(rule.Where[1]).Name.Kind);
        Assert.Equal(MatcherKind.Pattern, Assert.IsType<PropertyFacet>(rule.Requirements.Single().Facet).PropertySet.Kind);
    }

    [Fact]
    public void Layout_has_no_meaning()
    {
        var oneLine = ParseRule("rule R1 \"x\" for IfcWall where property A.B = true and property A.C = 1 require attribute Name require attribute Tag");

        Assert.Equal(2, oneLine.Where.Count);
        Assert.Equal(2, oneLine.Requirements.Count);
    }
}
