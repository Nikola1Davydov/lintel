using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintel.Mcp.Tests;

/// <summary>The server over the protocol: what it announces, and the path of an author from a requirement to an IDS file.</summary>
[Collection("server")]
public class ServerTests
{
    private static readonly string[] Tools =
    {
        "lintel_language_guide", "lintel_list_properties", "lintel_validate_rules", "lintel_compile_rules", "lintel_import_ids",
    };

    private readonly Server _server;

    public ServerTests(Server server) => _server = server;

    [Fact]
    public void The_server_says_what_it_is_and_in_which_order_its_tools_are_used()
    {
        Assert.Equal("lintel", _server.Client.ServerInfo?.Name);

        var instructions = _server.Client.ServerInstructions;
        Assert.NotNull(instructions);
        Assert.All(Tools, tool => Assert.Contains(tool, instructions));
        Assert.InRange(instructions!.Length, 300, 1500);
    }

    [Fact]
    public async Task The_tools_are_named_described_and_marked()
    {
        var tools = await _server.Client.ListToolsAsync();

        Assert.Equal(Tools.OrderBy(t => t), tools.Select(t => t.Name).OrderBy(t => t));
        foreach (var tool in tools)
        {
            // A description is read by a model with every request: long enough to say when to call the tool, and short.
            Assert.InRange(tool.Description.Length, 150, 900);
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.False(annotations!.OpenWorldHint);
            Assert.True(annotations.IdempotentHint);

            // Every parameter says what it is.
            foreach (var parameter in tool.JsonSchema.GetProperty("properties").EnumerateObject())
            {
                Assert.True(parameter.Value.TryGetProperty("description", out var description), $"{tool.Name}.{parameter.Name} has no description");
                Assert.False(string.IsNullOrWhiteSpace(description.GetString()));
            }
        }

        // The tools that only read say so; the two that can write a file do not.
        var readOnly = tools.Where(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true).Select(t => t.Name).OrderBy(t => t);
        Assert.Equal(new[] { "lintel_language_guide", "lintel_list_properties", "lintel_validate_rules" }, readOnly);
    }

    [Fact]
    public async Task The_guide_is_the_one_page_description_of_the_repository()
    {
        var guide = await _server.Call("lintel_language_guide");

        Assert.False(guide.Refused);
        Assert.Equal(File.ReadAllText(Path.Combine(Repository.Root, "LLM.md")), guide.Text);
    }

    [Fact]
    public async Task From_a_requirement_to_an_IDS_file()
    {
        using var folder = new Folder();

        // "Doors have a fire rating": which property is that?
        var found = await _server.Call("lintel_list_properties", ("class", "IfcDoor"), ("search", "fire"));
        Assert.Contains("Pset_DoorCommon.FireRating: IfcLabel, text in quotes", found.Text);

        // The first draft has an anchored pattern, as most models write it, and a measure without its unit.
        var draft = """
            ifc IFC4

            rule R1 "Fire rating of doors"
              source "Türen müssen eine Feuerwiderstandsklasse haben."
              for IfcDoor
              require property Pset_DoorCommon.FireRating matches "^(T|EI)[0-9]{2,3}$"

            rule R2 "Doors are at least 2.1 m high"
              for IfcDoor
              require attribute OverallHeight >= 2.1
            """;
        var rules = folder.File("doors.lintel");
        File.WriteAllText(rules, draft);

        var first = Json(await _server.Call("lintel_validate_rules", ("path", rules)));
        Assert.False(first.GetProperty("ok").GetBoolean());
        var codes = first.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()).ToList();
        Assert.Equal(new[] { "W509", "E409" }, codes);

        // The compiler does not write IDS for it, and says what to do.
        var refused = await _server.Call("lintel_compile_rules", ("path", rules), ("outputPath", folder.File("doors.ids")));
        Assert.True(refused.Refused);
        Assert.StartsWith("No IDS was written: the rules have 1 error.", refused.Text);
        Assert.False(File.Exists(folder.File("doors.ids")));

