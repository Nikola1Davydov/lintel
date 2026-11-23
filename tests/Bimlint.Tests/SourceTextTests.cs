using System.Linq;
using System.Text;
using Xunit;

namespace Bimlint.Tests;

/// <summary>The bytes of a file as text: UTF-8 is read, another encoding is refused with the place.</summary>
public class SourceTextTests
{
    private const string Rules =
        "ifc IFC4\nrule \"Brandschutz\"\n  source \"Alle tragenden Wände müssen ...\"\n  for IfcWall\n  require attribute Name\n";

    [Fact]
    public void UTF_8_is_read()
    {
        Assert.True(SourceText.TryDecode(Encoding.UTF8.GetBytes(Rules), out var text, out var problem));

        Assert.Equal(Rules, text);
        Assert.Null(problem);
    }

    [Fact]
    public void A_byte_order_mark_is_not_part_of_the_text()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Rules)).ToArray();

        Assert.True(SourceText.TryDecode(bytes, out var text, out _));

        Assert.Equal(Rules, text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UTF_16_with_a_byte_order_mark_is_read_as_Windows_PowerShell_writes_it(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(Rules)).ToArray();

        Assert.True(SourceText.TryDecode(bytes, out var text, out _));

        Assert.Equal(Rules, text);
    }

    [Fact]
    public void A_file_in_the_code_page_of_Windows_is_refused_with_the_line_and_the_place()
    {
        // 'ä' and 'ü' are one byte each in Windows-1252 and in Latin-1, and no UTF-8.
        Assert.False(SourceText.TryDecode(Encoding.Latin1.GetBytes(Rules), out var text, out var problem));

        Assert.Equal(string.Empty, text);
        Assert.Equal(
            "The text is not UTF-8: line 3 has a character in another encoding, near \"tragenden W?nde m?ssen .\". Save the file as UTF-8.",
            problem);
    }

    [Fact]
    public void Text_without_a_line_break_before_the_place_is_in_line_1()
    {
        Assert.False(SourceText.TryDecode(Encoding.Latin1.GetBytes("title \"Türen\""), out _, out var problem));

        Assert.Contains("line 1 ", problem);
        Assert.Contains("\"title \"T?ren\"\"", problem);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x69, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x69 })]
    public void UTF_32_is_refused(byte[] bytes)
    {
        Assert.False(SourceText.TryDecode(bytes, out _, out var problem));

        Assert.Equal("The text is UTF-32. Save the file as UTF-8.", problem);
    }

    [Fact]
    public void UTF_16_that_ends_in_the_middle_of_a_character_is_refused()
    {
        Assert.False(SourceText.TryDecode(new byte[] { 0xFF, 0xFE, 0x69, 0x00, 0x66 }, out _, out var problem));

        Assert.Contains("not valid UTF-16", problem);
    }

    [Fact]
    public void An_empty_file_is_an_empty_text()
    {
        Assert.True(SourceText.TryDecode(new byte[0], out var text, out _));

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void A_file_is_written_without_a_byte_order_mark()
    {
        Assert.Empty(SourceText.Utf8.GetPreamble());
        Assert.Equal(new byte[] { 0x57, 0xC3, 0xA4 }, SourceText.Utf8.GetBytes("Wä"));
    }
}
