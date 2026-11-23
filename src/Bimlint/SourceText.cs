using System;
using System.Text;

namespace Bimlint;

/// <summary>
/// The text of a file, from its bytes. A Bimlint file is UTF-8 (SPEC.md, section 3), and so is the IDS file that
/// the compiler writes. Bytes that are not UTF-8 are refused: read with replacement characters, a "Wände" that
/// an editor saved in the code page of Windows would reach the IDS file as "W?nde", and nobody would be told.
/// </summary>
public static class SourceText
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The encoding of every file that is written: UTF-8 without a byte order mark.</summary>
    public static Encoding Utf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Reads the bytes of a file as text. UTF-8 with or without a byte order mark is read, and so is UTF-16 with
    /// one, which is what the redirection of Windows PowerShell writes. Anything else is not read:
    /// <paramref name="problem"/> then says where the file stops being UTF-8 and what to do.
    /// </summary>
    public static bool TryDecode(byte[] bytes, out string text, out string? problem)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        var encoding = StrictUtf8;
        var start = 0;
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
        {
            start = 3;
        }
        else if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00) || StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
        {
            text = string.Empty;
            problem = "The text is UTF-32. Save the file as UTF-8.";
            return false;
        }
        else if (StartsWith(bytes, 0xFF, 0xFE) || StartsWith(bytes, 0xFE, 0xFF))
        {
            encoding = new UnicodeEncoding(bigEndian: bytes[0] == 0xFE, byteOrderMark: false, throwOnInvalidBytes: true);
            start = 2;
        }

        try
        {
            text = encoding.GetString(bytes, start, bytes.Length - start);
            problem = null;
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            problem = ReferenceEquals(encoding, StrictUtf8)
                ? "The text is not UTF-8: " + FirstForeignCharacter(bytes, start) + " Save the file as UTF-8."
                : "The text starts as UTF-16 and is not valid UTF-16. Save the file as UTF-8.";
            return false;
        }
    }

    /// <summary>
    /// Where the first character of another encoding is. The bytes are read once more, this time with a
    /// replacement character for what is not UTF-8, and the first one is the place.
    /// </summary>
    private static string FirstForeignCharacter(byte[] bytes, int start)
    {
        const char Replacement = '�';
        var lenient = Utf8.GetString(bytes, start, bytes.Length - start);
        var at = lenient.IndexOf(Replacement);
        if (at < 0)
        {
            return "it has a character in another encoding.";
        }

        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < at; i++)
        {
            if (lenient[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        const int Around = 12;
        var from = Math.Max(lineStart, at - Around);
        var to = at + 1;
        while (to < lenient.Length && to <= at + Around && lenient[to] != '\r' && lenient[to] != '\n')
        {
            to++;
        }

        var near = lenient.Substring(from, to - from).Replace(Replacement, '?').Trim();
        return $"line {line} has a character in another encoding, near \"{near}\".";
    }

    private static bool StartsWith(byte[] bytes, params byte[] mark)
    {
        if (bytes.Length < mark.Length)
        {
            return false;
        }

        for (var i = 0; i < mark.Length; i++)
        {
            if (bytes[i] != mark[i])
            {
                return false;
            }
        }

        return true;
    }
}
