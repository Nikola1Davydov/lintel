using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lintel.Diagnostics;
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
    public void Every_official_ids_file_goes_to_lintel_and_back_unchanged()
    {
        var files = Corpus.Files().ToList();
        Assert.True(files.Count > 300, $"only {files.Count} files found under {Corpus.Root}");

        var failures = new List<string>();
        var rejected = new List<string>();
        var accepted = new List<string>();

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var original = File.ReadAllText(path);
            var imported = LintelCompiler.Import(original);
            var compiled = LintelCompiler.Compile(imported.Source);
            var check = compiled.Check;

            // The printed text is in the grammar, whatever the IDS file contained.
            var grammar = check.Diagnostics.Where(d => d.Code.StartsWith("E1", StringComparison.Ordinal) || d.Code.StartsWith("E2", StringComparison.Ordinal)).ToList();
            if (grammar.Count > 0)
            {
                failures.Add($"{name}: not in the grammar: {grammar[0]}");
                continue;
            }

            // Printing what was read gives the same text: the canonical layout is stable.
            if (Printer.Print(check.File) != imported.Source)
            {
                failures.Add($"{name}: the printed text changes when it is read and printed again");
                continue;
            }

            // Files named invalid-* break a rule of the official audit. They must not get through silently.
            if (name.StartsWith("invalid-", StringComparison.Ordinal))
            {
                (check.Ok && imported.Notes.Count == 0 ? accepted : rejected).Add(name);
                continue;
            }

            // One example labels every value of a list with an annotation of its own. Lintel has a note for a whole
            // constraint only; the importer says so, and the file is not expected to come back unchanged.
            if (name == "IDS_random_example.ids")
            {
                Assert.All(imported.Notes, note => Assert.Contains("annotation of the single value", note.Message));
                Assert.True(check.Ok, check.Describe());
                continue;
            }

            // Every other file passes every check ...
            if (!check.Ok || imported.Notes.Count > 0)
            {
                failures.Add($"{name}: {check.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Error)?.ToString() ?? imported.Notes[0].ToString()}");
                continue;
            }

            // ... comes back as the same IDS ...
            var before = IdsXml.Canonical(original);
            var after = IdsXml.Canonical(compiled.Ids!);
            if (before != after)
            {
                failures.Add($"{name}: the IDS changes on the way through Lintel:\n{FirstDifference(before, after)}");
                continue;
            }

            // ... and what the compiler writes passes the official audit.
            var audit = IdsXml.Audit(compiled.Ids!);
            if (audit.Count > 0)
            {
                failures.Add($"{name}: the official audit rejects the output: {audit[0]}");
            }
        }

        _output.WriteLine($"{files.Count} files; {rejected.Count} of {rejected.Count + accepted.Count} invalid files are rejected");
        foreach (var name in accepted)
        {
            _output.WriteLine("  not rejected: " + name);
        }

        Assert.True(failures.Count == 0, $"{failures.Count} files:\n" + string.Join("\n", failures.Take(40)));
    }

    private static string FirstDifference(string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "(end)";
            var y = i < b.Length ? b[i] : "(end)";
            if (x != y)
            {
                return $"    original: {x.Trim()}\n    output:   {y.Trim()}";
            }
        }

        return string.Empty;
    }

    [CorpusFact]
    public void A_prohibited_specification_with_requirements_is_rejected()
    {
        var path = Corpus.Files().Single(f => f.EndsWith("invalid-prohibited_specifications_invalid_if_requirements_are_specified.ids", StringComparison.Ordinal));
        var imported = LintelCompiler.Import(File.ReadAllText(path));

        Assert.Contains("E305", LintelCompiler.Check(imported.Source).Codes());
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
