using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using IdsLib.IfcSchema;

namespace Bimlint.Ifc;

/// <summary>An IFC data type: its spelling, the XML base type of its values, and the SI unit IDS stores them in.</summary>
public sealed class IfcDataType
{
    internal IfcDataType(string name, string xmlBase, string? unit, IfcSchemaVersions versions)
    {
        Name = name;
        XmlBase = xmlBase;
        Unit = unit;
        Versions = versions;
    }

    /// <summary>The name as the IFC documentation spells it, such as <c>IfcLengthMeasure</c>.</summary>
    public string Name { get; }

    /// <summary><c>xs:string</c>, <c>xs:double</c>, <c>xs:integer</c>, <c>xs:boolean</c>, ...; empty when IDS defines none.</summary>
    public string XmlBase { get; }

    /// <summary>The SI unit as Bimlint writes it, such as <c>m</c> or <c>W/m2K</c>; <c>null</c> for a type without a unit.</summary>
    public string? Unit { get; }

    internal IfcSchemaVersions Versions { get; }

    public bool IsNumeric => XmlBase == "xs:double" || XmlBase == "xs:integer";

    public bool IsInteger => XmlBase == "xs:integer";

    public bool IsText => XmlBase == "xs:string";

    public bool IsBoolean => XmlBase == "xs:boolean";

    public bool IsValidIn(IfcSchema schema) => (Versions & schema.Flag) == schema.Flag;
}

/// <summary>The data types of IFC, by name.</summary>
public static class IfcDataTypes
{
    private static readonly Regex UnitForm = new(@"^[^\W\d]\w*(/[^\W\d]\w*)?$", RegexOptions.CultureInvariant);
    private static readonly Lazy<Dictionary<string, IfcDataType>> Types = new(Load);

    /// <summary>Every data type, as the IFC documentation spells it.</summary>
    public static IReadOnlyList<string> Names => Types.Value.Values.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The data type with this name in any case, or <c>null</c>.</summary>
    public static IfcDataType? Find(string name) =>
        Types.Value.TryGetValue(name.ToUpperInvariant(), out var type) ? type : null;

    private static Dictionary<string, IfcDataType> Load()
    {
        // ids-lib knows data types in upper case. The spelling of the documentation is collected from the places
        // where it names types: its list of class names, the enumerations and attribute types of each schema,
        // and the data types of the standard properties.
        var spelling = new Dictionary<string, string>(StringComparer.Ordinal);
        void Learn(string? name)
        {
            if (!string.IsNullOrEmpty(name) && name!.StartsWith("Ifc", StringComparison.Ordinal))
            {
                spelling[name.ToUpperInvariant()] = name;
            }
        }

        foreach (var name in SchemaInfo.AllConcreteClasses)
        {
            Learn(name.PascalCaseName);
        }

        foreach (var schema in new[] { SchemaInfo.SchemaIfc2x3, SchemaInfo.SchemaIfc4, SchemaInfo.SchemaIfc4x3 })
        {
            foreach (var classInfo in schema)
            {
                Learn(classInfo.Name);
                foreach (var attribute in classInfo.DirectAttributesInfo ?? Enumerable.Empty<AttributeInfo>())
                {
                    Learn(attribute.BaseType);
                }
            }

            foreach (var property in schema.PropertySets.SelectMany(set => set.Properties))
            {
                switch (property)
                {
                    case SingleValuePropertyType single:
                        Learn(single.DataType);
                        break;
                    case ListValuePropertyType list:
                        Learn(list.DataType);
                        break;
                    case TableValuePropertyType table:
                        Learn(table.DataType1);
                        Learn(table.DataType2);
                        break;
                }
            }
        }

        var types = new Dictionary<string, IfcDataType>(StringComparer.Ordinal);
        foreach (var info in SchemaInfo.AllDataTypes)
        {
            var upper = info.IfcDataTypeClassName.ToUpperInvariant();
            string? unit = null;
            if (info.Measure is not null)
            {
                // "W / m2 K" becomes W/m2K. A unit that cannot be written as a name with one slash ("1 / K") is left out:
                // such a measure takes a bare number in SI.
                var symbol = info.Measure.DefaultDisplay.Replace(" ", string.Empty).Replace("°", string.Empty);
                if (UnitForm.IsMatch(symbol))
                {
                    unit = symbol;
                }
            }

            types[upper] = new IfcDataType(
                spelling.TryGetValue(upper, out var spelled) ? spelled : info.IfcDataTypeClassName,
                info.BackingType ?? string.Empty,
                unit,
                info.ValidSchemaVersions);
        }

        return types;
    }
}

