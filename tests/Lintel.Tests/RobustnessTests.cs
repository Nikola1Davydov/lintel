using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lintel.Diagnostics;
using Lintel.Syntax;
using Lintel.Text;
using Xunit;

namespace Lintel.Tests;

/// <summary>
/// The checker is fed by a language model and must take anything: it never throws, never hangs,
/// and every range it reports lies inside the source.
/// </summary>
public class RobustnessTests
{
    private static readonly string Showcase = File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot, "examples", "showcase.lintel"));

    [Fact]
    public async Task Damaged_sources_are_checked_without_failure()
    {
        // A fixed seed: the same damaged sources on every run.
        var random = new Random(20261121);
        var fragments = new[]
        {
            "\"", "\"\"\"", "[", "]", ",", ".", "=", "==", "!=", "<=", ">", "/", "(", ")", "{", "}", ";", ":", "\n", " ", "#",
            "and", "or", "not", "where", "for", "rule", "require", "optional", "prohibit", "count", "in", "matches", "between",
            "length", "note", "uri", "type", "only", "any", "define", "ifc", "property", "attribute", "part", "of", "via",
            "IfcWall", "Pset_WallCommon", "150 mm", "0.", "-1e9", "true", "\\d", "^", "$", "Ä", "\t", "\r\n",
        };

        for (var round = 0; round < 1500; round++)
        {
            var source = Showcase;
            var edits = 1 + random.Next(4);
            for (var i = 0; i < edits; i++)
            {
                var position = random.Next(source.Length + 1);
                switch (random.Next(4))
                {
                    case 0:
                        source = source.Insert(position, fragments[random.Next(fragments.Length)]);
                        break;
                    case 1:
                        var length = Math.Min(random.Next(1, 30), source.Length - position);
                        source = source.Remove(position, length);
                        break;
                    case 2:
                        source = source.Substring(0, position);
                        break;
                    default:
                        var other = random.Next(source.Length + 1);
                        var (from, to) = (Math.Min(position, other), Math.Max(position, other));
                        to = Math.Min(to, from + 40);
                        source = source.Substring(0, from) + new string(source.Substring(from, to - from).Reverse().ToArray()) + source.Substring(to);
                        break;
                }
            }

            var check = Task.Run(() => LintelCompiler.Check(source));
            var finished = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.True(finished == check, $"the check did not end (round {round}):\n{source}");
            var result = await check;

            foreach (var diagnostic in result.Diagnostics)
            {
                AssertInside(diagnostic.Span, source, diagnostic, round);
                foreach (var edit in diagnostic.Fix ?? Array.Empty<TextEdit>())
                {
                    AssertInside(edit.Span, source, diagnostic, round);
                }
            }

            // What the parser understood can be printed and read again.
            Printer.Print(result.File);
            result.ToJson();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("# only a comment")]
    [InlineData("rule")]
    [InlineData("rule \"x\" for")]
    [InlineData("\"\"\"")]
    [InlineData("ifc")]
    [InlineData("define")]
    [InlineData("define X =")]
    [InlineData("define X = [")]
    [InlineData("rule \"x\" for IfcWall require property")]
    [InlineData("rule \"x\" for IfcWall require property A.")]
    [InlineData("rule \"x\" for IfcWall require property A.B between 1 and")]
    [InlineData("rule \"x\" for IfcWall require property A.B in [1,")]
    [InlineData("rule \"x\" for IfcWall require part of")]
    [InlineData("rule \"x\" for IfcWall type")]
    [InlineData("rule \"x\" for matches")]
    [InlineData("]]]] ,,,, ....")]
    public void Short_and_broken_sources(string source)
    {
        var result = LintelCompiler.Check(source);

        foreach (var diagnostic in result.Diagnostics)
        {
            AssertInside(diagnostic.Span, source, diagnostic, 0);
        }
    }

    [Fact]
    public void The_reserved_words_of_the_specification_are_the_keywords_of_the_lexer()
    {
        var spec = File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot, "SPEC.md"));
        var section = spec.Substring(spec.IndexOf("## 10. Reserved words", StringComparison.Ordinal));
        section = section.Substring(0, section.IndexOf("\n## ", 5, StringComparison.Ordinal));
        var paragraphs = section.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        string[] Words(string paragraph) => Regex.Matches(paragraph, "`([a-z]+)`").Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(w => w, StringComparer.Ordinal).ToArray();

        var current = Words(paragraphs.Single(p => p.TrimStart().StartsWith("`and`", StringComparison.Ordinal)));
        var future = Words(paragraphs.Single(p => p.Contains("Reserved for later levels")).Split('.')[0]);

        Assert.Equal(Keywords.Current.OrderBy(w => w, StringComparer.Ordinal), current);
        Assert.Equal(Keywords.Future.OrderBy(w => w, StringComparer.Ordinal), future);
    }

    private static void AssertInside(SourceSpan span, string source, Diagnostic diagnostic, int round)
    {
        Assert.True(
            span.Start >= 0 && span.End >= span.Start && span.End <= source.Length && span.Line >= 1 && span.Column >= 1,
            $"round {round}: {diagnostic} has the range {span.Start}..{span.End} in a source of {source.Length} characters");
    }
}
