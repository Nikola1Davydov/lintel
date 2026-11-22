using System.Collections.Generic;
using System.Linq;
using Lintel.Analysis;
using Lintel.Diagnostics;
using Lintel.Ids;
using Lintel.Model;
using Lintel.Syntax;
using Lintel.Text;

namespace Lintel;

/// <summary>The result of checking one source text.</summary>
public sealed class CheckResult
{
    internal CheckResult(RuleFile file, IReadOnlyList<Diagnostic> diagnostics, string? fixedSource, bool? fixedOk)
    {
        File = file;
        Diagnostics = diagnostics;
        Fixed = fixedSource;
        FixedOk = fixedOk;
    }

    public RuleFile File { get; }

    /// <summary>The problems, ordered by position.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>True when there is no error. Warnings do not count.</summary>
    public bool Ok => Diagnostics.All(d => d.Severity != Severity.Error);

    /// <summary>The source with every fix applied, when there are fixes.</summary>
    public string? Fixed { get; }

    /// <summary>Whether <see cref="Fixed"/> is free of errors; <c>null</c> when there are no fixes.</summary>
    public bool? FixedOk { get; }

    /// <summary>The result as JSON, in the format of SPEC.md, section 9.</summary>
    public string ToJson()
    {
        var json = new JsonWriter();
        json.BeginObject();
        json.Value("ok", Ok);
        json.Value("errors", Diagnostics.Count(d => d.Severity == Severity.Error));
        json.Value("warnings", Diagnostics.Count(d => d.Severity == Severity.Warning));
        json.BeginArray("diagnostics");
        foreach (var diagnostic in Diagnostics)
        {
            json.BeginObject();
            json.Value("severity", diagnostic.Severity.ToString().ToLowerInvariant());
            json.Value("code", diagnostic.Code);
            json.Value("name", diagnostic.Name);
            json.Value("message", diagnostic.Message);
            json.Value("line", diagnostic.Span.Line);
            json.Value("column", diagnostic.Span.Column);
            json.Value("endLine", diagnostic.Span.EndLine);
            json.Value("endColumn", diagnostic.Span.EndColumn);
            json.Value("rule", diagnostic.Rule);
            json.Value("hint", diagnostic.Hint);
            if (diagnostic.Expected is { Count: > 0 })
            {
                json.BeginArray("expected");
                foreach (var value in diagnostic.Expected.Take(MaxExpected))
                {
                    json.Item(value);
                }

                json.EndArray();
            }

            if (diagnostic.Fix is { Count: > 0 })
            {
                json.BeginArray("fix");
                foreach (var edit in diagnostic.Fix)
                {
                    json.BeginObject();
                    json.Value("line", edit.Span.Line);
                    json.Value("column", edit.Span.Column);
                    json.Value("endLine", edit.Span.EndLine);
                    json.Value("endColumn", edit.Span.EndColumn);
                    json.Value("text", edit.Text);
                    json.EndObject();
                }

                json.EndArray();
            }

            json.EndObject();
        }

        json.EndArray();
        if (Fixed is not null)
        {
            json.Value("fixed", Fixed);
            json.Value("fixedOk", FixedOk ?? false);
        }

        json.EndObject();
        return json.ToString();
    }

    private const int MaxExpected = 30;
}

/// <summary>The result of compiling one source text to IDS.</summary>
public sealed class CompileResult
{
    internal CompileResult(CheckResult check, string? ids)
    {
        Check = check;
        Ids = ids;
    }

    public CheckResult Check { get; }

    /// <summary>The IDS 1.0 file; <c>null</c> when the source has errors.</summary>
    public string? Ids { get; }
}

/// <summary>The result of reading an IDS file.</summary>
public sealed class ImportResult
{
    internal ImportResult(RuleFile file, string source, IReadOnlyList<Diagnostic> notes)
    {
        File = file;
        Source = source;
        Notes = notes;
    }

    public RuleFile File { get; }

    /// <summary>The IDS file as Lintel source, in the canonical layout.</summary>
    public string Source { get; }

    /// <summary>What the IDS file contains that IDS 1.0 does not allow, or that Lintel cannot represent.</summary>
    public IReadOnlyList<Diagnostic> Notes { get; }
}

/// <summary>The entry point of the library.</summary>
public static class LintelCompiler
{
    private const int MaxFixPasses = 8;

    /// <summary>Checks a source text and, when it has no errors, writes it as IDS 1.0.</summary>
    public static CompileResult Compile(string source)
    {
        var check = Check(source);
        return new CompileResult(check, check.Ok ? IdsEmitter.EmitText(check.File) : null);
    }

    /// <summary>Reads an IDS 1.0 file and writes it as Lintel source.</summary>
    public static ImportResult Import(string ids)
    {
        var notes = new DiagnosticBag();
        var file = IdsImporter.Import(ids, notes);
        return new ImportResult(file, Printer.Print(file), notes.Items);
    }

    /// <summary>
    /// Reads and checks a source text: the grammar, the rules of IDS, and every name, data type and unit against the
    /// IFC versions the file names. It also applies the fixes, pass by pass, and says whether the fixed source is clean.
    /// </summary>
    public static CheckResult Check(string source)
    {
        var (file, diagnostics) = Run(source);

        string? fixedSource = null;
        bool? fixedOk = null;
        var current = source;
        var currentDiagnostics = diagnostics;
        for (var pass = 0; pass < MaxFixPasses; pass++)
        {
            var next = Fixer.Apply(current, currentDiagnostics);
            if (next is null || next == current)
            {
                break;
            }

            current = next;
            currentDiagnostics = Run(current).Diagnostics;
            fixedSource = current;
            fixedOk = currentDiagnostics.All(d => d.Severity != Severity.Error);
        }

        return new CheckResult(file, diagnostics, fixedSource, fixedOk);
    }

    /// <summary>Reads a source text without checking it beyond the grammar.</summary>
    public static RuleFile Parse(string source, out IReadOnlyList<Diagnostic> diagnostics)
    {
        var bag = new DiagnosticBag();
        var file = Parser.Parse(source, bag);
        diagnostics = Ordered(bag);
        return file;
    }

    private static (RuleFile File, IReadOnlyList<Diagnostic> Diagnostics) Run(string source)
    {
        var bag = new DiagnosticBag();
        var file = Parser.Parse(source, bag);
        StructureChecker.Check(source, file, bag);
        SchemaChecker.Check(file, bag);
        return (file, Ordered(bag));
    }

    private static IReadOnlyList<Diagnostic> Ordered(DiagnosticBag bag) =>
        bag.Items
            .Select((diagnostic, index) => (diagnostic, index))
            .OrderBy(x => x.diagnostic.Span.Start)
            .ThenBy(x => x.index)
            .Select(x => x.diagnostic)
            .ToList();
}
