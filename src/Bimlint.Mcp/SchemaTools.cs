using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Bimlint.Analysis;
using Bimlint.Ifc;
using Bimlint.Syntax;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Bimlint.Mcp;

/// <summary>The tool that looks up names of the IFC schema, so that an author does not write them from memory.</summary>
[McpServerToolType]
public sealed class SchemaTools
{
    private const int MaxLimit = 200;

    /// <summary>A property set made for at most this many classes counts as made for those classes, not for elements in general.</summary>
    private const int SpecificSetClasses = 24;

    /// <summary>A page of at most this many properties also says what each one means.</summary>
    private const int MaxLinesWithMeaning = 25;

    private const int MaxMeaningCharacters = 160;

    [McpServerTool(Name = "bimlint_list_properties", Title = "Look up the IFC schema", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Looks up the IFC schema, so that rules use names that exist: the property sets and properties of the IFC "
        + "standard with their data types, units and allowed values, and the attributes, predefined types and subtypes "
        + "of a class. Give 'class' (IfcDoor) for an overview of the class and its property sets; add 'search' (fire) "
        + "to find properties by a part of their name; give 'propertySet' (Pset_DoorCommon) for every property of one "
        + "set. The names are English, as in IFC. A property that the standard does not have belongs to a property "
        + "set of your own, whose name does not start with Pset_ or Qto_.")]
    public static CallToolResult ListProperties(
        [Description("An IFC class, such as IfcDoor: its attributes, predefined types, subtypes and property sets.")] string? @class = null,
        [Description("A property set of the standard, such as Pset_DoorCommon: every property with its data type.")] string? propertySet = null,
        [Description("Words that the name of the set or the property contains, such as 'fire' or 'load bearing'.")] string? search = null,
        [Description("The IFC version of the rule file: IFC2X3, IFC4 or IFC4X3_ADD2.")] string ifc = "IFC4",
        [Description("The most lines to return, 1 to 200.")] int limit = 60,
        [Description("The number of lines to skip, for the next page.")] int offset = 0) =>
        Answer.Of(() => Answer.Done(Describe(@class, propertySet, search, ifc, limit, offset)));

    /// <summary>The answer of the tool as text.</summary>
    internal static string Describe(string? @class, string? propertySet, string? search, string ifc, int limit, int offset)
    {
        var schema = Schema(ifc);
        limit = Math.Min(Math.Max(limit, 1), MaxLimit);
        offset = Math.Max(offset, 0);

        var ifcClass = string.IsNullOrWhiteSpace(@class) ? null : Class(schema, @class!.Trim());
        if (!string.IsNullOrWhiteSpace(propertySet))
        {
            return DescribeSet(schema, propertySet!.Trim(), ifcClass, limit, offset);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            return Search(schema, search!, ifcClass, limit, offset);
        }

        if (ifcClass is not null)
        {
            return DescribeClass(schema, ifcClass, limit, offset);
        }

        throw new ToolProblem("Give a 'class' (IfcDoor), a 'propertySet' (Pset_DoorCommon) or a 'search' text (fire).");
    }

    // ---------------------------------------------------------------- lookups

    private static IfcSchema Schema(string version)
    {
        var name = (version ?? string.Empty).Trim().ToUpperInvariant();
        if (name == "IFC4X3")
        {
            name = "IFC4X3_ADD2";
        }

        return IfcSchema.Get(name)
            ?? throw new ToolProblem($"'{version}' is not an IFC version of IDS 1.0. Use IFC2X3, IFC4 or IFC4X3_ADD2.");
    }

    private static IfcClass Class(IfcSchema schema, string name)
    {
        if (schema.FindClassIgnoreCase(name) is { } found)
        {
            return found;
        }

        var nearest = Suggestions.Nearest(name, schema.ClassNames);
        var elsewhere = new[] { "IFC2X3", "IFC4", "IFC4X3_ADD2" }
            .Where(v => v != schema.Version && IfcSchema.Get(v)?.FindClassIgnoreCase(name) is not null)
            .ToList();
        var message = new StringBuilder($"{name} is not a class of {schema.Version}.");
        if (elsewhere.Count > 0)
        {
            message.Append($" It exists in {string.Join(" and ", elsewhere)}.");
        }

        message.Append(nearest is not null ? $" Did you mean {nearest}?" : string.Empty);
        message.Append(" Similar names: " + string.Join(", ", Suggestions.Top(name, schema.ClassNames, 8)) + ".");
        throw new ToolProblem(message.ToString());
    }