/// <summary>
/// The units an author may write. IDS stores every measure in its SI unit; the compiler converts.
/// </summary>
public static class Units
{
    private sealed class Alternative
    {
        public Alternative(string symbol, decimal factor, decimal offset = 0m, bool irrational = false)
        {
            Symbol = symbol;
            Factor = factor;
            Offset = offset;
            Irrational = irrational;
        }

        public string Symbol { get; }

        public decimal Factor { get; }

        public decimal Offset { get; }

        /// <summary>The factor is not a decimal fraction (degrees to radians): the result is computed in floating point.</summary>
        public bool Irrational { get; }
    }

    // SI unit of the measure -> the other units that may be written for it.
    private static readonly Dictionary<string, Alternative[]> Alternatives = new(StringComparer.Ordinal)
    {
        ["m"] = new[] { new Alternative("cm", 0.01m), new Alternative("mm", 0.001m) },
        ["m2"] = new[] { new Alternative("cm2", 0.0001m), new Alternative("mm2", 0.000001m) },
        ["rad"] = new[] { new Alternative("deg", 0m, irrational: true) },
        ["kg"] = new[] { new Alternative("g", 0.001m), new Alternative("t", 1000m) },
        ["s"] = new[] { new Alternative("min", 60m), new Alternative("h", 3600m), new Alternative("d", 86400m) },
        ["K"] = new[] { new Alternative("degC", 1m, 273.15m) },
        ["W"] = new[] { new Alternative("kW", 1000m) },
        ["Pa"] = new[] { new Alternative("kPa", 1000m), new Alternative("MPa", 1000000m) },
        ["N"] = new[] { new Alternative("kN", 1000m) },
    };

    /// <summary>The units that may be written for a data type: its SI unit first. Empty for a type without a unit.</summary>
    public static IReadOnlyList<string> For(IfcDataType type)
    {
        if (type.Unit is null)
        {
            return Array.Empty<string>();
        }

        var units = new List<string> { type.Unit };
        if (Alternatives.TryGetValue(type.Unit, out var alternatives))
        {
            units.AddRange(alternatives.Select(a => a.Symbol));
        }

        return units;
    }

    /// <summary>
    /// The number in the SI unit of the data type, as IDS stores it. A number that is written in the SI unit is
    /// returned unchanged, digit for digit. <c>null</c> when the unit does not belong to the data type.
    /// </summary>
    public static string? ToSi(string number, string unit, IfcDataType type)
    {
        if (type.Unit is null)
        {
            return null;
        }

        if (unit == type.Unit)
        {
            return number;
        }

        if (!Alternatives.TryGetValue(type.Unit, out var alternatives))
        {
            return null;
        }

        var alternative = alternatives.FirstOrDefault(a => a.Symbol == unit);
        if (alternative is null)
        {
            return null;
        }

        if (alternative.Irrational)
        {
            var degrees = double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
            return (degrees * Math.PI / 180.0).ToString("R", CultureInfo.InvariantCulture);
        }

        if (decimal.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            try
            {
                return Plain(value * alternative.Factor + alternative.Offset);
            }
            catch (OverflowException)
            {
                // fall through to floating point
            }
        }

        var approximate = double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (approximate * (double)alternative.Factor + (double)alternative.Offset).ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A decimal without the zeros that the multiplication left behind: 0.150 becomes 0.15.</summary>
    private static string Plain(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (text.IndexOf('.') >= 0)
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text.Length == 0 || text == "-" ? "0" : text;
    }
}
