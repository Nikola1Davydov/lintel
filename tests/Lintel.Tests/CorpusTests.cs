using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lintel.Diagnostics;
using Lintel.Ids;
using Lintel.Text;
using Xunit;
using Xunit.Abstractions;

namespace Lintel.Tests;

/// <summary>
/// The official IDS files of buildingSMART (the test cases of implementers and the examples) as a test of the
/// claim "every IDS 1.0 file can be written as Lintel". The files are not part of this repository:
/// clone https://github.com/buildingSMART/IDS next to it, or set LINTEL_IDS_REPO. Without them the tests are skipped.
/// </summary>
public class CorpusTests
{
    private readonly ITestOutputHelper _output;

    public CorpusTests(ITestOutputHelper output) => _output = output;

    [CorpusFact]
    public void Every_official_ids_file_can_be_written_as_lintel()
    {
        var files = Corpus.Files().ToList();
        Assert.True(files.Count > 300, $"only {files.Count} files found under {Corpus.Root}");

        var failures = new List<string>();
        var invalidRejected = 0;
        var invalidTotal = 0;

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var notes = new DiagnosticBag();
            var imported = IdsImporter.Import(File.ReadAllText(path), notes);
            var text = Printer.Print(imported);
            var result = LintelCompiler.Check(text);

            // The printed text is in the grammar, whatever the IDS file contained.
            var grammar = result.Diagnostics.Where(d => d.Code.StartsWith("E1", StringComparison.Ordinal) || d.Code.StartsWith("E2", StringComparison.Ordinal)).ToList();
            if (grammar.Count > 0)
            {
                failures.Add($"{name}: not in the grammar: {grammar[0]}");
                continue;
            }

            // Printing what was read gives the same text: the canonical layout is stable.
            var again = Printer.Print(result.File);
            if (again != text)
            {
                failures.Add($"{name}: the printed text changes when it is read and printed again");
                continue;
            }

            // Files named invalid-* break a rule of the IDS audit; the others must pass every check.
            if (name.StartsWith("invalid-", StringComparison.Ordinal))
            {
                invalidTotal++;
                if (!result.Ok || notes.Items.Count > 0)
                {
                    invalidRejected++;
                }
            }
            else if (!result.Ok || notes.Items.Count > 0)
            {
                failures.Add($"{name}: {result.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Error)?.ToString() ?? notes.Items[0].ToString()}");
            }
        }

        _output.WriteLine($"{files.Count} files; {invalidRejected} of {invalidTotal} invalid files are rejected without the IFC schema");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [CorpusFact]
    public void A_prohibited_specification_with_requirements_is_rejected()
    {
        var path = Corpus.Files().Single(f => f.EndsWith("invalid-prohibited_specifications_invalid_if_requirements_are_specified.ids", StringComparison.Ordinal));
        var text = Printer.Print(IdsImporter.Import(File.ReadAllText(path), new DiagnosticBag()));

        Assert.Contains("E305", LintelCompiler.Check(text).Codes());
    }
}

internal static class Corpus
{
    public static string? Root { get; } = Find();

    public static IEnumerable<string> Files() =>
        Root is null
            ? Enumerable.Empty<string>()
            : Directory.EnumerateFiles(Path.Combine(Root, "Documentation"), "*.ids", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal);

    private static string? Find()
    {
        var parent = Path.GetDirectoryName(TestSupport.RepositoryRoot) ?? string.Empty;
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("LINTEL_IDS_REPO"),
            Path.Combine(parent, "IDS"),
            Path.Combine(parent, "ids"),
            Path.Combine(parent, "buildingsmart", "ids"),
        };

        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && Directory.Exists(Path.Combine(c, "Documentation")));
    }
}

/// <summary>A test that needs the IDS repository of buildingSMART and is skipped without it.</summary>
public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (Corpus.Root is null)
        {
            Skip = "The IDS repository of buildingSMART was not found. Clone https://github.com/buildingSMART/IDS next to this repository or set LINTEL_IDS_REPO.";
        }
    }
}
