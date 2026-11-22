using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Lintel.Analysis;
using Lintel.Ifc;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lintel.Mcp;

/// <summary>The tool that looks up names of the IFC schema, so that an author does not write them from memory.</summary>
[McpServerToolType]
public sealed class SchemaTools
{
    private const int MaxLimit = 200;

    /// <summary>A property set made for at most this many classes counts as made for those classes, not for elements in general.</summary>
    private const int SpecificSetClasses = 24;

    [McpServerTool(Name = "lintel_list_properties", Title = "Look up the IFC schema", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
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
            foreach (var set in specific.Skip(offset).Take(limit))
            {
                text.Append("  ").Append(set.Name).Append(": ").Append(string.Join(", ", set.PropertyNames)).Append('\n');
            }

            text.Append(Page(specific.Count, limit, offset, "sets"));
        }

        if (general.Count > 0 && offset == 0)
        {
            text.Append($"Sets made for many classes, which {subject} can have too ({general.Count}): ");
            text.Append(string.Join(", ", general.Select(s => $"{s.Name} ({s.PropertyNames.Count})"))).Append(".\n");
        }

        text.Append("For data types, units and allowed values give 'propertySet', or 'search' with a part of a name.\n");
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

        var lines = set.PropertyNames.Select(property => Line(set, set.FindProperty(property)!)).ToList();
        foreach (var line in lines.Skip(offset).Take(limit))
        {
            text.Append(line).Append('\n');
        }

        text.Append(Page(lines.Count, limit, offset, "properties"));
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

        bool Matches(string text) => words.All(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        List<string> Find(IEnumerable<IfcPropertySet> sets) => sets
            .SelectMany(set => set.PropertyNames
                .Where(property => Matches(set.Name + "." + property))
                .Select(property => Line(set, set.FindProperty(property)!)))
            .ToList();

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
            foreach (var line in found.Skip(offset).Take(limit))
            {
                text.Append(line).Append('\n');
            }

            text.Append(Page(found.Count, limit, offset, "properties"));
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
            foreach (var line in elsewhere.Take(Math.Min(limit, 12)))
            {
                text.Append(line).Append('\n');
            }

            text.Append($"For {ifcClass.Name}, use a property set of your own, whose name does not start with Pset_ or Qto_.\n");
            return text.ToString();
        }

        text.Append($"Properties for {ifcClass.Name} in {schema.Version} that match '{search}' ({matches.Count}):\n");
        foreach (var line in matches.Skip(offset).Take(limit))
        {
            text.Append(line).Append('\n');
        }

        text.Append(Page(matches.Count, limit, offset, "properties"));
        return text.ToString();
    }

    private static string NothingFound(string search, string where) =>
        $"No property of the standard matches '{search}' {where}. The names are English and written without spaces, "
        + "such as FireRating or LoadBearing: try another word or a shorter part. "
        + "What the standard does not have goes into a property set of your own, whose name does not start with Pset_ or Qto_.\n";

    // ---------------------------------------------------------------- lines

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
