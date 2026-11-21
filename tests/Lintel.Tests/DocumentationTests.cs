using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Lintel.Tests;

/// <summary>
/// The examples of the documentation are checked, so the documentation cannot drift from the language.
/// A block marked <c>lintel</c> is a whole file; a block marked <c>lintel fragment</c> gets the header <c>ifc IFC4</c>.
/// </summary>
public class DocumentationTests
{
    private static readonly Regex Block = new(@"^```lintel(?<fragment> fragment)?[ \t]*\r?\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline);

    public static IEnumerable<object[]> ExampleFiles() =>
        Directory.GetFiles(Path.Combine(TestSupport.RepositoryRoot, "examples"), "*.lintel")
            .Select(path => new object[] { Path.GetFileName(path) });

    public static IEnumerable<object[]> Documents() =>
        new[] { "SPEC.md", "LLM.md", "README.md" }
            .Where(name => File.Exists(Path.Combine(TestSupport.RepositoryRoot, name)))
            .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Example_files_have_no_diagnostics(string file)
    {
        var source = File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot, "examples", file));

        var result = LintelCompiler.Check(source);

        Assert.True(result.Diagnostics.Count == 0, result.Describe());
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void Code_blocks_of_the_documentation_have_no_diagnostics(string document)
    {
        var text = File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot, document));
        var blocks = Block.Matches(text).Cast<Match>().ToList();
        Assert.NotEmpty(blocks);

        foreach (var block in blocks)
        {
            var code = block.Groups["code"].Value;
            var source = block.Groups["fragment"].Success ? TestSupport.WithHeader(code) : code;

            var result = LintelCompiler.Check(source);

            Assert.True(result.Diagnostics.Count == 0, $"{document}:\n{code}\n{result.Describe()}");
        }
    }

    [Fact]
    public void The_showcase_uses_every_keyword()
    {
        var source = File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot, "examples", "showcase.lintel"));
        var words = new HashSet<string>(Regex.Matches(source, @"[a-z]+").Cast<Match>().Select(m => m.Value));

        // 'import' and 'copyright' etc. are plain header statements; everything that shapes a rule must be shown.
        var shown = new[]
        {
            "ifc", "title", "define", "rule", "source", "description", "instructions", "for", "any", "only", "type",
            "where", "and", "require", "optional", "prohibit", "count", "entity", "attribute", "property",
            "classification", "material", "part", "of", "via", "datatype", "in", "matches", "between", "length",
            "note", "uri", "true",
        };

        var missing = shown.Where(word => !words.Contains(word)).ToList();
        Assert.True(missing.Count == 0, "not shown: " + string.Join(", ", missing));
    }
}
