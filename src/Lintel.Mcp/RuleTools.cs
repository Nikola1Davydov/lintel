using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml;
using Lintel.Diagnostics;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lintel.Mcp;

/// <summary>The tools that work on rules: the language guide, the checker, the compiler to IDS and the reader of IDS.</summary>
[McpServerToolType]
public sealed class RuleTools
{
    /// <summary>
    /// An IDS text longer than this is not returned as text: it would fill the context of the model, and clients
    /// cut or set aside long answers (Claude Code at 50,000 characters).
    /// </summary>
    internal const int MaxIdsCharacters = 40_000;

    [McpServerTool(Name = "lintel_language_guide", Title = "Lintel language guide", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Returns the Lintel language on one page: the shape of a rule file, 'for' and the facets, the constraints, "
        + "require / optional / prohibit, the mistakes that authors make, and examples. "
        + "Read it once before you write or change Lintel rules.")]
    public static string LanguageGuide() => ServerText.Guide;

    [McpServerTool(Name = "lintel_validate_rules", Title = "Check Lintel rules", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Checks Lintel rules and changes nothing: the grammar, the rules of IDS, and every IFC class, predefined type, "
        + "attribute, property set, property, data type and unit against the IFC version that the file names. "
        + "Call it after every change. Give the rules as text in 'source', or the path of a .lintel file in 'path'. "
        + "Returns JSON: 'ok' is true when there are no errors; 'diagnostics' holds each problem with severity, code, "
        + "message, line, column, hint, the expected values and a fix. When the answer has 'fixed', that is the whole "
        + "source with every fix applied: continue with it; when 'fixedOk' is true it has no errors left. "
        + "A warning is valid IDS that probably says something else than you mean: repair warnings too.")]
    public static CallToolResult ValidateRules(
        [Description("The rules as text. Give this or 'path'.")] string? source = null,
        [Description("The path of a .lintel file to check, full or relative to the project folder. Give this or 'source'.")] string? path = null) =>
        Answer.Of(() =>
        {
            var text = Files.Input(source, path, "source", "the rules", Files.Rules);
            return Answer.Done(LintelCompiler.Check(text).ToJson());
        });

    [McpServerTool(Name = "lintel_compile_rules", Title = "Compile Lintel rules to IDS", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = false)]
    [Description(
        "Compiles Lintel rules to a buildingSMART IDS 1.0 file. Give the rules as text in 'source', or the path of a "
        + ".lintel file in 'path'. With 'outputPath' the IDS is written to that .ids file and the answer is a short "
        + "summary; without it the answer is the IDS text. Nothing is written when the rules have errors, or warnings "
        + "that have a ready fix: the answer is then the JSON of lintel_validate_rules, with the repaired source in "
        + "'fixed'. Pass acceptWarnings: true to compile rules with such warnings as they are written.")]
    public static CallToolResult CompileRules(
        [Description("The rules as text. Give this or 'path'.")] string? source = null,
        [Description("The path of a .lintel file to compile, full or relative to the project folder. Give this or 'source'.")] string? path = null,
        [Description("Where to write the IDS file; the name ends with .ids. Without it the IDS text is returned.")] string? outputPath = null,
        [Description("Replace the file at 'outputPath' when it exists.")] bool overwrite = false,
        [Description("Compile although a warning has a ready fix.")] bool acceptWarnings = false) =>
        Answer.Of(() => Compile(source, path, outputPath, overwrite, acceptWarnings));

