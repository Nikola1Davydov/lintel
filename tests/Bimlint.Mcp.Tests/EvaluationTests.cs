using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Bimlint.Mcp.Tests;

/// <summary>
/// The questions of evaluation.xml, which test whether a language model can use the server. Each answer is
/// worked out here with the tools, so that the file cannot go stale.
/// </summary>
public class EvaluationTests
{
    private static readonly Dictionary<string, Func<string>> Answers = new()
    {
        // the data type of the property that marks a wall as load-bearing
        ["IfcBoolean"] = () => Regex.Match(Lookup("IfcWall", search: "load bearing"), @"Pset_WallCommon\.LoadBearing: (\w+)").Groups[1].Value,

        // the net height of a storey, as the standard spells it
        ["NetHeigtht"] = () => Regex.Match(Lookup("IfcBuildingStorey"), @"Qto_BuildingStoreyBaseQuantities: .*?(Net\w*H\w*t)\b").Groups[1].Value,

        // the predefined types of a slab
        ["6"] = () => Regex.Match(Lookup("IfcSlab"), @"type FLOOR': ([^.]*)\.").Groups[1].Value.Split(',').Length.ToString(),

        // walls of IFC2X3 have no predefined type
        ["No"] = () => Lookup("IfcWall", ifc: "IFC2X3").Contains("No predefined type.") ? "No" : "Yes",

        // the sets with a property AcousticRating
        ["8"] = () => Lookup(search: "AcousticRating").Split('\n').Count(l => l.Contains(".AcousticRating:")).ToString(),

        // the class names of 'for IfcWall'
        ["3"] = () => Regex.Matches(Compile("ifc IFC4\nrule \"x\"\n  for IfcWall\n  require attribute Name"), "<xs:enumeration value=\"IFCWALL").Count.ToString(),

        // a measure without its unit
        ["E409"] = () => JsonDocument.Parse(Text(RuleTools.ValidateRules("ifc IFC4\nrule \"x\"\n  for IfcDoor\n  require attribute OverallHeight >= 2.1")))
            .RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString()!,

        // the data type that the compiler writes for the thermal transmittance
        ["IFCTHERMALTRANSMITTANCEMEASURE"] = () => Regex.Match(
            Compile("ifc IFC4\nrule \"x\"\n  for IfcWall\n  require property Pset_WallCommon.ThermalTransmittance <= 0.24 W/m2K"),
            "dataType=\"(\\w+)\"").Groups[1].Value,

        // no set for walls has the property
        ["None"] = () => Lookup("IfcWall", search: "HandicapAccessible").StartsWith("No property of the sets for IfcWall") ? "None" : "?",

        // the keyword of the guide
        ["only"] = () => Regex.Match(RuleTools.LanguageGuide(), @"`for (\w+) IfcWall` \| exactly this class").Groups[1].Value,
    };

    [Fact]
    public void Every_answer_of_the_evaluation_is_what_the_tools_say()
    {
        var path = Path.Combine(Repository.Root, "src", "Bimlint.Mcp", "evaluation.xml");
        var pairs = XDocument.Load(path).Root!.Elements("qa_pair").ToList();

        Assert.Equal(10, pairs.Count);
        foreach (var pair in pairs)
        {
            var answer = pair.Element("answer")!.Value;
            Assert.True(Answers.ContainsKey(answer), $"No check for the answer '{answer}'.");
            Assert.Equal(answer, Answers[answer]());
            Assert.False(string.IsNullOrWhiteSpace(pair.Element("question")!.Value));
        }

        Assert.Equal(pairs.Count, pairs.Select(p => p.Element("answer")!.Value).Distinct().Count());
    }

    private static string Lookup(string? ifcClass = null, string? search = null, string ifc = "IFC4") =>
        SchemaTools.Describe(ifcClass, null, search, ifc, 200, 0);

    private static string Compile(string source) => Text(RuleTools.CompileRules(source));

    private static string Text(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.Content.OfType<TextContentBlock>().First().Text;
    }
}
