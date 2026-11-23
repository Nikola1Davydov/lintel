using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace Bimlint.Cli.Tests;

/// <summary>
/// The command line over its streams and files. The texts of requirements are German as often as not, and the
/// program runs on Windows as often as not: what it prints and writes is UTF-8 there too.
/// </summary>
public class CommandLineTests
{
    private const string Wording = "Alle tragenden Wände müssen eine Feuerwiderstandsklasse aufweisen.";

    private const string Rules = "ifc IFC4\n\nrule R1 \"Türen\"\n  source \"Alle Türen müssen eine Nummer haben.\"\n  for IfcDoor\n  require attribute Name\n";

    [Fact]
    public async Task What_is_redirected_is_UTF_8_whatever_the_console_is()
    {
        var run = await CommandLine.Start(new[] { "compile", Repository.Showcase });

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(Wording, run.Output);
        Assert.Equal("ids", XDocument.Parse(run.Output).Root!.Name.LocalName);
        Assert.Empty(run.ErrorBytes);
    }

    [Fact]
    public async Task The_output_option_writes_the_same_file_as_UTF_8_without_a_mark()
    {
        using var folder = new Folder();
        var path = Path.Combine(folder.Path, "new folder", "showcase.ids");

        var printed = await CommandLine.Start(new[] { "compile", Repository.Showcase });
        var written = await CommandLine.Start(new[] { "compile", Repository.Showcase, "-o", path });

        Assert.Equal(0, written.ExitCode);
        Assert.Equal("Wrote " + path, written.Output.Trim());
        Assert.Equal(printed.OutputBytes, File.ReadAllBytes(path));
        Assert.StartsWith("<?xml", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 5));
    }

    [Fact]
    public async Task Standard_input_is_read_as_UTF_8_whatever_the_console_is()
    {
        var run = await CommandLine.Start(new[] { "compile", "-" }, Encoding.UTF8.GetBytes(Rules));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Alle Türen müssen eine Nummer haben.", run.Output);
        Assert.Contains("name=\"Türen\"", run.Output);
    }

    [Fact]
    public async Task A_file_in_the_code_page_of_Windows_is_refused_and_nothing_is_written()
    {
        using var folder = new Folder();
        File.WriteAllBytes(folder.File("rules.bimlint"), Encoding.Latin1.GetBytes(Rules));

        var run = await CommandLine.Start(new[] { "compile", folder.File("rules.bimlint"), "-o", folder.File("rules.ids") });

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("rules.bimlint: The text is not UTF-8: line 3 has a character in another encoding, near \"rule R1 \"T?ren\"\". Save the file as UTF-8.", run.Error);
        Assert.Empty(run.OutputBytes);
        Assert.False(File.Exists(folder.File("rules.ids")));
    }

    [Fact]
    public async Task Warnings_are_printed_and_the_IDS_file_is_written()
    {
        using var folder = new Folder();
        const string anchored = "ifc IFC4\nrule \"Fire rating\"\n  for IfcWall\n  require property Pset_WallCommon.FireRating matches \"^F[0-9]{2}$\"\n";

        var run = await CommandLine.Start(new[] { "compile", "-", "-o", folder.File("rules.ids") }, Encoding.UTF8.GetBytes(anchored));

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(folder.File("rules.ids")));
        using var diagnostics = JsonDocument.Parse(run.Error);
        Assert.Equal(0, diagnostics.RootElement.GetProperty("errors").GetInt32());
        Assert.Equal("W509", diagnostics.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Errors_stop_the_compiler_and_nothing_is_written()
    {
        using var folder = new Folder();
        const string wrong = "ifc IFC4\nrule \"Fire rating\"\n  for IfcWal\n  require property Pset_WallCommon.FireRating\n";

        var run = await CommandLine.Start(new[] { "compile", "-", "-o", folder.File("rules.ids") }, Encoding.UTF8.GetBytes(wrong));

        Assert.Equal(1, run.ExitCode);
        Assert.Empty(run.OutputBytes);
        Assert.False(File.Exists(folder.File("rules.ids")));
        using var diagnostics = JsonDocument.Parse(run.Error);
        Assert.False(diagnostics.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("E401", diagnostics.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_check_prints_the_diagnostics_as_JSON_and_says_in_the_exit_code_whether_there_are_errors()
    {
        var clean = await CommandLine.Start(new[] { "check", Repository.Showcase });
        var wrong = await CommandLine.Start(new[] { "check", "-" }, Encoding.UTF8.GetBytes("ifc IFC4\nrule \"Wände\"\n  for IfcWal\n  require attribute Name\n"));

        Assert.Equal(0, clean.ExitCode);
        using var cleanJson = JsonDocument.Parse(clean.Output);
        Assert.True(cleanJson.RootElement.GetProperty("ok").GetBoolean());

        Assert.Equal(1, wrong.ExitCode);
        using var wrongJson = JsonDocument.Parse(wrong.Output);
        Assert.False(wrongJson.RootElement.GetProperty("ok").GetBoolean());
        // The fixed source is the whole text of the author: its umlauts come back as they went in.
        Assert.Contains("rule \"Wände\"", wrongJson.RootElement.GetProperty("fixed").GetString());
    }

    [Fact]
    public async Task An_IDS_file_becomes_rules_and_the_rules_become_the_same_IDS_file()
    {
        using var folder = new Folder();
        await CommandLine.Start(new[] { "compile", Repository.Showcase, "-o", folder.File("first.ids") });

        var imported = await CommandLine.Start(new[] { "import", folder.File("first.ids"), "-o", folder.File("back.bimlint") });
        var compiled = await CommandLine.Start(new[] { "compile", folder.File("back.bimlint"), "-o", folder.File("second.ids") });

        Assert.Equal(0, imported.ExitCode);
        Assert.Empty(imported.ErrorBytes);
        Assert.Contains(Wording, File.ReadAllText(folder.File("back.bimlint"), CommandLine.StrictUtf8));
        Assert.Equal(0, compiled.ExitCode);
        Assert.Equal(File.ReadAllBytes(folder.File("first.ids")), File.ReadAllBytes(folder.File("second.ids")));
    }

    [Fact]
    public async Task What_an_IDS_file_has_that_Bimlint_does_not_keep_goes_to_standard_error()
    {
        const string ids = """
            <?xml version="1.0" encoding="utf-8"?>
            <ids xmlns="http://standards.buildingsmart.org/IDS" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:schemaLocation="http://standards.buildingsmart.org/IDS http://standards.buildingsmart.org/IDS/1.0/ids.xsd">
              <info><title>Türen</title></info>
              <specifications>
                <specification name="s" ifcVersion="IFC4">
                  <applicability minOccurs="0" maxOccurs="unbounded"><entity><name><simpleValue>IFCWALL</simpleValue></name></entity></applicability>
                  <requirements><classification><system><xs:restriction base="xs:string"><xs:minLength value="3"/></xs:restriction></system></classification></requirements>
                </specification>
              </specifications>
            </ids>
            """;

        var run = await CommandLine.Start(new[] { "import", "-" }, Encoding.UTF8.GetBytes(ids));

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("title \"Türen\"", run.Output);
        Assert.Contains("W701", run.Error);
    }

    [Theory]
    [InlineData()]
    [InlineData("build", "rules.bimlint")]
    [InlineData("check")]
    [InlineData("check", "a.bimlint", "b.bimlint")]
    [InlineData("compile", "a.bimlint", "-o")]
    [InlineData("compile", "a.bimlint", "-o", "a.ids", "-o", "b.ids")]
    [InlineData("compile", "a.bimlint", "--out", "a.ids")]
    [InlineData("compile", "--help")]
    public async Task Wrong_usage_is_answered_with_the_usage(params string[] arguments)
    {
        var run = await CommandLine.Start(arguments);

        Assert.Equal(2, run.ExitCode);
        Assert.StartsWith("usage: bimlint check <file.bimlint>", run.Error);
        Assert.Empty(run.OutputBytes);
    }

    [Fact]
    public async Task The_result_never_replaces_the_file_it_is_made_from()
    {
        using var folder = new Folder();
        File.WriteAllText(folder.File("rules.bimlint"), Rules);

        var run = await CommandLine.Start(new[] { "compile", "rules.bimlint", "-o", folder.File("rules.bimlint") }, workingDirectory: folder.Path);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("The file of -o is the input file", run.Error);
        Assert.Equal(Rules, File.ReadAllText(folder.File("rules.bimlint")));
    }

    [Fact]
    public async Task A_file_that_is_not_there_is_said_so()
    {
        using var folder = new Folder();

        var run = await CommandLine.Start(new[] { "check", folder.File("missing.bimlint") });

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("missing.bimlint", run.Error);
        Assert.Equal(1, run.Error.Split('\n').Count(line => line.Trim().Length > 0));
    }
}
