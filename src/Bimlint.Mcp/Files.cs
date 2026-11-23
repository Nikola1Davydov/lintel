using System;
using System.IO;
using System.Linq;

namespace Bimlint.Mcp;

/// <summary>
/// The files that the tools read and write. A tool takes its input as text or as the path of a file, and it
/// touches only files of the kinds it is made for: rule files and IDS files.
/// </summary>
internal static class Files
{
    public static readonly string[] Rules = { ".bimlint" };
    public static readonly string[] Ids = { ".ids", ".xml" };

    private const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// The folder that a relative path starts at: the project folder when the client names one, as Claude Code
    /// does in CLAUDE_PROJECT_DIR, and the working folder of the server otherwise. A client is free to start
    /// the server in any folder, so the working folder alone says little.
    /// </summary>
    public static string BaseDirectory
    {
        get
        {
            var project = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR");
            return !string.IsNullOrWhiteSpace(project) && Directory.Exists(project) ? project! : Directory.GetCurrentDirectory();
        }
    }

    /// <summary>The input of a tool: the text itself, or the content of the file at the path.</summary>
    public static string Input(string? text, string? path, string textParameter, string what, string[] extensions)
    {
        var hasText = !string.IsNullOrEmpty(text);
        var hasPath = !string.IsNullOrWhiteSpace(path);
        if (hasText && hasPath)
        {
            throw new ToolProblem($"Give either '{textParameter}' or 'path', not both.");
        }

        if (!hasText && !hasPath)
        {
            throw new ToolProblem($"Give {what} as text in '{textParameter}', or the path of a {extensions[0]} file in 'path'.");
        }

        if (hasText)
        {
            return text!;
        }

        var full = Resolve(path!, extensions, what);
        var file = new FileInfo(full);
        if (!file.Exists)
        {
            throw new ToolProblem($"The file {full} does not exist. A relative path starts at {BaseDirectory}; give the full path when the file is elsewhere.");
        }

        if (file.Length > MaxBytes)
        {
            throw new ToolProblem($"The file {full} has {file.Length} bytes; the limit is {MaxBytes}.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolProblem($"The file {full} cannot be read: {exception.Message}");
        }

        // A file in the code page of Windows is refused, not read with replacement characters.
        return SourceText.TryDecode(bytes, out var content, out var problem)
            ? content
            : throw new ToolProblem($"The file {full} cannot be read. {problem}");
    }

    /// <summary>Writes the output of a tool to a file and returns its full path. An existing file is kept unless asked otherwise.</summary>
    public static string Output(string path, string content, string[] extensions, string what, bool overwrite)
    {
        var full = Resolve(path, extensions, what);
        if (File.Exists(full) && !overwrite)
        {
            throw new ToolProblem($"The file {full} exists already. Pass overwrite: true to replace it, or give another path.");
        }

        try
        {
            var directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(full, content, SourceText.Utf8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolProblem($"The file {full} cannot be written: {exception.Message}");
        }

        return full;
    }

    private static string Resolve(string path, string[] extensions, string what)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path.Trim(), BaseDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ToolProblem($"'{path}' is not a path: {exception.Message}");
        }

        if (!extensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
        {
            throw new ToolProblem($"The file of {what} ends with {string.Join(" or ", extensions)}; '{path}' does not.");
        }

        return full;
    }
}