    private static CallToolResult Compile(string? source, string? path, string? outputPath, bool overwrite, bool acceptWarnings)
    {
        var text = Files.Input(source, path, "source", "the rules", Files.Rules);
        var result = LintelCompiler.Compile(text);
        var check = result.Check;

        var errors = check.Diagnostics.Count(d => d.Severity == Severity.Error);
        if (errors > 0)
        {
            return Answer.Refused(
                $"No IDS was written: the rules have {Count(errors, "error")}. "
                + (check.FixedOk == true ? "The text in 'fixed' repairs them; compile that." : "Repair them and compile again.")
                + "\n" + check.ToJson());
        }

        // A warning with a ready fix is a mistake that costs nothing to repair; the IDS waits for the repair.
        var repairable = check.Diagnostics.Count(d => d.Severity == Severity.Warning && d.Fix is { Count: > 0 });
        if (repairable > 0 && !acceptWarnings)
        {
            return Answer.Refused(
                $"No IDS was written: {Count(repairable, "warning")} can be repaired at once. Compile the text in 'fixed', "
                + "or pass acceptWarnings: true to compile the rules as they are written.\n" + check.ToJson());
        }

        if (result.Ids is null)
        {
            return Answer.Refused("No IDS was written: the file has no rule.");
        }

        var rules = check.File.Rules.Count();
        var warnings = check.Diagnostics.Where(d => d.Severity == Severity.Warning).ToList();
        var remaining = warnings.Count == 0
            ? string.Empty
            : $"\n{Count(warnings.Count, "warning")} left as written:\n" + string.Join("\n", warnings.Select(w => $"- line {w.Span.Line}: {w.Code} {w.Message}"));

        // IDS needs a title; a file that says none is called "Untitled", which a reviewer will not want.
        if (check.File.Header.Get("title") is null)
        {
            remaining += "\nThe rules have no title, so the IDS is titled \"Untitled\". Add a line at the top: title \"...\".";
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var written = Files.Output(outputPath!, result.Ids, new[] { ".ids" }, "the IDS", overwrite);
            return Answer.Done($"Wrote {written}: IDS 1.0 with {Count(rules, "specification")}, {result.Ids.Length} characters.{remaining}");
        }

        if (result.Ids.Length > MaxIdsCharacters)
        {
            return Answer.Refused(
                $"The IDS has {result.Ids.Length} characters, too many to return as text. "
                + "Give 'outputPath', and it is written to a file.");
        }

        return remaining.Length == 0 ? Answer.Done(result.Ids) : Answer.Done(result.Ids, remaining.TrimStart('\n'));
    }

    [McpServerTool(Name = "lintel_import_ids", Title = "Read an IDS file as Lintel", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = false)]
    [Description(
        "Turns a buildingSMART IDS 1.0 file into Lintel rules, to read, review or change an existing IDS file. "
        + "Give the IDS as text in 'ids', or the path of an .ids file in 'path'. The answer is the Lintel source, and "
        + "a note for whatever the file contains that Lintel does not keep; without a note the rules compile back to "
        + "the same IDS. With 'outputPath' the source is written to that .lintel file.")]
    public static CallToolResult ImportIds(
        [Description("The IDS file as text. Give this or 'path'.")] string? ids = null,
        [Description("The path of an .ids file to read, full or relative to the project folder. Give this or 'ids'.")] string? path = null,
        [Description("Where to write the Lintel source; the name ends with .lintel. Without it the source is returned.")] string? outputPath = null,
        [Description("Replace the file at 'outputPath' when it exists.")] bool overwrite = false) =>
        Answer.Of(() => Import(ids, path, outputPath, overwrite));

    private static CallToolResult Import(string? ids, string? path, string? outputPath, bool overwrite)
    {
        var text = Files.Input(ids, path, "ids", "the IDS", Files.Ids);

        ImportResult imported;
        try
        {
            imported = LintelCompiler.Import(text);
        }
        catch (Exception exception) when (exception is XmlException or FormatException)
        {
            throw new ToolProblem("This is not an IDS file: " + exception.Message);
        }

        var check = LintelCompiler.Check(imported.Source);
        var errors = check.Diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = check.Diagnostics.Count(d => d.Severity == Severity.Warning);
        var status = new List<string> { $"{Count(imported.File.Rules.Count(), "rule")}." };
        if (imported.Notes.Count > 0)
        {
            status.Add("Lintel does not keep everything of this file:");
            status.AddRange(imported.Notes.Select(n => "- " + n.Message));
        }
        else if (errors == 0)
        {
            status.Add("The rules compile back to the same IDS.");
        }

        if (errors > 0)
        {
            status.Add($"As Lintel the file has {Count(errors, "error")}: it breaks rules of IDS. Call lintel_validate_rules on the source for the details.");
        }
        else if (warnings > 0)
        {
            status.Add($"The rules have {Count(warnings, "warning")}; call lintel_validate_rules on the source to see them.");
        }

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var written = Files.Output(outputPath!, imported.Source, Files.Rules, "the rules", overwrite);
            return Answer.Done($"Wrote {written}: " + string.Join("\n", status));
        }

        return Answer.Done(imported.Source, string.Join("\n", status));
    }

    private static string Count(int number, string noun) => number == 1 ? $"1 {noun}" : $"{number} {noun}s";
}