        // The author takes the fixed text, which has the anchors removed, and adds the unit.
        var repaired = first.GetProperty("fixed").GetString()!.Replace(">= 2.1", ">= 2.1 m");
        File.WriteAllText(rules, repaired);
        var second = Json(await _server.Call("lintel_validate_rules", ("path", rules)));
        Assert.True(second.GetProperty("ok").GetBoolean());
        Assert.Equal(0, second.GetProperty("diagnostics").GetArrayLength());

        var written = await _server.Call("lintel_compile_rules", ("path", rules), ("outputPath", folder.File("doors.ids")));
        Assert.False(written.Refused, written.All);
        Assert.Contains("IDS 1.0 with 2 specifications", written.Text);
        var ids = File.ReadAllText(folder.File("doors.ids"));
        Assert.Contains("<simpleValue>FireRating</simpleValue>", ids);
        Assert.Contains("(T|EI)[0-9]{2,3}", ids);
        Assert.DoesNotContain("^(T|EI)", ids);

        // The IDS file reads back as the same rules.
        var back = await _server.Call("lintel_import_ids", ("path", folder.File("doors.ids")));
        Assert.Contains("require property Pset_DoorCommon.FireRating matches \"(T|EI)[0-9]{2,3}\"", back.Text);
        Assert.Contains("require attribute OverallHeight >= 2.1 m", back.Text);
        Assert.Contains("The rules compile back to the same IDS.", back.Blocks[1]);
    }

    [Fact]
    public async Task A_warning_with_a_ready_fix_holds_the_IDS_back_until_it_is_accepted()
    {
        const string source = "ifc IFC4\nrule \"x\"\n  for IfcWall\n  require attribute Name matches \"^W.*\"";

        var refused = await _server.Call("lintel_compile_rules", ("source", source));
        Assert.True(refused.Refused);
        Assert.StartsWith("No IDS was written: 1 warning can be repaired at once.", refused.Text);
        Assert.Contains("\"fixed\"", refused.Text);
        Assert.Contains("acceptWarnings", refused.Text);

        var accepted = await _server.Call("lintel_compile_rules", ("source", source), ("acceptWarnings", true));
        Assert.False(accepted.Refused);
        Assert.StartsWith("<?xml", accepted.Text);
        Assert.Contains("^W.*", accepted.Text);
        Assert.Contains("1 warning left as written", accepted.Blocks[1]);

        // A warning without a fix does not hold anything back: there is nothing to repair at once.
        var plain = await _server.Call("lintel_compile_rules", ("source", "ifc IFC4\nrule \"x\"\n  for only IfcElement\n  require attribute Name"));
        Assert.False(plain.Refused);
        Assert.Contains("W508", plain.Blocks[1]);
    }

    [Fact]
    public async Task A_file_is_replaced_only_when_asked()
    {
        using var folder = new Folder();
        var output = folder.File("out.ids");
        const string source = "ifc IFC4\nrule \"x\"\n  for IfcSpace\n  require attribute Name";

        Assert.False((await _server.Call("lintel_compile_rules", ("source", source), ("outputPath", output))).Refused);
        var before = File.ReadAllText(output);

        var again = await _server.Call("lintel_compile_rules", ("source", source.Replace("IfcSpace", "IfcZone")), ("outputPath", output));
        Assert.True(again.Refused);
        Assert.Contains("overwrite: true", again.Text);
        Assert.Equal(before, File.ReadAllText(output));

        Assert.False((await _server.Call("lintel_compile_rules", ("source", source.Replace("IfcSpace", "IfcZone")), ("outputPath", output), ("overwrite", true))).Refused);
        Assert.Contains("IFCZONE", File.ReadAllText(output));
    }

    [Theory]
    [InlineData("lintel_validate_rules", "path", "/etc/passwd", "ends with .lintel")]
    [InlineData("lintel_validate_rules", "path", "nothing-here.lintel", "does not exist")]
    [InlineData("lintel_import_ids", "path", "rules.lintel", "ends with .ids or .xml")]
    [InlineData("lintel_import_ids", "ids", "<ids>", "This is not an IDS file")]
    [InlineData("lintel_import_ids", "ids", "<html/>", "This is not an IDS file")]
    [InlineData("lintel_list_properties", "class", "IfcWal", "Did you mean IfcWall?")]
    [InlineData("lintel_list_properties", "propertySet", "Pset_WallComon", "Did you mean Pset_WallCommon?")]
    [InlineData("lintel_list_properties", "ifc", "IFC5", "IFC2X3, IFC4 or IFC4X3_ADD2")]
    public async Task A_request_that_cannot_be_met_is_answered_with_what_to_do(string tool, string parameter, string value, string expected)
    {
        var answer = await _server.Call(tool, (parameter, value));

        Assert.True(answer.Refused);
        Assert.Contains(expected, answer.Text);
    }

    [Fact]
    public async Task Text_and_path_exclude_each_other()
    {
        Assert.Contains("Give the rules as text in 'source'", (await _server.Call("lintel_validate_rules")).Text);
        Assert.Contains("not both", (await _server.Call("lintel_validate_rules", ("source", "ifc IFC4"), ("path", "a.lintel"))).Text);
        Assert.Contains("the file has no rule", (await _server.Call("lintel_compile_rules", ("source", "ifc IFC4\ndefine L = [\"a\"]"))).Text);
    }

    [Fact]
    public async Task The_compiler_writes_into_a_file_only_with_the_ids_extension()
    {
        using var folder = new Folder();
        const string source = "ifc IFC4\nrule \"x\"\n  for IfcSpace\n  require attribute Name";

        var answer = await _server.Call("lintel_compile_rules", ("source", source), ("outputPath", folder.File("Program.cs")));

        Assert.True(answer.Refused);
        Assert.False(File.Exists(folder.File("Program.cs")));
    }

    [Fact]
    public async Task What_an_IDS_file_has_that_Lintel_does_not_keep_is_said()
    {
        const string ids = """
            <?xml version="1.0" encoding="utf-8"?>
            <ids xmlns="http://standards.buildingsmart.org/IDS" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="http://standards.buildingsmart.org/IDS http://standards.buildingsmart.org/IDS/1.0/ids.xsd">
              <info><title>t</title></info>
              <specifications>
                <specification name="s" ifcVersion="IFC4">
                  <applicability minOccurs="0" maxOccurs="unbounded"><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability>
                  <requirements><classification><system><xs:restriction base="xs:string"><xs:minLength value="3"/></xs:restriction></system></classification></requirements>
                </specification>
              </specifications>
            </ids>
            """;

        var answer = await _server.Call("lintel_import_ids", ("ids", ids));

        Assert.False(answer.Refused);
        Assert.Contains("Lintel does not keep everything of this file", answer.Blocks[1]);
        Assert.Contains("A name cannot be restricted with 'minLength'", answer.Blocks[1]);
    }

    [Fact]
    public async Task Problems_of_a_request_are_not_failures_of_the_server()
    {
        // Calls that are refused must not leave stack traces on standard error.
        await _server.Call("lintel_list_properties", ("class", "IfcNope"));
        await _server.Call("lintel_validate_rules", ("path", "missing.lintel"));
        await _server.Call("lintel_compile_rules", ("source", "rule"));

        Assert.DoesNotContain(_server.ErrorLines, line => line.Contains("Exception", StringComparison.Ordinal));
    }

    private static JsonElement Json(ToolAnswer answer)
    {
        Assert.False(answer.Refused, answer.All);
        using var document = JsonDocument.Parse(answer.Text);
        return document.RootElement.Clone();
    }
}