    /// <summary>
    /// The property sets of the standard that every element of the class can have, the most specific first:
    /// a set made for doors before a set made for every element.
    /// </summary>
    private static List<IfcPropertySet> SetsOf(IfcSchema schema, IfcClass ifcClass)
    {
        var concrete = ifcClass.ConcreteClasses;
        if (concrete.Count == 0)
        {
            return new List<IfcPropertySet>();
        }

        return schema.PropertySetNames
            .Select(name => schema.FindPropertySet(name)!)
            .Select(set => (Set: set, Classes: schema.ClassesOf(set)))
            .Where(x => concrete.All(x.Classes.Contains))
            .OrderBy(x => x.Classes.Count)
            .ThenBy(x => x.Set.Name, StringComparer.Ordinal)
            .Select(x => x.Set)
            .ToList();
    }

    // ---------------------------------------------------------------- a class

    private static string DescribeClass(IfcSchema schema, IfcClass ifcClass, int limit, int offset)
    {
        var text = new StringBuilder();
        text.Append(ifcClass.Name).Append(" in ").Append(schema.Version).Append('\n');

        var subtypes = ifcClass.ConcreteClasses.Where(c => c != ifcClass.Name).ToList();
        if (ifcClass.IsAbstract)
        {
            text.Append($"Abstract: 'for {ifcClass.Name}' selects its {subtypes.Count} subtypes, such as {Sample(subtypes, 6)}.\n");
        }
        else if (subtypes.Count > 0)
        {
            text.Append($"Subtypes, which 'for {ifcClass.Name}' includes: {Sample(subtypes, 12)}.\n");
        }

        if (ifcClass.PredefinedTypes.Count > 0)
        {
            text.Append($"Predefined types, written 'for {ifcClass.Name} type {ifcClass.PredefinedTypes[0]}': {string.Join(", ", ifcClass.PredefinedTypes)}. A type of your own is quoted.\n");
        }
        else
        {
            text.Append("No predefined type.\n");
        }

        var attributes = ifcClass.Attributes.Values
            .Where(a => a.XmlBase.Length > 0 && !a.IsCollection && a.Name != "PredefinedType")
            .Select(a => $"{a.Name} ({Kind(a)})")
            .ToList();
        text.Append("Attributes, written 'attribute Name': ").Append(string.Join(", ", attributes)).Append(".\n");

        var sets = SetsOf(schema, ifcClass);
        if (sets.Count == 0)
        {
            text.Append("The standard has no property set for this class. Use a property set of your own.\n");
            return text.ToString();
        }

        // The sets made for this class show their properties; a set made for every element is only named.
        var specific = sets.Where(s => schema.ClassesOf(s).Count <= SpecificSetClasses).ToList();
        var general = sets.Where(s => schema.ClassesOf(s).Count > SpecificSetClasses).ToList();
        var subject = ifcClass.IsAbstract ? "every subtype of " + ifcClass.Name : ifcClass.Name;
        if (specific.Count > 0)
        {
            text.Append($"Property sets of the standard made for {subject} ({specific.Count}), with their properties:\n");
            var marked = false;
            foreach (var set in specific.Skip(offset).Take(limit))
            {
                var names = set.PropertyNames.Select(property =>
                {
                    var odd = OtherSpelling(schema, set, property) is not null;
                    marked |= odd;
                    return odd ? property + " (sic)" : property;
                });
                text.Append("  ").Append(set.Name).Append(": ").Append(string.Join(", ", names)).Append('\n');
            }

            if (marked)
            {
                text.Append("(sic): the standard writes this name exactly so in this version, and differently in another; write it as it stands.\n");
            }

            text.Append(Page(specific.Count, limit, offset, "sets"));
        }

        if (general.Count > 0 && offset == 0)
        {
            text.Append($"Sets made for many classes, which {subject} can have too ({general.Count}): ");
            text.Append(string.Join(", ", general.Select(s => $"{s.Name} ({s.PropertyNames.Count})"))).Append(".\n");
        }

        text.Append("For data types, units, allowed values and what a property means give 'propertySet', or 'search' with a part of a name.\n");
        return text.ToString();
    }

