using System.Collections.Generic;
using System.Linq;
using Lintel.Ifc;
using Lintel.Model;
using Xunit;

namespace Lintel.Tests;

/// <summary>The IFC schema as the compiler sees it through ids-lib.</summary>
public class SchemaTests
{
    private static readonly IfcSchema Ifc2x3 = IfcSchema.Get("IFC2X3")!;
    private static readonly IfcSchema Ifc4 = IfcSchema.Get("IFC4")!;
    private static readonly IfcSchema Ifc4x3 = IfcSchema.Get("IFC4X3_ADD2")!;

    private static Matcher Classes(bool only, params string[] names)
    {
        var matcher = new Matcher(names.Length > 1 ? MatcherKind.AnyOf : MatcherKind.Exact, default) { Only = only };
        matcher.Names.AddRange(names.Select(n => new NameItem(n, false, default)));
        return matcher;
    }

    [Fact]
    public void Versions()
    {
        Assert.NotNull(Ifc2x3);
        Assert.NotNull(Ifc4);
        Assert.NotNull(Ifc4x3);
        Assert.Null(IfcSchema.Get("IFC5"));
    }

    [Fact]
    public void A_class_knows_its_subtypes_predefined_types_and_attributes()
    {
        var wall = Ifc4.FindClass("IfcWall")!;

        Assert.False(wall.IsAbstract);
        Assert.Equal(new[] { "IfcWall", "IfcWallElementedCase", "IfcWallStandardCase" }, wall.ConcreteClasses);
        Assert.Contains("SHEAR", wall.PredefinedTypes);
        Assert.Contains("USERDEFINED", wall.PredefinedTypes);
        Assert.Equal("xs:string", wall.Attributes["Name"].XmlBase);
        Assert.True(wall.IsA("IfcElement"));

        var element = Ifc4.FindClass("IfcElement")!;
        Assert.True(element.IsAbstract);
        Assert.DoesNotContain("IfcElement", element.ConcreteClasses);
        Assert.Contains("IfcWall", element.ConcreteClasses);
        Assert.Null(Ifc4.FindClass("IFCWALL"));
        Assert.Equal("IfcWall", Ifc4.FindClassIgnoreCase("IFCWALL")!.Name);
    }

    [Fact]
    public void Classes_differ_between_versions()
    {
        Assert.NotNull(Ifc4.FindClass("IfcBuildingElement"));
        Assert.Null(Ifc4x3.FindClass("IfcBuildingElement"));
        Assert.NotNull(Ifc4x3.FindClass("IfcBuiltElement"));
        Assert.Equal(new[] { "IfcWall", "IfcWallStandardCase" }, Ifc2x3.FindClass("IfcWall")!.ConcreteClasses);
    }

    [Fact]
    public void Standard_property_sets()
    {
        var set = Ifc4.FindPropertySet("Pset_WallCommon")!;

        Assert.Equal(new[] { "IfcWall" }, set.ApplicableClasses);
        Assert.Equal(new[] { "IfcLabel" }, set.FindProperty("FireRating")!.DataTypes);
        Assert.Equal(new[] { "IfcBoolean" }, set.FindProperty("LoadBearing")!.DataTypes);
        Assert.Equal(new[] { "IfcThermalTransmittanceMeasure" }, set.FindProperty("ThermalTransmittance")!.DataTypes);
        Assert.Contains("NEW", set.FindProperty("Status")!.AllowedValues!);
        Assert.Equal(new[] { "IfcLengthMeasure" }, Ifc4.FindPropertySet("Qto_SlabBaseQuantities")!.FindProperty("Width")!.DataTypes);
        Assert.Null(Ifc4.FindPropertySet("Pset_Nonsense"));
    }

    [Theory]
    [InlineData("IfcLabel", "xs:string", null)]
    [InlineData("IfcBoolean", "xs:boolean", null)]
    [InlineData("IfcInteger", "xs:integer", null)]
    [InlineData("IfcReal", "xs:double", null)]
    [InlineData("IfcLengthMeasure", "xs:double", "m")]
    [InlineData("IfcAreaMeasure", "xs:double", "m2")]
    [InlineData("IfcPlaneAngleMeasure", "xs:double", "rad")]
    [InlineData("IfcThermalTransmittanceMeasure", "xs:double", "W/m2K")]
    [InlineData("IfcThermodynamicTemperatureMeasure", "xs:double", "K")]
    [InlineData("IfcCountMeasure", "xs:integer", null)]
    public void Data_types(string name, string xmlBase, string? unit)
    {
        var type = IfcDataTypes.Find(name.ToUpperInvariant())!;

        Assert.Equal(name, type.Name);
        Assert.Equal(xmlBase, type.XmlBase);
        Assert.Equal(unit, type.Unit);
    }

    [Fact]
    public void Every_data_type_is_spelled_as_in_the_documentation()
    {
        var upperCase = IfcDataTypes.Names.Where(n => n == n.ToUpperInvariant()).ToList();

        Assert.True(upperCase.Count == 0, "no spelling for: " + string.Join(", ", upperCase));
    }

