using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Bimlint.Cli.Tests;

/// <summary>
/// The command line as a shell meets it: a process of its own, with its input and its two outputs as bytes.
/// </summary>
internal static class CommandLine
{
    /// <summary>The encoding that a redirected stream must have: UTF-8, and nothing that is not.</summary>
    public static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Runs the program. Its console is not UTF-8, as on Windows: on Linux and macOS the locale says so; on
    /// Windows the code page of the machine is what it is, and the result must be the same.
    /// </summary>
    public static async Task<Run> Start(string[] arguments, byte[]? input = null, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Bimlint.Cli.dll"));
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["LC_ALL"] = "en_US.ISO-8859-1";
        start.Environment["LANG"] = "en_US.ISO-8859-1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The command line did not start.");
        var output = ReadToEnd(process.StandardOutput.BaseStream);
        var error = ReadToEnd(process.StandardError.BaseStream);
        if (input is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(input);
        }

        process.StandardInput.Close();
        var bytes = await output;
        var errorBytes = await error;
        await process.WaitForExitAsync();
        return new Run(process.ExitCode, bytes, errorBytes);
    }

    private static async Task<byte[]> ReadToEnd(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}

/// <summary>What one run of the program left: its exit code and the bytes of its two outputs.</summary>
internal sealed record Run(int ExitCode, byte[] OutputBytes, byte[] ErrorBytes)
{
    /// <summary>Standard output as text; throws when it is not UTF-8.</summary>
    public string Output => CommandLine.StrictUtf8.GetString(OutputBytes);

    /// <summary>Standard error as text; throws when it is not UTF-8.</summary>
    public string Error => CommandLine.StrictUtf8.GetString(ErrorBytes);
}

internal static class Repository
{
    /// <summary>The repository root: the first folder above the test assembly that holds the solution file.</summary>
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Bimlint.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Bimlint.slnx was not found above the test assembly.");
        }
    }

    public static string Showcase => Path.Combine(Root, "examples", "showcase.bimlint");
}

/// <summary>A folder for the files of one test; it is removed with its content.</summary>
internal sealed class Folder : IDisposable
{
    public Folder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bimlint-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // a file that is still open is left to the system
        }
    }
}
