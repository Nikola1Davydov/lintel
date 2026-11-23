using System;
using System.IO;
using Bimlint;

namespace Bimlint.Cli;

/// <summary>
/// The command line: <c>check</c> prints the diagnostics of a Bimlint file as JSON, <c>compile</c> writes it as
/// IDS, <c>import</c> writes an IDS file as Bimlint.
/// Exit code 0: no error. 1: the rules have errors. 2: wrong usage, or a file that cannot be read or written.
/// </summary>
public static class Program
{
    private const string StandardInput = "-";

    public static int Main(string[] args)
    {
        if (!TryReadArguments(args, out var command, out var input, out var output))
        {
            PrintLine("usage: bimlint check <file.bimlint>                      prints the diagnostics as JSON", toError: true);
            PrintLine("       bimlint compile <file.bimlint> [-o <file.ids>]    writes the IDS file; diagnostics go to standard error", toError: true);
            PrintLine("       bimlint import <file.ids> [-o <file.bimlint>]     writes the IDS file as Bimlint", toError: true);
            PrintLine("Without -o the result is printed. '-' as the file reads standard input. Files are UTF-8.", toError: true);
            return 2;
        }

        if (output is not null && IsSameFile(input, output))
        {
            PrintLine("The file of -o is the input file: the result would replace what it is made from.", toError: true);
            return 2;
        }

        if (!TryRead(input, out var source))
        {
            return 2;
        }

        if (command == "import")
        {
            var imported = BimlintCompiler.Import(source);
            foreach (var note in imported.Notes)
            {
                PrintLine(note.ToString(), toError: true);
            }

            return Emit(imported.Source, output) ? 0 : 2;
        }

        if (command == "compile")
        {
            var compiled = BimlintCompiler.Compile(source);
            if (compiled.Ids is null)
            {
                PrintLine(compiled.Check.Ok ? "The file has no rule: there is nothing to write." : compiled.Check.ToJson(), toError: true);
                return 1;
            }

            if (compiled.Check.Diagnostics.Count > 0)
            {
                // Warnings do not stop the compiler, and they do not pass unseen either.
                PrintLine(compiled.Check.ToJson(), toError: true);
            }

            return Emit(compiled.Ids, output) ? 0 : 2;
        }

        var result = BimlintCompiler.Check(source);
        if (!Emit(result.ToJson() + Environment.NewLine, output))
        {
            return 2;
        }

        return result.Ok ? 0 : 1;
    }

    private static bool TryReadArguments(string[] args, out string command, out string input, out string? output)
    {
        command = args.Length > 0 ? args[0] : string.Empty;
        input = string.Empty;
        output = null;
        if (command != "check" && command != "compile" && command != "import")
        {
            return false;
        }

        var hasInput = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "-o" || args[i] == "--output")
            {
                if (output is not null || i + 1 >= args.Length)
                {
                    return false;
                }

                output = args[++i];
            }
            else if (args[i].Length > 1 && args[i][0] == '-')
            {
                // an option that does not exist; a file with such a name is given as ./-name
                return false;
            }
            else if (!hasInput)
            {
                input = args[i];
                hasInput = true;
            }
            else
            {
                return false;
            }
        }

        return hasInput && output != string.Empty;
    }

    /// <summary>Reads the input as UTF-8 and says so when it is something else.</summary>
    private static bool TryRead(string path, out string text)
    {
        try
        {
            if (path == StandardInput && !Console.IsInputRedirected)
            {
                // Typed at a console: the console knows its own code page.
                text = Console.In.ReadToEnd();
                return true;
            }

            var bytes = path == StandardInput ? ReadToEnd(Console.OpenStandardInput()) : File.ReadAllBytes(path);
            if (SourceText.TryDecode(bytes, out text, out var problem))
            {
                return true;
            }

            PrintLine($"{(path == StandardInput ? "Standard input" : path)}: {problem}", toError: true);
            return false;
        }
        catch (Exception exception) when (IsFileProblem(exception))
        {
            text = string.Empty;
            PrintLine(exception.Message, toError: true);
            return false;
        }
    }

    /// <summary>Writes the result to the file of <c>-o</c>, as UTF-8, or prints it.</summary>
    private static bool Emit(string text, string? path)
    {
        if (path is null)
        {
            Print(text, toError: false);
            return true;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(full, text, SourceText.Utf8);
            PrintLine("Wrote " + full, toError: false);
            return true;
        }
        catch (Exception exception) when (IsFileProblem(exception))
        {
            PrintLine($"{path} cannot be written: {exception.Message}", toError: true);
            return false;
        }
    }

    private static void PrintLine(string text, bool toError) => Print(text + Environment.NewLine, toError);

    /// <summary>
    /// Prints to a stream of the process. What goes to a file or to another program is UTF-8, whatever the code
    /// page of the console is: on Windows that code page is not UTF-8, and an IDS file printed in it and
    /// redirected to a file stops being valid XML at the first umlaut. A console itself is written to as it expects.
    /// </summary>
    private static void Print(string text, bool toError)
    {
        var redirected = toError ? Console.IsErrorRedirected : Console.IsOutputRedirected;
        if (!redirected)
        {
            (toError ? Console.Error : Console.Out).Write(text);
            return;
        }

        var bytes = SourceText.Utf8.GetBytes(text);
        using var stream = toError ? Console.OpenStandardError() : Console.OpenStandardOutput();
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    private static byte[] ReadToEnd(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool IsSameFile(string input, string output)
    {
        if (input == StandardInput)
        {
            return false;
        }

        try
        {
            var ignoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
            return string.Equals(
                Path.GetFullPath(input),
                Path.GetFullPath(output),
                ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (IsFileProblem(exception))
        {
            // not a path: reading or writing says so
            return false;
        }
    }

    private static bool IsFileProblem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
