using System;
using System.IO;
using System.Linq;

namespace Lintel.Mcp;

/// <summary>
/// The files that the tools read and write. A tool takes its input as text or as the path of a file, and it
/// touches only files of the kinds it is made for: rule files and IDS files.
/// </summary>
internal static class Files
{
    public static readonly string[] Rules = { ".lintel" };
    public static readonly string[] Ids = { ".ids", ".xml" };

    private const long MaxBytes = 4 * 1024 * 1024;

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
            throw new ToolProblem($"The file {full} does not exist. A relative path starts at {Directory.GetCurrentDirectory()}.");
        }

        if (file.Length > MaxBytes)
        {
            throw new ToolProblem($"The file {full} has {file.Length} bytes; the limit is {MaxBytes}.");
        }

        try
        {
            return File.ReadAllText(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ToolProblem($"The file {full} cannot be read: {exception.Message}");
        }
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

            File.WriteAllText(full, content);
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
            full = Path.GetFullPath(path.Trim());
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
