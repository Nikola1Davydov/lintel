using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Bimlint.Mcp.Tests;

/// <summary>The server over the protocol: what it announces, and the path of an author from a requirement to an IDS file.</summary>
[Collection("server")]
public class ServerTests
{
    private static readonly string[] Tools =
    {
        "bimlint_language_guide", "bimlint_list_properties", "bimlint_validate_rules", "bimlint_compile_rules", "bimlint_import_ids",
    };

    private readonly Server _server;

    public ServerTests(Server server) => _server = server;

    [Fact]
    public void The_server_says_what_it_is_and_in_which_order_its_tools_are_used()
    {
        Assert.Equal("bimlint", _server.Client.ServerInfo?.Name);

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
        Assert.Equal(new[] { "bimlint_language_guide", "bimlint_list_properties", "bimlint_validate_rules" }, readOnly);
    }

    [Fact]
    public async Task The_guide_is_the_one_page_description_of_the_repository()
    {
        var guide = await _server.Call("bimlint_language_guide");

        Assert.False(guide.Refused);
        Assert.Equal(File.ReadAllText(Path.Combine(Repository.Root, "LLM.md")), guide.Text);
    }

    [Fact]
    public async Task From_a_requirement_to_an_IDS_file()
    {
        using var folder = new Folder();

        // "Doors have a fire rating": which property is that?
        var found = await _server.Call("bimlint_list_properties", ("class", "IfcDoor"), ("search", "fire"));
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
        var rules = folder.File("doors.bimlint");
        File.WriteAllText(rules, draft);

        var first = Json(await _server.Call("bimlint_validate_rules", ("path", rules)));
        Assert.False(first.GetProperty("ok").GetBoolean());
        var codes = first.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetProperty("code").GetString()).ToList();
        Assert.Equal(new[] { "W509", "E409" }, codes);

        // The compiler does not write IDS for it, and says what to do.
        var refused = await _server.Call("bimlint_compile_rules", ("path", rules), ("outputPath", folder.File("doors.ids")));
        Assert.True(refused.Refused);
        Assert.StartsWith("No IDS was written: the rules have 1 error.", refused.Text);
        Assert.False(File.Exists(folder.File("doors.ids")));

        // The author takes the fixed text, which has the anchors removed, and adds the unit.
        var repaired = first.GetProperty("fixed").GetString()!.Replace(">= 2.1", ">= 2.1 m");
        File.WriteAllText(rules, repaired);
        var second = Json(await _server.Call("bimlint_validate_rules", ("path", rules)));
        Assert.True(second.GetProperty("ok").GetBoolean());
        Assert.Equal(0, second.GetProperty("diagnostics").GetArrayLength());

        var written = await _server.Call("bimlint_compile_rules", ("path", rules), ("outputPath", folder.File("doors.ids")));
        Assert.False(written.Refused, written.All);
        Assert.Contains("IDS 1.0 with 2 specifications", written.Text);
        var ids = File.ReadAllText(folder.File("doors.ids"));
        Assert.Contains("<simpleValue>FireRating</simpleValue>", ids);
        Assert.Contains("(T|EI)[0-9]{2,3}", ids);
        Assert.DoesNotContain("^(T|EI)", ids);

