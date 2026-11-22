using System.Linq;
using Xunit;

namespace Lintel.Mcp.Tests;

/// <summary>The lookup of the IFC schema: what an author gets for the questions an author has.</summary>
public class SchemaToolTests
{
    private static string Describe(string? ifcClass = null, string? propertySet = null, string? search = null, string ifc = "IFC4", int limit = 60, int offset = 0) =>
        SchemaTools.Describe(ifcClass, propertySet, search, ifc, limit, offset);

    [Fact]
    public void A_class_shows_what_a_rule_can_say_about_it()
    {
        var door = Describe("IfcDoor");

        Assert.StartsWith("IfcDoor in IFC4\n", door);
        Assert.Contains("Subtypes, which 'for IfcDoor' includes: IfcDoorStandardCase.", door);
        Assert.Contains("DOOR, GATE, TRAPDOOR, USERDEFINED, NOTDEFINED", door);
        Assert.Contains("OverallHeight (IfcPositiveLengthMeasure, unit m, cm, mm)", door);
        Assert.Contains("Name (IfcLabel)", door);

        // the sets made for doors come with their properties, the sets of every element by name
        Assert.Contains("  Pset_DoorCommon: ", door);
        Assert.Contains("FireRating", door);
        Assert.Contains("  Qto_DoorBaseQuantities: Area, Height, Perimeter, Width", door);
        Assert.Contains("Pset_Warranty (8)", door);
        Assert.DoesNotContain("  Pset_Warranty: ", door);
        Assert.InRange(door.Length, 500, 3000);
    }

    [Fact]
    public void A_class_is_found_in_any_case_and_an_abstract_class_says_so()
    {
        Assert.StartsWith("IfcWall in IFC4\n", Describe("ifcwall"));

        var element = Describe("IfcElement");
        Assert.Contains("Abstract: 'for IfcElement' selects its", element);
        Assert.Contains("No predefined type.", element);
        Assert.Contains("Pset_Condition", element);
    }

    [Fact]
    public void A_version_has_its_own_classes_and_sets()
    {
        Assert.Contains("No predefined type.", Describe("IfcWall", ifc: "IFC2X3"));
        Assert.StartsWith("IfcBuiltElement in IFC4X3_ADD2\n", Describe("IfcBuiltElement", ifc: "IFC4X3"));

        var missing = Assert.Throws<ToolProblem>(() => Describe("IfcBuiltElement"));
        Assert.Contains("It exists in IFC4X3_ADD2.", missing.Message);
    }

    [Fact]
    public void A_property_set_shows_data_types_units_and_allowed_values()
    {
        var common = Describe(propertySet: "Pset_WallCommon");

        Assert.StartsWith("Pset_WallCommon in IFC4, made for IfcWall with subtypes and type objects.\n", common);
        Assert.Contains("Pset_WallCommon.FireRating: IfcLabel, text in quotes. Meaning: Fire rating given according to the national fire safety classification", common);
        Assert.Contains("Pset_WallCommon.LoadBearing: IfcBoolean, true or false. Meaning: ", common);
        Assert.Contains("Pset_WallCommon.ThermalTransmittance: IfcThermalTransmittanceMeasure, a number with its unit: W/m2K. Meaning: ", common);
        Assert.Contains("Pset_WallCommon.Status: IfcLabel, one of \"NEW\", \"EXISTING\", \"DEMOLISH\", \"TEMPORARY\", \"OTHER\", \"NOTKNOWN\", \"UNSET\". Meaning: ", common);

        var quantities = Describe(propertySet: "Qto_WallBaseQuantities");
        Assert.Contains("Qto_WallBaseQuantities.Width: IfcLengthMeasure, a number with its unit: m, cm, mm. Meaning: ", quantities);
        Assert.Contains("Qto_WallBaseQuantities.NetVolume: IfcVolumeMeasure, a number with its unit: m3. Meaning: ", quantities);

        // every property on one line, and no line too long to read
        var lines = common.Split('\n').Where(l => l.StartsWith("Pset_WallCommon.")).ToList();
        Assert.Equal(11, lines.Count);
        Assert.All(lines, line => Assert.InRange(line.Length, 30, 420));
    }

    [Fact]
    public void Every_line_of_a_property_set_is_a_property_that_the_checker_accepts()
    {
        // What the tool tells an author must hold when the author writes it.
        foreach (var name in new[] { "Pset_WallCommon", "Pset_DoorCommon", "Qto_SlabBaseQuantities", "Pset_SpaceCommon" })
        {
            var lines = Describe(propertySet: name).Split('\n').Where(l => l.StartsWith(name + ".")).ToList();
            Assert.NotEmpty(lines);
            var first = Describe(propertySet: name).Split('\n')[0];
            var made = first.Substring(first.IndexOf("made for ") + 9).Split(' ', ',')[0];

            var source = "ifc IFC4\nrule \"x\"\n  for " + made + "\n" + string.Join("\n", lines.Select(l => "  require property " + l.Substring(0, l.IndexOf(':'))));
            var check = LintelCompiler.Check(source);
            Assert.True(check.Ok, name + ": " + string.Join("; ", check.Diagnostics.Select(d => d.ToString())));
        }
    }

    [Fact]
    public void A_set_that_is_made_for_another_class_says_so()
    {
        var answer = Describe("IfcWall", "Pset_DoorCommon");

        Assert.Contains("It is NOT made for IfcWall", answer);
        Assert.Contains("Pset_WallCommon", answer);
    }