    private static string Kind(IfcAttribute attribute)
    {
        var type = IfcDataTypes.Find(attribute.IfcType);
        var units = type is null ? Array.Empty<string>() : Units.For(type);
        return units.Count > 0 ? $"{attribute.IfcType}, unit {string.Join(", ", units)}" : attribute.IfcType;
    }

    // ---------------------------------------------------------------- a property set

    private static string DescribeSet(IfcSchema schema, string name, IfcClass? ifcClass, int limit, int offset)
    {
        var set = schema.FindPropertySet(name)
            ?? schema.PropertySetNames
                .Where(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                .Select(n => schema.FindPropertySet(n))
                .FirstOrDefault();
        if (set is null)
        {
            var nearest = Suggestions.Nearest(name, schema.PropertySetNames);
            var candidates = ifcClass is null
                ? Suggestions.Top(name, schema.PropertySetNames, 8)
                : SetsOf(schema, ifcClass).Select(s => s.Name).Take(12).ToList();
            throw new ToolProblem(
                $"{name} is not a property set of the standard in {schema.Version}."
                + (nearest is not null ? $" Did you mean {nearest}?" : string.Empty)
                + (ifcClass is null ? " Similar names: " : $" The sets for {ifcClass.Name}: ")
                + string.Join(", ", candidates) + "."
                + " A set of your own needs no lookup: give it a name that does not start with Pset_ or Qto_.");
        }

        var text = new StringBuilder();
        text.Append($"{set.Name} in {schema.Version}, made for {string.Join(", ", set.ApplicableClasses)} with subtypes and type objects.\n");
        if (ifcClass is not null && !ifcClass.ConcreteClasses.Any(schema.ClassesOf(set).Contains))
        {
            var own = SetsOf(schema, ifcClass).Select(s => s.Name).Take(12).ToList();
            text.Append($"It is NOT made for {ifcClass.Name}: a rule 'for {ifcClass.Name}' that requires it is rejected. The sets for {ifcClass.Name}: {string.Join(", ", own)}.\n");
        }

        var hits = set.PropertyNames.Select(property => new Hit(set, set.FindProperty(property)!, false)).ToList();
        AppendLines(text, schema, hits, limit, offset);
        return text.ToString();
    }

    // ---------------------------------------------------------------- a search

    private static string Search(IfcSchema schema, string search, IfcClass? ifcClass, int limit, int offset)
    {
        var words = search.Split(new[] { ' ', '\t', '\n', ',', '.', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            throw new ToolProblem("The search text is empty. Give a part of a name, such as 'fire'.");
        }

        // The names of the standard have slips of the pen, such as NetHeigtht for the net height of a storey.
        // A word that is one letter away from a word of the name finds it too; such a line says so.
        List<Hit> Find(IEnumerable<IfcPropertySet> sets)
        {
            var exact = new List<Hit>();
            var near = new List<Hit>();
            foreach (var set in sets)
            {
                foreach (var property in set.PropertyNames)
                {
                    var name = set.Name + "." + property;
                    if (words.All(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        exact.Add(new Hit(set, set.FindProperty(property)!, false));
                    }
                    else if (words.All(w => name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || IsNearWordOf(w, name)))
                    {
                        near.Add(new Hit(set, set.FindProperty(property)!, true));
                    }
                }
            }

            exact.AddRange(near);
            return exact;
        }

        var all = schema.PropertySetNames.Select(name => schema.FindPropertySet(name)!).ToList();
        var text = new StringBuilder();
        if (ifcClass is null)
        {
            var found = Find(all);
            if (found.Count == 0)
            {
                return NothingFound(search, $"in {schema.Version}");
            }

            text.Append($"Properties of the standard in {schema.Version} that match '{search}' ({found.Count}). Give 'class' to see only those of one class.\n");
            AppendLines(text, schema, found, limit, offset);
            return text.ToString();
        }

        var own = SetsOf(schema, ifcClass);
        var matches = Find(own);
        if (matches.Count == 0)
        {
            // The property may exist for another class: say so, because requiring it for this class is an error.
            var ownNames = new HashSet<string>(own.Select(s => s.Name), StringComparer.Ordinal);
            var elsewhere = Find(all.Where(s => !ownNames.Contains(s.Name)));
            if (elsewhere.Count == 0)
            {
                return NothingFound(search, $"for {ifcClass.Name} in {schema.Version}");
            }

            text.Append($"No property of the sets for {ifcClass.Name} in {schema.Version} matches '{search}'. Sets made for other classes have {elsewhere.Count}; they cannot be required for {ifcClass.Name}:\n");
            foreach (var hit in elsewhere.Take(Math.Min(limit, 12)))
            {
                text.Append(Line(hit.Set, hit.Property)).Append('\n');
            }

            text.Append($"For {ifcClass.Name}, use a property set of your own, whose name does not start with Pset_ or Qto_; such a set needs no lookup.\n");
            return text.ToString();
        }

        text.Append($"Properties for {ifcClass.Name} in {schema.Version} that match '{search}' ({matches.Count}):\n");
        AppendLines(text, schema, matches, limit, offset);
        return text.ToString();
    }

    /// <summary>True when the word is one letter away from one of the words that the name is made of.</summary>
    private static bool IsNearWordOf(string word, string name)
    {
        if (word.Length < 5)
        {
            return false;
        }

        var lower = word.ToLowerInvariant();
        return WordsOf(name).Any(part => Math.Abs(part.Length - lower.Length) <= 1 && EditDistance.Of(part, lower) <= 1);
    }

    /// <summary>The words of a name such as Qto_SpaceBaseQuantities.NetFloorArea, in lower case.</summary>
    private static IEnumerable<string> WordsOf(string name)
    {
        var word = new StringBuilder();
        foreach (var c in name)
        {
            if (!char.IsLetter(c) || (char.IsUpper(c) && word.Length > 0 && char.IsLower(word[word.Length - 1])))
            {
                if (word.Length > 0)
                {
                    yield return word.ToString().ToLowerInvariant();
                    word.Clear();
                }
            }

            if (char.IsLetter(c))
            {
                word.Append(c);
            }
        }

        if (word.Length > 0)
        {
            yield return word.ToString().ToLowerInvariant();
        }
    }

    private static string NothingFound(string search, string where) =>
        $"No property of the standard matches '{search}' {where}. The names are English and written without spaces, "
        + "such as FireRating or LoadBearing: try another word or a shorter part. "
        + "What the standard does not have goes into a property set of your own, whose name does not start with Pset_ or Qto_; "
        + "such a set needs no lookup.\n";

    // ---------------------------------------------------------------- lines

    /// <summary>A property that a lookup found; <see cref="Near"/> when only a similar spelling found it.</summary>
    private sealed class Hit
    {
        public Hit(IfcPropertySet set, IfcProperty property, bool near)
        {
            Set = set;
            Property = property;
            Near = near;
        }

        public IfcPropertySet Set { get; }

        public IfcProperty Property { get; }

        public bool Near { get; }
    }

    /// <summary>
    /// One page of properties, a line each: the name as a rule writes it, the data type and the way its value is
    /// written, a remark when the standard spells the name oddly, and on a short page what the property means.
    /// </summary>
    private static void AppendLines(StringBuilder text, IfcSchema schema, IReadOnlyList<Hit> hits, int limit, int offset)
    {
        var page = hits.Skip(offset).Take(limit).ToList();
        var withMeaning = page.Count <= MaxLinesWithMeaning;
        foreach (var hit in page)
        {
            text.Append(Line(hit.Set, hit.Property));
            if (OtherSpelling(schema, hit.Set, hit.Property.Name) is { } other)
            {
                text.Append($". Written exactly so in {schema.Version} ({other})");
            }
            else if (hit.Near)
            {
                text.Append(". Found by a similar spelling");
            }

            if (withMeaning && Meaning(hit.Property) is { Length: > 0 } meaning)
            {
                text.Append(". Meaning: ").Append(meaning);
            }

            text.Append('\n');
        }

        if (!withMeaning && page.Any(h => h.Property.Definition.Length > 0))
        {
            text.Append("Narrow the search, or give 'propertySet', to see what each property means.\n");
        }

        text.Append(Page(hits.Count, limit, offset, "properties"));
    }

    /// <summary>
    /// The standard has slips of the pen that a later version repairs, such as NetHeigtht for the net height of a
    /// storey in IFC4. When another version has the same set with a name that differs by a letter or two, this
    /// names that version and its spelling; an author who knows the other spelling then trusts the odd one.
    /// </summary>
    private static string? OtherSpelling(IfcSchema schema, IfcPropertySet set, string property)
    {
        if (property.Length < 5)
        {
            return null;
        }

        var lower = property.ToLowerInvariant();
        foreach (var version in new[] { "IFC2X3", "IFC4", "IFC4X3_ADD2" }.Where(v => v != schema.Version))
        {
            var other = IfcSchema.Get(version)?.FindPropertySet(set.Name);
            if (other is null || other.FindProperty(property) is not null)
            {
                continue;
            }

            var twin = other.PropertyNames.FirstOrDefault(name =>
                Math.Abs(name.Length - property.Length) <= 2
                && set.FindProperty(name) is null
                && EditDistance.Of(name.ToLowerInvariant(), lower) <= 2);
            if (twin is not null)
            {
                return $"{version}: {twin}";
            }
        }

        return null;
    }

    /// <summary>What a property means: the first sentence of its definition in the standard, on one line.</summary>
    private static string Meaning(IfcProperty property)
    {
        var words = property.Definition.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var line = string.Join(" ", words);
        var end = line.IndexOf(". ", StringComparison.Ordinal);
        if (end >= 20)
        {
            line = line.Substring(0, end);
        }

        line = line.TrimEnd('.');
        return line.Length <= MaxMeaningCharacters ? line : line.Substring(0, MaxMeaningCharacters).TrimEnd() + "...";
    }

    /// <summary>One property as it is written in a rule, with its data type and the way its value is written.</summary>
    private static string Line(IfcPropertySet set, IfcProperty property)
    {
        var name = set.Name + "." + property.Name;
        if (property.AllowedValues is { Count: > 0 } values)
        {
            return $"{name}: {string.Join(" or ", property.DataTypes)}, one of {string.Join(", ", values.Select(v => "\"" + v + "\""))}";
        }

        if (property.DataTypes.Count != 1)
        {
            return $"{name}: {string.Join(" or ", property.DataTypes)}; write the one you mean after 'datatype'";
        }

        var type = IfcDataTypes.Find(property.DataTypes[0]);
        return type is null ? $"{name}: {property.DataTypes[0]}" : $"{name}: {type.Name}, {Written(type)}";
    }

