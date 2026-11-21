using System;
using System.IO;
using Lintel;
using Lintel.Diagnostics;

namespace Lintel.Cli;

/// <summary>
/// The command line of the prototype: <c>check</c> prints the diagnostics of a Lintel file as JSON,
/// <c>import</c> prints an IDS file as Lintel. Exit code 0: no error. 1: errors. 2: wrong usage.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2 || (args[0] != "check" && args[0] != "import"))
        {
            Console.Error.WriteLine("usage: lintel check <file.lintel>    prints the diagnostics as JSON ('-' reads standard input)");
            Console.Error.WriteLine("       lintel import <file.ids>      prints the IDS file as Lintel");
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
            var diagnostics = new DiagnosticBag();
            var file = Lintel.Ids.IdsImporter.Import(source, diagnostics);
            Console.Out.Write(Lintel.Text.Printer.Print(file));
            foreach (var diagnostic in diagnostics.Items)
            {
                Console.Error.WriteLine(diagnostic);
            }

            return 0;
        }

        var result = LintelCompiler.Check(source);
        Console.Out.WriteLine(result.ToJson());
        return result.Ok ? 0 : 1;
    }
}