    [Fact]
    public void A_search_finds_properties_by_parts_of_their_names()
    {
        var fire = Describe("IfcDoor", search: "fire");
        Assert.StartsWith("Properties for IfcDoor in IFC4 that match 'fire' (2):\n", fire);
        Assert.Contains("Pset_DoorCommon.FireRating: IfcLabel, text in quotes. Meaning: Fire rating for this object\n", fire);
        Assert.Contains("Pset_DoorCommon.FireExit: IfcBoolean, true or false. Meaning: ", fire);

        // several words must all be found, in any case
        var bearing = Describe("IfcWall", search: "LOAD bearing");
        Assert.Contains("Pset_WallCommon.LoadBearing: IfcBoolean", bearing);
        Assert.DoesNotContain("FireRating", bearing);

        // without a class: every set of the version
        var everywhere = Describe(search: "FireRating");
        Assert.Contains("Pset_WallCommon.FireRating", everywhere);
        Assert.Contains("Pset_DoorCommon.FireRating", everywhere);
    }

    [Fact]
    public void A_search_finds_a_name_that_the_standard_misspells()
    {
        // The net height of a storey is 'NetHeigtht' in the IFC4 standard. An author looks for 'height'.
        var height = Describe("IfcBuildingStorey", search: "net height");

        Assert.Contains("Qto_BuildingStoreyBaseQuantities.NetHeigtht: IfcLengthMeasure", height);

        // the odd name is no mistake of the tool: the line says where the other spelling is found
        Assert.Contains("Written exactly so in IFC4 (IFC4X3_ADD2: NetHeight)", height);
        Assert.Contains("Standard net height of this storey", height);

        // the overview of the class marks the name too
        var storey = Describe("IfcBuildingStorey");
        Assert.Contains("NetHeigtht (sic)", storey);
        Assert.Contains("(sic): the standard writes this name exactly so", storey);

        // the same for a name that one version writes the American and the other the British way
        Assert.Contains("GlassColor (sic)", Describe("IfcDoor"));
        Assert.Contains("Written exactly so in IFC4 (IFC4X3_ADD2: GlassColour)", Describe(propertySet: "Pset_DoorWindowGlazingType"));
        Assert.Contains("Written exactly so in IFC4X3_ADD2 (IFC2X3: GlassColor)", Describe(propertySet: "Pset_DoorWindowGlazingType", ifc: "IFC4X3_ADD2"));

        // a set that every version spells the same has no such remark
        Assert.DoesNotContain("Written exactly so", Describe(propertySet: "Pset_WallCommon"));

        // a match by the letters comes first, and a short word finds nothing by accident
        var any = Describe("IfcBuildingStorey", search: "height");
        Assert.True(any.IndexOf("GrossHeight") < any.IndexOf("NetHeigtht"));
        Assert.DoesNotContain("Written exactly so", Describe("IfcWall", search: "fire"));

        // the version that spells it right says nothing about it in the overview of the other
        Assert.Contains("NetHeight: IfcLengthMeasure", Describe("IfcBuildingStorey", search: "net height", ifc: "IFC4X3_ADD2"));
    }

    [Fact]
    public void A_long_page_leaves_the_meanings_out()
    {
        var many = Describe(search: "fire");

        Assert.DoesNotContain("Meaning: ", many);
        Assert.Contains("Narrow the search, or give 'propertySet', to see what each property means.", many);
    }

    [Fact]
    public void A_logical_value_is_not_called_true_or_false()
    {
        // IDS takes IfcLogical as text and does not say how its values are spelled.
        var storey = Describe(propertySet: "Pset_BuildingStoreyCommon");

        Assert.Contains("Pset_BuildingStoreyCommon.AboveGround: IfcLogical, true, false or unknown, which IDS compares as text", storey);
        Assert.Contains("Pset_BuildingStoreyCommon.EntranceLevel: IfcBoolean, true or false.", storey);
    }

    [Fact]
    public void A_search_without_a_match_says_what_to_do()
    {
        var german = Describe("IfcWall", search: "Brandschutz");
        Assert.Contains("No property of the standard matches 'Brandschutz' for IfcWall in IFC4.", german);
        Assert.Contains("a property set of your own", german);

        // a property of another class: named, and marked as not usable here
        var elsewhere = Describe("IfcWall", search: "HandicapAccessible");
        Assert.Contains("No property of the sets for IfcWall in IFC4 matches", elsewhere);
        Assert.Contains("Pset_DoorCommon.HandicapAccessible", elsewhere);
        Assert.Contains("they cannot be required for IfcWall", elsewhere);
    }

    [Fact]
    public void A_long_answer_comes_in_pages()
    {
        var first = Describe(search: "Rating", limit: 5);
        Assert.Equal(5, first.Split('\n').Count(l => l.Contains(": Ifc")));
        Assert.Contains("Lines 1 to 5 of ", first);
        Assert.Contains("For the next page give offset: 5.", first);

        var second = Describe(search: "Rating", limit: 5, offset: 5);
        Assert.Contains("Lines 6 to 10 of ", second);
        Assert.Empty(first.Split('\n').Where(l => l.Contains(": Ifc")).Intersect(second.Split('\n').Where(l => l.Contains(": Ifc"))));

        Assert.Contains("Nothing from line", Describe(search: "Rating", limit: 5, offset: 10_000));
    }

    [Fact]
    public void Something_must_be_asked()
    {
        var nothing = Assert.Throws<ToolProblem>(() => Describe());

        Assert.Contains("Give a 'class'", nothing.Message);
    }
}