    [Theory]
    [InlineData("150", "mm", "IfcLengthMeasure", "0.15")]
    [InlineData("0.15", "m", "IfcLengthMeasure", "0.15")]
    [InlineData("0.150", "m", "IfcLengthMeasure", "0.150")]
    [InlineData("40", "cm", "IfcLengthMeasure", "0.4")]
    [InlineData("2500", "mm2", "IfcAreaMeasure", "0.0025")]
    [InlineData("180", "deg", "IfcPlaneAngleMeasure", "3.141592653589793")]
    [InlineData("1.5", "t", "IfcMassMeasure", "1500")]
    [InlineData("2", "h", "IfcTimeMeasure", "7200")]
    [InlineData("20", "degC", "IfcThermodynamicTemperatureMeasure", "293.15")]
    [InlineData("1", "kW", "IfcPowerMeasure", "1000")]
    [InlineData("0.24", "W/m2K", "IfcThermalTransmittanceMeasure", "0.24")]
    [InlineData("1e3", "mm", "IfcLengthMeasure", "1")]
    [InlineData("-5", "mm", "IfcLengthMeasure", "-0.005")]
    public void Units_are_converted_to_SI(string number, string unit, string type, string expected)
    {
        Assert.Equal(expected, Units.ToSi(number, unit, IfcDataTypes.Find(type)!));
    }

    [Fact]
    public void A_unit_of_another_measure_is_refused()
    {
        Assert.Null(Units.ToSi("5", "kg", IfcDataTypes.Find("IfcLengthMeasure")!));
        Assert.Null(Units.ToSi("5", "m", IfcDataTypes.Find("IfcLabel")!));
        Assert.Equal(new[] { "m", "cm", "mm" }, Units.For(IfcDataTypes.Find("IfcLengthMeasure")!));
        Assert.Empty(Units.For(IfcDataTypes.Find("IfcReal")!));
    }

    [Fact]
    public void A_class_selects_its_subtypes_and_only_selects_itself()
    {
        var schemas = new[] { Ifc4 };

        Assert.Equal(new[] { "IFCWALL", "IFCWALLELEMENTEDCASE", "IFCWALLSTANDARDCASE" }, Resolver.ExpandClasses(Classes(false, "IfcWall"), schemas));
        Assert.Equal(new[] { "IFCWALL" }, Resolver.ExpandClasses(Classes(true, "IfcWall"), schemas));
        Assert.Equal(new[] { "IFCDOOR", "IFCDOORSTANDARDCASE", "IFCWINDOW", "IFCWINDOWSTANDARDCASE" }, Resolver.ExpandClasses(Classes(false, "IfcDoor", "IfcWindow"), schemas));
        Assert.Equal(new[] { "IFCSPACE" }, Resolver.ExpandClasses(Classes(false, "IfcSpace"), schemas));
        Assert.True(Resolver.ExpandClasses(Classes(false, "IfcElement"), schemas).Count > 100);
    }

    [Fact]
    public void Subtypes_of_several_versions_are_joined()
    {
        Assert.Equal(
            new[] { "IFCWALL", "IFCWALLELEMENTEDCASE", "IFCWALLSTANDARDCASE" },
            Resolver.ExpandClasses(Classes(false, "IfcWall"), new[] { Ifc2x3, Ifc4 }));
        Assert.Equal(new[] { "IFCWALL", "IFCWALLSTANDARDCASE" }, Resolver.ExpandClasses(Classes(false, "IfcWall"), new[] { Ifc2x3 }));
    }

    [Fact]
    public void A_list_of_classes_folds_back_to_the_names_that_select_it()
    {
        var schemas = new[] { Ifc4 };

        Assert.Equal(new[] { "IfcWall" }, Resolver.FoldClasses(new[] { "IFCWALL", "IFCWALLELEMENTEDCASE", "IFCWALLSTANDARDCASE" }, schemas));
        Assert.Equal(new[] { "IfcSpace" }, Resolver.FoldClasses(new[] { "IFCSPACE" }, schemas));
        Assert.Equal(new[] { "IfcDoor", "IfcWindow" }, Resolver.FoldClasses(new[] { "IFCDOOR", "IFCDOORSTANDARDCASE", "IFCWINDOW", "IFCWINDOWSTANDARDCASE" }, schemas));

        // not the whole family, another order, or an unknown name: the list stays as written
        Assert.Null(Resolver.FoldClasses(new[] { "IFCWALL" }, schemas));
        Assert.Null(Resolver.FoldClasses(new[] { "IFCWALL", "IFCWALLSTANDARDCASE" }, schemas));
        Assert.Null(Resolver.FoldClasses(new[] { "IFCWALLSTANDARDCASE", "IFCWALL", "IFCWALLELEMENTEDCASE" }, schemas));
        Assert.Null(Resolver.FoldClasses(new[] { "IFCNONSENSE" }, schemas));

        var all = Resolver.ExpandClasses(Classes(false, "IfcElement"), schemas);
        Assert.Equal(new[] { "IfcElement" }, Resolver.FoldClasses(all, schemas));
    }
}
