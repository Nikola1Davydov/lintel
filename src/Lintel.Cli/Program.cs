using System;
using System.IO;
using Lintel;
using Lintel.Diagnostics;

namespace Lintel.Cli;

/// <summary>
/// The command line of the prototype: <c>lintel check file.lintel</c> prints the diagnostics as JSON.
/// Exit code 0: no error. 1: errors. 2: wrong usage.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "check")
        {
            Console.Error.WriteLine("usage: lintel check <file.lintel>   ('-' reads standard input)");
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

        var result = LintelCompiler.Check(source);
        Console.Out.WriteLine(result.ToJson());
        return result.Ok ? 0 : 1;
    }
}
