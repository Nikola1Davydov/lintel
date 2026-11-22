using System.IO;

namespace Lintel.Mcp;

/// <summary>The texts that the server gives an agent besides the descriptions of its tools.</summary>
internal static class ServerText
{
    /// <summary>What the server is for and in which order its tools are used. A client shows it to the model once.</summary>
    public const string Instructions =
        "Lintel is a small language for information requirements on IFC building models. It compiles to buildingSMART IDS 1.0. "
        + "To turn requirements into rules: "
        + "1. Read lintel_language_guide once. "
        + "2. Look up the names of property sets, properties, attributes and predefined types with lintel_list_properties; do not write them from memory. "
        + "3. Write one rule for each requirement, with its original wording in 'source'. "
        + "4. Check with lintel_validate_rules until it reports nothing; when the answer has 'fixed', continue with that text. "
        + "5. Write the IDS file with lintel_compile_rules. "
        + "lintel_import_ids turns an existing IDS file into Lintel. "
        + "A person reviews and signs the rules: leave out what IDS cannot express and say so in a comment, never approximate it.";

    private static string? _guide;

    /// <summary>The language on one page: the file LLM.md of the repository, which is built into the server.</summary>
    public static string Guide
    {
        get
        {
            if (_guide is null)
            {
                using var stream = typeof(ServerText).Assembly.GetManifestResourceStream("Lintel.Mcp.LLM.md")
                    ?? throw new InvalidDataException("The language guide is missing from the server.");
                using var reader = new StreamReader(stream);
                _guide = reader.ReadToEnd();
            }

            return _guide;
        }
    }
}