        // The IDS file reads back as the same rules.
        var back = await _server.Call("bimlint_import_ids", ("path", folder.File("doors.ids")));
        Assert.Contains("require property Pset_DoorCommon.FireRating matches \"(T|EI)[0-9]{2,3}\"", back.Text);
        Assert.Contains("require attribute OverallHeight >= 2.1 m", back.Text);
        Assert.Contains("The rules compile back to the same IDS.", back.Blocks[1]);
    }

    [Fact]
    public async Task A_warning_with_a_ready_fix_holds_the_IDS_back_until_it_is_accepted()
    {
        const string source = "ifc IFC4\nrule \"x\"\n  for IfcWall\n  require attribute Name matches \"^W.*\"";

        var refused = await _server.Call("bimlint_compile_rules", ("source", source));
        Assert.True(refused.Refused);
        Assert.StartsWith("No IDS was written: 1 warning can be repaired at once.", refused.Text);
        Assert.Contains("\"fixed\"", refused.Text);
        Assert.Contains("acceptWarnings", refused.Text);

        var accepted = await _server.Call("bimlint_compile_rules", ("source", source), ("acceptWarnings", true));
        Assert.False(accepted.Refused);
        Assert.StartsWith("<?xml", accepted.Text);
        Assert.Contains("^W.*", accepted.Text);
        Assert.Contains("1 warning left as written", accepted.Blocks[1]);

        // A warning without a fix does not hold anything back: there is nothing to repair at once.
        var plain = await _server.Call("bimlint_compile_rules", ("source", "ifc IFC4\nrule \"x\"\n  for only IfcElement\n  require attribute Name"));
        Assert.False(plain.Refused);
        Assert.Contains("W508", plain.Blocks[1]);
    }

    [Fact]
    public async Task A_file_without_a_title_is_compiled_and_the_answer_says_how_to_give_one()
    {
        const string rule = "ifc IFC4\nrule \"x\"\n  for IfcSpace\n  require attribute Name";

        var untitled = await _server.Call("bimlint_compile_rules", ("source", rule));
        Assert.False(untitled.Refused);
        Assert.Contains("<title>Untitled</title>", untitled.Text);
        Assert.Contains("Add a line at the top: title \"...\"", untitled.Blocks[1]);

        var titled = await _server.Call("bimlint_compile_rules", ("source", "title \"Spaces\"\n" + rule));
        Assert.Contains("<title>Spaces</title>", titled.Text);
        Assert.Single(titled.Blocks);
    }

    [Fact]
    public async Task A_file_is_replaced_only_when_asked()
    {
        using var folder = new Folder();
        var output = folder.File("out.ids");
        const string source = "ifc IFC4\nrule \"x\"\n  for IfcSpace\n  require attribute Name";

        Assert.False((await _server.Call("bimlint_compile_rules", ("source", source), ("outputPath", output))).Refused);
        var before = File.ReadAllText(output);

        var again = await _server.Call("bimlint_compile_rules", ("source", source.Replace("IfcSpace", "IfcZone")), ("outputPath", output));
        Assert.True(again.Refused);
        Assert.Contains("overwrite: true", again.Text);
        Assert.Equal(before, File.ReadAllText(output));

        Assert.False((await _server.Call("bimlint_compile_rules", ("source", source.Replace("IfcSpace", "IfcZone")), ("outputPath", output), ("overwrite", true))).Refused);
        Assert.Contains("IFCZONE", File.ReadAllText(output));
    }

    [Theory]
    [InlineData("bimlint_validate_rules", "path", "/etc/passwd", "ends with .bimlint")]
    [InlineData("bimlint_validate_rules", "path", "nothing-here.bimlint", "does not exist")]
    [InlineData("bimlint_import_ids", "path", "rules.bimlint", "ends with .ids or .xml")]
    [InlineData("bimlint_import_ids", "ids", "<ids>", "This is not an IDS file")]
    [InlineData("bimlint_import_ids", "ids", "<html/>", "This is not an IDS file")]
    [InlineData("bimlint_list_properties", "class", "IfcWal", "Did you mean IfcWall?")]
    [InlineData("bimlint_list_properties", "propertySet", "Pset_WallComon", "Did you mean Pset_WallCommon?")]
    [InlineData("bimlint_list_properties", "ifc", "IFC5", "IFC2X3, IFC4 or IFC4X3_ADD2")]
    public async Task A_request_that_cannot_be_met_is_answered_with_what_to_do(string tool, string parameter, string value, string expected)
    {
        var answer = await _server.Call(tool, (parameter, value));

        Assert.True(answer.Refused);
        Assert.Contains(expected, answer.Text);
    }

    [Fact]
    public async Task Text_and_path_exclude_each_other()
    {
        Assert.Contains("Give the rules as text in 'source'", (await _server.Call("bimlint_validate_rules")).Text);
        Assert.Contains("not both", (await _server.Call("bimlint_validate_rules", ("source", "ifc IFC4"), ("path", "a.bimlint"))).Text);
        Assert.Contains("the file has no rule", (await _server.Call("bimlint_compile_rules", ("source", "ifc IFC4\ndefine L = [\"a\"]"))).Text);
    }

    [Fact]
    public async Task The_compiler_writes_into_a_file_only_with_the_ids_extension()
    {
        using var folder = new Folder();
        const string source = "ifc IFC4\nrule \"x\"\n  for IfcSpace\n  require attribute Name";

        var answer = await _server.Call("bimlint_compile_rules", ("source", source), ("outputPath", folder.File("Program.cs")));

        Assert.True(answer.Refused);
        Assert.False(File.Exists(folder.File("Program.cs")));
    }

    [Fact]
    public async Task A_file_in_the_code_page_of_Windows_is_refused_with_the_line()
    {
        // Read with replacement characters, the wording of the requirement would reach the IDS file as "T?ren".
        using var folder = new Folder();
        const string rules = "ifc IFC4\nrule \"Türen\"\n  for IfcDoor\n  require attribute Name\n";
        File.WriteAllBytes(folder.File("rules.bimlint"), System.Text.Encoding.Latin1.GetBytes(rules));

        var answer = await _server.Call("bimlint_compile_rules", ("path", folder.File("rules.bimlint")), ("outputPath", folder.File("rules.ids")));

        Assert.True(answer.Refused);
        Assert.Contains("The text is not UTF-8: line 2 has a character in another encoding, near \"rule \"T?ren\"\". Save the file as UTF-8.", answer.Text);
        Assert.False(File.Exists(folder.File("rules.ids")));
    }

    [Fact]
    public async Task A_file_is_written_as_UTF_8_without_a_mark()
    {
        using var folder = new Folder();
        const string rules = "ifc IFC4\nrule \"Türen\"\n  for IfcDoor\n  require attribute Name\n";

        var answer = await _server.Call("bimlint_compile_rules", ("source", rules), ("outputPath", folder.File("rules.ids")));

        Assert.False(answer.Refused);
        var bytes = File.ReadAllBytes(folder.File("rules.ids"));
        Assert.Equal((byte)'<', bytes[0]);
        Assert.Contains("name=\"Türen\"", new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
    }

    [Fact]
    public async Task What_an_IDS_file_has_that_Bimlint_does_not_keep_is_said()
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

        var answer = await _server.Call("bimlint_import_ids", ("ids", ids));

        Assert.False(answer.Refused);
        Assert.Contains("Bimlint does not keep everything of this file", answer.Blocks[1]);
        Assert.Contains("A name cannot be restricted with 'minLength'", answer.Blocks[1]);
    }

    [Fact]
    public async Task Problems_of_a_request_are_not_failures_of_the_server()
    {
        // Calls that are refused must not leave stack traces on standard error.
        await _server.Call("bimlint_list_properties", ("class", "IfcNope"));
        await _server.Call("bimlint_validate_rules", ("path", "missing.bimlint"));
        await _server.Call("bimlint_compile_rules", ("source", "rule"));

        Assert.DoesNotContain(_server.ErrorLines, line => line.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_relative_path_starts_at_the_project_folder_that_the_client_names()
    {
        // A client may start the server in any folder; Claude Code names the project in CLAUDE_PROJECT_DIR.
        using var project = new Folder();
        using var elsewhere = new Folder();
        Directory.CreateDirectory(Path.Combine(project.Path, "requirements"));
        File.WriteAllText(Path.Combine(project.Path, "requirements", "walls.bimlint"), "ifc IFC4\nrule \"x\"\n  for IfcWall\n  require attribute Name");

        var transport = new ModelContextProtocol.Client.StdioClientTransport(new ModelContextProtocol.Client.StdioClientTransportOptions
        {
            Name = "bimlint",
            Command = "dotnet",
            Arguments = new[] { Path.Combine(AppContext.BaseDirectory, "Bimlint.Mcp.dll") },
            WorkingDirectory = elsewhere.Path,
            EnvironmentVariables = new Dictionary<string, string?> { ["CLAUDE_PROJECT_DIR"] = project.Path },
        });
        await using var client = await ModelContextProtocol.Client.McpClient.CreateAsync(transport);

        var written = new ToolAnswer(await client.CallToolAsync("bimlint_compile_rules", new Dictionary<string, object?>
        {
            ["path"] = "requirements/walls.bimlint",
            ["outputPath"] = "out/walls.ids",
        }));

        Assert.False(written.Refused, written.All);
        Assert.True(File.Exists(Path.Combine(project.Path, "out", "walls.ids")));
        Assert.Empty(Directory.GetFileSystemEntries(elsewhere.Path));
    }

    [Fact]
    public async Task A_long_IDS_text_goes_to_a_file_not_into_the_answer()
    {
        using var folder = new Folder();

        // 'for IfcElement' is written as every element class: eight such rules are more text than a model should read.
        var source = "ifc IFC4\n" + string.Concat(Enumerable.Range(1, 8).Select(i => $"rule \"r{i}\"\n  for IfcElement\n  require attribute Name\n"));

        var asText = await _server.Call("bimlint_compile_rules", ("source", source));
        Assert.True(asText.Refused);
        Assert.Contains("Give 'outputPath'", asText.Text);

        var asFile = await _server.Call("bimlint_compile_rules", ("source", source), ("outputPath", folder.File("elements.ids")));
        Assert.False(asFile.Refused, asFile.All);
        Assert.InRange(asFile.Text.Length, 1, 400);
        Assert.True(new FileInfo(folder.File("elements.ids")).Length > 40_000);
    }

    private static JsonElement Json(ToolAnswer answer)
    {
        Assert.False(answer.Refused, answer.All);
        using var document = JsonDocument.Parse(answer.Text);
        return document.RootElement.Clone();
    }
}