    /// <summary>How a value of a data type is written in a rule.</summary>
    private static string Written(IfcDataType type)
    {
        var units = Units.For(type);
        if (units.Count > 0)
        {
            return "a number with its unit: " + string.Join(", ", units);
        }

        // IDS takes a logical value as text and does not say how its three values are spelled.
        if (type.Name == "IfcLogical")
        {
            return "true, false or unknown, which IDS compares as text without fixing the spelling: require it without a value";
        }

        return type.XmlBase switch
        {
            "xs:string" => "text in quotes",
            "xs:boolean" => "true or false",
            "xs:integer" => "a whole number",
            "xs:double" => "a number",
            "xs:date" => "a date as text, \"2026-11-22\"",
            "xs:dateTime" => "a date with time as text, \"2026-11-22T10:00:00\"",
            "xs:time" => "a time as text, \"10:00:00\"",
            "xs:duration" => "a duration as text, \"P1D\"",
            _ => "text in quotes",
        };
    }

    private static string Sample(IReadOnlyList<string> names, int count) =>
        names.Count <= count ? string.Join(", ", names) : string.Join(", ", names.Take(count)) + $" and {names.Count - count} more";

    private static string Page(int total, int limit, int offset, string what)
    {
        if (offset >= total && total > 0)
        {
            return $"Nothing from line {offset + 1} on: there are {total} {what}.\n";
        }

        var last = Math.Min(offset + limit, total);
        return last < total || offset > 0
            ? $"Lines {offset + 1} to {last} of {total} {what}." + (last < total ? $" For the next page give offset: {last}.\n" : "\n")
            : string.Empty;
    }
}
