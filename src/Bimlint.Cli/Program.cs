using System;
using System.IO;
using Bimlint;

namespace Bimlint.Cli;

/// <summary>
/// The command line of the prototype: <c>check</c> prints the diagnostics of a Bimlint file as JSON,
/// <c>compile</c> prints it as IDS, <c>import</c> prints an IDS file as Bimlint.
/// Exit code 0: no error. 1: errors. 2: wrong usage.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2 || (args[0] != "check" && args[0] != "compile" && args[0] != "import"))
        {
            Console.Error.WriteLine("usage: bimlint check <file.bimlint>     prints the diagnostics as JSON ('-' reads standard input)");
            Console.Error.WriteLine("       bimlint compile <file.bimlint>   prints the IDS file; on errors, the diagnostics");
            Console.Error.WriteLine("       bimlint import <file.ids>       prints the IDS file as Bimlint");
            return 2;
        }

        string source;
        try
        {
            source = args[1] == "-" ? Console.In.ReadToEnd() : File.ReadAllText(args[1]);
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        if (args[0] == "import")
        {
            var imported = BimlintCompiler.Import(source);
            Console.Out.Write(imported.Source);
            foreach (var note in imported.Notes)
            {
                Console.Error.WriteLine(note);
            }

            return 0;
        }

        if (args[0] == "compile")
        {
            var compiled = BimlintCompiler.Compile(source);
            if (compiled.Ids is null)
            {
                Console.Error.WriteLine(compiled.Check.Ok ? "The file has no rule: there is nothing to write." : compiled.Check.ToJson());
                return 1;
            }

            Console.Out.Write(compiled.Ids);
            return 0;
        }

        var result = BimlintCompiler.Check(source);
        Console.Out.WriteLine(result.ToJson());
        return result.Ok ? 0 : 1;
    }
}
